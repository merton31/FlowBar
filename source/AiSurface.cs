using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static LumaDock.Visuals;

namespace LumaDock;

internal sealed partial class DockWindow
{
    private void AiSurface(DockPanel content, Window window, string id)
    {
        if (id is "Claude Usage" or "OpenCode Usage")
        {
            LocalUsageSurface(content, id == "Claude Usage");
            return;
        }
        var panel = Stack();
        var list = Stack();
        void Paint()
        {
            list.Children.Clear();
            var providers = id == "AI Limits" ? AiMetrics.QuotaIds : new[] { id };
            foreach (string provider in providers)
            {
                var quota = P.Quotas.GetValueOrDefault(provider);
                string label = quota is null ? "Add readings" : L10n.F($"Session {quota.SessionPercent}% · Week {quota.WeekPercent}%");
                AddRow(list, provider, Button(label, () => EditQuota(provider, Paint), quota is null ? Row : SurfaceInk));
                if (quota is not null)
                {
                    var meta = Text(L10n.F($"Updated {quota.Changed:g}") + (quota.Reset.HasValue ? L10n.F($" · Reset {quota.Reset:g}") : ""), 11, false, Muted);
                    meta.Margin = new(12, 0, 0, 4);
                    list.Children.Add(meta);
                }
            }
        }
        panel.Children.Add(Text("Manually entered usage readings", 13, false, Muted));
        panel.Children.Add(list);
        content.Children.Add(Scroll(panel));
        Paint();
    }

    private void EditQuota(string provider, Action refresh) => Surface(provider, 500, 294, false, (window, shell) =>
    {
        var existing = P.Quotas.GetValueOrDefault(provider);
        var panel = Stack(Right(Text(provider, 17, true), CloseButton(window)));
        panel.Margin = new(14);
        var session = Input("0–100", existing?.SessionPercent.ToString() ?? "", "Session used (%)");
        var week = Input("0–100", existing?.WeekPercent.ToString() ?? "", "Week used (%)");
        session.Width = week.Width = 82;
        AddRow(panel, "Session used (%)", session);
        AddRow(panel, "Week used (%)", week);
        var date = DateField("Reset date", 128, Accent(P), existing?.Reset);
        AddRow(panel, "Reset date", date);
        var status = Text("", 12, false, Muted);
        panel.Children.Add(status);
        panel.Children.Add(Button("Save", () =>
        {
            if (!int.TryParse(session.Text, out var daily) || daily is < 0 or > 100 ||
                !int.TryParse(week.Text, out var usage) || usage is < 0 or > 100)
            { status.Text = L10n.T("Enter percentages between 0 and 100."); return; }
            P.Quotas[provider] = new AiQuota { SessionPercent = daily, WeekPercent = usage,
                Reset = date.SelectedDate, Changed = DateTime.Now };
            model.Save(false);
            window.Close();
            refresh();
        }, Accent(P)));
        shell.Child = panel;
    });

    private void LocalUsageSurface(DockPanel parent, bool claude)
    {
        var panel = Stack();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var defaultPath = claude ? Path.Combine(profile, ".claude", "projects")
            : Path.Combine(profile, ".local", "share", "opencode", "opencode.db");
        string? configured = claude ? P.ClaudeProjects : P.OpenCodeDatabase;
        var input = Input(claude ? "Claude Code projects folder" : "OpenCode database", configured ?? defaultPath);
        var state = Text("Local token totals · last seven days", 12, false, Muted);
        var rows = Stack();
        panel.Children.Add(Right(input, Button("Scan", () => _ = Read(true))));
        panel.Children.Add(state);
        panel.Children.Add(rows);
        parent.Children.Add(Scroll(panel));
        if (configured is not null) _ = Read(false);
        async Task Read(bool save)
        {
            string path = input.Text.Trim();
            if (claude ? !Directory.Exists(path) : !File.Exists(path))
            { state.Text = L10n.T(claude ? "Choose a Claude Code projects directory." : "Enter the local opencode.db path."); return; }
            state.Text = L10n.T("Reading local usage…");
            try
            {
                var metrics = claude ? await AiMetrics.Claude(path) : await AiMetrics.OpenCode(path);
                if (claude) model.ClaudeUsage = metrics;
                else model.OpenCodeUsage = metrics;
                if (save)
                {
                    if (claude) P.ClaudeProjects = path;
                    else P.OpenCodeDatabase = path;
                    model.Save(false);
                }
                rows.Children.Clear();
                state.Text = L10n.T(metrics.Length == 0 ? "No local token records found" : "Local token totals · last seven days");
                foreach (var usage in metrics)
                    AddRow(rows, usage.Model, Text(L10n.F($"{usage.Input:N0} in  /  {usage.Output:N0} out"), 12, false, Muted));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { state.Text = L10n.T("Unable to read this directory."); }
        }
    }
}
