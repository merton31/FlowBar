using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LumaDock;

// Встроенный движок распознавания речи из FlowBar (без Handy): библиотека transcribe.cpp и модель GGUF скачиваются один раз, проверяются по SHA-256
// и работают на этом компьютере. Распознавание идёт в отдельном процессе «LumaDock.exe --voice-local», чтобы сбой движка не ронял док.
// Перенесено из FlowBar.LocalVoice.cs.
internal static class LocalVoice
{
    internal sealed record Model(string Id, string Label, string Revision, string Digest, long Size)
    {
        public string FileName => Id + "-Q8_0.gguf";
    }

    public static readonly Model[] Models =
    [
        new("gigaam-v3-e2e-rnnt", "GigaAM v3 RNN-T · Russian · 274 MB", "3efd5b39882ceadca5570a8fb9fa1a2c801d6d6d", "78d63b47723b7f8d78c6113a6ef983b5a86e2a86f6c273e1f5cb6967b1c4467a", 273724832),
        new("gigaam-v3-e2e-ctc", "GigaAM v3 CTC · Russian · 272 MB", "59717de63d50dd9dd93b3249da6ad3f2f1c4ad6b", "9ccce4750dc813a493d96ca15ee251712bedec15ac9a02fa3d2bd732f08ae5eb", 272151136),
        new("whisper-small", "Whisper Small · many languages · 270 MB", "4afdc7942ecf1997f359deae48a63506b062eac4", "9b9c8811bbcc82a7766f0fb0925614bdacb0923b2cc630daeac17108b655b860", 269751136)
    ];

    private const string EngineTag = "0.4.2";
    private const string EnginePackage = "transcribe-native-windows-x86_64-cpu-vulkan";
    private const string EngineDigest = "6094102cf57b404b050707ceb258b12ce647b92e801b5d46077214e5b030c8aa";
    private const int WorkerSeconds = 24;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private static bool installing;

    // Для --smoke: папка движка и моделей заменяется временной, чтобы проверка не трогала настоящие данные
    internal static string? RootOverride { get; set; }
    internal static string Root => RootOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumaDockSpeech");
    internal static string EngineFolder => Path.Combine(Root, "engine-" + EngineTag, EnginePackage);
    internal static string ModelsFolder => Path.Combine(Root, "models");
    private static string HandyModelsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "com.pais.handy", "models");
    internal static bool Supported => Environment.Is64BitProcess && !HandyInstaller.IsArm();

    public static Model Selected(string? id) => Models.FirstOrDefault(model => model.Id == id) ?? Models[0];
    internal static string ModelPath(Model model) => Path.Combine(ModelsFolder, model.FileName);
    private static bool SizeMatches(string file, Model model) => File.Exists(file) && new FileInfo(file).Length == model.Size;
    public static bool Ready(Model model) => File.Exists(Path.Combine(EngineFolder, "transcribe.dll")) && SizeMatches(ModelPath(model), model);

    internal static async Task<string> Sha256Async(string path, CancellationToken stop)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, stop)).ToLowerInvariant();
    }

    // Скачивает файл, не больше maxBytes, и сверяет SHA-256; при любом отклонении файл удаляется
    internal static async Task FetchAsync(string url, string target, string digest, long maxBytes, Action<string> progress, string label, CancellationToken stop)
    {
        progress(L10n.F($"Downloading {label}…"));
        try
        {
            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stop))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? 0, written = 0;
                int previous = -1;
                await using var source = await response.Content.ReadAsStreamAsync(stop);
                await using var output = File.Create(target);
                var buffer = new byte[81920];
                int count;
                while ((count = await source.ReadAsync(buffer, stop)) > 0)
                {
                    written += count;
                    if (written > maxBytes) throw new InvalidDataException("The file is larger than expected.");
                    await output.WriteAsync(buffer.AsMemory(0, count), stop);
                    int percent = total > 0 ? (int)(written * 100 / total) : 0;
                    if (percent / 5 != previous / 5 || written == total) progress(L10n.F($"Downloading {label}: {percent}%"));
                    previous = percent;
                }
            }
            if (await Sha256Async(target, stop) != digest) throw new InvalidDataException("The checksum did not match.");
        }
        catch
        {
            try { File.Delete(target); } catch (IOException) { }
            throw;
        }
    }

    // Распаковка tar.gz системной tar.exe во временную папку
    internal static async Task UnpackAsync(string archive, string folder, CancellationToken stop)
    {
        string tar = Path.Combine(Environment.SystemDirectory, "tar.exe");
        await RunAsync(tar, ["-xf", archive, "-C", folder], TimeSpan.FromMinutes(2), stop);
    }

    // Скачивает движок и модель (или копирует уже проверенную модель Handy) в папки LumaDockSpeech
    public static async Task PrepareAsync(Model model, Action<string> progress, CancellationToken stop)
    {
        if (installing) return;
        installing = true;
        string staging = Path.Combine(Root, "download-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (Ready(model)) return;
            Directory.CreateDirectory(staging);
            if (!File.Exists(Path.Combine(EngineFolder, "transcribe.dll")))
            {
                string archive = Path.Combine(staging, "engine.tar.gz");
                string url = "https://github.com/handy-computer/transcribe.cpp/releases/download/v" + EngineTag + "/transcribe-native-" + EngineTag + "-windows-x86_64-cpu-vulkan.tar.gz";
                await FetchAsync(url, archive, EngineDigest, 24 * 1024 * 1024, progress, L10n.T("engine"), stop);
                string unpack = Path.Combine(staging, "engine");
                Directory.CreateDirectory(unpack);
                progress(L10n.T("Preparing the engine…"));
                await UnpackAsync(archive, unpack, stop);
                if (!File.Exists(Path.Combine(unpack, EnginePackage, "transcribe.dll"))) throw new InvalidDataException("The recognition library is missing.");
                string destinationEngine = Path.GetDirectoryName(EngineFolder)!;
                if (Directory.Exists(destinationEngine)) Directory.Move(destinationEngine, Path.Combine(staging, "incomplete-engine"));
                Directory.Move(unpack, destinationEngine);
            }
            string destination = ModelPath(model);
            if (!SizeMatches(destination, model))
            {
                string candidate = Path.Combine(staging, model.FileName);
                string existing = Path.Combine(HandyModelsFolder, model.FileName);
                if (SizeMatches(existing, model) && await Sha256Async(existing, stop) == model.Digest)
                {
                    progress(L10n.T("Copying the verified Handy model…"));
                    File.Copy(existing, candidate);
                    if (await Sha256Async(candidate, stop) != model.Digest) throw new InvalidDataException("The model changed while copying.");
                }
                else
                {
                    string url = "https://huggingface.co/handy-computer/" + model.Id + "-gguf/resolve/" + model.Revision + "/" + model.FileName;
                    await FetchAsync(url, candidate, model.Digest, model.Size, progress, L10n.T("model"), stop);
                }
                Directory.CreateDirectory(ModelsFolder);
                File.Move(candidate, destination, true);
            }
        }
        finally
        {
            installing = false;
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // Текст из записи: WAV режется на отрывки до 24 секунд, их по порядку разбирает процесс --voice-local
    public static async Task<string> TranscribeAsync(Model model, string wav, CancellationToken stop)
    {
        string folder = Path.Combine(Path.GetTempPath(), "LumaDockSpeech-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var parts = await Task.Run(() => Dictation.SliceWave(wav, folder, stop, WorkerSeconds), stop);
            if (parts.Count == 0) return "";
            string json = await RunAsync(Environment.ProcessPath!, ["--voice-local", EngineFolder, ModelPath(model), folder], TimeSpan.FromMinutes(35), stop);
            return (JsonSerializer.Deserialize<string>(json) ?? "").Trim();
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    internal static async Task<string> RunAsync(string exe, string[] args, TimeSpan timeout, CancellationToken stop)
    {
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in args) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("The process could not be started.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(timeout);
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            stop.ThrowIfCancellationRequested();
            throw new TimeoutException("The process took too long.");
        }
        string result = await output, log = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException(log.Trim().Length > 0 ? log.Trim() : "The process failed.");
        return result;
    }

    // ---- Процесс --voice-local: загружает transcribe.dll, читает отрывки *.wav из папки и печатает текст одной JSON-строкой ----

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitBackends(byte[] directory);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Open(byte[] model, nint load, nint session, out nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Run(nint session, float[] pcm, int length, nint parameters);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint FullText(nint session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Free(nint session);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadLibraryEx(string file, nint reserved, uint flags);

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + "\0");

    private static string NativeText(nint pointer)
    {
        if (pointer == 0) return "";
        int length = 0;
        while (Marshal.ReadByte(pointer, length) != 0) length++;
        var bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
    }

    // Звук отрывка как числа от -1 до 1; формат только PCM 16 кГц, моно, 16 бит и не длиннее 24 секунд
    internal static float[] ReadSlice(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.BaseStream.Position = 16;
        int formatLength = reader.ReadInt32();
        if (formatLength < 16 || formatLength > 65536) throw new InvalidDataException("Invalid WAV format.");
        var format = reader.ReadBytes(formatLength);
        if (BitConverter.ToUInt16(format, 2) != 1 || BitConverter.ToInt32(format, 4) != 16000 || BitConverter.ToUInt16(format, 14) != 16)
            throw new InvalidDataException("PCM 16 kHz mono is required.");
        reader.BaseStream.Position += formatLength & 1;
        if (reader.ReadUInt32() != 0x61746164) throw new InvalidDataException("The WAV has no audio.");
        int bytes = reader.ReadInt32();
        if (bytes < 0 || bytes % 2 != 0 || bytes > WorkerSeconds * 32000) throw new InvalidDataException("Invalid WAV length.");
        var pcm = new float[bytes / 2];
        for (int i = 0; i < pcm.Length; i++) pcm[i] = reader.ReadInt16() / 32768f;
        return pcm;
    }

    public static int RunWorker(string[] args)
    {
        if (args.Length != 4) return 2;
        try
        {
            string directory = Path.GetFullPath(args[1]);
            Environment.SetEnvironmentVariable("TRANSCRIBE_BACKENDS", "cpu");
            nint library = LoadLibraryEx(Path.Combine(directory, "transcribe.dll"), 0, 0x1100);
            if (library == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
            var init = Export<InitBackends>("transcribe_init_backends");
            var open = Export<Open>("transcribe_open");
            var run = Export<Run>("transcribe_run");
            var fullText = Export<FullText>("transcribe_full_text");
            var free = Export<Free>("transcribe_session_free");
            int status = init(Utf8(directory));
            if (status != 0) throw new InvalidOperationException("Engine initialisation failed: " + status);
            status = open(Utf8(args[2]), 0, 0, out nint session);
            if (status != 0) throw new InvalidOperationException("Model loading failed: " + status);
            var text = new List<string>();
            try
            {
                foreach (string file in Directory.GetFiles(args[3], "*.wav").OrderBy(path => path, StringComparer.Ordinal))
                {
                    var pcm = ReadSlice(file);
                    if (pcm.Length == 0) continue;
                    status = run(session, pcm, pcm.Length, 0);
                    if (status != 0) throw new InvalidOperationException("Speech recognition failed: " + status);
                    string part = NativeText(fullText(session)).Trim();
                    if (part.Length > 0) text.Add(part);
                }
            }
            finally { free(session); }
            using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            stdout.WriteLine(JsonSerializer.Serialize(string.Join(Environment.NewLine, text)));
            return 0;
        }
        catch (Exception ex)
        {
            using var stderr = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false));
            stderr.WriteLine(ex.Message);
            return 3;
        }
    }
}
