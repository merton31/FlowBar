using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class Program
{
    const int Cover = 0xF015AF;
    record Result(string Screen, Size Resolution, int RefreshHz, int Sent, int Submitted, int Distinct, int CoverFrames, int OtherFrames,
        int MissingCaptureFrames, double Seconds, double IntervalP95, double DelayP50, double DelayP95,
        double LocalMiB, double PeakDeltaMiB, int QuietSubmissions, double QuietSeconds);

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Native.SetProcessDpiAwarenessContext(-4);
            bool ios = args.Contains("--ios");
            string choice = args.FirstOrDefault(arg => !arg.StartsWith("--")) ?? "DISPLAY2";
            var screens = Screen.AllScreens.Where(s => choice == "all" ||
                s.DeviceName.EndsWith(choice, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (screens.Length == 0) throw new ArgumentException("Display not found: " + choice);
            return Run(screens, ios);
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    static int Run(Screen[] screens, bool ios)
    {
        var scenes = new List<Scene>();
        using var stop = new CancellationTokenSource();
        var workers = new List<Task<Result>>();
        try
        {
            foreach (var screen in screens) scenes.Add(new Scene(screen));
            foreach (var scene in scenes)
                workers.Add(Task.Factory.StartNew(() => Measure(scene, ios, stop.Token),
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));

            using var timer = new Native.Timer();
            var clock = Stopwatch.StartNew();
            while (scenes.Any(s => !s.Ready))
            {
                if (workers.Any(w => w.IsCompleted)) Task.WaitAll(workers.Where(w => w.IsCompleted).ToArray());
                if (clock.Elapsed.TotalSeconds > 8) throw new TimeoutException("GPU capture did not start.");
                Application.DoEvents();
                timer.Wait(10000);
            }

            // Offer up to 320 source changes/s per screen, identical for both renderers.
            long start = Stopwatch.GetTimestamp();
            foreach (var scene in scenes) scene.Start = start;
            int sequence = 0;
            long end = start + Stopwatch.Frequency * 3;
            while (Stopwatch.GetTimestamp() < end)
            {
                Application.DoEvents();
                foreach (var scene in scenes) scene.Paint(++scene.Sent);
                long next = start + ++sequence * Stopwatch.Frequency / 320;
                timer.Wait(Math.Max(1500, (next - Stopwatch.GetTimestamp()) * 10000000 / Stopwatch.Frequency));
            }
            long quietStart = Stopwatch.GetTimestamp();
            foreach (var scene in scenes) scene.QuietStart = quietStart;
            while (Stopwatch.GetTimestamp() - quietStart < Stopwatch.Frequency)
            {
                Application.DoEvents();
                timer.Wait(10000);
            }
            stop.Cancel();
            while (workers.Any(w => !w.IsCompleted)) { Application.DoEvents(); Thread.Sleep(1); }
            foreach (var worker in workers)
            {
                Result m = worker.GetAwaiter().GetResult();
                Console.WriteLine(FormattableString.Invariant(
                    $"screen={m.Screen} resolution={m.Resolution.Width}x{m.Resolution.Height} mode_Hz={m.RefreshHz} profile={(ios ? "iOS" : "Liquid")} seconds={m.Seconds:F3} source={m.Sent} submitted={m.Submitted} unique={m.Distinct} unique/s={m.Distinct/m.Seconds:F1} cover={m.CoverFrames} other={m.OtherFrames} coalesced={m.MissingCaptureFrames} gap_p95_ms={m.IntervalP95:F2} capture_to_read_p50_ms={m.DelayP50:F2} capture_to_read_p95_ms={m.DelayP95:F2} process_local_MiB={m.LocalMiB:F2} peak_delta_MiB={m.PeakDeltaMiB:F2} quiet_submits/s={m.QuietSubmissions/m.QuietSeconds:F1}"));
            }
            return workers.All(w => w.Result.CoverFrames == 0 && w.Result.OtherFrames <= 3 && w.Result.Distinct > 30) ? 0 : 2;
        }
        finally
        {
            stop.Cancel();
            while (workers.Any(w => !w.IsCompleted)) { Application.DoEvents(); Thread.Sleep(1); }
            foreach (var scene in scenes) scene.Dispose();
        }
    }

    static Result Measure(Scene scene, bool ios, CancellationToken stop)
    {
        using var lens = new GpuLens(scene.Screen.DeviceName, scene.Screen.Bounds, scene.Bounds, scene.Overlay,
            ios, true, 30, 18, 15, 1);
        ulong peak = lens.LocalMemory();
        long lastStamp = 0, lastMemory = 0;
        int submitted = 0, quietSubmissions = 0, distinct = 0, cover = 0, unknown = 0, previous = -1;
        var intervals = new List<double>();
        var delays = new List<double>();
        while (!stop.IsCancellationRequested)
        {
            bool drawn = lens.DrawNext();
            if (drawn)
            {
                scene.Ready = true;
                if (scene.QuietStart != 0) quietSubmissions++;
                else if (scene.Start != 0) submitted++;
            }
            while (lens.ReadProbe(out int rgb, out long stamp))
            {
                if (scene.Start == 0 || stamp < scene.Start || (scene.QuietStart != 0 && stamp >= scene.QuietStart)) continue;
                if (rgb == Cover) { cover++; continue; }
                if (!scene.Colors.ContainsKey(rgb)) { unknown++; continue; }
                if (rgb == previous) continue;
                if (lastStamp != 0) intervals.Add(Milliseconds(stamp - lastStamp));
                delays.Add(Milliseconds(Stopwatch.GetTimestamp() - stamp));
                lastStamp = stamp;
                previous = rgb;
                distinct++;
            }
            long now = Stopwatch.GetTimestamp();
            if (now - lastMemory > Stopwatch.Frequency / 10)
            {
                lastMemory = now;
                peak = Math.Max(peak, lens.LocalMemory());
            }
        }
        double seconds = (scene.QuietStart - scene.Start) / (double)Stopwatch.Frequency;
        double quietSeconds = (Stopwatch.GetTimestamp() - scene.QuietStart) / (double)Stopwatch.Frequency;
        return new Result(scene.Screen.DeviceName, scene.Screen.Bounds.Size, Native.RefreshRate(scene.Screen.DeviceName),
            scene.Sent, submitted, distinct, cover, unknown,
            lens.AccumulatedFrames, seconds, Percentile(intervals, 0.95), Percentile(delays, 0.50),
            Percentile(delays, 0.95), lens.LocalMemory() / 1048576d,
            (peak - Math.Min(peak, lens.MemoryBefore)) / 1048576d, quietSubmissions, quietSeconds);
    }

    static double Milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
    static double Percentile(List<double> values, double fraction)
    {
        if (values.Count == 0) return double.NaN;
        values.Sort();
        return values[(int)Math.Ceiling((values.Count - 1) * fraction)];
    }

    sealed class Scene : IDisposable
    {
        readonly Native.Layer backdrop, producer, cover;
        public readonly Screen Screen;
        public readonly Rectangle Bounds;
        public readonly nint Overlay;
        public readonly ConcurrentDictionary<int, byte> Colors = new();
        public int Sent;
        public volatile bool Ready;
        long start, quietStart;
        public long Start { get => Interlocked.Read(ref start); set => Interlocked.Exchange(ref start, value); }
        public long QuietStart { get => Interlocked.Read(ref quietStart); set => Interlocked.Exchange(ref quietStart, value); }

        public Scene(Screen screen)
        {
            Screen = screen;
            Bounds = new Rectangle(screen.Bounds.Left + 180, screen.Bounds.Top + 170,
                Math.Min(1180, screen.Bounds.Width - 360), 75);
            int centerX = Bounds.Left + Bounds.Width / 2, centerY = Bounds.Top + Bounds.Height / 2;
            var back = Bounds;
            back.Inflate(35, 35);
            backdrop = new Native.Layer(back, "Flowbar test backdrop");
            backdrop.Paint(0x151E47);
            backdrop.Show(-1);
            producer = new Native.Layer(new Rectangle(centerX - 16, centerY - 16, 32, 32), "Flowbar test source");
            Paint(0);
            producer.Show(-1);
            cover = new Native.Layer(new Rectangle(centerX - 7, centerY - 7, 14, 14), "Flowbar test exclusion");
            cover.Paint(Cover);
            Native.Exclude(cover.Handle);
            cover.Show(-1);
            Overlay = Native.Window(0x082000A8, Bounds, "Flowbar GPU glass preview");
            Native.Exclude(Overlay);
            Native.Show(Overlay, -1, Bounds);
        }

        public void Paint(int seq)
        {
            int rgb = ((seq * 97 % 251 + 1) << 16) | ((seq * 113 % 253 + 1) << 8) | (seq * 137 % 249 + 1);
            Colors.TryAdd(rgb, 0);
            producer.Paint(rgb);
        }

        public void Dispose()
        {
            Native.DestroyWindow(Overlay);
            cover.Dispose(); producer.Dispose(); backdrop.Dispose();
        }
    }
}

internal static unsafe class Native
{
    [StructLayout(LayoutKind.Explicit, Size = 220)] struct DisplayMode
    {
        [FieldOffset(68)] public ushort Size;
        [FieldOffset(184)] public int Frequency;
    }
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] struct Blend { public byte Op,Flags,Alpha,Format; }
    [StructLayout(LayoutKind.Sequential)] struct BitmapInfo
    { public int Size,Width,Height; public ushort Planes,Bits; public int Compress,ImageSize,X,Y,Colors,Important; }
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string device, int mode, ref DisplayMode data);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowDisplayAffinity(nint hwnd,uint affinity);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int w,int h,uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern nint CreateWindowEx(uint ex,string cls,
        string name,uint style,int x,int y,int w,int h,nint parent,nint menu,nint instance,nint parameter);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(nint hwnd,nint dest,ref Point where,
        ref Point size,nint source,ref Point from,int key,ref Blend blend,int flags);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] static extern nint CreateDIBSection(nint dc,ref BitmapInfo info,int usage,out nint pixels,nint section,uint offset);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint dc,nint bitmap);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint hwnd);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(nint dc);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern nint CreateWaitableTimerEx(nint attributes,string? name,uint flags,uint access);
    [DllImport("kernel32.dll")] static extern bool SetWaitableTimer(nint timer,ref long due,int period,nint callback,nint parameter,bool resume);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(nint handle,int timeout);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);

    public static int RefreshRate(string device)
    {
        var mode = new DisplayMode { Size = 220 };
        return EnumDisplaySettings(device, -1, ref mode) ? mode.Frequency : 0;
    }

    public static nint Window(uint style, Rectangle rect, string name)
    {
        var hwnd = CreateWindowEx(style, "Static", name, 0x80000000, rect.X, rect.Y, rect.Width, rect.Height, 0, 0, 0, 0);
        if (hwnd == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx");
        return hwnd;
    }
    public static void Exclude(nint hwnd)
    {
        if (!SetWindowDisplayAffinity(hwnd, 0x11))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowDisplayAffinity");
    }
    public static void Show(nint hwnd, nint after, Rectangle rect)
    {
        if (!SetWindowPos(hwnd, after, rect.X, rect.Y, rect.Width, rect.Height, 0x50))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowPos");
    }

    public sealed class Layer : IDisposable
    {
        readonly nint dc, bitmap, old, pixels;
        readonly Rectangle rect;
        public readonly nint Handle;
        public Layer(Rectangle rect, string label)
        {
            this.rect = rect;
            Handle = Window(0x080800A8, rect, label);
            dc = CreateCompatibleDC(0);
            var info = new BitmapInfo { Size = 40, Width = rect.Width, Height = -rect.Height, Planes = 1, Bits = 32 };
            bitmap = CreateDIBSection(dc, ref info, 0, out pixels, 0, 0);
            if (dc == 0 || bitmap == 0) { Dispose(); throw new InvalidOperationException("Cannot create test source"); }
            old = SelectObject(dc, bitmap);
        }
        public void Paint(int rgb)
        {
            new Span<int>((void*)pixels, rect.Width * rect.Height).Fill(rgb | unchecked((int)0xff000000));
            var position = new Point { X = rect.X, Y = rect.Y };
            var size = new Point { X = rect.Width, Y = rect.Height };
            var zero = new Point();
            var blend = new Blend { Alpha = 255, Format = 1 };
            if (!UpdateLayeredWindow(Handle, 0, ref position, ref size, dc, ref zero, 0, ref blend, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateLayeredWindow");
        }
        public void Show(nint after) => Native.Show(Handle, after, rect);
        public void Dispose()
        {
            if (old != 0) SelectObject(dc, old);
            if (bitmap != 0) DeleteObject(bitmap);
            if (dc != 0) DeleteDC(dc);
            DestroyWindow(Handle);
        }
    }

    public sealed class Timer : IDisposable
    {
        readonly nint timer = CreateWaitableTimerEx(0, null, 2, 0x100002);
        public void Wait(long hundredNs)
        {
            if (timer == 0) { Thread.Sleep(1); return; }
            long due = -hundredNs;
            SetWaitableTimer(timer, ref due, 0, 0, 0, false);
            WaitForSingleObject(timer, 50);
        }
        public void Dispose() { if (timer != 0) CloseHandle(timer); }
    }
}
