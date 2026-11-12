// Стеклянный материал панели и карточек.
// Размытый реальный фон (HostBackdropBrush композиции Windows) рисуется в отдельном окне-подложке ровно под капсулой,
// с точной скруглённой обрезкой. Почему не SetWindowCompositionAttribute + SetWindowRgn, как раньше: на Windows 11
// системное размытие окна регион игнорирует (и у многослойного, и у обычного окна) — за капсулой был виден прямоугольник окна.
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Windows.Graphics.Effects;
using WUC = Windows.UI.Composition;

namespace FlowBar
{
    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICompositorDesktopInterop { void CreateDesktopWindowTarget(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool isTopmost, [MarshalAs(UnmanagedType.IUnknown)] out object target); }

    [ComImport, Guid("2FC57384-A068-44D7-A331-30982FCF7177"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IGraphicsEffectD2D1Interop
    {
        void GetEffectId(out Guid id);
        [PreserveSig] int GetNamedPropertyMapping([MarshalAs(UnmanagedType.LPWStr)] string name, out uint index, out uint mapping);
        void GetPropertyCount(out uint count);
        [PreserveSig] int GetProperty(uint index, out IntPtr value);
        [PreserveSig] int GetSource(uint index, out IntPtr source);
        void GetSourceCount(out uint count);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class GlassBlurEffect : IGraphicsEffect, IGraphicsEffectSource, IGraphicsEffectD2D1Interop
    {
        const int InvalidArgument = unchecked((int)0x80070057);
        static readonly Guid BlurId = new Guid("1FEB6D69-2FE6-4AC9-8C58-1D7F93E7A6A5");
        static readonly Guid PropertyValueId = new Guid("4BD682DD-7554-40E9-9A9B-82654EDE7E62");
        static readonly Guid PropertyValueStaticsId = new Guid("629BDBC8-D932-4FF4-96B9-8D96C5C1E858");
        [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string text, int length, out IntPtr value);
        [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr value);
        [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr name, ref Guid id, out IntPtr factory);
        delegate int BoxSingle(IntPtr factory, float value, out IntPtr boxed);
        delegate int BoxUInt32(IntPtr factory, uint value, out IntPtr boxed);
        readonly WUC.CompositionEffectSourceParameter input = new WUC.CompositionEffectSourceParameter("Backdrop");
        public string Name { get; set; }
        public void GetEffectId(out Guid id) { id = BlurId; }
        public void GetPropertyCount(out uint count) { count = 3; }
        public void GetSourceCount(out uint count) { count = 1; }
        public int GetNamedPropertyMapping(string name, out uint index, out uint mapping)
        {
            index = 0; mapping = 1; // GRAPHICS_EFFECT_PROPERTY_MAPPING_DIRECT
            return name == "StandardDeviation" ? 0 : InvalidArgument;
        }
        public int GetProperty(uint index, out IntPtr value)
        {
            value = IntPtr.Zero;
            if (index > 2) return InvalidArgument;
            // .NET Framework converts WinRT PropertyValue boxes to CLR primitives. Keep the native boxes for the effect ABI.
            const string runtimeClass = "Windows.Foundation.PropertyValue";
            IntPtr name, factory = IntPtr.Zero, boxed = IntPtr.Zero;
            int hr = WindowsCreateString(runtimeClass, runtimeClass.Length, out name);
            if (hr < 0) return hr;
            try
            {
                var factoryId = PropertyValueStaticsId;
                hr = RoGetActivationFactory(name, ref factoryId, out factory);
                if (hr < 0) return hr;
                var vtable = Marshal.ReadIntPtr(factory);
                if (index == 0)
                {
                    var create = (BoxSingle)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vtable, 14 * IntPtr.Size), typeof(BoxSingle));
                    hr = create(factory, 8f, out boxed);
                }
                else
                {
                    var create = (BoxUInt32)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vtable, 11 * IntPtr.Size), typeof(BoxUInt32));
                    hr = create(factory, 1, out boxed); // balanced optimization and hard border
                }
                if (hr < 0) return hr;
                var iid = PropertyValueId;
                return Marshal.QueryInterface(boxed, ref iid, out value);
            }
            finally
            {
                if (boxed != IntPtr.Zero) Marshal.Release(boxed);
                if (factory != IntPtr.Zero) Marshal.Release(factory);
                WindowsDeleteString(name);
            }
        }
        public int GetSource(uint index, out IntPtr source)
        {
            source = index == 0 ? Marshal.GetComInterfaceForObject(input, typeof(IGraphicsEffectSource)) : IntPtr.Zero;
            return index == 0 ? 0 : InvalidArgument;
        }
    }

    class Backdrop
    {
        [StructLayout(LayoutKind.Sequential)] struct DQO { public int Size, Thread, Apartment; }
        [DllImport("CoreMessaging.dll")] static extern int CreateDispatcherQueueController(DQO o, out IntPtr ctl);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);

        static IntPtr dq; static WUC.Compositor comp; static DateTime failedAt = DateTime.MinValue;
        static WUC.CompositionEffectFactory blurFactory;
        static bool blurUnavailable;
        // сбой не выключает стекло навсегда: через минуту — новая попытка (сменилась среда, перезапустился DWM)
        static bool Failed { get { return (DateTime.Now - failedAt).TotalSeconds < 60; } }
        HwndSource host; WUC.Desktop.DesktopWindowTarget target; WUC.SpriteVisual vis; WUC.CompositionRoundedRectangleGeometry geo;
        WUC.CompositionBackdropBrush systemBrush, desktopBrush;
        WUC.CompositionEffectBrush customBrush;
        GlassLensWindow lensWindow;
        bool lensFailed;
        float lastBlur = float.NaN;
        bool shown;

        // «Эффекты прозрачности» Windows выключены или композиция недоступна — стекла нет, темы рисуются сплошной поверхностью
        public static bool Available { get { return Why == null; } }
        static string lastError; static DateTime whyAt; static string whyCached;
        // почему стекла нет (null — есть): видно в «Оформлении» и в отчёте. Проверка раз в 3 с
        public static string Why
        {
            get
            {
                if (Failed) return "Windows не дала создать размытие: " + lastError;
                if ((DateTime.Now - whyAt).TotalSeconds < 3) return whyCached;
                whyAt = DateTime.Now; whyCached = null;
                bool regOn = true;
                try { var v = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1); regOn = !(v is int) || (int)v != 0; } catch { }
                if (!regOn) return whyCached = "в Windows выключены «Эффекты прозрачности»";
                // учитывает и галочку, и режим энергосбережения, в котором Windows сама выключает прозрачность
                try { if (!new Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled) return whyCached = "Windows сейчас выключила прозрачность — обычно из-за режима энергосбережения"; } catch { }
                return null;
            }
        }

        public static Backdrop Create()
        {
            if (!Available) return null;
            var b = new Backdrop();
            try
            {
                if (comp == null)
                {
                    // очередь диспетчера нужна композиции в этом потоке; уже созданная другим кодом — тоже годится (тогда вызов вернёт ошибку, а компоновщик создастся)
                    if (dq == IntPtr.Zero) { int hr = CreateDispatcherQueueController(new DQO { Size = 12, Thread = 2, Apartment = 2 }, out dq); if (hr != 0) Bar.Log(new Exception("Стекло: очередь диспетчера не создана, код 0x" + hr.ToString("X8") + " — пробуем с уже существующей")); }
                    comp = new WUC.Compositor();
                }
                // без перенаправления (рисует только визуал композиции), не активируется, без Alt+Tab, поверх всех, мышь проходит
                var p = new HwndSourceParameters("FlowBar glass") { WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x00200000 | 0x08000000 | 0x80 | 0x20 | 0x8, Width = 1, Height = 1 };
                b.host = new HwndSource(p);
                int on = 1, ar = DwmSetWindowAttribute(b.host.Handle, 17 /* DWMWA_USE_HOSTBACKDROPBRUSH */, ref on, 4);
                if (ar != 0) throw new Exception("DWMWA_USE_HOSTBACKDROPBRUSH: код 0x" + ar.ToString("X8")); // без него подложка была бы пустой — честнее сплошная поверхность
                object t; ((ICompositorDesktopInterop)(object)comp).CreateDesktopWindowTarget(b.host.Handle, false, out t);
                b.target = (WUC.Desktop.DesktopWindowTarget)t;
                b.vis = comp.CreateSpriteVisual(); b.vis.Brush = b.systemBrush = comp.CreateHostBackdropBrush();
                b.geo = comp.CreateRoundedRectangleGeometry(); b.vis.Clip = comp.CreateGeometricClip(b.geo);
                b.target.Root = b.vis;
                return b;
            }
            catch (Exception ex)
            {
                b.Dispose(); if (blurFactory != null) { blurFactory.Dispose(); blurFactory = null; }
                comp = null; blurUnavailable = false; failedAt = DateTime.Now; lastError = ex.Message;
                Bar.Log(new Exception("Стеклянный материал недоступен — темы рисуются сплошной поверхностью, новая попытка через минуту", ex)); return null;
            }
        }

        // x, y, w, h — физические пиксели экрана; r — радиус углов в пикселях; подложка встаёт сразу под окно owner
        public void Set(IntPtr owner, int x, int y, int w, int h, double r, bool show, float blur = -1)
        {
            SetLens(owner, x, y, w, h, r, show, blur, default(LensSettings), System.Drawing.Rectangle.Empty, null);
        }

        internal void SetLens(IntPtr owner, int x, int y, int w, int h, double r, bool show, float blur,
            LensSettings lens, System.Drawing.Rectangle monitor, string device, bool preferGpu = false)
        {
            if (host == null) return;
            // сбой композиции не должен валить панель: объект Windows мог быть закрыт (смена видеорежима, перезапуск DWM) — стекла нет, заливка станет сплошной
            try
            {
                if (!show || w < 1 || h < 1)
                {
                    if (lensWindow != null) lensWindow.Stop();
                }
                else if (UseLens(owner, new System.Drawing.Rectangle(x, y, w, h), monitor, r, lens, device, preferGpu))
                {
                    SetCore(lensWindow.StackBelow(owner), x, y, w, h, r, show, blur);
                    return;
                }
                SetCore(owner, x, y, w, h, r, show, blur);
            }
            catch (Exception ex)
            {
                failedAt = DateTime.Now; lastError = ex.Message; comp = null; blurFactory = null; blurUnavailable = false;
                Bar.Log(new Exception("Подложка стекла сломалась (host=" + (host != null) + ", shown=" + shown + ", " + w + "×" + h + "): темы рисуются сплошной поверхностью, новая попытка через минуту", ex));
                Dispose();
            }
        }

        bool UseLens(IntPtr owner, System.Drawing.Rectangle bounds, System.Drawing.Rectangle monitor, double radius, LensSettings lens, string device, bool preferGpu)
        {
            if (!lens.Enabled)
            {
                if (lensWindow != null) { lensWindow.Dispose(); lensWindow = null; }
                lensFailed = false;
                return false;
            }
            if (lensFailed) return false;
            try
            {
                if (lensWindow == null) lensWindow = new GlassLensWindow();
                lensWindow.PreferGpu = preferGpu;
                if (lensWindow.Show(owner, bounds, monitor, radius, lens, device)) return true;
            }
            catch (Exception ex) { Bar.Log(new Exception("Преломление недоступно. Используется системное стекло.", ex)); }
            if (lensWindow != null) { lensWindow.Dispose(); lensWindow = null; }
            lensFailed = true;
            return false;
        }
        void SetCore(IntPtr owner, int x, int y, int w, int h, double r, bool show, float blur)
        {
            if (!show || w < 1 || h < 1) { if (shown) { ShowWindow(host.Handle, 0); shown = false; } return; }
            SetBlur(blur);
            var size = new Vector2(w, h);
            if (vis.Size != size) { vis.Size = size; geo.Size = size; }
            float rr = (float)Math.Min(r, Math.Min(w, h) / 2.0); geo.CornerRadius = new Vector2(rr, rr);
            SetWindowPos(host.Handle, owner == IntPtr.Zero ? new IntPtr(-1) : owner, x, y, w, h, 0x10 | 0x40); // NOACTIVATE | SHOWWINDOW
            if (!shown) Animate(0, 1, 150); // проявляется вместе с содержимым, а не раньше него
            shown = true;
        }
        void SetBlur(float blur)
        {
            if (blur == lastBlur) return;
            lastBlur = blur;
            if (blur < 0 || blurUnavailable) { vis.Brush = systemBrush; return; }
            try
            {
                if (desktopBrush == null) desktopBrush = comp.CreateBackdropBrush();
                if (blur == 0) { vis.Brush = desktopBrush; return; }
                if (customBrush == null)
                {
                    if (blurFactory == null)
                        blurFactory = comp.CreateEffectFactory(new GlassBlurEffect { Name = "Blur" }, new[] { "Blur.StandardDeviation" });
                    customBrush = blurFactory.CreateBrush();
                    customBrush.SetSourceParameter("Backdrop", desktopBrush);
                }
                customBrush.Properties.InsertScalar("Blur.StandardDeviation", blur);
                vis.Brush = customBrush;
            }
            catch (Exception ex)
            {
                blurUnavailable = true;
                vis.Brush = systemBrush;
                Bar.Log(new Exception("Регулируемое размытие недоступно. Используется системное стекло.", ex));
            }
        }
        // уход вместе с карточкой
        public void Fade() { if (lensWindow != null) lensWindow.Fade(0, 120); if (host != null && shown) try { Animate(1, 0, 120); } catch (Exception ex) { Bar.Log(ex); } }
        public void Unfade() { if (lensWindow != null) lensWindow.Fade(1, 150); if (host != null && shown) try { Animate(0, 1, 150); } catch (Exception ex) { Bar.Log(ex); } } // панель вернули, пока она уезжала
        void Animate(float from, float to, int ms)
        {
            var a = comp.CreateScalarKeyFrameAnimation(); a.InsertKeyFrame(0f, from); a.InsertKeyFrame(1f, to); a.Duration = TimeSpan.FromMilliseconds(ms);
            vis.StartAnimation("Opacity", a);
        }

        public void Dispose()
        {
            if (lensWindow != null) { lensWindow.Dispose(); lensWindow = null; }
            if (host == null) return;
            try
            {
                if (target != null) { target.Root = null; target.Dispose(); }
                if (vis != null) vis.Dispose();
                if (geo != null) geo.Dispose();
                if (customBrush != null) customBrush.Dispose();
                if (desktopBrush != null) desktopBrush.Dispose();
                if (systemBrush != null) systemBrush.Dispose();
                host.Dispose();
            }
            catch (Exception ex) { Bar.Log(ex); }
            host = null;
        }
    }
}
