using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace LumaDock;

internal static partial class WindowsShell
{
    private static string? recovery;
    private static uint originalTaskbarState;
    private static bool originalTaskbarVisibility;
    private static nint hiddenTaskbar;
    private static bool taskbarAutoHideOnly;
    private static readonly string recoveryDirectory = AppData.Root;
    public static uint TaskbarRestartMessage => RegisterWindowMessage("TaskbarCreated");
    public static bool TaskbarChanged => recovery is not null;

    public static (bool Found, bool Visible, uint State) InspectTaskbar()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        var data = new AppBarData { Size = Marshal.SizeOf<AppBarData>(), Hwnd = taskbar };
        return taskbar == 0 ? (false, false, 0) : (true, IsWindowVisible(taskbar), (uint)SHAppBarMessage(4, ref data));
    }

    public static bool TaskbarOccupiesScreen()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == 0 || !IsWindowVisible(taskbar) || !GetWindowRect(taskbar, out var rect)) return false;
        var bounds = System.Windows.Forms.Screen.FromHandle(taskbar).Bounds;
        var visible = System.Drawing.Rectangle.Intersect(bounds, System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
        return visible.Width > 5 && visible.Height > 5;
    }

    public static bool HideTaskbar(bool autoHideOnly = false)
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == 0) return false;
        if (recovery is null)
        {
            var data = new AppBarData { Size = Marshal.SizeOf<AppBarData>(), Hwnd = taskbar };
            originalTaskbarState = (uint)SHAppBarMessage(4, ref data);
            originalTaskbarVisibility = IsWindowVisible(taskbar);
            var receipt = Path.Combine(recoveryDirectory, $"taskbar-{Environment.ProcessId}.restore");
            try
            {
                Directory.CreateDirectory(recoveryDirectory);
                File.WriteAllText(receipt, originalTaskbarState.ToString(CultureInfo.InvariantCulture) + "|" + originalTaskbarVisibility);
                var start = new ProcessStartInfo(Environment.ProcessPath!)
                {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                };
                start.ArgumentList.Add("--watch-taskbar");
                start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                using var watchdog = System.Diagnostics.Process.Start(start);
                if (watchdog is null)
                {
                    File.Delete(receipt);
                    return false;
                }
                recovery = receipt;
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
            {
                TryDelete(receipt);
                return false;
            }
        }
        if (taskbar != hiddenTaskbar || taskbarAutoHideOnly != autoHideOnly)
        {
            ApplyTaskbarState(taskbar, originalTaskbarState | 1, autoHideOnly);
            hiddenTaskbar = taskbar;
            taskbarAutoHideOnly = autoHideOnly;
        }
        else if (!autoHideOnly && IsWindowVisible(taskbar)) SetTaskbarWindow(taskbar, false);
        return true;
    }

    public static void RestoreTaskbar()
    {
        if (recovery is null) return;
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == 0) return;
        ApplyTaskbarState(taskbar, originalTaskbarState, originalTaskbarVisibility);
        TryDelete(recovery);
        recovery = null;
        hiddenTaskbar = 0;
    }

    public static void WatchTaskbar(int pid)
    {
        try
        {
            using var parent = System.Diagnostics.Process.GetProcessById(pid);
            parent.WaitForExit();
        }
        catch (ArgumentException) { }
        // A new instance may have taken over while this process was waiting.
        using var instance = new System.Threading.Mutex(true, "Local\\FlowBar2.Desktop", out bool first);
        if (first) RestoreTaskbarFromReceipt(pid);
    }

    public static void RestoreTaskbarFromReceipt(int? pid = null)
    {
        if (!Directory.Exists(recoveryDirectory)) return;
        var receipts = pid.HasValue ? new[] { Path.Combine(recoveryDirectory, $"taskbar-{pid.Value}.restore") }
            : Directory.GetFiles(recoveryDirectory, "taskbar-*.restore");
        foreach (string receipt in receipts)
        {
            var taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar == 0 || !File.Exists(receipt)) continue;
            try
            {
                var record = File.ReadAllText(receipt).Split('|');
                bool visible = record.Length < 2 || !bool.TryParse(record[1], out var prior) || prior;
                if (uint.TryParse(record[0], out var state)) ApplyTaskbarState(taskbar, state & 3, visible);
                TryDelete(receipt);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        if (pid is null)
        {
            var taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != 0) SetTaskbarWindow(taskbar, true);
        }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void ApplyTaskbarState(nint taskbar, uint state, bool visible)
    {
        var data = new AppBarData { Size = Marshal.SizeOf<AppBarData>(), Hwnd = taskbar, Param = (nint)state };
        SHAppBarMessage(10, ref data);
        SetTaskbarWindow(taskbar, visible);
    }

    private static void SetTaskbarWindow(nint taskbar, bool visible)
    {
        ShowWindowAsync(taskbar, visible ? 5 : 0);
        const uint unchanged = 0x4000 | 0x10 | 0x04 | 0x02 | 0x01;
        SetWindowPos(taskbar, 0, 0, 0, 0, 0, unchanged | (visible ? 0x40u : 0x80u));
    }

    public static bool IsDesktopActive()
    {
        nint window = GetForegroundWindow();
        if (window == 0) return true;
        var name = new StringBuilder(128);
        GetClassName(window, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW";
    }

    public static (string Name, nint Handle)? ForegroundApp()
    {
        var window = GetForegroundWindow();
        if (window == 0 || IsDesktopActive()) return null;
        GetWindowThreadProcessId(window, out var pid);
        if (pid == Environment.ProcessId) return null;
        var path = ProcessPath(pid);
        var name = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(name) ? null : (name, window);
    }

    public static TimeSpan IdleTime()
    {
        var last = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        return GetLastInputInfo(ref last) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - last.Ticks)) : TimeSpan.Zero;
    }

    public static bool? StartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue("FlowBar") is string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { return null; }
    }

    public static bool Startup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key is null) return false;
            key.DeleteValue("LumaDock", false); // запись прежнего названия
            if (enabled) key.SetValue("FlowBar", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("FlowBar", false);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    public static string InputLanguage()
    {
        var thread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        int lang = (int)(GetKeyboardLayout(thread).ToInt64() & 0xffff);
        try { return CultureInfo.GetCultureInfo(lang).TwoLetterISOLanguageName.ToUpperInvariant(); }
        catch (CultureNotFoundException) { return "—"; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size, Ticks; }
    [DllImport("user32")] private static extern bool GetLastInputInfo(ref LastInput info);
    [DllImport("user32")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
}
