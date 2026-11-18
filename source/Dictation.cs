using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace LumaDock;

// Диктовка из FlowBar: запись с микрофона в WAV (16 кГц, моно), распознавание программой Handy на этом компьютере и добавление текста в заметки.
// Запись длиннее 160 секунд режется на отрывки, Handy распознаёт их по очереди. Перенесено из FlowBar.Voice.cs.
internal static class Dictation
{
    public const int SegmentSeconds = 160;
    public const int MaxMinutes = 30;
    private const string RussianModel = "gigaam-v3-e2e-rnnt";
    private static MediaCapture? capture;
    private static string? wavPath;
    // Для --smoke: запись и распознавание подменяются тестовыми, чтобы проверка не зависела от микрофона и Handy
    internal static bool Fixture { get; set; }

    public static bool IsRecording { get; private set; }
    public static DateTime StartedAt { get; private set; }

    // Ошибка с готовым ключом сообщения для пользователя (переводится через L10n.T)
    internal sealed class DictationException(string message) : Exception(message);

    public static string? HandyPath()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new List<string>
        {
            Path.Combine(local, "Handy", "handy.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Handy", "handy.exe"),
            Path.Combine(local, "Microsoft", "WinGet", "Links", "handy.exe")
        };
        foreach (string raw in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            string folder = raw.Trim().Trim('"');
            if (Path.IsPathRooted(folder)) candidates.Add(Path.Combine(folder, "handy.exe"));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    public static async Task StartAsync()
    {
        if (IsRecording) return;
        if (Fixture) { IsRecording = true; StartedAt = DateTime.Now; return; }
        var media = new MediaCapture();
        try
        {
            await media.InitializeAsync(new MediaCaptureInitializationSettings { StreamingCaptureMode = StreamingCaptureMode.Audio });
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetTempPath());
            var file = await folder.CreateFileAsync("LumaDockVoice-" + Guid.NewGuid().ToString("N") + ".wav", CreationCollisionOption.FailIfExists);
            var profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.Low);
            profile.Audio = AudioEncodingProperties.CreatePcm(16000, 1, 16);
            await media.StartRecordToStorageFileAsync(profile, file);
            wavPath = file.Path;
        }
        catch (UnauthorizedAccessException)
        {
            media.Dispose();
            throw new DictationException("Allow desktop apps to use the microphone in Windows.");
        }
        catch (Exception) when (!Fixture)
        {
            media.Dispose();
            throw new DictationException("The microphone could not be started.");
        }
        capture = media;
        IsRecording = true;
        StartedAt = DateTime.Now;
    }

    // Останавливает запись и возвращает путь к WAV
    public static async Task<string> StopAsync()
    {
        if (!IsRecording) throw new InvalidOperationException("Dictation is not recording.");
        IsRecording = false;
        if (Fixture) return FixtureWave();
        var media = capture;
        capture = null;
        try { if (media is not null) await media.StopRecordAsync(); }
        finally { media?.Dispose(); }
        return wavPath ?? throw new DictationException("The recording could not be saved.");
    }

    public static void Discard(string? wav)
    {
        if (wav is null) return;
        try { File.Delete(wav); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // Текст из записи: Handy разбирает отрывки по очереди, progress(номер, всего)
    public static async Task<string> TranscribeAsync(string wav, CancellationToken stop, Action<int, int> progress)
    {
        if (Fixture) { progress(1, 1); return "Тестовая запись диктовки."; }
        string handy = HandyPath() ?? throw new DictationException("Handy was not found. Install Handy and its GigaAM v3 model to recognise speech.");
        string folder = Path.GetDirectoryName(wav)!;
        string models = await RunAsync(handy, ["--list-models", "--json"], folder, TimeSpan.FromSeconds(25), stop);
        using var list = JsonDocument.Parse(models);
        string? modelId = null;
        foreach (var item in list.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("is_downloaded", out var downloaded) || downloaded.ValueKind != JsonValueKind.True) continue;
            string? id = item.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
            if (id is null) continue;
            modelId ??= id;
            if (id == RussianModel) { modelId = id; break; }
        }
        if (modelId is null) throw new DictationException("Handy has no downloaded speech model. Download GigaAM v3 E2E-RNN-T in Handy.");
        string work = Path.Combine(folder, "LumaDockVoiceParts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var parts = await Task.Run(() => SliceWave(wav, work, stop), stop);
            var transcript = new List<string>();
            for (int index = 0; index < parts.Count; index++)
            {
                progress(index + 1, parts.Count);
                string json = await RunAsync(handy, ["--transcribe-file", parts[index], "--model", modelId, "--json"], work, TimeSpan.FromMinutes(3), stop);
                using var response = JsonDocument.Parse(json);
                if (!response.RootElement.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
                    throw new DictationException("Handy did not return any text.");
                string part = text.GetString()!.Trim();
                if (part.Length > 0) transcript.Add(part);
            }
            return string.Join(Environment.NewLine, transcript);
        }
        finally
        {
            foreach (string part in Directory.GetFiles(work, "*.wav")) Discard(part);
            try { Directory.Delete(work); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // Дописывает текст в заметки с датой, чтобы записи не сливались
    public static void AppendToNotes(DockModel model, string text)
    {
        var p = model.Settings;
        string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        p.Notes = (p.Notes.Length == 0 ? "" : p.Notes.TrimEnd() + Environment.NewLine + Environment.NewLine) + stamp + Environment.NewLine + text.Trim();
        model.Save(false);
    }

    private static async Task<string> RunAsync(string exe, string[] args, string directory, TimeSpan timeout, CancellationToken stop)
    {
        stop.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in args) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new DictationException("Handy could not be started.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(timeout);
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            stop.ThrowIfCancellationRequested();
            throw new DictationException("Handy took too long to recognise the recording.");
        }
        string result = await output, log = await error;
        if (process.ExitCode != 0)
            throw new DictationException(log.Contains("model", StringComparison.OrdinalIgnoreCase)
                ? "Handy could not load the speech model. Install GigaAM v3 E2E-RNN-T in Handy and try again."
                : "Handy could not recognise the recording.");
        return result;
    }

    // Режет WAV на отрывки по seconds секунд, сохраняя формат записи
    internal static List<string> SliceWave(string wav, string output, CancellationToken stop, int seconds = SegmentSeconds)
    {
        var paths = new List<string>();
        using var reader = new BinaryReader(File.OpenRead(wav), Encoding.ASCII);
        var source = reader.BaseStream;
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("A WAV file is expected.");
        reader.ReadUInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("A WAV file is expected.");
        byte[]? format = null;
        long dataOffset = 0, dataSize = 0;
        while (source.Length - source.Position >= 8 && (format is null || dataOffset == 0))
        {
            string tag = new(reader.ReadChars(4));
            uint size = reader.ReadUInt32();
            long next = source.Position + size + (size & 1);
            if (source.Position + size > source.Length) throw new InvalidDataException("The WAV file is incomplete.");
            if (tag == "fmt " && size >= 16 && size <= 65536) format = reader.ReadBytes((int)size);
            if (tag == "data") { dataOffset = source.Position; dataSize = size; }
            source.Position = next;
        }
        if (format is null || dataOffset == 0) throw new InvalidDataException("The WAV file has no audio.");
        int alignment = BitConverter.ToUInt16(format, 12);
        long bytesPerSecond = BitConverter.ToUInt32(format, 8);
        if (alignment == 0 || bytesPerSecond == 0 || dataSize % alignment != 0) throw new InvalidDataException("The WAV format is invalid.");
        long partSize = bytesPerSecond * seconds;
        partSize -= partSize % alignment;
        if (partSize == 0 || partSize > int.MaxValue - 65540) throw new InvalidDataException("The WAV format is invalid.");
        source.Position = dataOffset;
        var buffer = new byte[64 * 1024];
        for (long remaining = dataSize; remaining > 0;)
        {
            stop.ThrowIfCancellationRequested();
            int bytes = (int)Math.Min(remaining, partSize);
            string path = Path.Combine(output, (paths.Count + 1).ToString("D3", CultureInfo.InvariantCulture) + ".wav");
            using (var writer = new BinaryWriter(File.Create(path), Encoding.ASCII))
            {
                writer.Write("RIFF".ToCharArray());
                writer.Write(20 + format.Length + (format.Length & 1) + bytes + (bytes & 1));
                writer.Write("WAVEfmt ".ToCharArray());
                writer.Write(format.Length);
                writer.Write(format);
                if ((format.Length & 1) != 0) writer.Write((byte)0);
                writer.Write("data".ToCharArray());
                writer.Write(bytes);
                for (int pending = bytes; pending > 0;)
                {
                    int count = source.Read(buffer, 0, Math.Min(pending, buffer.Length));
                    if (count == 0) throw new EndOfStreamException();
                    writer.Write(buffer, 0, count);
                    pending -= count;
                }
                if ((bytes & 1) != 0) writer.Write((byte)0);
            }
            remaining -= bytes;
            paths.Add(path);
        }
        return paths;
    }

    // Тихий WAV на секунду для проверки без микрофона: 16 кГц, моно, 16 бит
    internal static string FixtureWave(int seconds = 1)
    {
        string path = Path.Combine(Path.GetTempPath(), "LumaDockVoice-fixture-" + Guid.NewGuid().ToString("N") + ".wav");
        int bytes = 16000 * 2 * seconds;
        using var writer = new BinaryWriter(File.Create(path), Encoding.ASCII);
        writer.Write("RIFF".ToCharArray()); writer.Write(36 + bytes); writer.Write("WAVEfmt ".ToCharArray());
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data".ToCharArray()); writer.Write(bytes); writer.Write(new byte[bytes]);
        return path;
    }
}
