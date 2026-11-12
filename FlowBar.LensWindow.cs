using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32.SafeHandles;
using PixelRect = System.Drawing.Rectangle;
using PixelSize = System.Drawing.Size;

namespace FlowBar
{
    internal sealed class GlassLensWindow : IDisposable
    {
        [StructLayout(LayoutKind.Explicit, Size = 220)] struct DisplayMode
        {
            [FieldOffset(68)] public ushort Size;
            [FieldOffset(184)] public int Frequency;
        }
        [StructLayout(LayoutKind.Sequential)] struct Blend { public byte Operation, Flags, Alpha, Format; }
        [StructLayout(LayoutKind.Sequential)] struct LayerUpdate
        {
            public int Size;
            public IntPtr Destination, Position, Extent, Source, Offset;
            public uint ColorKey;
            public IntPtr Blend;
            public uint Flags;
            public IntPtr Dirty;
        }
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateWindowEx(uint extendedStyle,
            string windowClass, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string device, int mode, ref DisplayMode data);
        [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destination,
            ref N.POINT position, ref N.POINT size, IntPtr source, ref N.POINT offset, uint colorKey, ref Blend blend, uint flags);
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindowIndirect(IntPtr window, ref LayerUpdate update);
        sealed class Request
        {
            public PixelRect Target, Monitor, Covered;
            public double Radius;
            public LensSettings Lens;
            public int Frequency;
            public IntPtr GpuHost;
            public string Display;
        }
        readonly object gate = new object();
        readonly AutoResetEvent wake;
        readonly IntPtr host;
        readonly Dispatcher dispatcher;
        readonly LensDib output = new LensDib();
        readonly SolidColorBrush opacity = new SolidColorBrush(Colors.White);
        IntPtr owner, gpuHost;
        volatile float gpuOpacity = 1;
        long gpuRetryAt;
        Request wanted, requested, ready, pixelRequest;
        DispatcherOperation queuedStart;
        int[] readyPixels;
        PixelRect readyRegion;
        bool presentQueued, disposing, workerStarted, shown, gpuShown, gpuHostVisible;
        PixelRect presentedBounds;
        string display;
        int refreshRate;
        long lastRefreshQuery;
        public bool Failed { get; private set; }
        internal bool PreferGpu { get; set; }

        public GlassLensWindow()
        {
            var value = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber", null);
            int build;
            if (!int.TryParse(value as string, out build)) build = Environment.OSVersion.Version.Build;
            if (build < 19041) throw new NotSupportedException("Преломление требует Windows 10 2004 или новее.");
            dispatcher = Dispatcher.CurrentDispatcher;
            // A native layered popup keeps WPF from replacing the bitmap between frames.
            host = CreateWindowEx(0x080800A8, "Static", "FlowBar lens", 0x80000000, 0, 0, 1, 1,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (host == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            wake = new AutoResetEvent(false);
            opacity.Changed += (s, e) =>
            {
                gpuOpacity = (float)opacity.Opacity;
                if (shown) Submit(presentedBounds);
            };
        }

        public bool Show(IntPtr parent, PixelRect bounds, PixelRect monitor, double radius, LensSettings material, string device)
        {
            if (Failed || disposing || parent == IntPtr.Zero) return false;
            if (owner != parent)
            {
                Stop();
                owner = parent;
                opacity.BeginAnimation(Brush.OpacityProperty, null); opacity.Opacity = 1;
            }
            long now = Stopwatch.GetTimestamp();
            if (refreshRate == 0 || device != display || now - lastRefreshQuery >= Stopwatch.Frequency * 3)
            {
                display = device; lastRefreshQuery = now;
                var mode = new DisplayMode { Size = 220 };
                refreshRate = EnumDisplaySettings(device, -1, ref mode) && mode.Frequency >= 24 ? Math.Min(mode.Frequency, 360) : 60;
            }
            var covered = bounds;
            N.RECT windowRect;
            if (!PreferGpu && N.GetWindowRect(parent, out windowRect))
                covered = PixelRect.Union(bounds, PixelRect.FromLTRB(windowRect.L, windowRect.T, windowRect.R, windowRect.B));
            if (wanted != null && wanted.Target == bounds && wanted.Monitor == monitor && wanted.Covered == covered && wanted.Radius == radius &&
                wanted.Frequency == refreshRate && wanted.Lens.SameAs(material)) return true;
            IntPtr accelerated = PreferGpu && material.Blur <= 8 && !string.IsNullOrEmpty(device) &&
                monitor.Contains(bounds) && now >= Interlocked.Read(ref gpuRetryAt) ? PrepareGpuHost() : IntPtr.Zero;
            wanted = new Request { Target = bounds, Monitor = monitor, Covered = covered, Radius = radius, Lens = material,
                Frequency = refreshRate, Display = device, GpuHost = accelerated };
            if (queuedStart == null)
                queuedStart = dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(StartCapture));
            return true;
        }

        IntPtr PrepareGpuHost()
        {
            if (gpuHost != IntPtr.Zero) return gpuHost;
            gpuHost = CreateWindowEx(0x082000A8, "Static", "FlowBar GPU lens", 0x80000000, 0, 0, 1, 1,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (gpuHost != IntPtr.Zero) return gpuHost;
            Interlocked.Exchange(ref gpuRetryAt, Stopwatch.GetTimestamp() + Stopwatch.Frequency * 60);
            return IntPtr.Zero;
        }

        void StartCapture()
        {
            queuedStart = null;
            lock (gate)
            {
                if (disposing || wanted == null) return;
                requested = wanted; ready = null;
                if (wanted.GpuHost != IntPtr.Zero)
                {
                    var bounds = wanted.Target;
                    gpuHostVisible = N.SetWindowPos(gpuHost, owner, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x50);
                }
                else HideGpuHost();
                if (!workerStarted)
                {
                    var thread = new Thread(CaptureLoop) { IsBackground = true, Name = "Flowbar glass capture" };
                    thread.Start(); workerStarted = true;
                }
                wake.Set();
            }
        }

        void CaptureLoop()
        {
            GpuLens gpu = null;
            try { CaptureLoop(ref gpu); }
            finally { if (gpu != null) gpu.Dispose(); }
        }

        void CaptureLoop(ref GpuLens gpu)
        {
            using (wake)
            using (var clock = new LensClock(wake))
            using (var sampler = new LensSampler())
            {
                Request active = null;
                int unchanged = 0;
                while (true)
                {
                    Request request;
                    lock (gate) { if (disposing) return; request = requested; }
                    if (request == null)
                    {
                        if (gpu != null) { gpu.Dispose(); gpu = null; }
                        active = null; wake.WaitOne(); continue;
                    }
                    long start = Stopwatch.GetTimestamp();
                    try
                    {
                        if (request != active)
                        {
                            if (gpu != null) { gpu.Dispose(); gpu = null; }
                            if (request.GpuHost != IntPtr.Zero)
                                try
                                {
                                    gpu = new GpuLens(request.Display, request.Monitor, request.Target, request.GpuHost,
                                        request.Lens.Profile == LensProfile.Ios, false, (float)request.Radius,
                                        request.Lens.Width, request.Lens.Shift, request.Lens.Blur);
                                }
                                catch (Exception ex) { ReportGpuFailure(request, ex); }
                            if (gpu == null) sampler.Prepare(request.Target, request.Monitor, request.Covered, request.Radius, request.Lens);
                            active = request; unchanged = 0;
                        }
                        if (gpu != null)
                        {
                            try
                            {
                                gpu.SetOpacity(gpuOpacity);
                                if (gpu.DrawNext() && unchanged == 0)
                                {
                                    unchanged = 1;
                                    dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                                    {
                                        if (request != wanted || disposing) return;
                                        if (shown) { ShowWindow(host, 0); shown = false; }
                                        if (!gpuShown) Bar.Info("преломление: рисует видеокарта, " + request.Target.Width + "×" + request.Target.Height + ", сила " + Math.Round(request.Lens.Shift, 1) + ", размытие " + request.Lens.Blur);
                                        gpuShown = true;
                                    }));
                                }
                                continue;
                            }
                            catch (Exception ex)
                            {
                                gpu.Dispose(); gpu = null;
                                ReportGpuFailure(request, ex);
                                sampler.Prepare(request.Target, request.Monitor, request.Covered, request.Radius, request.Lens);
                                unchanged = 0;
                            }
                        }
                        PixelRect damage;
                        if (sampler.Sample(out damage)) { unchanged = 0; Publish(request, sampler.Map, damage); }
                        else if (unchanged < 20) unchanged++;
                    }
                    catch (Exception ex)
                    {
                        lock (gate)
                        {
                            if (disposing) return;
                            if (request != requested) continue;
                            requested = null;
                        }
                        dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                        { if (request == wanted) Fail(ex); }));
                    }
                    int frequency = unchanged >= 20 ? Math.Min(144, request.Frequency) : request.Frequency;
                    long remaining = Stopwatch.Frequency / frequency - (Stopwatch.GetTimestamp() - start);
                    clock.Wait(Math.Max(Stopwatch.Frequency / 4000, remaining));
                }
            }
        }

        void ReportGpuFailure(Request request, Exception error)
        {
            Interlocked.Exchange(ref gpuRetryAt, Stopwatch.GetTimestamp() + Stopwatch.Frequency * 60);
            dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (request == wanted && !disposing) HideGpuHost();
                Bar.Log(new Exception("Аппаратное преломление недоступно. Стекло рассчитывается на процессоре.", error));
            }));
        }

        void HideGpuHost()
        {
            if (gpuHost != IntPtr.Zero) ShowWindow(gpuHost, 0);
            gpuShown = gpuHostVisible = false;
        }

        void Publish(Request request, GlassLensMap map, PixelRect region)
        {
            lock (gate)
            {
                if (disposing || request != requested) return;
                if (readyPixels == null || readyPixels.Length != map.Pixels.Length) readyPixels = new int[map.Pixels.Length];
                int width = request.Target.Width;
                if (pixelRequest != request || region.Size == request.Target.Size)
                    Array.Copy(map.Pixels, readyPixels, readyPixels.Length);
                else
                    for (int y = region.Top; y < region.Bottom; y++)
                    {
                        int row = y * width + region.Left;
                        Array.Copy(map.Pixels, row, readyPixels, row, region.Width);
                    }
                pixelRequest = request;
                readyRegion = ready == request ? PixelRect.Union(readyRegion, region) : region;
                ready = request;
                if (presentQueued) return;
                presentQueued = true;
                dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(PresentLatest));
            }
        }

        void PresentLatest()
        {
            PixelRect bounds, damage;
            try
            {
                lock (gate)
                {
                    presentQueued = false;
                    if (disposing || ready == null || ready != wanted) return;
                    bounds = ready.Target;
                    damage = readyRegion;
                    output.Resize(bounds.Size);
                    if (!shown || bounds != presentedBounds || damage.Size == bounds.Size)
                        Marshal.Copy(readyPixels, 0, output.Pixels, readyPixels.Length);
                    else
                        for (int y = damage.Top; y < damage.Bottom; y++)
                        {
                            int row = y * bounds.Width + damage.Left;
                            Marshal.Copy(readyPixels, row, output.Pixels + row * 4, damage.Width);
                        }
                    ready = null;
                }
                if (!(shown && bounds == presentedBounds && damage.Size != bounds.Size && SubmitRegion(damage)) && !Submit(bounds)) return;
                if (!shown || bounds != presentedBounds)
                    N.SetWindowPos(host, owner, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x10 | 0x40);
                if (!shown) Bar.Info("преломление: рисует процессор, " + bounds.Width + "×" + bounds.Height);
                shown = true; presentedBounds = bounds;
            }
            catch (Exception ex) { Fail(ex); }
        }

        unsafe bool SubmitRegion(PixelRect region)
        {
            var offset = new N.POINT();
            var blend = new Blend { Alpha = (byte)Math.Round(opacity.Opacity * 255), Format = 1 };
            var rect = new N.RECT { L = region.Left, T = region.Top, R = region.Right, B = region.Bottom };
            var update = new LayerUpdate { Size = sizeof(LayerUpdate), Source = output.Dc, Offset = (IntPtr)(&offset),
                Blend = (IntPtr)(&blend), Flags = 2, Dirty = (IntPtr)(&rect) };
            return UpdateLayeredWindowIndirect(host, ref update);
        }

        bool Submit(PixelRect bounds)
        {
            var position = new N.POINT { X = bounds.X, Y = bounds.Y };
            var size = new N.POINT { X = bounds.Width, Y = bounds.Height };
            var zero = new N.POINT();
            var blend = new Blend { Alpha = (byte)Math.Round(opacity.Opacity * 255), Format = 1 };
            if (UpdateLayeredWindow(host, IntPtr.Zero, ref position, ref size, output.Dc, ref zero, 0, ref blend, 2)) return true;
            Fail(new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error())); return false;
        }

        void Fail(Exception ex)
        {
            Failed = true; Stop(); Bar.Log(new Exception("Преломление недоступно. Используется системное стекло.", ex));
        }

        public void Fade(double to, int ms)
        {
            if (shown || gpuShown) opacity.BeginAnimation(Brush.OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)));
        }

        internal IntPtr StackBelow(IntPtr parent)
        {
            if (gpuHostVisible) return gpuHost;
            return shown ? host : parent;
        }

        public void Stop()
        {
            if (queuedStart != null) { queuedStart.Abort(); queuedStart = null; }
            wanted = null;
            lock (gate) { requested = ready = null; if (workerStarted && !disposing) wake.Set(); }
            if (shown) ShowWindow(host, 0);
            HideGpuHost();
            shown = false;
            owner = IntPtr.Zero;
        }

        public void Dispose()
        {
            if (disposing) return;
            Stop();
            lock (gate)
            {
                disposing = true;
                if (workerStarted) wake.Set(); else wake.Dispose();
            }
            output.Dispose(); DestroyWindow(host);
            if (gpuHost != IntPtr.Zero) { DestroyWindow(gpuHost); gpuHost = IntPtr.Zero; }
        }
    }

    internal sealed class LensClock : IDisposable
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWaitableTimerEx(IntPtr attributes, string name, uint flags, uint access);
        [DllImport("kernel32.dll")] static extern bool SetWaitableTimer(IntPtr timer, ref long time, int period, IntPtr completion, IntPtr arg, bool resume);
        readonly AutoResetEvent wake;
        readonly EventWaitHandle timer;
        readonly WaitHandle[] waits;

        public LensClock(AutoResetEvent wake)
        {
            this.wake = wake;
            var handle = CreateWaitableTimerEx(IntPtr.Zero, null, 2, 0x00100002);
            if (handle == IntPtr.Zero) return;
            timer = new EventWaitHandle(false, EventResetMode.AutoReset);
            timer.SafeWaitHandle = new SafeWaitHandle(handle, true);
            waits = new WaitHandle[] { wake, timer };
        }

        public void Wait(long ticks)
        {
            long due = -Math.Max(1, ticks * 10000000 / Stopwatch.Frequency);
            if (timer != null && SetWaitableTimer(timer.SafeWaitHandle.DangerousGetHandle(), ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                WaitHandle.WaitAny(waits);
            else wake.WaitOne((int)Math.Max(1, (-due + 9999) / 10000));
        }

        public void Dispose() { if (timer != null) timer.Dispose(); }
    }

    internal sealed class LensDib : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct DibHeader
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, Bits;
            public uint Compression, SizeImage;
            public int XDensity, YDensity;
            public uint Colors, Important;
        }
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref DibHeader header, uint usage, out IntPtr pixels, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")] static extern bool GdiFlush();
        IntPtr dib, oldDib;
        PixelSize size;
        public IntPtr Dc { get; private set; }
        public IntPtr Pixels { get; private set; }

        public void Resize(PixelSize next)
        {
            if (Dc != IntPtr.Zero && size == next) return;
            Dispose();
            var header = new DibHeader { Size = 40, Width = next.Width, Height = -next.Height, Planes = 1, Bits = 32 };
            Dc = CreateCompatibleDC(IntPtr.Zero);
            IntPtr pixels;
            dib = CreateDIBSection(Dc, ref header, 0, out pixels, IntPtr.Zero, 0);
            if (Dc == IntPtr.Zero || dib == IntPtr.Zero) throw new InvalidOperationException("Не удалось создать буфер преломления.");
            oldDib = SelectObject(Dc, dib);
            if (oldDib == IntPtr.Zero || oldDib == new IntPtr(-1)) throw new InvalidOperationException("Не удалось подключить буфер преломления.");
            Pixels = pixels; size = next;
        }

        public void Dispose()
        {
            if (oldDib != IntPtr.Zero && oldDib != new IntPtr(-1)) SelectObject(Dc, oldDib);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (Dc != IntPtr.Zero) DeleteDC(Dc);
            oldDib = dib = Dc = Pixels = IntPtr.Zero;
        }

        public bool CopyFromDesktop(PixelRect bounds)
        {
            Resize(bounds.Size);
            var screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) return false;
            try
            {
                if (!BitBlt(Dc, 0, 0, bounds.Width, bounds.Height, screen, bounds.X, bounds.Y, 0x40CC0020)) return false;
                GdiFlush();
                return true;
            }
            finally { ReleaseDC(IntPtr.Zero, screen); }
        }
    }

    internal sealed class LensSampler : IDisposable
    {
        readonly LensDib input = new LensDib();
        PixelRect source, target, coveredPixels;
        int[] frame, previous;
        int coverage;
        bool dirty;
        public GlassLensMap Map { get; private set; }

        public void Prepare(PixelRect target, PixelRect monitor, PixelRect covered, double radius, LensSettings lens)
        {
            this.target = target;
            source = target;
            source.Inflate(lens.Padding, lens.Padding);
            source.Intersect(monitor);
            if (source.Width < 1 || source.Height < 1 || (long)source.Width * source.Height > 4000000)
                throw new InvalidOperationException("Стеклянная область вне монитора или слишком велика.");
            input.Resize(source.Size);
            int length = source.Width * source.Height;
            if (frame == null || frame.Length != length)
            { frame = new int[length]; previous = new int[length]; }
            coveredPixels = PixelRect.Intersect(covered, source);
            coveredPixels.Offset(-source.Left, -source.Top);
            Map = new GlassLensMap(target, source, monitor, radius, lens, covered);
            coverage = lens.Padding + (int)Math.Ceiling(Math.Max(radius, lens.Width));
            dirty = true;
        }

        public bool Sample(out PixelRect region)
        {
            region = PixelRect.Empty;
            if (!input.CopyFromDesktop(source))
                throw new InvalidOperationException("Windows не дала прочитать область под панелью.");
            Marshal.Copy(input.Pixels, frame, 0, frame.Length);
            for (int y = coveredPixels.Top; y < coveredPixels.Bottom; y++)
                Array.Clear(frame, y * source.Width + coveredPixels.Left, coveredPixels.Width);
            var changes = dirty ? new PixelRect(0, 0, source.Width, source.Height) : FindChanges();
            if (changes.IsEmpty) return false;
            region = new PixelRect(0, 0, target.Width, target.Height);
            if (!dirty)
            {
                changes.Offset(source.X - target.X, source.Y - target.Y);
                changes.Inflate(coverage, coverage);
                region.Intersect(changes);
            }
            Map.Render(frame, region);
            var swap = previous; previous = frame; frame = swap; dirty = false;
            return region.Width > 0 && region.Height > 0;
        }

        PixelRect FindChanges()
        {
            int left = source.Width, top = source.Height, right = 0, bottom = 0;
            for (int y = 0; y < source.Height; y++)
            {
                int row = y * source.Width, first = 0;
                while (first < source.Width && ((frame[row + first] ^ previous[row + first]) & 0xFFFFFF) == 0) first++;
                if (first == source.Width) continue;
                int last = source.Width - 1;
                while (last > first && ((frame[row + last] ^ previous[row + last]) & 0xFFFFFF) == 0) last--;
                left = Math.Min(left, first); right = Math.Max(right, last + 1);
                top = Math.Min(top, y); bottom = y + 1;
            }
            return left < right ? PixelRect.FromLTRB(left, top, right, bottom) : PixelRect.Empty;
        }

        public void Dispose() { input.Dispose(); }
    }
}
