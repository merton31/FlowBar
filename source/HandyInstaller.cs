using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LumaDock;

// Установка Handy по нажатию, как во FlowBar: последняя версия находится на странице выпусков проекта cjpais/Handy (GitHub),
// установщик скачивается, проверяется по SHA-256 из ответа GitHub и запускается. Берётся только x64- или arm64-установщик
// с github.com из выпусков самого Handy и только с контрольной суммой. Перенесено из FlowBar.HandyInstall.cs.
internal static class HandyInstaller
{
    private const string LatestApi = "https://api.github.com/repos/cjpais/Handy/releases/latest";
    private static readonly HttpClient Http = CreateClient();
    private static bool downloading;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FlowBar");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public sealed record Installer(string Name, string Url, string Sha256);

    internal static bool IsArm()
    {
        string? architecture = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
        return string.Equals(architecture, "ARM64", StringComparison.OrdinalIgnoreCase);
    }

    // Установщик из ответа GitHub: имя Handy_<версия>_x64-setup.exe (или arm64), адрес на github.com и обязательная сумма SHA-256
    internal static Installer? Pick(string json, bool arm)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        var wanted = new Regex(@"^Handy_[0-9][0-9A-Za-z.\-]*_" + (arm ? "arm64" : "x64") + @"-setup\.exe$", RegexOptions.CultureInvariant);
        foreach (var asset in assets.EnumerateArray())
        {
            string? name = asset.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
            string? url = asset.TryGetProperty("browser_download_url", out var urlValue) ? urlValue.GetString() : null;
            string? digest = asset.TryGetProperty("digest", out var digestValue) ? digestValue.GetString() : null;
            if (name is null || url is null || !wanted.IsMatch(name)) continue;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com"
                || !uri.AbsolutePath.StartsWith("/cjpais/Handy/releases/download/", StringComparison.Ordinal)) continue;
            if (digest is null || !Regex.IsMatch(digest, @"^sha256:[0-9a-fA-F]{64}$")) continue;
            return new Installer(name, url, digest[7..].ToLowerInvariant());
        }
        return null;
    }

    // Скачивает установщик во временную папку и сверяет сумму; progress — проценты. Неверная сумма — файл удаляется и выбрасывается ошибка
    internal static async Task<string> FetchAsync(Installer installer, Action<int> progress, CancellationToken stop)
    {
        string folder = Path.Combine(Path.GetTempPath(), "LumaDock-handy");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, installer.Name);
        using (var response = await Http.GetAsync(installer.Url, HttpCompletionOption.ResponseHeadersRead, stop))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? 0, got = 0;
            int last = -1;
            await using var source = await response.Content.ReadAsStreamAsync(stop);
            await using var target = File.Create(file);
            var buffer = new byte[81920];
            int count;
            while ((count = await source.ReadAsync(buffer, stop)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, count), stop);
                got += count;
                if (total > 0 && got * 100 / total / 5 != last / 5) { last = (int)(got * 100 / total); progress(last); }
            }
        }
        string sum;
        await using (var stream = File.OpenRead(file)) sum = Convert.ToHexString(await SHA256.HashDataAsync(stream, stop)).ToLowerInvariant();
        if (sum != installer.Sha256)
        {
            try { File.Delete(file); } catch (IOException) { }
            throw new InvalidDataException("The Handy checksum did not match.");
        }
        return file;
    }

    // По нажатию: найти последнюю версию, скачать, проверить, запустить установщик; status — подпись для пользователя
    public static async Task DownloadAsync(Action<string> status)
    {
        if (downloading) { status(L10n.T("Handy is already downloading.")); return; }
        downloading = true;
        try
        {
            status(L10n.T("Looking for the latest Handy…"));
            var installer = Pick(await Http.GetStringAsync(LatestApi), IsArm()) ?? throw new InvalidDataException("No suitable installer in the latest release.");
            status(L10n.F($"Downloading {installer.Name}…"));
            string path = await FetchAsync(installer, percent => status(L10n.F($"Downloading {installer.Name}: {percent}%")), CancellationToken.None);
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception)
            {
                // например, отказ в окне разрешений Windows: файл скачан и проверен, но не запущен
                status(L10n.T("Handy was downloaded but the installer was not started. Open it from the temporary folder or press the button again."));
                return;
            }
            status(L10n.T("Handy was downloaded and verified. Finish the installation in its window, then press the button again."));
        }
        catch (Exception)
        {
            status(L10n.T("Handy could not be downloaded automatically. Check the connection and try again."));
        }
        finally { downloading = false; }
    }
}
