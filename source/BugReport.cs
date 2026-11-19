using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace LumaDock;

// Последние действия с панелью: нажатия на плитки и пункты меню, открытые и закрытые окна, показ и скрытие дока, события системы (экран, панель задач, полноэкранное окно).
// Живёт в памяти кольцом из 80 записей; уходит в отчёт и дописывается в error.log рядом с ошибкой. Имён программ и путей здесь нет.
internal static class ActivityLog
{
    private const int Capacity = 80;
    private static readonly object gate = new();
    private static readonly Queue<string> events = new();

    public static void Record(string kind, string? detail = null)
    {
        var line = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + kind + (string.IsNullOrEmpty(detail) ? "" : " " + detail);
        lock (gate)
        {
            events.Enqueue(line.Length > 160 ? line[..160] : line);
            while (events.Count > Capacity) events.Dequeue();
        }
    }

    // Идентификатор плитки без имени программы: «app:game.exe» → «app»
    public static string Tile(string id) => id.StartsWith("app:", StringComparison.Ordinal) ? "app" : id;

    public static string[] Last(int count)
    {
        lock (gate) return events.Skip(Math.Max(0, events.Count - count)).ToArray();
    }
}

// Журнал необработанных ошибок: нужен, чтобы в отчёте об ошибке было что-то кроме версии
internal static class ErrorLog
{
    private static readonly object gate = new();
    private static readonly Dictionary<string, DateTime> recent = [];
    internal static string PathOf => System.IO.Path.Combine(AppData.Root, "error.log");
    // в самопроверках журнал пользователя не трогаем
    internal static bool Disabled;

    // Ошибка с пометкой, где она случилась («[Radios] System.IO…»). Одна и та же ошибка чаще раза в минуту не пишется: опрос данных иначе забил бы журнал
    public static void Write(Exception? error, string? area = null) =>
        Append(error is null ? null : area + "|" + error.GetType().Name + "|" + error.Message, area, error?.ToString());

    // Сбой без исключения (например, «Windows не ответила вовремя»): попадает в отчёт как FlowBar.Note
    public static void Note(string area, string text) => Append(area + "|note|" + text, area, "FlowBar.Note: " + text);

    private static void Append(string? key, string? area, string? text)
    {
        if (Disabled || key is null || text is null) return;
        try
        {
            lock (gate)
            {
                if (recent.TryGetValue(key, out var at) && DateTime.UtcNow - at < TimeSpan.FromMinutes(1)) return;
                if (recent.Count > 200) recent.Clear();
                recent[key] = DateTime.UtcNow;
                Directory.CreateDirectory(AppData.Root);
                var file = new FileInfo(PathOf);
                if (file.Exists && file.Length > 256 * 1024) File.Move(PathOf, System.IO.Path.Combine(AppData.Root, "error.old.log"), true);
                // после ошибки — что делали с панелью перед ней (строки с отступом, разбор отчёта их пропускает)
                var before = string.Concat(ActivityLog.Last(15).Select(entry => "    действия: " + entry + Environment.NewLine));
                File.AppendAllText(PathOf, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ", CultureInfo.InvariantCulture)
                    + (area is null ? "" : "[" + area + "] ") + text + Environment.NewLine + before, Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

// Отчёт об ошибке: описание от пользователя и по желанию диагностика уходят на приёмник разработчика (тот же, что у FlowBar 1.x: HTTPS POST с JSON, см. tools\bug-report-contract.md в репозитории FlowBar).
// Личное в тексте ошибок (пути, адреса, почта) заменяется перед показом, а пользователь видит всё, что уйдёт, и сам нажимает «Отправить».
internal static class BugReport
{
    public const string Url = "https://flowbat-reports.netlify.app/bug-report";
    public const int MaxBytes = 32 * 1024;
    public const int DiagnosticsLimit = 10000; // знаков диагностики: вместе с описанием укладывается в MaxBytes
    private static readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };

    public sealed class DeliveryException(bool limited) : Exception("The report was not delivered") { public bool Limited { get; } = limited; }

    // 2.0.0.3 → 2.0.0c (буква — четвёртое число версии)
    public static string VersionText => Updates.Display(Updates.Current);

    // Приёмник принимает только HTTPS без учётных данных, запроса и якоря
    public static Uri? Endpoint(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var result) && result.Scheme == Uri.UriSchemeHttps
            && result.UserInfo.Length == 0 && result.Fragment.Length == 0 && result.Query.Length == 0 ? result : null;

    // Подтверждение приёмника: {"ok":true,"report_id":"…"} с тем же номером, что отправлен
    public static string ReadReceipt(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True
            || !root.TryGetProperty("report_id", out var id) || id.ValueKind != JsonValueKind.String)
            throw new InvalidDataException();
        var number = id.GetString() ?? "";
        if (!Regex.IsMatch(number, @"\A[A-Za-z0-9_-]{1,64}\z")) throw new InvalidDataException();
        return number;
    }

    // Отправка; возвращает номер отчёта из подтверждения. DeliveryException(Limited) — приёмник ответил 429; остальные сбои — другие исключения
    public static async Task<string> SendAsync(Uri endpoint, string id, string description, string report, HttpMessageHandler? handler = null)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { report_id = id, version = VersionText, description, report },
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        if (payload.Length > MaxBytes) throw new InvalidDataException();
        using var own = handler is null ? null : new HttpClient(handler, false) { Timeout = TimeSpan.FromSeconds(12) };
        var http = own ?? client;
        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        request.Headers.UserAgent.ParseAdd("FlowBar/" + VersionText);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        if ((int)response.StatusCode == 429) throw new DeliveryException(true);
        if (!response.IsSuccessStatusCode) throw new DeliveryException(false);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync().ConfigureAwait(false), Encoding.UTF8);
        var buffer = new char[4097];
        int count = await reader.ReadBlockAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
        if (count >= buffer.Length) throw new InvalidDataException();
        var receipt = ReadReceipt(new string(buffer, 0, count));
        if (!string.Equals(receipt, id, StringComparison.Ordinal)) throw new InvalidDataException();
        return receipt;
    }

    private static readonly Regex ExceptionLine = new(
        @"^\s*(?:(\d{1,4}[./:-]\S*\s+\d{1,2}:\d{2}(?::\d{2})?)\s+|\s*(--->)\s*)?(?:\[([^\]\r\n]{1,40})\]\s+)?((?:(?:System|Microsoft|Windows|FlowBar|LumaDock)\.[A-Za-z0-9_.]+Exception)|FlowBar\.Note)\b(?:\s*\((0x[0-9A-Fa-f]{8})\))?(?:\s*:\s*(.*))?",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex FrameLine = new(@"^\s*(?:at|в)\s+([A-Za-z_][A-Za-z0-9_.+<>`]*)\(", RegexOptions.CultureInvariant);
    // Личное в тексте ошибки: пути к файлам, сетевые пути, адреса и почта заменяются, сам текст остаётся (по нему видна причина)
    private static readonly Regex Private = new(@"[A-Za-z]:\\[^\s'""]*|\\\\[^\s'""]+|\w+://\S+|[\w.+-]+@[\w-]+\.[\w.]+", RegexOptions.CultureInvariant);

    private static string CleanText(string text)
    {
        text = Private.Replace(text.Trim(), "<скрыто>");
        return text.Length > 240 ? text[..239] + "…" : text;
    }

    // Ошибки из журнала для отчёта: время, тип, текст без личного и строки стека с кодом программы; одинаковые схлопываются в «×N»; влезает не больше 60 строк, свежие в конце
    public static string CleanLog(IEnumerable<string> lines)
    {
        var order = new List<string>();
        var count = new Dictionary<string, int>();
        var time = new Dictionary<string, string>();
        List<string>? block = null;
        string? blockTime = null;
        bool topFrame = false;
        void Flush()
        {
            if (block is null) return;
            var key = string.Join("\n", block);
            count[key] = count.GetValueOrDefault(key) + 1;
            time[key] = blockTime ?? "";
            order.Remove(key);
            order.Add(key);
            block = null;
        }
        foreach (var line in lines)
        {
            var error = ExceptionLine.Match(line);
            if (error.Success)
            {
                string entry = (error.Groups[3].Success ? "[" + CleanText(error.Groups[3].Value) + "] " : "") + error.Groups[4].Value;
                if (error.Groups[5].Success) entry += " (" + error.Groups[5].Value + ")";
                if (error.Groups[6].Success && error.Groups[6].Value.Trim().Length > 0) entry += ": " + CleanText(error.Groups[6].Value);
                if (error.Groups[2].Success && block is not null) { block.Add("  ---> " + entry); topFrame = true; continue; }
                Flush();
                block = [entry];
                blockTime = error.Groups[1].Success ? error.Groups[1].Value : null;
                topFrame = true;
                continue;
            }
            var frame = FrameLine.Match(line);
            if (block is not null && frame.Success)
            {
                string name = frame.Groups[1].Value;
                if ((topFrame || name.StartsWith("LumaDock.", StringComparison.Ordinal) || name.StartsWith("FlowBar.", StringComparison.Ordinal)) && name.Length <= 160 && block.Count < 12) block.Add("  " + name);
                topFrame = false;
            }
            else if (line.Length > 0 && !char.IsWhiteSpace(line[0])) Flush();
        }
        Flush();
        var result = new List<string>();
        for (int index = order.Count - 1; index >= 0; index--)
        {
            var key = order[index];
            var part = key.Split('\n');
            if (result.Count + part.Length > 60) break;
            part[0] = (time[key].Length > 0 ? time[key] + " " : "") + (count[key] > 1 ? "×" + count[key] + " " : "") + part[0];
            result.InsertRange(0, part);
        }
        return string.Join("\r\n", result);
    }

    // Диагностика, которая уйдёт вместе с отчётом: версия, Windows, мониторы, основные настройки (без имён программ и путей) и последние ошибки
    // Все настройки-переключатели и числа (bool, числа, перечисления) и несколько безопасных строк; списки, пути, города и учётные данные не включаются
    public static string SettingsSummary(Preferences settings)
    {
        var allowedText = new HashSet<string> { "Style", "Accent", "Sound", "UiLanguage", "VoiceEngine", "ClearWidgets", "WindowsOnDocks" };
        var parts = new List<string>();
        foreach (var property in typeof(Preferences).GetProperties())
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;
            var type = property.PropertyType;
            if (type == typeof(string) ? !allowedText.Contains(property.Name) : !(type == typeof(bool) || type == typeof(int) || type == typeof(double) || type.IsEnum)) continue;
            object? value = property.GetValue(settings);
            parts.Add(property.Name + "=" + (value is IFormattable number ? number.ToString(null, CultureInfo.InvariantCulture) : value));
        }
        return string.Join(", ", parts);
    }

    public static string Diagnostics(Preferences settings, IEnumerable<string>? openWindows = null)
    {
        var text = new StringBuilder();
        text.AppendLine("FlowBar " + VersionText);
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
        {
            if (key is not null && int.TryParse(Convert.ToString(key.GetValue("CurrentBuild"), CultureInfo.InvariantCulture), out int build))
            {
                int.TryParse(Convert.ToString(key.GetValue("UBR"), CultureInfo.InvariantCulture), out int revision);
                text.AppendLine($"Windows, сборка {build}.{revision}");
            }
            else text.AppendLine("Windows " + Environment.OSVersion.Version);
        }
        int number = 0;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            text.AppendLine($"Монитор {++number}{(screen.Primary ? " (основной)" : "")}, {screen.Bounds.Width} × {screen.Bounds.Height}");
        text.AppendLine("Настройки: " + SettingsSummary(settings));
        int apps = settings.Items.Count(item => item.StartsWith("app:", StringComparison.Ordinal));
        text.AppendLine("Виджеты в доке (по порядку): " + string.Join(", ", settings.Items.Select(item => item.StartsWith("app:", StringComparison.Ordinal) ? "app" : item)) + $" (программ: {apps})");
        if (openWindows is not null) text.AppendLine("Открыто сейчас: " + (openWindows.Any() ? string.Join(", ", openWindows) : "ничего"));
        var recent = ActivityLog.Last(40);
        text.AppendLine().AppendLine("Последние действия с панелью (старые сверху)");
        text.AppendLine(recent.Length == 0 ? "Записей нет." : string.Join("\r\n", recent));
        var lines = new List<string>();
        foreach (var name in new[] { "error.old.log", "error.log" })
        {
            var path = System.IO.Path.Combine(AppData.Root, name);
            try { if (File.Exists(path)) lines.AddRange(File.ReadLines(path)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        var clean = CleanLog(lines.Skip(Math.Max(0, lines.Count - 1200)));
        // весь отчёт укладывается в лимит приёмника: если не влезает, отрезаются самые старые ошибки (с целой строки), а не свежие
        int room = DiagnosticsLimit - text.Length - 40;
        if (clean.Length > room) clean = room <= 0 ? "" : clean[^room..].Substring(clean[^room..].IndexOf('\n') + 1);
        text.AppendLine().AppendLine("Последние ошибки");
        text.AppendLine(clean.Length == 0 ? "Записей для отправки нет." : clean);
        return text.ToString();
    }
}
