using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static LumaDock.Visuals;

namespace LumaDock;

internal sealed partial class DockWindow
{
    private void TraySurface(Window window, Border shell)
    {
        var main = new DockPanel { Margin = new(12) };
        var heading = (FrameworkElement)Right(Text("System tray", 16, true), CloseButton(window));
        heading.Margin = new(2, 0, 1, 7);
        DockPanel.SetDock(heading, Dock.Top);
        main.Children.Add(heading);
        var status = Text("", 12, false, Muted);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new(2, 3, 2, 8);
        var icons = Stack();
        var refresh = Button("Refresh", () => { });
        var overflow = Button("Hidden icons", () => { });
        overflow.Visibility = Visibility.Collapsed;
        var native = Button("Windows tray", () => WindowsShell.Key(0x5B, 0x42));
        var actions = new WrapPanel();
        foreach (Button action in new[] { refresh, overflow, native })
        {
            action.Margin = new(0, 0, 5, 0);
            actions.Children.Add(action);
        }
        var footer = Stack(status, actions);
        DockPanel.SetDock(footer, Dock.Bottom);
        main.Children.Add(footer);
        main.Children.Add(Scroll(icons));
        shell.Child = main;

        WindowsShell.TraySnapshot? snapshot = null;
        bool busy = false, usingNativeTray = false;
        refresh.Click += (_, _) => _ = Refresh();
        overflow.Click += (_, _) => _ = ShowOverflow();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            if (window.IsActive && !icons.IsMouseOver && !icons.IsKeyboardFocusWithin) _ = Refresh();
        };
        window.Loaded += (_, _) =>
        {
            if (smoke)
            {
                AddIcons([new("Sample tray icon", "sample-1", 0, null, null), new("Example sync", "sample-2", 0, null, null)]);
                return;
            }
            status.Text = L10n.T("Reading system tray…");
            _ = Refresh();
            timer.Start();
        };
        window.Closed += (_, _) => timer.Stop();
        if (!smoke) window.Deactivated += (_, _) => window.Dispatcher.BeginInvoke(() =>
        {
            if (!usingNativeTray && window.IsLoaded && !window.IsActive) window.Close();
        });

        // Значки трея с панели задач Windows; если её не видно (её скрывает FlowBar), запасной список запущенных программ со значком в трее
        static WindowsShell.TraySnapshot ReadAny()
        {
            var read = WindowsShell.ReadTray();
            if (read.Pinned.Length + read.Overflow.Length > 0) return read;
            var apps = WindowsShell.TrayApps();
            return apps.Length > 0 ? new(apps, [], null, false, false) : read;
        }

        async Task Refresh()
        {
            if (busy || smoke) return;
            busy = true;
            try { Paint(await Task.Run(ReadAny)); }
            finally { busy = false; }
        }

        async Task ShowOverflow()
        {
            if (busy || smoke || snapshot?.Chevron is not { } button) return;
            busy = usingNativeTray = true;
            try
            {
                if (await Task.Run(() => WindowsShell.InvokeTray(button)))
                {
                    for (int attempt = 0; attempt < 5 && window.IsLoaded; attempt++)
                    {
                        await Task.Delay(120);
                        var latest = await Task.Run(WindowsShell.ReadTray);
                        if (!latest.OverflowOpen) continue;
                        Paint(latest);
                        return;
                    }
                }
                if (window.IsLoaded) status.Text = L10n.T("Open hidden icons from the Windows taskbar, then refresh.");
            }
            finally { busy = usingNativeTray = false; }
        }

        async Task Run(WindowsShell.TrayItem item, bool menu)
        {
            if (busy || smoke) return;
            busy = usingNativeTray = true;
            try
            {
                bool submitted = item.Open is { } open ? await Task.Run(() => { open(); return true; }) : await Task.Run(() => WindowsShell.InvokeTray(item, menu));
                if (!window.IsLoaded) return;
                if (submitted) window.Close();
                else status.Text = L10n.T(menu ? "Windows could not focus this icon. Use its menu in the Windows tray."
                    : "This icon is unavailable. Refresh or open it from the Windows tray.");
            }
            finally { busy = usingNativeTray = false; }
        }

        void Paint(WindowsShell.TraySnapshot current)
        {
            if (!window.IsLoaded) return;
            snapshot = current;
            overflow.Visibility = current.Chevron is null ? Visibility.Collapsed : Visibility.Visible;
            overflow.IsEnabled = !current.OverflowOpen;
            if (icons.IsKeyboardFocusWithin) return;
            icons.Children.Clear();
            AddIcons(current.Pinned);
            if (current.Overflow.Length > 0)
            {
                var label = Text("Hidden icons", 12, false, Muted);
                label.Margin = new(2, 7, 0, 3);
                icons.Children.Add(label);
                AddIcons(current.Overflow);
            }
            bool listOfApps = current.Pinned.Length > 0 && current.Pinned[0].Open is not null;
            status.Text = listOfApps ? L10n.T("The Windows taskbar is hidden, so these are running apps with a tray icon. A click opens the app.")
                : current.Pinned.Length + current.Overflow.Length > 0 ? "" : current.HasVisibleTray
                ? L10n.T("Windows did not expose tray icons.") : L10n.T("Show the Windows taskbar to read its tray icons.");
        }

        void AddIcons(WindowsShell.TrayItem[] entries)
        {
            foreach (var group in entries.Chunk(7))
            {
                var row = RowStack();
                row.HorizontalAlignment = HorizontalAlignment.Center;
                foreach (var item in group)
                {
                    FrameworkElement visual;
                    if (item.Icon is { } icon)
                    {
                        var picture = new Image { Source = icon, Stretch = Stretch.Uniform, Width = 24, Height = 24 };
                        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
                        visual = picture;
                    }
                    else
                    {
                        var letter = Text(StringInfo.GetNextTextElement(item.Name).ToUpperInvariant(), 13, true, Paper);
                        letter.HorizontalAlignment = HorizontalAlignment.Center;
                        visual = Card(letter, 5, Row);
                        visual.Width = visual.Height = 22;
                    }
                    var entry = Button(visual, () => _ = Run(item, false), Brushes.Transparent, 11);
                    entry.Width = entry.Height = 43;
                    entry.Padding = new(0);
                    entry.ToolTip = item.Name;
                    AutomationProperties.SetName(entry, item.Name);
                    entry.PreviewMouseRightButtonUp += (_, e) => { e.Handled = true; _ = Run(item, true); };
                    entry.PreviewKeyDown += (_, e) =>
                    {
                        if (e.Key != Key.Apps && (e.Key != Key.F10 || (Keyboard.Modifiers & ModifierKeys.Shift) == 0)) return;
                        e.Handled = true;
                        _ = Run(item, true);
                    };
                    row.Children.Add(entry);
                }
                icons.Children.Add(row);
            }
        }
    }
}
