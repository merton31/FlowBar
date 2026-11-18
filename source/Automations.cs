using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LumaDock;

internal sealed class AutomationPreferences
{
    public bool Subscriptions { get; set; } = true;
    public bool Meeting { get; set; } = true;
    public bool FocusMusic { get; set; } = true;
    public bool Health { get; set; } = true;
    public bool WorkInbox { get; set; } = true;
    public bool AiFallback { get; set; } = true;
    public bool Milestones { get; set; } = true;
    public bool Sunset { get; set; }
    public bool Quiet { get; set; } = true;
    public string QuietFrom { get; set; } = "22:00";
    public string QuietTo { get; set; } = "08:00";
    public List<Subscription> Renewals { get; set; } = [];
    public Dictionary<string, DateTime> Fired { get; set; } = new();
}

internal sealed class Subscription
{
    public string Name { get; set; } = "";
    public DateTime Next { get; set; }
}

internal sealed class AutomationRunner(DockModel model)
{
    private AutomationPreferences Settings => model.Settings.Automations;
    private DateTime lastScan;
    private bool resumeMedia;
    private bool? lastSunMode;
    private int hotSeconds;
    public event Action<string, string>? Notify;

    public void FocusChanged()
    {
        if (!Settings.FocusMusic) { resumeMedia = false; return; }
        if (model.OnBreak && model.Media.Current?.State == "Playing")
        {
            resumeMedia = true;
            WindowsShell.Key(0xB3);
        }
        else if (!model.OnBreak && resumeMedia)
        {
            resumeMedia = false;
            if (model.Media.Current?.State == "Paused") WindowsShell.Key(0xB3);
        }
    }

    public void Tick(DateTime now)
    {
        if (now - lastScan < TimeSpan.FromSeconds(15)) return;
        lastScan = now;
        bool quiet = IsQuiet(now.TimeOfDay, Settings);
        bool modified = false;
        if (!quiet)
        {
            foreach (var todo in model.Settings.Todos.Where(todo => !todo.Done && !todo.Reminded && todo.Due.HasValue && todo.Due <= now))
            {
                todo.Reminded = true;
                modified = true;
                Notify?.Invoke("Todo reminder", todo.Text);
            }
            if (Settings.Subscriptions)
                foreach (var renewal in Settings.Renewals.Where(item => item.Next > now && item.Next - now <= TimeSpan.FromHours(24)))
                    modified |= Fire($"renewal:{renewal.Name}:{renewal.Next:O}", "Subscription renewal", L10n.F($"{renewal.Name} · {renewal.Next:g}"), now);

            if (Settings.Meeting)
                foreach (var meeting in model.Settings.Events.Where(item => !item.AllDay && item.Start >= now && item.Start - now <= TimeSpan.FromMinutes(5)))
                    if (Fire($"meeting:{meeting.Title}:{meeting.Start:O}", "Meeting soon", meeting.Title, now))
                    {
                        modified = true;
                        if (model.Media.Current?.State == "Playing") WindowsShell.Key(0xB3);
                    }
            if (Settings.WorkInbox && model.Connected.Dashboards.TryGetValue("Linear", out var work))
            {
                var unseen = work.Entries.Where(entry => !Settings.Fired.ContainsKey("work:" + (entry.Url ?? entry.Title))).ToArray();
                if (unseen.Length > 0)
                {
                    foreach (var entry in unseen) Settings.Fired["work:" + (entry.Url ?? entry.Title)] = now;
                    Notify?.Invoke("Work inbox", L10n.F($"{unseen.Length} assigned issue{(unseen.Length == 1 ? "" : "s")} in Linear"));
                    modified = true;
                }
            }
            if (Settings.AiFallback)
            {
                var quotas = model.Settings.Quotas.Where(item => now - item.Value.Changed < TimeSpan.FromDays(2)
                    && (!item.Value.Reset.HasValue || item.Value.Reset > now)).ToArray();
                var alternative = quotas.OrderBy(item => Math.Max(item.Value.SessionPercent, item.Value.WeekPercent))
                    .FirstOrDefault(item => item.Value.SessionPercent < 80 && item.Value.WeekPercent < 80);
                if (alternative.Value is not null)
                    foreach (var item in quotas.Where(item => item.Value.SessionPercent >= 100 || item.Value.WeekPercent >= 100))
                        modified |= Fire($"ai:{item.Key}:{item.Value.Changed:O}", "AI usage limit",
                            L10n.F($"{L10n.T(item.Key)} has reached its recorded limit. {L10n.T(alternative.Key)} has capacity."), now);
            }
            if (Settings.Milestones && model.Connected.Dashboards.TryGetValue("Total Revenue", out var revenue) &&
                !revenue.Partial && revenue.Revenue is { } lines)
            {
                foreach (var currency in lines.GroupBy(line => line.Currency))
                {
                    decimal total = currency.Sum(line => line.Amount);
                    var reached = new[] { 100, 500, 1000, 2500, 5000, 10000, 25000, 50000, 100000, 250000, 500000, 1000000 }
                        .LastOrDefault(value => total >= value);
                    if (reached > 0)
                        modified |= Fire($"milestone:{currency.Key}:{reached}:{now:yyyy-MM}", "Revenue milestone",
                            L10n.F($"{reached:N0} {currency.Key} over the last 30 days"), now);
                }
            }
        }
        if (Settings.Health)
        {
            hotSeconds = model.Temperature is >= 90 ? hotSeconds + 15 : 0;
            if (!quiet && hotSeconds >= 45)
                modified |= Fire($"thermal:{now:yyyy-MM-dd}", "High temperature", L10n.F($"{model.Temperature:F0} °C reported by Windows"), now);
            if (!quiet && now.Minute % 5 == 0)
                foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed))
                    if (drive.TotalSize > 0 && (double)drive.AvailableFreeSpace / drive.TotalSize < .05)
                        modified |= Fire($"disk:{drive.Name}:{now:yyyy-MM-dd}", "Storage is almost full", drive.Name, now);
        }
        if (Settings.Sunset && model.Data.Forecast?.Days.FirstOrDefault() is { } day && day.Date.Date == now.Date &&
            Math.Abs((model.Data.Forecast.Updated - now).TotalHours) < 1)
        {
            bool light = now >= day.Sunrise && now < day.Sunset;
            if (lastSunMode != light)
            {
                lastSunMode = light;
                WindowsShell.SetTheme(light);
            }
        }
        else lastSunMode = null;
        if (modified) model.Save(false);
    }

    private bool Fire(string key, string title, string message, DateTime now)
    {
        if (Settings.Fired.ContainsKey(key)) return false;
        foreach (string expired in Settings.Fired.Where(item => item.Value < now.AddDays(-14)).Select(item => item.Key).ToArray())
            Settings.Fired.Remove(expired);
        Settings.Fired[key] = now;
        Notify?.Invoke(title, message);
        return true;
    }

    internal static bool IsQuiet(TimeSpan current, AutomationPreferences preferences)
    {
        if (!preferences.Quiet || !TimeSpan.TryParse(preferences.QuietFrom, CultureInfo.InvariantCulture, out var from)
            || !TimeSpan.TryParse(preferences.QuietTo, CultureInfo.InvariantCulture, out var to) || from == to) return false;
        return from < to ? current >= from && current < to : current >= from || current < to;
    }
}
