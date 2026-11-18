using System.Collections.Generic;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LumaDock;

// Папка данных приложения. До версии 2.0 называлась LumaDock: при первом запуске под именем FlowBar профиль копируется оттуда
// (старая папка остаётся нетронутой). Данные прежней панели FlowBar 1.x лежат в %APPDATA%\FlowBar и здесь не трогаются.
internal static class AppData
{
    private static string? root;
    public static string Root => root ??= Prepare();

    private static string Prepare()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string target = Path.Combine(local, "FlowBar"), legacy = Path.Combine(local, "LumaDock");
        try
        {
            if (!File.Exists(Path.Combine(target, "profile.json")) && File.Exists(Path.Combine(legacy, "profile.json")))
            {
                Directory.CreateDirectory(target);
                foreach (string file in Directory.GetFiles(legacy))
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return target;
    }
}

// Установка в %LOCALAPPDATA%\Programs\FlowBar: тот же путь, что и у прежней панели FlowBar 1.x, поэтому её обновление «Скачать и установить»
// и автозапуск Windows подхватывают новую версию без лишних действий. Запущенный откуда угодно FlowBar.exe ставит себя сам.
internal static class Installer
{
    public static string InstallDir => Environment.GetEnvironmentVariable("FLOWBAR_INSTALL_DIR") is { Length: > 0 } custom
        ? custom : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FlowBar");
    public static string InstalledExe => Path.Combine(InstallDir, "FlowBar.exe");

    // Файлы прежней панели 1.x, которые новой версии не нужны
    private static readonly string[] Obsolete = ["FlowBar.exe.config", "flowbar-watch.exe"];

    // true — это была установка: установленная копия запущена, текущий процесс должен завершиться
    public static bool RunFromElsewhere()
    {
        string? self = Environment.ProcessPath;
        if (self is null || Environment.GetEnvironmentVariable("FLOWBAR_NO_INSTALL") == "1") return false;
        if (string.Equals(Path.GetFullPath(self), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            string installed = Install(self, true);
            Process.Start(new ProcessStartInfo(installed) { WorkingDirectory = InstallDir })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return false; // не удалось поставить — работаем из текущего места
        }
    }

    public static string Install(string source, bool stopRunning)
    {
        Directory.CreateDirectory(InstallDir);
        if (stopRunning) StopRunning();
        foreach (string name in Obsolete)
        {
            try { File.Delete(Path.Combine(InstallDir, name)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        string target = InstalledExe;
        for (int attempt = 0; ; attempt++)
        {
            try { File.Copy(source, target, true); return target; }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 20) { Thread.Sleep(250); } // прежняя копия ещё закрывается
        }
    }

    // Останавливает прежнюю панель (FlowBar 1.x, LumaDock или более раннюю 2.x). Панели 1.x шлём команду «quit» тем же сообщением, что и её собственное
    // обновление (она сохраняет данные и сама возвращает панель Windows); процессам без окон (серверы MCP --mcp) ждать нечего. Сторож flowbar-watch.exe
    // сам возвращает панель задач Windows, когда панель закрылась, поэтому его добиваем в последнюю очередь.
    private static void StopRunning()
    {
        int self = Environment.ProcessId;
        var panels = Process.GetProcessesByName("FlowBar").Concat(Process.GetProcessesByName("LumaDock")).Where(process => process.Id != self).ToList();
        var pids = panels.Select(process => (uint)process.Id).ToHashSet();
        var withWindows = new HashSet<uint>();
        uint quit = RegisterWindowMessageW("FlowBarOpen");
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint pid);
            if (pids.Contains(pid)) { withWindows.Add(pid); PostMessageW(window, quit, QuitCode, 0); }
            return true;
        }, 0);
        foreach (var process in panels)
        {
            try { process.CloseMainWindow(); }
            catch (InvalidOperationException) { }
        }
        foreach (var process in panels)
        {
            try { if (!process.WaitForExit(withWindows.Contains((uint)process.Id) ? 4000 : 0)) { process.Kill(); process.WaitForExit(3000); } }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
        }
        foreach (var watcher in Process.GetProcessesByName("flowbar-watch"))
        {
            try { if (!watcher.WaitForExit(3000)) watcher.Kill(); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
        }
    }

    // номер команды «quit» в списке OpenNames панели FlowBar 1.x
    private const int QuitCode = 24;
    private delegate bool WindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
}

// Обновление с GitHub: тот же формат выпусков, что у FlowBar 1.x (архив FlowBar*.zip с FlowBar.exe, контрольная сумма SHA-256 от GitHub).
internal static class Updates
{
    public const string Repo = "merton31/FlowBar";
    public sealed record Release(Version Version, string Tag, string Asset, string Url, string? Sha256);

    public static Version Current => typeof(Updates).Assembly.GetName().Version ?? new Version(2, 0, 0, 0);

    // 2.0.0 → 2.0.0.0, 1.6.13f → 1.6.13.6 (буква в конце — четвёртое число, как в FlowBar 1.x)
    public static bool TryParseTag(string tag, out Version version)
    {
        var match = Regex.Match(tag, @"^v?(\d+)\.(\d+)\.(\d+)([a-z]?)$");
        version = new Version();
        if (!match.Success) return false;
        version = new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value),
            match.Groups[4].Length == 0 ? 0 : match.Groups[4].Value[0] - 'a' + 1);
        return true;
    }

    public static Release ParseLatest(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!TryParseTag(tag, out var version)) throw new InvalidOperationException("Unexpected release tag: " + tag);
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (!name.StartsWith("FlowBar", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
            string url = asset.GetProperty("browser_download_url").GetString() ?? "";
            // скачиваем только из выпусков этого репозитория
            if (!url.StartsWith($"https://github.com/{Repo}/releases/download/", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected download address.");
            string? digest = asset.TryGetProperty("digest", out var value) ? value.GetString() : null;
            return new(version, tag, name, url, digest is not null && digest.StartsWith("sha256:", StringComparison.Ordinal) ? digest[7..].ToLowerInvariant() : null);
        }
        throw new InvalidOperationException("The release has no FlowBar*.zip archive.");
    }

    private static HttpClient Client()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FlowBar");
        return client;
    }

    public static async Task<Release> LatestAsync()
    {
        using var client = Client();
        return ParseLatest(await client.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
    }

    // Скачивает архив, сверяет сумму, распаковывает и запускает новый FlowBar.exe: он сам станет установленной копией
    public static async Task InstallAsync(Release release)
    {
        if (release.Sha256 is null) throw new InvalidOperationException("GitHub did not supply a checksum.");
        string folder = Path.Combine(Path.GetTempPath(), "FlowBar-update", release.Tag);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
        string zip = Path.Combine(folder, release.Asset);
        using (var client = Client())
        using (var response = await client.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = File.Create(zip);
            await source.CopyToAsync(target);
        }
        string got;
        await using (var stream = File.OpenRead(zip)) got = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
        if (got != release.Sha256) throw new InvalidOperationException("The downloaded archive does not match its checksum.");
        string unpacked = Path.Combine(folder, "files");
        ZipFile.ExtractToDirectory(zip, unpacked);
        string? exe = Directory.GetFiles(unpacked, "FlowBar.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (exe is null) throw new InvalidOperationException("The archive has no FlowBar.exe.");
        if (!Version.TryParse(FileVersionInfo.GetVersionInfo(exe).FileVersion, out var built) || built <= Current)
            throw new InvalidOperationException("The archive is not newer than the installed version.");
        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe) })?.Dispose();
    }

    public static bool IsUpdateError(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or InvalidOperationException or IOException or JsonException or KeyNotFoundException
            or InvalidDataException or UnauthorizedAccessException or Win32Exception;
}
