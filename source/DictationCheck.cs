using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;

namespace LumaDock;

// LumaDock.exe --check-dictation <папка>: настоящая проверка без подмен. Секунда записи с микрофона (запись тут же удаляется)
// и распознавание Handy для речи, которую Windows сама произносит. Результат — result.txt в папке.
internal static class DictationCheck
{
    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var report = new StringBuilder();
        int code = 0;
        try { Task.Run(() => Check(report)).GetAwaiter().GetResult(); }
        catch (Exception ex) { report.AppendLine("СБОЙ: " + ex.GetType().Name + ": " + ex.Message); code = 1; }
        File.WriteAllText(Path.Combine(folder, "result.txt"), report.ToString(), Encoding.UTF8);
        return code;
    }

    private static async Task Check(StringBuilder report)
    {
        Dictation.Fixture = false;
        string? handy = Dictation.HandyPath();
        report.AppendLine("Handy: " + (handy ?? "не найден"));
        // 1. запись с микрофона: интерфейс записи WinRT, файл WAV 16 кГц моно
        await Dictation.StartAsync();
        await Task.Delay(1500);
        string recorded = await Dictation.StopAsync();
        long size = new FileInfo(recorded).Length;
        Dictation.Discard(recorded);
        report.AppendLine($"Запись с микрофона: {size} байт за 1,5 с (ожидается около 48000)");
        if (size < 20000) throw new InvalidOperationException("Запись оказалась слишком короткой.");
        // 2. распознавание: Windows произносит русскую фразу, Handy возвращает текст
        var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(item => item.Language.StartsWith("ru", StringComparison.OrdinalIgnoreCase));
        report.AppendLine("Русский голос Windows: " + (voice?.DisplayName ?? "нет"));
        if (voice is null || handy is null) { report.AppendLine("Распознавание не проверялось: нужен русский голос Windows и Handy."); return; }
        using var synthesizer = new SpeechSynthesizer { Voice = voice };
        using var speech = await synthesizer.SynthesizeTextToStreamAsync("Привет. Это проверка диктовки. Купить хлеб и молоко.");
        var bytes = new byte[speech.Size];
        using (var reader = new DataReader(speech.GetInputStreamAt(0)))
        {
            await reader.LoadAsync((uint)speech.Size);
            reader.ReadBytes(bytes);
        }
        string wav = Path.Combine(Path.GetTempPath(), "LumaDockVoice-check-" + Guid.NewGuid().ToString("N") + ".wav");
        await File.WriteAllBytesAsync(wav, bytes);
        try
        {
            string text = await Dictation.TranscribeAsync(wav, CancellationToken.None, (_, _) => { });
            report.AppendLine("Распознано: " + text);
            if (text.Length == 0) throw new InvalidOperationException("Handy вернул пустой текст.");
        }
        finally { Dictation.Discard(wav); }
        report.AppendLine("OK");
    }
}
