using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace LumaDock;

internal static partial class Program
{
    // Реальные маршруты WPF: пункт меню и FileDrop с настоящим ярлыком Windows.
    // Профиль отдельный, пользовательские закрепления и окна не меняются.
    private static async System.Threading.Tasks.Task VerifyAppPinning(string folder)
    {
        string dir = Path.Combine(folder, "pinning");
        Directory.CreateDirectory(dir);
        string app = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        string id = "app:" + app, shortcut = Path.Combine(dir, "Editor.lnk");
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object? link = null;
        try
        {
            link = ((dynamic)shell).CreateShortcut(shortcut);
            ((dynamic)link).TargetPath = app;
            ((dynamic)link).Save();
        }
        finally
        {
            if (link is not null) Marshal.FinalReleaseComObject(link);
            Marshal.FinalReleaseComObject(shell);
        }
        var model = new DockModel(Path.Combine(dir, "profile.json"));
        model.Settings.Items = ["Start", "Search", "Clock"];
        model.Settings.Locked = false;
        model.Save(false);
        var dock = new DockWindow(model, true);
        dock.Show();
        try
        {
            var running = new List<WindowsShell.WindowInfo> { new(0, "Editor", app, "notepad") };
            ContextMenu Menu(List<WindowsShell.WindowInfo> windows)
            {
                var menu = Visuals.DockMenu();
                dock.FillAppMenu(menu, id, app, windows, null, []);
                return menu;
            }
            static MenuItem Action(ContextMenu menu, string text) => menu.Items.OfType<MenuItem>()
                .Single(item => Equals(item.Header, L10n.T(text)));
            static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            // Берём меню самой плитки, причём окно находится на другом мониторе.
            var tile = (Border)typeof(DockWindow).GetMethod("AppTile", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(dock, [id, new List<WindowsShell.WindowInfo>(), running, 58d, 36d])!;
            var openMenu = tile.ContextMenu!;
            Action(openMenu, "Close window");
            openMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute;
            openMenu.HorizontalOffset = 140;
            openMenu.VerticalOffset = 60;
            openMenu.IsOpen = true;
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            await System.Threading.Tasks.Task.Delay(120);
            CaptureElement(openMenu, Path.Combine(dir, "pin-open-app.png"));
            Click(Action(openMenu, "Pin to dock"));
            openMenu.IsOpen = false;
            if (!model.IsAppPinned(id) || model.Settings.Items.IndexOf(id) != 2)
                throw new InvalidOperationException("Pinning an open app through its context menu failed.");
            if (!new DockModel(model.Profile).IsAppPinned(id))
                throw new InvalidOperationException("The pin did not survive a profile reload.");
            // Закрытие приложения: меню получает пустой список окон, но закрепление остаётся.
            var closedMenu = Menu([]);
            Action(closedMenu, "Unpin from dock");
            if (closedMenu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, L10n.T("Close window"))))
                throw new InvalidOperationException("A closed app still offers closing a window.");

            void Drop(string path)
            {
                var data = new DataObject(DataFormats.FileDrop, new[] { path });
                var drop = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                    [data, DragDropKeyStates.None, DragDropEffects.Link, dock, new Point()], null)!;
                drop.RoutedEvent = DragDrop.DropEvent;
                dock.RaiseEvent(drop);
            }
            Drop(shortcut);
            Drop(app.ToUpperInvariant());
            if (model.Settings.Items.Count(item => item.StartsWith("app:", StringComparison.Ordinal)) != 1)
                throw new InvalidOperationException("Dropping a shortcut or different path case created a duplicate pin.");
            // Старый профиль с exe и ярлыком: открепление из меню удаляет обе записи.
            model.Settings.Items.Add("app:" + shortcut);
            Click(Action(Menu(running), "Unpin from dock"));
            if (model.IsAppPinned(id) || new DockModel(model.Profile).IsAppPinned(id))
                throw new InvalidOperationException("Unpin did not remove aliases or did not persist.");
            model.ImportWindowsPins([id]);
            if (model.IsAppPinned(id)) throw new InvalidOperationException("Windows import restored an explicitly unpinned app.");
            Drop(shortcut);
            if (!model.IsAppPinned(id) || !model.Settings.Items.Contains("app:" + shortcut))
                throw new InvalidOperationException("A dropped shortcut was not kept as the launch target.");
            Click(Action(Menu(running), "Unpin from dock"));
            if (model.IsAppPinned(id)) throw new InvalidOperationException("A shortcut pin cannot be unpinned using its running exe.");
            model.Settings.Locked = true;
            Drop(shortcut);
            if (model.IsAppPinned(id)) throw new InvalidOperationException("A locked panel accepted a dropped pin.");
            model.Settings.Locked = false;
            // Дополнительный монитор сохраняет свою раскладку, не меняя основную.
            var monitor = model.SetupFor("Pinning test display");
            monitor.Widgets = "own";
            monitor.Items = ["Start", "Clock"];
            model.SetAppPinned(id, true, monitor.Items);
            var reloaded = new DockModel(model.Profile);
            if (model.IsAppPinned(id) || !reloaded.IsAppPinned(id, reloaded.SetupFor("Pinning test display").Items))
                throw new InvalidOperationException("A monitor's app pin was lost or changed the main layout.");
            model.SetAppPinned("app:" + shortcut, false, monitor.Items);
            if (model.IsAppPinned(id, monitor.Items)) throw new InvalidOperationException("Unpinning on another monitor failed.");
            File.WriteAllText(Path.Combine(dir, "result.txt"),
                "Context-menu pin/unpin; closed app; restart; exe/shortcut/case deduplication; Windows import; locked drop; monitor persistence: OK");
        }
        finally { dock.Close(); }
    }
}
