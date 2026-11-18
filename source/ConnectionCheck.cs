using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LumaDock;

// FlowBar.exe --check-connections <папка>: настоящая проверка без подмен. Связь с Codex, Claude Code и Gemini (короткий запрос тем же путём, что и разбор диктовки,
// стоит немного токенов) и сравнение закреплённых версий моделей распознавания с репозиторием Hugging Face. Не установленный помощник — не ошибка.
internal static class ConnectionCheck
{
    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var report = new StringBuilder();
        int code = 0;
        try
        {
            Task.Run(() => Check(report)).GetAwaiter().GetResult();
            report.AppendLine("OK");
        }
        catch (Exception ex) { report.AppendLine("СБОЙ: " + ex.GetType().Name + ": " + ex.Message); code = 1; }
        File.WriteAllText(Path.Combine(folder, "result.txt"), report.ToString(), Encoding.UTF8);
        return code;
    }

    private static async Task Check(StringBuilder report)
    {
        VoiceAssistant.Fixture = false;
        foreach (string provider in new[] { "codex", "claude", "agy" })
        {
            string? path = VoiceAssistant.Binary(provider);
            var result = await VoiceAssistant.CheckAsync(provider, CancellationToken.None);
            report.AppendLine(VoiceAssistant.DisplayName(provider) + ": " + (path ?? "не найден") + " -> "
                + (result.Ok ? $"связь есть, ответ за {result.Elapsed.TotalSeconds:0.0} с" : "нет связи: " + result.Message));
        }
        foreach (var model in LocalVoice.Models)
        {
            var latest = await LocalVoice.LatestAsync(model, CancellationToken.None);
            report.AppendLine($"{model.Id}: в репозитории {latest.Revision[..8]}, в программе {model.Revision[..8]}, "
                + (latest.Digest == model.Digest && latest.Size == model.Size ? "версия та же" : "ЕСТЬ НОВАЯ ВЕРСИЯ"));
        }
    }
}
