// Стеклянный материал панели и карточек.
// Размытый реальный фон (HostBackdropBrush композиции Windows) рисуется в отдельном окне-подложке ровно под капсулой,
// с точной скруглённой обрезкой. Почему не SetWindowCompositionAttribute + SetWindowRgn, как раньше: на Windows 11
// системное размытие окна регион игнорирует (и у многослойного, и у обычного окна) — за капсулой был виден прямоугольник окна.
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using WUC = Windows.UI.Composition;

namespace FlowBar
{
    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICompositorDesktopInterop { void CreateDesktopWindowTarget(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool isTopmost, [MarshalAs(UnmanagedType.IUnknown)] out object target); }

    class Backdrop
    {
        [StructLayout(LayoutKind.Sequential)] struct DQO { public int Size, Thread, Apartment; }
        [DllImport("CoreMessaging.dll")] static extern int CreateDispatcherQueueController(DQO o, out IntPtr ctl);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);

        static IntPtr dq; static WUC.Compositor comp; static DateTime failedAt = DateTime.MinValue;
        // сбой не выключает стекло навсегда: через минуту — новая попытка (сменилась среда, перезапустился DWM)
        static bool Failed { get { return (DateTime.Now - failedAt).TotalSeconds < 60; } }
        HwndSource host; WUC.Desktop.DesktopWindowTarget target; WUC.SpriteVisual vis; WUC.CompositionRoundedRectangleGeometry geo;
        bool shown;

        // «Эффекты прозрачности» Windows выключены или композиция недоступна — стекла нет, темы рисуются сплошной поверхностью
        public static bool Available
        {
            get
            {
                if (Failed) return false;
                try { var v = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1); return !(v is int) || (int)v != 0; }
                catch { return true; }
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
                b.vis = comp.CreateSpriteVisual(); b.vis.Brush = comp.CreateHostBackdropBrush();
                b.geo = comp.CreateRoundedRectangleGeometry(); b.vis.Clip = comp.CreateGeometricClip(b.geo);
                b.target.Root = b.vis;
                return b;
            }
            catch (Exception ex)
            {
                b.Dispose(); comp = null; failedAt = DateTime.Now; // частично созданное — освобождаем; компоновщик пересоздадим при следующей попытке
                Bar.Log(new Exception("Стеклянный материал недоступен — темы рисуются сплошной поверхностью, новая попытка через минуту", ex)); return null;
            }
        }

        // x, y, w, h — физические пиксели экрана; r — радиус углов в пикселях; подложка встаёт сразу под окно owner
        public void Set(IntPtr owner, int x, int y, int w, int h, double r, bool show)
        {
            if (host == null) return;
            if (!show || w < 1 || h < 1) { if (shown) { ShowWindow(host.Handle, 0); shown = false; } return; }
            var size = new Vector2(w, h);
            if (vis.Size != size) { vis.Size = size; geo.Size = size; }
            float rr = (float)Math.Min(r, Math.Min(w, h) / 2.0); geo.CornerRadius = new Vector2(rr, rr);
            SetWindowPos(host.Handle, owner == IntPtr.Zero ? new IntPtr(-1) : owner, x, y, w, h, 0x10 | 0x40); // NOACTIVATE | SHOWWINDOW
            if (!shown) Animate(0, 1, 150); // проявляется вместе с содержимым, а не раньше него
            shown = true;
        }
        // уход вместе с карточкой
        public void Fade() { if (host != null && shown) Animate(1, 0, 120); }
        void Animate(float from, float to, int ms)
        {
            var a = comp.CreateScalarKeyFrameAnimation(); a.InsertKeyFrame(0f, from); a.InsertKeyFrame(1f, to); a.Duration = TimeSpan.FromMilliseconds(ms);
            vis.StartAnimation("Opacity", a);
        }

        public void Dispose()
        {
            if (host == null) return;
            try { if (target != null) { target.Root = null; target.Dispose(); } if (vis != null) vis.Dispose(); if (geo != null) geo.Dispose(); host.Dispose(); } catch (Exception ex) { Bar.Log(ex); }
            host = null;
        }
    }
}
