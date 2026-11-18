using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static LumaDock.Visuals;

namespace LumaDock;

// Меню по правому щелчку на значке программы: «Widgets…» и «Settings», разделы списка переходов программы (Недавние, Tasks…),
// сама программа, «Pin to dock» и красные «End task» и «Close window». Вид и состав как у Lumabar.
internal sealed partial class DockWindow
{
    private static readonly Dictionary<string, string> AppTitles = new(StringComparer.OrdinalIgnoreCase);

    // Название программы для меню: описание файла (Discord, Claude), иначе имя файла с заглавной буквы
    private static string AppTitle(string path, List<WindowsShell.WindowInfo> running)
    {
        if (AppTitles.TryGetValue(path, out var cached)) return cached;
        string title = "";
        try
        {
            var info = FileVersionInfo.GetVersionInfo(WindowsShell.ResolveApp(path));
            title = (info.FileDescription ?? "").Trim();
            if (title.Length == 0) title = (info.ProductName ?? "").Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        if (title.Length == 0)
        {
            string raw = running.FirstOrDefault()?.Name ?? Path.GetFileNameWithoutExtension(path);
            title = raw.Length == 0 ? path : char.ToUpperInvariant(raw[0]) + raw[1..];
        }
        return AppTitles[path] = title;
    }

    internal void FillAppMenu(ContextMenu menu, string id, string path, List<WindowsShell.WindowInfo> running, ImageSource? icon,
        List<JumpList.Group>? groups = null)
    {
        menu.Items.Clear();
        menu.Items.Add(Menu("Widgets…", () => OpenGallery(), MenuWidgetsIcon()));
        menu.Items.Add(Menu("Settings", () => OpenSettings(), MenuGlyph("")));
        menu.Items.Add(new Separator());
        string name = AppTitle(path, running);
        groups ??= running.Count > 0 ? JumpList.ForWindow(running[0].Handle) : [];
        foreach (var group in groups)
        {
            menu.Items.Add(MenuHeader(group.Title.Length > 0 ? group.Title : name));
            foreach (var entry in group.Entries)
                menu.Items.Add(MenuText(entry.Title, () => { if (!JumpList.Launch(entry)) Report(L10n.F($"Could not open {entry.Title}")); }, icon is null ? null : MenuIcon(icon)));
        }
        if (groups.Count > 0) menu.Items.Add(new Separator());
        menu.Items.Add(MenuText(name, () =>
        {
            CloseThumbnail();
            if (running.Count == 1) WindowsShell.Activate(running[0].Handle, true);
            else if (running.Count > 1) WindowsShell.ActivateNext(running);
            else if (!WindowsShell.Launch(path)) Report(L10n.F($"Could not open {path}"));
        }, icon is null ? MenuGlyph("") : MenuIcon(icon)));
        menu.Items.Add(Menu(P.Items.Contains(id) ? "Unpin from dock" : "Pin to dock", () => model.ToggleWidget(id)));
        if (running.Count == 0) return;
        // Красные пункты. «End task» принудительно завершает программу, если она зависла: системные процессы не завершаются, перед завершением спрашиваем
        if (!WindowsShell.IsProtected(running[0].App))
        {
            var end = Menu("End task", () =>
            {
                if (MessageBox.Show(this, L10n.F($"End {name}? Unsaved data in it will be lost."), "FlowBar", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                if (!WindowsShell.EndTasks(running.Select(item => item.Handle).ToArray(), running[0].App)) Report(L10n.F($"Could not end {name}"));
            });
            end.Foreground = Brush("#FF453A");
            menu.Items.Add(end);
        }
        var close = Menu(running.Count == 1 ? "Close window" : "Close all windows", () => running.ForEach(window => WindowsShell.CloseWindow(window.Handle)));
        close.Foreground = Brush("#FF453A");
        menu.Items.Add(close);
    }
}
