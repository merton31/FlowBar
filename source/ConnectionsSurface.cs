using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static LumaDock.Visuals;

namespace LumaDock;

internal sealed partial class DockWindow
{
    private void ConnectedSurface(DockPanel content, Window window, string widgetId)
    {
        string id = ConnectedData.BaseId(widgetId);
        var panel = Stack();
        var primary = Text("—", 24, true);
        var status = Text("", 12, false, Muted);
        status.TextWrapping = TextWrapping.Wrap;
        var rows = Stack();
        int priceDays = 30;
        panel.Children.Add(primary);
        panel.Children.Add(status);
        panel.Children.Add(rows);
        var controls = new WrapPanel();
        controls.Margin = new(0, 7, 0, 0);
        controls.Children.Add(Button("Refresh", () => _ = Refresh(true)));
        if (id == "Total Revenue")
        {
            foreach (string provider in ConnectedData.Finance)
                controls.Children.Add(Button(provider, () => ConfigureProvider(provider, () => _ = Refresh(true))));
        }
        else
        {
            var definition = ConnectedData.Providers[id];
            controls.Children.Add(Button("Connect", () => ConfigureProvider(id, () => _ = Refresh(true))));
            controls.Children.Add(Button("Dashboard", () => WindowsShell.Launch(definition.Dashboard)));
        }
        panel.Children.Add(controls);
        content.Children.Add(Scroll(panel));
        Paint();
        if (!smoke) _ = Refresh(false);

        async Task Refresh(bool force)
        {
            status.Text = L10n.T("Refreshing…");
            await model.Connected.Refresh(id, P, force);
            Paint();
        }

        void Paint()
        {
            rows.Children.Clear();
            var dashboard = model.Connected.Dashboards.GetValueOrDefault(id);
            primary.Text = L10n.Metric(dashboard?.Value ?? "—");
            status.Text = L10n.T(model.Connected.Errors.GetValueOrDefault(id) ??
                (dashboard is null ? "Connect this service to load data." : L10n.Metric(dashboard.Detail) + (dashboard.Partial ? " · " + L10n.T("partial results") : "")));
            if (dashboard is null) return;
            if (id == "Stock Chart")
            {
                if (dashboard.Prices is { Length: >= 2 } prices)
                {
                    rows.Children.Add(Segments(priceDays, value => { priceDays = value; Paint(); }, ("1W", 7), ("1M", 30), ("3M", 90)));
                    var chart = new StockTrace(false) { Height = 144, Margin = new(10, 10, 10, 7) };
                    var start = prices[^1].At.AddDays(-priceDays);
                    var visible = prices.Where(price => price.At >= start).ToArray();
                    if (visible.Length >= 2)
                    {
                        chart.Set(visible);
                        rows.Children.Add(Card(chart, 13));
                    }
                    else rows.Children.Add(Text("Not enough daily closes in this period.", 12, false, Muted));
                }
                else if (dashboard.PriceStatus is { } note)
                    rows.Children.Add(Text(note, 12, false, Muted));
            }
            if ((widgetId.EndsWith(" Chart", StringComparison.Ordinal) || id == "Total Revenue") && dashboard.Revenue is { } revenue)
                foreach (var currency in revenue.GroupBy(line => line.Currency).OrderBy(group => group.Key)) RevenueChart(rows, currency.Key, currency);
            foreach (var entry in dashboard.Entries)
            {
                var info = RawText(L10n.Metric(entry.Detail), 12, false, Muted);
                if (entry.Url is not null && Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                {
                    var open = Button("Open", () => WindowsShell.Launch(uri.AbsoluteUri));
                    if (id == "Linear")
                    {
                        var toDo = Button("+ Todo", () =>
                        {
                            if (!P.Todos.Any(todo => todo.Text == entry.Title)) model.AddTodo(entry.Title);
                        });
                        AddRow(rows, entry.Title, RowStack(open, toDo), localizeTitle: false);
                    }
                    else AddRow(rows, entry.Title, RowStack(info, open), localizeTitle: false);
                }
                else AddRow(rows, entry.Title, info);
            }
        }
    }

    private void ConfigureProvider(string id, Action refresh)
    {
        var definition = ConnectedData.Providers[id];
        Surface("Connect · " + id, 510, 290, false, (window, shell) =>
        {
            var panel = Stack(Right(Text(L10n.F($"Connect {L10n.T(definition.Name)}"), 18, true), CloseButton(window)));
            panel.Margin = new(14);
            var prompt = Text(definition.SecretLabel, 13, false, Muted);
            prompt.Margin = new(0, 9, 0, 3);
            var key = PasswordField(L10n.T(definition.SecretLabel));
            var account = Input(definition.AccountLabel ?? "", P.ProviderAccounts.GetValueOrDefault(id) ?? "");
            var status = Text(CredentialStore.Read(id) is null ? "" : "A credential is saved in Windows.", 12, false, Muted);
            panel.Children.Add(prompt);
            panel.Children.Add(key);
            if (definition.AccountLabel is not null) panel.Children.Add(account);
            panel.Children.Add(status);
            panel.Children.Add(RowStack(Button("Save and refresh", () =>
            {
                string value = key.Password.Trim();
                if (value.Length != 0 && !CredentialStore.Save(id, value))
                { status.Text = L10n.T("Windows could not save this credential."); return; }
                if (value.Length == 0 && CredentialStore.Read(id) is null)
                { status.Text = L10n.T("Enter a valid credential."); return; }
                P.ProviderAccounts[id] = account.Text.Trim();
                model.Save(false);
                key.Clear();
                window.Close();
                refresh();
            }, Accent(P)), Button("Disconnect", () =>
            {
                model.Connected.Disconnect(id);
                P.ProviderAccounts.Remove(id);
                model.Save(false);
                window.Close();
                refresh();
            })));
            shell.Child = panel;
        });
    }

    private void RevenueChart(StackPanel rows, string currency, IEnumerable<ConnectedData.Line> entries)
    {
        var points = entries.GroupBy(entry => entry.At.Date).ToDictionary(group => group.Key, group => group.Sum(entry => entry.Amount));
        var chart = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Height = 60, Margin = new(0, 3, 0, 5) };
        decimal peak = Math.Max(1, points.Values.Select(Math.Abs).DefaultIfEmpty(0).Max());
        for (int index = 0; index < 30; index++)
        {
            DateTime day = DateTime.Today.AddDays(index - 29);
            decimal number = points.GetValueOrDefault(day);
            var bar = Card(null, 3, number == 0 ? Line : number < 0 ? Brush("#F4514D") : Accent(P));
            bar.Height = 2 + 56 * (double)(Math.Abs(number) / peak);
            bar.VerticalAlignment = VerticalAlignment.Bottom;
            bar.Margin = new(1, 0, 1, 0);
            bar.ToolTip = $"{day:MMM dd} · {number:N2} {currency}";
            chart.Children.Add(bar);
        }
        rows.Children.Add(Text(currency, 12, true, Muted));
        rows.Children.Add(chart);
    }
}
