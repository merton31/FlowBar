using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace LumaDock;

internal sealed class AiQuota
{
    public int SessionPercent { get; set; }
    public int WeekPercent { get; set; }
    public DateTime? Reset { get; set; }
    public DateTime Changed { get; set; }
}

internal static class AiMetrics
{
    public static readonly string[] QuotaIds = ["Claude Plan Usage", "ChatGPT Usage", "Gemini Usage", "Grok Usage",
        "GLM Usage", "OpenCode Plan Usage", "Cursor Usage"];
    public static bool Supports(string id) => id is "AI Limits" or "Claude Usage" or "OpenCode Usage" || QuotaIds.Contains(id);
    internal record ModelUsage(string Model, long Input, long Output);

    public static async Task<ModelUsage[]> Claude(string folder)
    {
        return await Task.Run(() =>
        {
            var tally = new Dictionary<string, (long Input, long Output)>();
            var observed = new HashSet<string>();
            var boundary = DateTimeOffset.UtcNow.AddDays(-7);
            var files = new DirectoryInfo(folder).EnumerateFiles("*.jsonl",
                new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 4 })
                .Where(file => file.LastWriteTimeUtc >= boundary.UtcDateTime).OrderByDescending(file => file.LastWriteTimeUtc).Take(500).ToArray();
            foreach (var file in files)
            {
                try
                {
                    foreach (var row in File.ReadLines(file.FullName))
                    {
                        if (row.Length is 0 or > 1_000_000) continue;
                        try
                        {
                            using var data = JsonDocument.Parse(row);
                            var line = data.RootElement;
                            if (!line.TryGetProperty("type", out var category) || category.GetString() != "assistant" ||
                                !line.TryGetProperty("timestamp", out var time) || !time.TryGetDateTimeOffset(out var moment) || moment < boundary ||
                                !line.TryGetProperty("message", out var message) || !message.TryGetProperty("usage", out var usage) ||
                                !message.TryGetProperty("id", out var id) || id.GetString() is not { Length: > 0 } messageId ||
                                !message.TryGetProperty("model", out var model) || model.GetString() is not { Length: > 0 } name ||
                                !observed.Add(messageId)) continue;
                            var prior = tally.GetValueOrDefault(name);
                            tally[name] = (prior.Input + Number(usage, "input_tokens") + Number(usage, "cache_creation_input_tokens")
                                + Number(usage, "cache_read_input_tokens"), prior.Output + Number(usage, "output_tokens"));
                        }
                        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return tally.Select(pair => new ModelUsage(pair.Key, pair.Value.Input, pair.Value.Output))
                .OrderByDescending(pair => pair.Input + pair.Output).ToArray();
        });
    }

    private static long Number(JsonElement item, string key) =>
        item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;

    public static Task<ModelUsage[]> OpenCode(string file) => Task.Run(() =>
    {
        if (sqlite3_open_v2(file, out nint database, 1, 0) != 0)
        {
            if (database != 0) sqlite3_close(database);
            throw new IOException("OpenCode database could not be opened.");
        }
        try
        {
            sqlite3_busy_timeout(database, 1000);
            const string sql = "SELECT data FROM message WHERE time_created >= ? ORDER BY time_created DESC LIMIT 100000";
            if (sqlite3_prepare_v2(database, sql, -1, out var query, 0) != 0)
                throw new IOException("This OpenCode database uses a different message schema.");
            try
            {
                sqlite3_bind_int64(query, 1, DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeMilliseconds());
                var tally = new Dictionary<string, (long Input, long Output)>();
                int code;
                while ((code = sqlite3_step(query)) == 100)
                {
                    var bytes = sqlite3_column_bytes(query, 0);
                    if (bytes is <= 0 or > 1_000_000) continue;
                    string? content = Marshal.PtrToStringUTF8(sqlite3_column_text(query, 0), bytes);
                    if (content is null) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(content);
                        var row = doc.RootElement;
                        if (!row.TryGetProperty("role", out var role) || role.GetString() != "assistant"
                            || !row.TryGetProperty("modelID", out var mdl) || mdl.GetString() is not { Length: > 0 } model
                            || !row.TryGetProperty("tokens", out var tokens)) continue;
                        var prior = tally.GetValueOrDefault(model);
                        long cache = 0;
                        if (tokens.TryGetProperty("cache", out var counts)) cache = Number(counts, "read") + Number(counts, "write");
                        tally[model] = (prior.Input + Number(tokens, "input") + cache, prior.Output + Number(tokens, "output"));
                    }
                    catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
                }
                if (code != 101) throw new IOException("OpenCode usage could not be read.");
                return tally.Select(pair => new ModelUsage(pair.Key, pair.Value.Input, pair.Value.Output))
                    .OrderByDescending(pair => pair.Input + pair.Output).ToArray();
            }
            finally { sqlite3_finalize(query); }
        }
        finally { sqlite3_close(database); }
    });

    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string name, out nint db, int flags, nint vfs);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(nint db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out nint stmt, nint tail);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_int64(nint stmt, int index, long value);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(nint db, int milliseconds);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(nint stmt);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern nint sqlite3_column_text(nint stmt, int column);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_bytes(nint stmt, int column);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(nint stmt);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(nint db);
}
