using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LumaDock;

internal sealed class DesktopActivity(string file)
{
    public sealed class Slice
    {
        public DateTimeOffset Start { get; set; }
        public string App { get; set; } = "";
        public string? Site { get; set; }
        public int Seconds { get; set; }
    }

    private readonly List<Slice> history = Read(file);
    private DateTimeOffset? previous;
    private string? previousApp;
    private string? previousSite;
    private Slice? active;
    private bool dirty;
    private DateTimeOffset stored;

    public void Sample(DateTimeOffset now, string? app, TimeSpan idle, string? site = null)
    {
        if (idle >= TimeSpan.FromMinutes(2)) app = null;
        if (app is not null && previous.HasValue && previousApp is not null)
        {
            var seconds = (int)Math.Round((now - previous.Value).TotalSeconds);
            if (seconds is > 0 and <= 3)
            {
                DateTimeOffset start = previous.Value;
                if (active?.App != previousApp || active.Site != previousSite || active.Start.Date != start.Date || active.Start.Hour != start.Hour)
                {
                    active = new Slice { Start = start, App = previousApp, Site = previousSite };
                    history.Add(active);
                }
                active.Seconds += seconds;
                dirty = true;
            }
            else active = null;
        }
        else active = null;
        previous = now;
        previousApp = app;
        previousSite = app is null ? null : site;
        if (dirty && now - stored >= TimeSpan.FromMinutes(1)) Flush();
    }

    public void Stop()
    {
        previous = null;
        previousApp = null;
        previousSite = null;
        active = null;
        Flush();
    }

    public Slice[] Between(DateTime start, DateTime end) => history
        .Where(slice => slice.Start.LocalDateTime.Date >= start.Date && slice.Start.LocalDateTime.Date < end.Date).ToArray();

    public void Clear()
    {
        history.Clear();
        previous = null;
        previousApp = null;
        previousSite = null;
        active = null;
        dirty = true;
        Flush();
    }

    public void StopSites()
    {
        previousSite = null;
        active = null;
    }

    public void ClearSites()
    {
        foreach (var slice in history) slice.Site = null;
        StopSites();
        dirty = true;
        Flush();
    }

    public void Flush()
    {
        if (!dirty) return;
        try
        {
            var cutoff = DateTimeOffset.Now.AddDays(-90);
            history.RemoveAll(slice => slice.Start < cutoff);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string pending = file + ".tmp";
            File.WriteAllText(pending, JsonSerializer.Serialize(history));
            File.Move(pending, file, true);
            dirty = false;
            stored = DateTimeOffset.Now;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static List<Slice> Read(string file)
    {
        if (!File.Exists(file)) return [];
        try { return JsonSerializer.Deserialize<List<Slice>>(File.ReadAllText(file)) ?? []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
}
