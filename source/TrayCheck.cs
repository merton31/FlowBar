using System;
using System.IO;
using System.Linq;
using System.Text;

namespace LumaDock;

// FlowBar.exe --check-tray <папка>: настоящее чтение системного трея Windows без подмен. Пишет в result.txt сколько значков найдено
// (закреплённые, скрытые, кнопка «Показать скрытые значки») и видна ли панель задач. Успех — строка OK в конце.
internal static class TrayCheck
{
    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var report = new StringBuilder();
        int code = 0;
        try
        {
            var snapshot = WindowsShell.ReadTray();
            report.AppendLine($"Панель задач видна: {snapshot.HasVisibleTray}; закреплённых значков: {snapshot.Pinned.Length}; скрытых (окно открыто: {snapshot.OverflowOpen}): {snapshot.Overflow.Length}; кнопка скрытых значков: {snapshot.Chevron is not null}");
            foreach (var item in snapshot.Pinned.Concat(snapshot.Overflow))
                report.AppendLine($"  {item.Name} (значок {(item.Icon is null ? "нет" : item.Icon.PixelWidth + "x" + item.Icon.PixelHeight)})");
            var apps = WindowsShell.TrayApps();
            report.AppendLine($"Запасной список (запущенные программы со значком в трее): {apps.Length}");
            foreach (var item in apps)
                report.AppendLine($"  {item.Name} (значок {(item.Icon is null ? "нет" : item.Icon.PixelWidth + "x" + item.Icon.PixelHeight)}, открывается: {item.Open is not null})");
            if (snapshot.Pinned.Length + snapshot.Overflow.Length + apps.Length == 0)
            {
                report.AppendLine("СБОЙ: трей не прочитан ни с панели задач, ни из списка программ.");
                code = 1;
            }
            else report.AppendLine("OK");
        }
        catch (Exception ex) { report.AppendLine("СБОЙ: " + ex.GetType().Name + ": " + ex.Message); code = 1; }
        File.WriteAllText(Path.Combine(folder, "result.txt"), report.ToString(), Encoding.UTF8);
        return code;
    }
}
