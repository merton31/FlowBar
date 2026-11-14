using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace FlowBar
{
    partial class Bar
    {
        // Укажите адрес приёмника при выпуске. Для проверки на своём ПК можно задать FLOWBAR_REPORT_URL.
        const string BugReportUrl = "https://flowbat-reports.netlify.app/bug-report";
        const int BugReportMaxBytes = 32 * 1024;
        static Window bugReportWindow;
        static readonly Regex ReportException = new Regex(
            @"^\s*(?:(\d{1,4}[./:-]\S*\s+\d{1,2}:\d{2}(?::\d{2})?(?:\s+(?:AM|PM))?)\s+|\s*(--->)\s*)?((?:System|Microsoft|Windows|FlowBar)\.[A-Za-z0-9_.]+Exception)\b(?:\s*\((0x[0-9A-Fa-f]{8})\))?(?:\s*:\s*(.*))?",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        static readonly Regex ReportFrame = new Regex(
            @"^\s*(?:at|в)\s+([A-Za-z_][A-Za-z0-9_.+<>`]*)\(",
            RegexOptions.CultureInvariant);
        // Личное в тексте ошибки: пути к файлам, сетевые пути, адреса и почта — заменяются, сам текст остаётся (по нему видна причина)
        static readonly Regex ReportPrivate = new Regex(
            @"[A-Za-z]:\\[^\s'""]*|\\\\[^\s'""]+|\w+://\S+|[\w.+-]+@[\w-]+\.[\w.]+",
            RegexOptions.CultureInvariant);

        static string CleanReportText(string text)
        {
            text = ReportPrivate.Replace(text.Trim(), "<скрыто>");
            return text.Length > 240 ? text.Substring(0, 239) + "…" : text; // тексты WPF длинные: на 160 знаках обрывалась сама суть
        }

        // Ошибки из журнала для отчёта: время, тип, текст без личного, верхняя строка стека (где ошибка возникла) и строки FlowBar.
        // Одинаковые ошибки схлопываются в одну запись «×N» со временем последнего раза — иначе один частый сбой вытеснял все остальные.
        internal static string CleanBugReportLog(IEnumerable<string> lines)
        {
            var order = new List<string>();
            var count = new Dictionary<string, int>();
            var time = new Dictionary<string, string>();
            List<string> block = null;
            string blockTime = null;
            bool topFrame = false;
            Action flush = delegate
            {
                if (block == null) return;
                var key = string.Join("\r\n", block);
                int n;
                count.TryGetValue(key, out n);
                count[key] = n + 1;
                time[key] = blockTime ?? "";
                order.Remove(key);
                order.Add(key);
                block = null;
            };
            foreach (var line in lines)
            {
                var error = ReportException.Match(line);
                if (error.Success)
                {
                    string entry = error.Groups[3].Value;
                    if (error.Groups[4].Success) entry += " (" + error.Groups[4].Value + ")";
                    if (error.Groups[5].Success && error.Groups[5].Value.Trim().Length > 0) entry += ": " + CleanReportText(error.Groups[5].Value);
                    if (error.Groups[2].Success && block != null) { block.Add("  ---> " + entry); topFrame = true; continue; }
                    flush();
                    block = new List<string> { entry };
                    blockTime = error.Groups[1].Success ? error.Groups[1].Value : null;
                    topFrame = true;
                    continue;
                }
                var frame = ReportFrame.Match(line);
                if (block != null && frame.Success)
                {
                    string name = frame.Groups[1].Value;
                    if ((topFrame || name.StartsWith("FlowBar.", StringComparison.Ordinal)) && name.Length <= 160 && block.Count < 12) block.Add("  " + name);
                    topFrame = false;
                }
                else if (line.Length > 0 && !char.IsWhiteSpace(line[0])) flush();
            }
            flush();
            // Самые свежие записи — в конце; старые отбрасываются, когда не влезают в 60 строк
            var result = new List<string>();
            for (int i = order.Count - 1; i >= 0; i--)
            {
                var key = order[i];
                var part = key.Split(new[] { "\r\n" }, StringSplitOptions.None);
                if (result.Count + part.Length > 60) break;
                part[0] = (time[key].Length > 0 ? time[key] + " " : "") + (count[key] > 1 ? "×" + count[key] + " " : "") + part[0];
                result.InsertRange(0, part);
            }
            return string.Join("\r\n", result);
        }

        static string BuildBugDiagnostics()
        {
            var result = new StringBuilder();
            result.AppendLine("FlowBar " + Setup.VersionText);
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                int build, revision;
                if (key != null && int.TryParse(Convert.ToString(key.GetValue("CurrentBuild"), CultureInfo.InvariantCulture), out build))
                {
                    int.TryParse(Convert.ToString(key.GetValue("UBR"), CultureInfo.InvariantCulture), out revision);
                    result.AppendLine("Windows, сборка " + build + "." + revision);
                }
                else result.AppendLine("Windows " + Environment.OSVersion.Version);
            }
            int index = 0;
            foreach (var bar in bars)
                result.AppendLine("Монитор " + (++index) + (bar.scr.Primary ? " (основной)" : "") +
                    ", " + bar.scr.Bounds.Width + " × " + bar.scr.Bounds.Height + ", масштаб " + Math.Round(bar.MonScale * 100) + "%");
            var lines = new List<string>();
            lock (logLock)
                foreach (var name in new[] { "error.old.log", "error.log" })
                    try { if (File.Exists(F(name))) lines.AddRange(File.ReadLines(F(name))); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            var clean = CleanBugReportLog(lines.Skip(Math.Max(0, lines.Count - 400)));
            result.AppendLine().AppendLine("Последние ошибки");
            result.AppendLine(clean.Length == 0 ? "Записей для отправки нет." : clean);
            return result.ToString();
        }

        internal static Uri BugReportEndpoint(string url)
        {
            Uri result;
            return Uri.TryCreate(url, UriKind.Absolute, out result) &&
                result.Scheme == Uri.UriSchemeHttps && result.UserInfo.Length == 0 && result.Fragment.Length == 0 && result.Query.Length == 0 ? result : null;
        }

        internal static string ReadBugReportReceipt(string json)
        {
            var result = new JavaScriptSerializer { MaxJsonLength = 4096 }.Deserialize<Dictionary<string, object>>(json);
            object ok, id;
            var number = result != null && result.TryGetValue("report_id", out id) ? id as string : null;
            if (result == null || !result.TryGetValue("ok", out ok) || !Equals(ok, true) ||
                number == null || !Regex.IsMatch(number, @"\A[A-Za-z0-9_-]{1,64}\z"))
                throw new InvalidDataException();
            return number;
        }

        static string SendBugReport(Uri endpoint, string id, string description, string report)
        {
            var payload = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new
            {
                report_id = id, version = Setup.VersionText, description = description, report = report
            }));
            if (payload.Length > BugReportMaxBytes) throw new InvalidDataException();
            var req = (HttpWebRequest)WebRequest.Create(endpoint);
            req.Method = "POST"; req.ContentType = "application/json; charset=utf-8"; req.Accept = "application/json";
            req.AllowAutoRedirect = false; req.Timeout = 12000; req.ReadWriteTimeout = 12000;
            req.ContentLength = payload.Length; req.UserAgent = "FlowBar/" + Setup.VersionText;
            using (var stream = req.GetRequestStream()) stream.Write(payload, 0, payload.Length);
            using (var response = (HttpWebResponse)req.GetResponse())
            {
                if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300) throw new InvalidDataException();
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    var buffer = new char[4097];
                    int count = reader.ReadBlock(buffer, 0, buffer.Length);
                    if (count >= buffer.Length) throw new InvalidDataException();
                    var receipt = ReadBugReportReceipt(new string(buffer, 0, count));
                    if (!string.Equals(receipt, id, StringComparison.Ordinal)) throw new InvalidDataException();
                    return receipt;
                }
            }
        }

        static void ShowBugReport()
        {
            if (bugReportWindow != null) { bugReportWindow.Activate(); return; }
            var w = AppWindow("Сообщить об ошибке", 660, 625);
            if (settings != null) { w.Owner = settings; w.WindowStartupLocation = WindowStartupLocation.CenterOwner; w.ShowInTaskbar = false; }
            var endpoint = BugReportEndpoint((DemoMode ? Environment.GetEnvironmentVariable("FLOWBAR_REPORT_URL") : null) ?? BugReportUrl);
            var diagnostic = "Диагностика недоступна.";
            try { diagnostic = BuildBugDiagnostics(); } catch (Exception ex) { Log(ex); }
            var body = new StackPanel { Margin = new Thickness(22, 18, 22, 16) };
            body.Children.Add(SH("Сообщить об ошибке"));
            body.Children.Add(SHint("Опишите, что произошло и как это повторить. Проверьте данные перед отправкой."));
            var description = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 88, MaxLength = 1000,
                Padding = new Thickness(8), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            System.Windows.Automation.AutomationProperties.SetName(description, "Описание ошибки");
            body.Children.Add(SSub("Описание ошибки"));
            body.Children.Add(description);
            var include = CheckRow("Приложить диагностику", null, true, on => { });
            body.Children.Add(include);
            var preview = new TextBox { Text = diagnostic, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 162,
                Padding = new Thickness(8), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
            System.Windows.Automation.AutomationProperties.SetName(preview, "Предпросмотр диагностики");
            body.Children.Add(preview);
            var status = SHint(endpoint == null ? "Приёмник ещё не подключён. Отчёт можно сохранить в файл." :
                "После отправки отчёт получит разработчик FlowBar.");
            System.Windows.Automation.AutomationProperties.SetLiveSetting(status, System.Windows.Automation.AutomationLiveSetting.Polite);
            bool sending = false, sent = false;
            string requestId = Guid.NewGuid().ToString("N");
            Border send = null;
            Action refresh = () =>
            {
                preview.Visibility = include.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                send.IsEnabled = endpoint != null && !sending && !sent && !string.IsNullOrWhiteSpace(description.Text);
                send.Opacity = send.IsEnabled ? 1 : 0.55;
            };
            Action edited = () =>
            {
                requestId = Guid.NewGuid().ToString("N"); sent = false;
                if (endpoint != null) status.Text = "После отправки отчёт получит разработчик FlowBar.";
                refresh();
            };
            send = Btn("Отправить", async () =>
            {
                if (send == null || !send.IsEnabled) return;
                var problem = description.Text.Trim();
                var data = include.IsChecked == true ? diagnostic : "";
                var id = requestId;
                sending = true; description.IsReadOnly = true; include.IsEnabled = false;
                refresh(); status.Text = "Отправляем отчёт…";
                string message, receipt = null;
                try
                {
                    receipt = await Task.Run(() => SendBugReport(endpoint, id, problem, data)).ConfigureAwait(false);
                    message = "Отчёт доставлен. Номер " + receipt + ".";
                }
                catch (WebException ex)
                {
                    var response = ex.Response as HttpWebResponse;
                    bool limited = response != null && (int)response.StatusCode == 429;
                    if (response != null) response.Dispose();
                    message = limited ? "Слишком много запросов. Повторите позже или сохраните файл." :
                        "Доставка не подтверждена. Повторите отправку или сохраните файл.";
                }
                catch (Exception) { message = "Доставка не подтверждена. Отчёт можно сохранить в файл."; }
                await w.Dispatcher.InvokeAsync(() =>
                {
                    sent = receipt != null;
                    sending = false; description.IsReadOnly = false; include.IsEnabled = true;
                    status.Text = message; refresh();
                });
            }, true);
            var save = Btn("Сохранить файл", () =>
            {
                try
                {
                    var dialog = new SaveFileDialog { FileName = "FlowBar-отчёт-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm"),
                        DefaultExt = ".txt", Filter = "Текстовый файл (*.txt)|*.txt" };
                    if (dialog.ShowDialog(w) != true) return;
                    var report = description.Text.Trim();
                    if (include.IsChecked == true) report += "\r\n\r\n" + diagnostic;
                    File.WriteAllText(dialog.FileName, report.TrimStart(), Encoding.UTF8);
                    status.Text = "Отчёт сохранён.";
                }
                catch (Exception) { status.Text = "Не удалось сохранить файл. Выберите другую папку."; }
            });
            var actions = endpoint == null ? CardActions(save) : CardActions(send, save);
            body.Children.Add(actions); body.Children.Add(status);
            description.TextChanged += (s, e) => edited();
            include.Checked += (s, e) => edited(); include.Unchecked += (s, e) => edited();
            refresh();
            w.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            StyleInputs(w, CardFg);
            w.Closed += (s, e) => bugReportWindow = null;
            bugReportWindow = w; w.Show(); w.Activate(); description.Focus();
        }
    }
}
