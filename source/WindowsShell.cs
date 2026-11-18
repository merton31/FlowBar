using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LumaDock;

internal static partial class WindowsShell
{
    public record WindowInfo(nint Handle, string Title, string App, string Name);
    public record struct Usage(int Cpu, int Ram, int Battery, bool HasBattery);
    private static ulong lastIdle, lastTotal;
    private static readonly Dictionary<string, string> ShortcutTargets = new(StringComparer.OrdinalIgnoreCase);

    public static List<WindowInfo> Windows()
    {
        var list = new List<WindowInfo>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || GetWindow(window, 4) != 0) return true;
            var style = GetWindowLongPtr(window, -20).ToInt64();
            if ((style & 0x80) != 0 || IsCloaked(window)) return true;
            var title = new StringBuilder(512);
            if (GetWindowText(window, title, title.Capacity) == 0) return true;
            GetWindowThreadProcessId(window, out uint pid);
            if (pid == Environment.ProcessId) return true;
            using var process = Process.GetProcessByIdSafe(pid);
            if (process is null) return true;
            var path = ProcessPath(pid);
            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name)) name = process.ProcessName;
            list.Add(new(window, title.ToString(), string.IsNullOrEmpty(path) ? name + ".exe" : path, name));
            return true;
        }, 0);
        return list;
    }

    // Системные и собственные процессы, которые нельзя завершать кнопкой «End task»
    private static readonly string[] ProtectedProcesses =
        ["explorer", "csrss", "winlogon", "wininit", "services", "lsass", "dwm", "smss", "system", "svchost", "fontdrvhost", "sihost", "taskhostw", "flowbar", "lumadock", "applicationframehost"];

    internal static bool IsProtected(string path) =>
        ProtectedProcesses.Contains(Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase);

    // Завершает процесс окна. Перед этим проверяется, что процесс с этим номером всё ещё запущен из ожидаемой программы
    // (номер мог достаться другой программе) и что это не системный и не наш процесс
    // Завершает процессы окон программы по одному разу (у нескольких окон один процесс); окно, которое уже исчезло, не считается ошибкой
    internal static bool EndTasks(IEnumerable<nint> windows, string expectedPath)
    {
        var processes = new HashSet<uint>();
        foreach (var window in windows)
        {
            GetWindowThreadProcessId(window, out uint pid);
            if (pid != 0) processes.Add(pid);
        }
        bool ended = processes.Count > 0;
        foreach (uint pid in processes) ended &= EndProcess(pid, expectedPath);
        return ended;
    }

    // Путь проверяется и процесс завершается на одном и том же дескрипторе, поэтому номер процесса не может достаться другой программе между проверкой и завершением
    internal static bool EndProcess(uint pid, string expectedPath)
    {
        if (pid == 0 || pid == Environment.ProcessId || IsProtected(expectedPath)) return false;
        var handle = OpenProcess(0x1001, false, pid); // PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION
        if (handle == 0) return false;
        try
        {
            string actual = ImagePath(handle);
            if (actual.Length == 0 || IsProtected(actual)) return false;
            if (!string.Equals(actual, ResolveApp(expectedPath), StringComparison.OrdinalIgnoreCase) && !string.Equals(actual, expectedPath, StringComparison.OrdinalIgnoreCase)) return false;
            return TerminateProcess(handle, 1);
        }
        finally { CloseHandle(handle); }
    }

    // Путь программы, которой принадлежит окно (для поиска значка, когда окно мигает)
    internal static string WindowApp(nint window)
    {
        GetWindowThreadProcessId(window, out uint pid);
        return pid == 0 ? "" : ProcessPath(pid);
    }

    // Сообщение оболочки о том, что окно мигает в панели задач (HSHELL_FLASH): для него окно дока подписывается на сообщения оболочки
    internal const int ShellFlash = 0x8006;
    internal static readonly uint ShellHookMessage = RegisterWindowMessage("SHELLHOOK");
    internal static bool RegisterShellHook(nint window) => RegisterShellHookWindow(window);
    internal static void UnregisterShellHook(nint window) => DeregisterShellHookWindow(window);

    private static class Process
    {
        public static System.Diagnostics.Process? GetProcessByIdSafe(uint pid)
        {
            try { return System.Diagnostics.Process.GetProcessById((int)pid); }
            catch (ArgumentException) { return null; }
        }
    }

    private static string ProcessPath(uint pid)
    {
        var handle = OpenProcess(0x1000, false, pid);
        if (handle == 0) return "";
        try { return ImagePath(handle); }
        finally { CloseHandle(handle); }
    }

    private static string ImagePath(nint handle)
    {
        var path = new StringBuilder(2048);
        var size = path.Capacity;
        return QueryFullProcessImageName(handle, 0, path, ref size) ? path.ToString() : "";
    }

    public static string ResolveApp(string path) => path.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path)
        : path;

    public static void PreventActivation(nint window) =>
        SetWindowLongPtr(window, -20, new nint(GetWindowLongPtr(window, -20).ToInt64() | 0x08000000));

    public static List<string> StartMenuShortcuts()
    {
        var items = new List<string>();
        foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
        {
            var root = Environment.GetFolderPath(folder);
            if (Directory.Exists(root)) items.AddRange(Directory.EnumerateFiles(root, "*.lnk",
                new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }));
        }
        return items;
    }

    public static string MatchApp(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return ResolveApp(path);
        if (ShortcutTargets.TryGetValue(path, out var result)) return result;
        object? automation = null, link = null;
        result = path;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is not null)
            {
                automation = Activator.CreateInstance(type);
                link = ((dynamic)automation!).CreateShortcut(path);
                string target = ((dynamic)link).TargetPath;
                if (Path.IsPathFullyQualified(target) && File.Exists(target)) result = target;
            }
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
        finally
        {
            if (link is not null && Marshal.IsComObject(link)) Marshal.ReleaseComObject(link);
            if (automation is not null && Marshal.IsComObject(automation)) Marshal.ReleaseComObject(automation);
        }
        return ShortcutTargets[path] = result;
    }

    public static bool Launch(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo(ResolveApp(path)) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        { return false; }
    }

    public static ImageSource? Icon(string app, nint window = default)
    {
        if (window != 0)
        {
            SendMessageTimeout(window, 0x7F, 1, 0, 2, 50, out var icon);
            if (icon == 0) icon = GetClassLongPtr(window, -14);
            if (icon != 0) return MakeIcon(icon);
        }
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(MatchApp(app));
            return icon is null ? null : MakeIcon(icon.Handle);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or System.ComponentModel.Win32Exception)
        { return null; }
    }

    private static ImageSource MakeIcon(nint icon)
    {
        var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        bitmap.Freeze();
        return bitmap;
    }

    public static bool IsFullscreen(Rect screenPixels)
    {
        var handle = GetForegroundWindow();
        if (handle == 0 || !GetWindowRect(handle, out var rect)) return false;
        GetWindowThreadProcessId(handle, out var pid);
        if (pid == Environment.ProcessId) return false;
        var className = new StringBuilder(128);
        GetClassName(handle, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        DwmGetWindowAttribute(handle, 9, out NativeRect frame, Marshal.SizeOf<NativeRect>());
        if (frame.Right > frame.Left) rect = frame;
        return rect.Left <= screenPixels.Left && rect.Top <= screenPixels.Top
            && rect.Right >= screenPixels.Right && rect.Bottom >= screenPixels.Bottom;
    }

    public static void Activate(nint handle, bool toggle = false)
    {
        if (toggle && handle == GetForegroundWindow() && !IsIconic(handle)) ShowWindowAsync(handle, 6);
        else
        {
            if (IsIconic(handle)) ShowWindowAsync(handle, 9);
            SetForegroundWindow(handle);
        }
    }

    public static void ActivateNext(IReadOnlyList<WindowInfo> windows)
    {
        nint current = GetForegroundWindow();
        int next = 0;
        for (int index = 0; index < windows.Count; index++)
            if (windows[index].Handle == current) { next = (index + 1) % windows.Count; break; }
        Activate(windows[next].Handle);
    }

    public static void CloseWindow(nint handle) => PostMessage(handle, 0x10, 0, 0);
    public static void Key(params byte[] keys)
    {
        foreach (byte key in keys) keybd_event(key, 0, 0, 0);
        for (var i = keys.Length - 1; i >= 0; i--) keybd_event(keys[i], 0, 2, 0);
    }

    public static void ToggleTheme()
        => SetTheme(!IsLightTheme());

    public static bool IsLightTheme()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }

    public static void SetTheme(bool light)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", true);
        if (key is null) return;
        var value = light ? 1 : 0;
        key.SetValue("AppsUseLightTheme", value);
        key.SetValue("SystemUsesLightTheme", value);
        SendMessageTimeoutSetting(0xffff, 0x1A, 0, "ImmersiveColorSet", 2, 250, out _);
    }

    public static Rect? Reserve(nint window, Rect screen, int height, bool register)
    {
        var appbar = new AppBarData { Size = Marshal.SizeOf<AppBarData>(), Hwnd = window, Callback = 0x8066, Edge = 3 };
        if (register && SHAppBarMessage(0, ref appbar) == 0) return null;
        appbar.Rect = new NativeRect { Left = (int)screen.Left, Top = (int)screen.Bottom - height,
            Right = (int)screen.Right, Bottom = (int)screen.Bottom };
        SHAppBarMessage(2, ref appbar);
        appbar.Rect.Top = appbar.Rect.Bottom - height;
        if (SHAppBarMessage(3, ref appbar) == 0)
        {
            if (register) Release(window);
            return null;
        }
        return new Rect(appbar.Rect.Left, appbar.Rect.Top, Math.Max(0, appbar.Rect.Right - appbar.Rect.Left),
            Math.Max(0, appbar.Rect.Bottom - appbar.Rect.Top));
    }

    public static void Release(nint window)
    {
        var appbar = new AppBarData { Size = Marshal.SizeOf<AppBarData>(), Hwnd = window };
        SHAppBarMessage(1, ref appbar);
    }

    public static Usage ReadUsage()
    {
        GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
        var total = kernel + user;
        var cpu = lastTotal == 0 || lastTotal == total ? 0
            : Math.Clamp((int)Math.Round(100.0 * (1 - (double)(idle - lastIdle) / (total - lastTotal))), 0, 100);
        lastIdle = idle;
        lastTotal = total;
        var memory = new MemoryInfo { Size = (uint)Marshal.SizeOf<MemoryInfo>() };
        GlobalMemoryStatusEx(ref memory);
        GetSystemPowerStatus(out var power);
        return new(cpu, (int)memory.Load, power.Percent, power.Percent <= 100 && (power.Flags & 128) == 0);
    }

    public static (double Volume, bool Muted)? Audio(double? volume = null, bool toggleMute = false, bool capture = false)
    {
        object? manager = null;
        IMMDevice? device = null;
        object? endpoint = null;
        try
        {
            manager = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!);
            ((IMMDeviceEnumerator)manager!).DefaultEndpoint(capture ? 1 : 0, capture ? 2 : 1, out device);
            Guid iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 23, 0, out endpoint);
            var audio = (IAudioEndpointVolume)endpoint;
            if (volume.HasValue) audio.SetVolume((float)volume.Value, Guid.Empty);
            audio.GetMute(out bool muted);
            if (toggleMute) audio.SetMute(muted = !muted, Guid.Empty);
            audio.GetVolume(out float value);
            return (value, muted);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException) { return null; }
        finally
        {
            if (endpoint is not null) Marshal.ReleaseComObject(endpoint);
            if (device is not null) Marshal.ReleaseComObject(device);
            if (manager is not null) Marshal.ReleaseComObject(manager);
        }
    }

    // Живая миниатюра окна внутри окна-хозяина: вписывается в прямоугольник с сохранением пропорций окна и стоит по центру; координаты в пикселях
    public static nint Thumbnail(nint host, nint source, int left, int top, int width, int height)
    {
        if (DwmRegisterThumbnail(host, source, out nint thumbnail) != 0) return 0;
        int x = left, y = top, w = width, h = height;
        if (DwmQueryThumbnailSourceSize(thumbnail, out var size) == 0 && size.Width > 0 && size.Height > 0)
        {
            double scale = Math.Min((double)width / size.Width, (double)height / size.Height);
            w = Math.Max(1, (int)(size.Width * scale));
            h = Math.Max(1, (int)(size.Height * scale));
            x = left + (width - w) / 2;
            y = top + (height - h) / 2;
        }
        var properties = new ThumbnailProperties { Flags = 1 | 4 | 8 | 16, Visible = true, Opacity = 255,
            Destination = new NativeRect { Left = x, Top = y, Right = x + w, Bottom = y + h } };
        DwmUpdateThumbnailProperties(thumbnail, ref properties);
        return thumbnail;
    }

    // Скруглённые углы окна (Windows 11); на Windows 10 вызов ничего не меняет
    public static void RoundCorners(nint window)
    {
        int round = 2;
        DwmSetWindowAttribute(window, 33, ref round, sizeof(int));
    }

    public static string? Wallpaper()
    {
        var path = new StringBuilder(1024);
        return SystemParametersInfo(0x73, (uint)path.Capacity, path, 0) && File.Exists(path.ToString())
            ? path.ToString() : null;
    }

    private static bool IsCloaked(nint window) =>
        DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        void EnumEndpoints(int flow, uint state, out IEndpointCollection devices);
        void DefaultEndpoint(int flow, int role, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid iid, int context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        void OpenPropertyStore(uint mode, out IEndpointProperties properties);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        void Register();
        void Unregister();
        void ChannelCount();
        void SetVolumeDb();
        void SetVolume(float value, in Guid context);
        void GetVolumeDb();
        void GetVolume(out float value);
        void SetChannelVolumeDb();
        void SetChannelVolume();
        void GetChannelVolumeDb();
        void GetChannelVolume();
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool value, in Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool value);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct AppBarData
    { public int Size; public nint Hwnd; public int Callback, Edge; public NativeRect Rect; public nint Param; }
    [StructLayout(LayoutKind.Sequential)] private struct ThumbnailProperties
    {
        public uint Flags; public NativeRect Destination, Source; public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible, SourceClientOnly;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInfo
    { public uint Size, Load; public ulong Total, Available, Page, PageAvailable, Virtual, VirtualAvailable, Extended; }
    [StructLayout(LayoutKind.Sequential)] private struct PowerInfo
    { public byte AC, Flags, Percent, Reserved; public uint Lifetime, FullLifetime; }
    private delegate bool WindowEnumerator(nint window, nint param);
    [DllImport("user32")] private static extern bool EnumWindows(WindowEnumerator callback, nint param);
    [DllImport("user32")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32")] private static extern bool IsIconic(nint window);
    [DllImport("user32")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32", EntryPoint = "GetClassLongPtrW")] private static extern nint GetClassLongPtr(nint window, int index);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder title, int size);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder title, int size);
    [DllImport("user32")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32")] private static extern bool RegisterShellHookWindow(nint window);
    [DllImport("user32")] private static extern bool DeregisterShellHookWindow(nint window);
    [DllImport("user32")] private static extern nint GetForegroundWindow();
    [DllImport("user32")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32")] private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string className, string? title);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern bool PostMessage(nint window, uint msg, nint wParam, nint lParam);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
    [DllImport("user32", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)] private static extern nint SendMessageTimeoutSetting(nint window, uint message, nint wParam, string lParam, uint flags, uint timeout, out nint result);
    [DllImport("user32")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    [DllImport("kernel32")] private static extern nint OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32")] private static extern bool TerminateProcess(nint handle, uint exitCode);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(nint handle, int flags, StringBuilder path, ref int size);
    [DllImport("kernel32")] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32")] private static extern bool GlobalMemoryStatusEx(ref MemoryInfo info);
    [DllImport("kernel32")] private static extern bool GetSystemPowerStatus(out PowerInfo info);
    [DllImport("shell32")] private static extern nuint SHAppBarMessage(uint message, ref AppBarData data);
    [DllImport("dwmapi")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out int value, int size);
    [DllImport("dwmapi")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out NativeRect value, int size);
    [DllImport("dwmapi")] private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi")] private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref ThumbnailProperties properties);
    [DllImport("dwmapi")] public static extern int DwmUnregisterThumbnail(nint thumbnail);
    [StructLayout(LayoutKind.Sequential)] private struct ThumbnailSize { public int Width, Height; }
    [DllImport("dwmapi")] private static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out ThumbnailSize size);
    [DllImport("dwmapi")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern bool SystemParametersInfo(uint action, uint param, StringBuilder value, uint flags);
}
