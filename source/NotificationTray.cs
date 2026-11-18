using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace LumaDock;

internal static partial class WindowsShell
{
    internal record TrayItem(string Name, string Id, nint Window, AutomationElement? Element, BitmapSource? Icon);
    internal record TraySnapshot(TrayItem[] Pinned, TrayItem[] Overflow, TrayItem? Chevron, bool HasVisibleTray, bool OverflowOpen);

    private static readonly System.Windows.Automation.Condition TrayControls = new OrCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));

    public static TraySnapshot ReadTray()
    {
        var taskbars = new List<nint>();
        var overflows = new List<nint>();
        nint primary = FindWindow("Shell_TrayWnd", null);
        if (primary == 0) return new([], [], null, false, false);
        GetWindowThreadProcessId(primary, out uint shell);
        if (shell == 0) return new([], [], null, false, false);
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            GetWindowThreadProcessId(window, out uint process);
            if (process != shell) return true;
            var name = new StringBuilder(96);
            GetClassName(window, name, name.Capacity);
            switch (name.ToString())
            {
                case "Shell_TrayWnd": taskbars.Insert(0, window); break;
                case "Shell_SecondaryTrayWnd": taskbars.Add(window); break;
                case "NotifyIconOverflowWindow":
                case "TopLevelWindowForOverflowXamlIsland": overflows.Add(window); break;
            }
            return true;
        }, 0);

        var pinned = new List<TrayItem>();
        var overflow = new List<TrayItem>();
        TrayItem? chevron = null;
        foreach (nint window in taskbars.Concat(overflows))
        {
            bool hidden = overflows.Contains(window);
            try
            {
                var root = AutomationElement.FromHandle(window);
                foreach (AutomationElement element in root.FindAll(TreeScope.Descendants, TrayControls))
                {
                    try
                    {
                        var info = element.Current;
                        string label = info.Name.Trim();
                        if (label.Length == 0 || info.IsOffscreen || !info.IsEnabled) continue;
                        string kind = info.ClassName;
                        string id = info.AutomationId;
                        bool toggle = IsOverflowButton(kind, id, label);
                        if (!toggle && !IsTrayIcon(element, kind, id, hidden)) continue;
                        var item = new TrayItem(label, window + ":" + string.Join(".", element.GetRuntimeId()), window,
                            element, toggle ? null : CaptureTrayIcon(window, info.BoundingRectangle));
                        if (toggle)
                        {
                            if (!hidden) chevron ??= item;
                        }
                        else (hidden ? overflow : pinned).Add(item);
                    }
                    catch (Exception ex) when (TrayUnavailable(ex)) { }
                }
            }
            catch (Exception ex) when (TrayUnavailable(ex)) { }
        }
        return new(pinned.DistinctBy(item => item.Id).ToArray(), overflow.DistinctBy(item => item.Id).ToArray(),
            chevron, taskbars.Count > 0, overflows.Count > 0);
    }

    internal static bool IsOverflowButton(string kind, string id, string label) =>
        id is "NotificationChevron" or "OverflowChevron" ||
        kind is "SystemTray.ChevronIconView" or "SystemTray.OverflowChevron" ||
        (id == "SystemTrayIcon" || kind.StartsWith("SystemTray.", StringComparison.Ordinal)) &&
        (label.StartsWith("Show hidden icons", StringComparison.OrdinalIgnoreCase) ||
         label.StartsWith("Показать скрытые значки", StringComparison.OrdinalIgnoreCase));

    private static bool IsTrayIcon(AutomationElement element, string kind, string id, bool overflow)
    {
        if (kind.Contains("NotifyIcon", StringComparison.OrdinalIgnoreCase)) return true;
        if (overflow && id == "SystemTrayIcon") return true;
        var walker = TreeWalker.RawViewWalker;
        var parent = element;
        for (int depth = 0; depth < 8; depth++)
        {
            parent = walker.GetParent(parent);
            if (parent is null) return false;
            string name = parent.Current.ClassName;
            if (name is "TrayNotifyWnd" or "NotifyIconOverflowWindow") return true;
            if (name.Contains("NotifyIcon", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("NotificationArea", StringComparison.OrdinalIgnoreCase)) return true;
            if (name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") break;
        }
        return false;
    }

    public static bool InvokeTray(TrayItem item, bool contextMenu = false)
    {
        try
        {
            if (item.Element is not { } element) return false;
            if (!IsWindowVisible(item.Window) || element.Current.IsOffscreen || !element.Current.IsEnabled) return false;
            if (contextMenu)
            {
                element.SetFocus();
                if (!element.Current.HasKeyboardFocus || TrayRoot(GetForegroundWindow(), 2) != item.Window) return false;
                Key(0x10, 0x79);
                return true;
            }
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke)) ((InvokePattern)invoke).Invoke();
            else if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expansion))
                ((ExpandCollapsePattern)expansion).Expand();
            else if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle)) ((TogglePattern)toggle).Toggle();
            else return false;
            return true;
        }
        catch (Exception ex) when (TrayUnavailable(ex)) { return false; }
    }

    private static BitmapSource? CaptureTrayIcon(nint owner, Rect bounds)
    {
        if (bounds.IsEmpty || !double.IsFinite(bounds.X + bounds.Y + bounds.Width + bounds.Height) ||
            bounds.Width is < 8 or > 360 || bounds.Height is < 8 or > 180) return null;
        uint dpi = GetDpiForWindow(owner);
        int size = Math.Min((int)Math.Min(bounds.Width, bounds.Height), (int)Math.Round(20 * Math.Clamp(dpi, 96u, 288u) / 96d));
        int x = (int)Math.Round(bounds.X + (bounds.Width - size) / 2);
        int y = (int)Math.Round(bounds.Y + (bounds.Height - size) / 2);
        bool IsOnTray(int px, int py) => TrayRoot(TrayWindowAt(new TrayPoint(px, py)), 2) == owner;
        if (!IsOnTray(x, y) || !IsOnTray(x + size - 1, y) || !IsOnTray(x, y + size - 1) ||
            !IsOnTray(x + size - 1, y + size - 1) || !IsOnTray(x + size / 2, y + size / 2)) return null;
        try
        {
            using var bitmap = new System.Drawing.Bitmap(size, size);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(x, y, 0, 0, bitmap.Size);
            if (!IsOnTray(x + size / 2, y + size / 2)) return null;
            nint handle = bitmap.GetHbitmap();
            try
            {
                var image = Imaging.CreateBitmapSourceFromHBitmap(handle, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                return image;
            }
            finally { DeleteTrayBitmap(handle); }
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException) { return null; }
    }

    private static bool TrayUnavailable(Exception ex) => ex is ElementNotAvailableException or COMException
        or InvalidOperationException or UnauthorizedAccessException or ArgumentException;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct TrayPoint(int x, int y)
    {
        public readonly int X = x;
        public readonly int Y = y;
    }

    [DllImport("user32", EntryPoint = "WindowFromPoint")] private static extern nint TrayWindowAt(TrayPoint point);
    [DllImport("user32", EntryPoint = "GetAncestor")] private static extern nint TrayRoot(nint window, uint flag);
    [DllImport("user32")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("gdi32", EntryPoint = "DeleteObject")] private static extern bool DeleteTrayBitmap(nint bitmap);
}
