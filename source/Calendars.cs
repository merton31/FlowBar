using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace LumaDock;

internal sealed class CalendarEvent
{
    public string Title { get; set; } = "";
    public string Location { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool AllDay { get; set; }
    public string Source { get; set; } = "";
}

internal static class Calendars
{
    private static readonly HttpClient web = new() { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 3 * 1024 * 1024 };

    public static async Task<CalendarEvent[]> Load(string source)
    {
        string body;
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            body = await web.GetStringAsync(uri);
        else if (Path.IsPathRooted(source) && File.Exists(source) && new FileInfo(source).Length < 3 * 1024 * 1024)
            body = await File.ReadAllTextAsync(source);
        else throw new FormatException("Choose an ICS file or enter an HTTPS calendar URL.");
        return Parse(body, source, DateTime.Today.AddMonths(-1), DateTime.Today.AddYears(1));
    }

    public static CalendarEvent[] Parse(string body, string source, DateTime first, DateTime last)
    {
        var result = new List<CalendarEvent>();
        var unfolded = new List<string>();
        foreach (string line in body.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Length == 0) continue;
            if ((line[0] == ' ' || line[0] == '\t') && unfolded.Count > 0) unfolded[^1] += line[1..];
            else unfolded.Add(line);
        }
        Dictionary<string, (string Parameters, string Value)>? item = null;
        var exclusions = new List<DateTime>();
        foreach (string line in unfolded)
        {
            if (line == "BEGIN:VEVENT")
            {
                item = new(StringComparer.OrdinalIgnoreCase);
                exclusions.Clear();
                continue;
            }
            if (item is null) continue;
            if (line == "END:VEVENT")
            {
                if (item.TryGetValue("DTSTART", out var start) && ParseTime(start.Value, start.Parameters) is DateTime at &&
                    !item.GetValueOrDefault("STATUS").Value.EqualsOrdinalIgnoreCase("CANCELLED"))
                {
                    var end = item.GetValueOrDefault("DTEND");
                    var duration = (ParseTime(end.Value, end.Parameters) ?? at.AddMinutes(start.Value.Length == 8 ? 1440 : 60)) - at;
                    if (duration <= TimeSpan.Zero) duration = TimeSpan.FromMinutes(30);
                    var repeat = item.GetValueOrDefault("RRULE").Value;
                    foreach (DateTime occurrence in Occurrences(at, repeat, first, last).Where(date => !exclusions.Contains(date)))
                        result.Add(new CalendarEvent { Start = occurrence, End = occurrence + duration, AllDay = start.Value.Length == 8, Source = source,
                            Title = Unescape(item.GetValueOrDefault("SUMMARY").Value) ?? "Event",
                            Location = Unescape(item.GetValueOrDefault("LOCATION").Value) ?? "" });
                }
                item = null;
                if (result.Count >= 10000) break;
                continue;
            }
            int separator = line.IndexOf(':');
            if (separator <= 0) continue;
            string[] key = line[..separator].Split(';', 2);
            string parameters = key.Length == 2 ? key[1] : "";
            string value = line[(separator + 1)..];
            if (key[0] == "EXDATE")
                exclusions.AddRange(value.Split(',').Select(text => ParseTime(text, parameters)).OfType<DateTime>());
            else if (!item.ContainsKey(key[0])) item.Add(key[0], (parameters, value));
        }
        return result.OrderBy(x => x.Start).ToArray();
    }

    private static bool EqualsOrdinalIgnoreCase(this string? value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static DateTime? ParseTime(string? value, string? parameters)
    {
        if (!DateTime.TryParseExact(value, new[] { "yyyyMMdd", "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmmss'Z'" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return null;
        if (value!.EndsWith('Z')) return DateTime.SpecifyKind(time, DateTimeKind.Utc).ToLocalTime();
        var zone = parameters?.Split(';').FirstOrDefault(part => part.StartsWith("TZID=", StringComparison.OrdinalIgnoreCase))?[5..].Trim('"');
        if (zone is null) return time;
        try { return TimeZoneInfo.ConvertTime(time, TimeZoneInfo.FindSystemTimeZoneById(zone), TimeZoneInfo.Local); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { return time; }
    }

    private static IEnumerable<DateTime> Occurrences(DateTime at, string? rule, DateTime first, DateTime last)
    {
        var fields = (rule ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Split('=', 2))
            .Where(part => part.Length == 2).GroupBy(part => part[0]).ToDictionary(group => group.Key, group => group.First()[1]);
        string? frequency = fields.GetValueOrDefault("FREQ");
        if (frequency is not ("DAILY" or "WEEKLY" or "MONTHLY" or "YEARLY"))
        {
            if (at >= first && at < last) yield return at;
            yield break;
        }
        int interval = int.TryParse(fields.GetValueOrDefault("INTERVAL"), out int step) ? Math.Clamp(step, 1, 365) : 1;
        int count = int.TryParse(fields.GetValueOrDefault("COUNT"), out int limit) ? Math.Clamp(limit, 0, 40000) : 40000;
        DateTime until = ParseTime(fields.GetValueOrDefault("UNTIL"), null) ?? last;
        DateTime day = at;
        var weekdays = fields.GetValueOrDefault("BYDAY")?.Split(',').ToHashSet(StringComparer.Ordinal);
        if (frequency == "WEEKLY" && weekdays is { Count: > 0 })
        {
            var abbreviations = new[] { "SU", "MO", "TU", "WE", "TH", "FR", "SA" };
            int remaining = count;
            for (int days = 0; days < 40000 && day < last && day <= until && remaining > 0; days++, day = at.AddDays(days))
            {
                int week = (days + ((int)at.DayOfWeek + 6) % 7) / 7;
                if (week % interval != 0 || !weekdays.Contains(abbreviations[(int)day.DayOfWeek])) continue;
                remaining--;
                if (day >= first) yield return day;
            }
            yield break;
        }
        for (int cycle = 0; cycle < count; cycle++)
        {
            try
            {
                day = frequency switch
                {
                    "DAILY" => at.AddDays(cycle * interval),
                    "WEEKLY" => at.AddDays(cycle * interval * 7.0),
                    "MONTHLY" => at.AddMonths(cycle * interval),
                    _ => at.AddYears(cycle * interval)
                };
            }
            catch (ArgumentOutOfRangeException) { break; }
            if (day >= last || day > until) break;
            if (day >= first) yield return day;
        }
    }

    private static string? Unescape(string? value) => value?.Replace("\\n", "\n", StringComparison.OrdinalIgnoreCase)
        .Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");
}
