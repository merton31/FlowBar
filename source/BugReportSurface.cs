using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static LumaDock.Visuals;

namespace LumaDock;

// Окно «Сообщить об ошибке»: пользователь описывает проблему, видит диагностику, которая уйдёт вместе с ней, и сам нажимает «Отправить» (или сохраняет всё в файл).
internal sealed partial class DockWindow
{
    private const int ReportTextLimit = 10000; // знаков диагностики: вместе с описанием укладывается в 32 КиБ приёмника

    internal Window OpenBugReport() => Surface("Report a problem", 560, 640, false, (window, shell) =>
    {
        var title = (FrameworkElement)Right(Text("Report a problem", 18, true), CloseButton(window));
        title.Margin = new(0, 0, 0, 6);
        var hint = Text("Describe what went wrong. The report goes to the FlowBar developer.", 12.5, false, Muted);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new(0, 0, 0, 10);
        var description = Input("Problem description");
        description.AcceptsReturn = true;
        description.TextWrapping = TextWrapping.Wrap;
        description.MaxLength = 1000;
        description.Height = 130;
        description.VerticalContentAlignment = VerticalAlignment.Top;
        description.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var preview = Input("Diagnostics unavailable");
        preview.IsReadOnly = true;
        preview.AcceptsReturn = true;
        preview.TextWrapping = TextWrapping.NoWrap;
        preview.FontFamily = new("Cascadia Mono, Consolas");
        preview.FontSize = 11.5;
        preview.Height = 190;
        preview.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        preview.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        System.Windows.Automation.AutomationProperties.SetName(preview, L10n.T("Diagnostics"));
        string diagnostics = "";
        try
        {
            diagnostics = BugReport.Diagnostics(P);
            if (diagnostics.Length > ReportTextLimit) diagnostics = diagnostics[..ReportTextLimit];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        bool attach = diagnostics.Length > 0;
        preview.Text = diagnostics;
        var status = Text("", 12.5, false, Muted);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new(2, 8, 0, 0);

        string id = Guid.NewGuid().ToString("N");
        bool busy = false, sent = false;
        Button send = null!;
        void Refresh() => send.IsEnabled = !busy && !sent && description.Text.Trim().Length > 0;
        // правка текста или состава отчёта — это уже другой отчёт: новый номер, отправить можно снова
        void Edited() { id = Guid.NewGuid().ToString("N"); sent = false; Refresh(); }
        string Report() => attach ? diagnostics : "";

        send = Button(Text("Send", 14, true, Paper), () => { }, Accent(P), 12);
        send.Padding = new(18, 7, 18, 7);
        System.Windows.Automation.AutomationProperties.SetName(send, L10n.T("Send"));
        send.Click += async (_, _) =>
        {
            var endpoint = BugReport.Endpoint(BugReport.Url);
            if (endpoint is null || busy) return;
            busy = true;
            Refresh();
            status.Foreground = Muted;
            status.Text = L10n.T("Sending the report…");
            try
            {
                string receipt = await BugReport.SendAsync(endpoint, id, description.Text.Trim(), Report());
                sent = true;
                status.Foreground = Paper;
                status.Text = L10n.F($"Report delivered. Number {receipt}.");
            }
            catch (BugReport.DeliveryException ex) when (ex.Limited)
            {
                status.Text = L10n.T("Too many requests. Try again later or save the file.");
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or OperationCanceledException
                or InvalidDataException or System.Text.Json.JsonException or BugReport.DeliveryException)
            {
                status.Text = L10n.T("Delivery was not confirmed. Repeat sending or save the file.");
            }
            finally { busy = false; Refresh(); }
        };
        var save = Button(Text("Save file", 14, true, Paper), () =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Text|*.txt", FileName = "FlowBar-report.txt", DefaultExt = ".txt" };
            if (dialog.ShowDialog(window) != true) return;
            try
            {
                File.WriteAllText(dialog.FileName, description.Text.Trim() + "\r\n\r\n" + Report(), new UTF8Encoding(true));
                status.Foreground = Paper;
                status.Text = L10n.T("Report saved.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                status.Foreground = Muted;
                status.Text = L10n.T("Could not save the file. Choose another folder.");
            }
        }, Brush("#3A3A3C"), 12);
        save.Padding = new(18, 7, 18, 7);
        save.Margin = new(0, 0, 8, 0);
        description.TextChanged += (_, _) => Edited();

        var panel = new StackPanel { Margin = new(13) };
        panel.Children.Add(title);
        panel.Children.Add(hint);
        panel.Children.Add(description);
        AddToggle(panel, "Attach diagnostics", attach, enabled =>
        {
            attach = enabled && diagnostics.Length > 0;
            preview.Opacity = attach ? 1 : .4;
            Edited();
        });
        preview.Margin = new(0, 4, 0, 0);
        panel.Children.Add(preview);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) };
        buttons.Children.Add(save);
        buttons.Children.Add(send);
        panel.Children.Add(buttons);
        panel.Children.Add(status);
        Refresh();
        shell.Child = Scroll(panel);
    });
}
