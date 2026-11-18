using System;
using System.IO;
using System.Linq;
using System.Text;

namespace LumaDock;

// Проверка списков переходов на настоящих запущенных программах (--check-jumplist <папка>): для каждой программы с окном выводится только число разделов и пунктов,
// названия пунктов (имена чатов, файлов) в результат не пишутся. Результат в result.txt, успешный заканчивается строкой OK.
internal static class JumpListCheck
{
    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var log = new StringBuilder();
        try
        {
            int apps = 0, withLists = 0;
            foreach (var group in WindowsShell.Windows().GroupBy(window => window.App, StringComparer.OrdinalIgnoreCase))
            {
                apps++;
                var lists = group.Select(window => JumpList.ForWindow(window.Handle)).FirstOrDefault(found => found.Count > 0) ?? [];
                string id = JumpList.AppId(group.First().Handle) ?? "";
                log.AppendLine($"{Path.GetFileNameWithoutExtension(group.Key)}: AppUserModelID {(id.Length > 0 ? "есть" : "нет")}, разделов {lists.Count}, пунктов {lists.Sum(item => item.Entries.Count)}");
                if (lists.Count > 0) withLists++;
            }
            log.AppendLine($"программ с окнами: {apps}, со списком переходов: {withLists}");
            log.AppendLine("OK");
            File.WriteAllText(Path.Combine(folder, "result.txt"), log.ToString(), new UTF8Encoding(false));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(folder, "result.txt"), log.ToString() + ex, new UTF8Encoding(false));
            return 1;
        }
    }
}
