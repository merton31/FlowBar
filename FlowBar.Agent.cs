// Агенты (Claude Code, Codex и другие) управляют FlowBar через MCP.
// «FlowBar.exe --mcp» — сервер MCP на stdio. Сам он ничего не хранит: каждое действие уходит работающей панели
// по именованному каналу и выполняется в ней, в потоке интерфейса. Так данные не расходятся, а панель сразу показывает изменения.
// Канал, а не файлы в %APPDATA%: агент, запущенный из приложения Microsoft Store, видит подменённую папку данных.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;

namespace FlowBar
{
    static class Agent
    {
        const string Pipe = "FlowBarAgent";
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        static readonly UTF8Encoding utf8 = new UTF8Encoding(false);

        // инструмент: имя, описание, свойства «имя:тип:описание» через «;», обязательные через «,»
        public static readonly string[][] Tools = {
            new[] { "add_task", "Добавить задачу в список задач FlowBar.", "text:string:Текст задачи;due:string:Срок — дата ГГГГ-ММ-ДД (необязательно)", "text" },
            new[] { "list_tasks", "Показать задачи FlowBar: несделанные и сделанные.", "", "" },
            new[] { "complete_task", "Отметить задачу сделанной. Ищется по части текста.", "text:string:Текст задачи или его часть", "text" },
            new[] { "add_note", "Дописать строку в заметки FlowBar.", "text:string:Текст заметки", "text" },
            new[] { "read_notes", "Прочитать заметки FlowBar.", "", "" },
            new[] { "add_reminder", "Поставить напоминание: в указанное время FlowBar покажет уведомление. Нужно либо at, либо in_minutes.", "text:string:О чём напомнить;at:string:Местное время, ГГГГ-ММ-ДДTЧЧ:ММ;in_minutes:number:Через сколько минут", "text" },
            new[] { "list_reminders", "Показать ожидающие напоминания.", "", "" },
            new[] { "cancel_reminder", "Отменить напоминание. Ищется по части текста.", "text:string:Текст напоминания или его часть", "text" },
            new[] { "notify", "Показать уведомление FlowBar сейчас.", "title:string:Заголовок;text:string:Текст", "text" },
            new[] { "start_timer", "Запустить таймер FlowBar.", "minutes:number:Длительность в минутах", "minutes" },
            new[] { "list_events", "Ближайшие события из календарей, подключённых к FlowBar.", "days:number:На сколько дней вперёд, по умолчанию 2", "" } };

        static Dictionary<string, object> Schema(string[] t)
        {
            var props = new Dictionary<string, object>();
            foreach (var p in t[2].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)) { var x = p.Split(new[] { ':' }, 3); props[x[0]] = new Dictionary<string, object> { { "type", x[1] }, { "description", x[2] } }; }
            return new Dictionary<string, object> { { "type", "object" }, { "properties", props }, { "required", t[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) } };
        }

        // ---------- сторона агента: MCP на stdio, по одному сообщению JSON-RPC в строке ----------
        public static int RunMcp()
        {
            var stdin = new StreamReader(Console.OpenStandardInput(), utf8); var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
            string line;
            while ((line = stdin.ReadLine()) != null)
            {
                var reply = HandleLine(line, Ask);
                if (reply != null) stdout.WriteLine(reply);
            }
            return 0;
        }
        // ответ на строку запроса; null — отвечать не нужно (уведомление или мусор). ask — выполнение инструмента
        public static string HandleLine(string line, Func<string, Dictionary<string, object>, KeyValuePair<bool, string>> ask)
        {
            Dictionary<string, object> rq; try { rq = js.Deserialize<Dictionary<string, object>>(line); } catch { return null; }
            object id; if (rq == null || !rq.TryGetValue("id", out id) || id == null) return null;
            var method = rq.ContainsKey("method") ? rq["method"] as string : null; if (method == null) return null;
            var pr = rq.ContainsKey("params") ? rq["params"] as Dictionary<string, object> : null;
            var resp = new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", id } };
            try
            {
                if (method == "initialize")
                    resp["result"] = new Dictionary<string, object> { { "protocolVersion", pr != null && pr.ContainsKey("protocolVersion") ? pr["protocolVersion"] : "2024-11-05" },
                        { "capabilities", new Dictionary<string, object> { { "tools", new Dictionary<string, object>() } } },
                        { "serverInfo", new Dictionary<string, object> { { "name", "flowbar" }, { "version", Setup.VersionText } } },
                        { "instructions", "Задачи, заметки, напоминания, таймер и уведомления панели FlowBar на этом компьютере. Действия выполняет работающая панель." } };
                else if (method == "ping") resp["result"] = new Dictionary<string, object>();
                else if (method == "tools/list")
                    resp["result"] = new Dictionary<string, object> { { "tools", Tools.Select(t => new Dictionary<string, object> { { "name", t[0] }, { "description", t[1] }, { "inputSchema", Schema(t) } }).ToArray() } };
                else if (method == "tools/call")
                {
                    var name = pr == null ? null : pr["name"] as string; var args = pr != null && pr.ContainsKey("arguments") ? pr["arguments"] as Dictionary<string, object> : null;
                    var r = Tools.Any(t => t[0] == name) ? ask(name, args ?? new Dictionary<string, object>()) : new KeyValuePair<bool, string>(false, "Нет такого инструмента: " + name);
                    resp["result"] = new Dictionary<string, object> { { "content", new[] { new Dictionary<string, object> { { "type", "text" }, { "text", r.Value } } } }, { "isError", !r.Key } };
                }
                else resp["error"] = new Dictionary<string, object> { { "code", -32601 }, { "message", "Method not found: " + method } };
            }
            catch (Exception ex) { resp.Remove("result"); resp["error"] = new Dictionary<string, object> { { "code", -32603 }, { "message", ex.Message } }; }
            return js.Serialize(resp);
        }
        // запрос работающей панели
        static KeyValuePair<bool, string> Ask(string tool, Dictionary<string, object> args)
        {
            try
            {
                using (var p = new NamedPipeClientStream(".", Pipe, PipeDirection.InOut))
                {
                    try { p.Connect(2500); } catch (TimeoutException) { return new KeyValuePair<bool, string>(false, "FlowBar не запущен или доступ агентов выключен: «Настройки» → «Подключения» → «Агенты»."); }
                    var wr = new StreamWriter(p, utf8) { AutoFlush = true }; var rd = new StreamReader(p, utf8);
                    wr.WriteLine(js.Serialize(new Dictionary<string, object> { { "tool", tool }, { "args", args } }));
                    var d = js.Deserialize<Dictionary<string, object>>(rd.ReadLine() ?? "{}");
                    return new KeyValuePair<bool, string>(d.ContainsKey("ok") && (bool)d["ok"], d.ContainsKey("text") ? d["text"] as string : "");
                }
            }
            catch (Exception ex) { return new KeyValuePair<bool, string>(false, "Нет связи с FlowBar: " + ex.Message); }
        }

        // ---------- сторона панели: канал, по одному запросу на подключение ----------
        static Thread server;
        public static void StartServer()
        {
            if (server != null) return;
            server = new Thread(() =>
            {
                while (true)
                    try
                    {
                        using (var p = new NamedPipeServerStream(Pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None))
                        {
                            p.WaitForConnection();
                            using (var guard = new Timer(_ => { try { p.Dispose(); } catch { } }, null, 5000, Timeout.Infinite)) // клиент молчит дольше 5 с — отключаем, канал свободен для других
                            {
                            var rd = new StreamReader(p, utf8); var wr = new StreamWriter(p, utf8) { AutoFlush = true };
                            KeyValuePair<bool, string> r;
                            try
                            {
                                var rq = js.Deserialize<Dictionary<string, object>>(rd.ReadLine() ?? "{}");
                                guard.Change(Timeout.Infinite, Timeout.Infinite); // запрос получен: если интерфейс занят дольше 5 с, канал не рвём — иначе действие выполнится, а агент получит ошибку и повторит его
                                var tool = rq["tool"] as string; var args = rq["args"] as Dictionary<string, object> ?? new Dictionary<string, object>();
                                r = (KeyValuePair<bool, string>)Application.Current.Dispatcher.Invoke(new Func<KeyValuePair<bool, string>>(() => Bar.AgentCall(tool, args)));
                            }
                            catch (Exception ex) { Bar.Log(ex); r = new KeyValuePair<bool, string>(false, "Ошибка FlowBar: " + ex.Message); }
                            wr.WriteLine(js.Serialize(new Dictionary<string, object> { { "ok", r.Key }, { "text", r.Value } }));
                            p.WaitForPipeDrain();
                            }
                        }
                    }
                    catch (Exception ex) { Bar.Log(ex); Thread.Sleep(1000); }
            }) { IsBackground = true, Name = "FlowBar agent pipe" };
            server.Start();
        }
    }

    partial class Bar
    {
        // ---------- напоминания: время и текст, хранятся в reminders.txt, показываются уведомлением ----------
        class Reminder { public DateTime At; public string Text; }
        static List<Reminder> reminders;
        static void LoadReminders()
        {
            reminders = new List<Reminder>();
            foreach (var l in Lines("reminders.txt")) { int i = l.IndexOf('|'); DateTime at; if (i > 0 && DateTime.TryParseExact(l.Substring(0, i), "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out at)) reminders.Add(new Reminder { At = at, Text = l.Substring(i + 1) }); }
        }
        static void SaveReminders() { Persist("напоминания", () => SafeFile.WriteLines(F("reminders.txt"), reminders.OrderBy(r => r.At).Select(r => r.At.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture) + "|" + r.Text.Replace("\r", " ").Replace("\n", " ")))); }
        static void AddReminder(DateTime at, string text) { if (reminders == null) LoadReminders(); reminders.Add(new Reminder { At = at, Text = text }); SaveReminders(); }
        // варианты «напомнить»: подпись и время
        public static List<Tuple<string, DateTime>> RemindChoices() { return RemindChoices(DateTime.Now); }
        public static List<Tuple<string, DateTime>> RemindChoices(DateTime now)
        {
            var evening = now.Date.AddHours(19); var morning = now.Date.AddDays(1).AddHours(9);
            var l = new List<Tuple<string, DateTime>> { Tuple.Create("через 30 минут", now.AddMinutes(30)), Tuple.Create("через час", now.AddHours(1)) };
            if (evening > now.AddHours(1)) l.Add(Tuple.Create("сегодня в 19:00", evening));
            l.Add(Tuple.Create("завтра в 9:00", morning));
            return l;
        }
        internal static void CheckReminders()
        {
            if (reminders == null) LoadReminders();
            var due = reminders.Where(r => r.At <= DateTime.Now).ToList(); if (due.Count == 0) return;
            foreach (var r in due) { reminders.Remove(r); if (Primary != null) Primary.Notify("Напоминание", r.Text, true, ""); }
            SaveReminders();
        }

        // время напоминания: «at» — местное время, либо «in_minutes»; null — не разобрать
        public static DateTime? AgentWhen(Dictionary<string, object> a, DateTime now)
        {
            object v; DateTime at;
            if (a.TryGetValue("at", out v) && v is string && DateTime.TryParse((string)v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out at)) return at;
            double m; if (a.TryGetValue("in_minutes", out v) && v != null && double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out m) && m > 0) return now.AddMinutes(m);
            return null;
        }
        static string AStr(Dictionary<string, object> a, string k) { object v; return a.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture).Trim() : ""; }

        // действие агента — в потоке интерфейса панели
        internal static KeyValuePair<bool, string> AgentCall(string tool, Dictionary<string, object> a)
        {
            Func<string, KeyValuePair<bool, string>> ok = s => new KeyValuePair<bool, string>(true, s), no = s => new KeyValuePair<bool, string>(false, s);
            if (C("agent", "1") != "1") return no("Доступ агентов выключен: «Настройки FlowBar» → «Подключения» → «Агенты».");
            var p = Primary; if (p == null) return no("Панель FlowBar ещё не готова.");
            string text = AStr(a, "text"); var now = DateTime.Now;
            Info("агент: " + tool); // текст не пишем: журнал уходит в отчёт для разработчика
            if (reminders == null) LoadReminders();
            if ((tool == "list_tasks" || tool == "read_notes" || tool == "list_reminders" || tool == "list_events") && C("agent.read", "0") != "1")
                return no("Чтение задач, заметок и календаря выключено: «Настройки FlowBar» → «Подключения» → «Разрешить агентам читать задачи, заметки и календарь».");
            switch (tool)
            {
                case "add_task":
                    {
                        if (text == "") return no("Пустой текст задачи.");
                        DateTime due; bool hasDue = DateTime.TryParseExact(AStr(a, "due"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out due);
                        if (AStr(a, "due") != "" && !hasDue) return no("Срок нужен в виде ГГГГ-ММ-ДД.");
                        todos.Add(new Todo { Text = text.Replace("\r", " ").Replace("\n", " "), Due = hasDue ? (DateTime?)due : null }); p.SaveTodos();
                        return ok("Задача добавлена: " + text + (hasDue ? " (до " + due.ToString("d MMMM") + ")" : ""));
                    }
                case "list_tasks":
                    return ok(todos.Count == 0 ? "Задач нет." : string.Join("\n", todos.OrderBy(t => t.Done).Select(t => (t.Done ? "[x] " : "[ ] ") + t.Text + (t.Due.HasValue ? " (до " + t.Due.Value.ToString("d MMMM") + ")" : ""))));
                case "complete_task":
                    {
                        var t = todos.FirstOrDefault(x => !x.Done && x.Text.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (text == "" || t == null) return no("Несделанная задача с таким текстом не найдена.");
                        t.Done = true; p.SaveTodos(); return ok("Отмечено сделанным: " + t.Text);
                    }
                case "add_note":
                    {
                        if (text == "") return no("Пустой текст заметки.");
                        // открытый редактор держит свой текст и при сохранении перезаписал бы файл — закрываем его (он сохраняется), потом дописываем
                        All(b => { if (b.panel != null && b.panelTitle == "Заметки") b.panel.Close(); });
                        Func<string, string> append = s => (s ?? "").TrimEnd('\r', '\n') + ((s ?? "").Trim() == "" ? "" : "\r\n") + text + "\r\n";
                        if (unsavedNote != null) { unsavedNote = append(unsavedNote); return ok("Записано в заметки (сохранятся вместе с несохранённым текстом)."); }
                        SafeFile.Write(F("notes.txt"), append(SafeFile.Read(F("notes.txt")))); live.Note = null;
                        return ok("Записано в заметки.");
                    }
                case "read_notes": { var n = (SafeFile.Read(F("notes.txt")) ?? "").Trim(); return ok(n == "" ? "Заметок нет." : n); }
                case "add_reminder":
                    {
                        if (text == "") return no("Пустой текст напоминания.");
                        var at = AgentWhen(a, now); if (at == null) return no("Нужно время: at в виде ГГГГ-ММ-ДДTЧЧ:ММ или in_minutes.");
                        if (at.Value <= now) return no("Это время уже прошло: " + at.Value.ToString("d MMMM HH:mm") + ".");
                        reminders.Add(new Reminder { At = at.Value, Text = text }); SaveReminders();
                        return ok("Напомню " + at.Value.ToString("d MMMM в HH:mm") + ": " + text);
                    }
                case "list_reminders":
                    return ok(reminders.Count == 0 ? "Напоминаний нет." : string.Join("\n", reminders.OrderBy(r => r.At).Select(r => r.At.ToString("d MMMM HH:mm") + " — " + r.Text)));
                case "cancel_reminder":
                    {
                        var r = reminders.OrderBy(x => x.At).FirstOrDefault(x => x.Text.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (text == "" || r == null) return no("Напоминание с таким текстом не найдено.");
                        reminders.Remove(r); SaveReminders(); return ok("Отменено: " + r.Text);
                    }
                case "notify":
                    if (text == "") return no("Пустой текст уведомления.");
                    p.Notify(AStr(a, "title") == "" ? "Сообщение агента" : AStr(a, "title"), text, true, ""); return ok("Уведомление показано.");
                case "start_timer":
                    {
                        double m; if (!double.TryParse(AStr(a, "minutes"), NumberStyles.Float, CultureInfo.InvariantCulture, out m) || m <= 0 || m > 1440) return no("Минуты — число от 1 до 1440.");
                        timerEnd = now.AddMinutes(m); timerLeft = TimeSpan.Zero; timerRunning = true; return ok("Таймер запущен до " + timerEnd.ToString("HH:mm") + ".");
                    }
                case "list_events":
                    {
                        double d; if (!double.TryParse(AStr(a, "days"), NumberStyles.Float, CultureInfo.InvariantCulture, out d) || d <= 0) d = 2;
                        var ev = calEvents.Where(e => e.End > now && e.Start < now.AddDays(Math.Min(d, 60))).OrderBy(e => e.Start).Take(40).ToList();
                        if (calFeeds.Count == 0) return ok("Календари к FlowBar не подключены.");
                        return ok(ev.Count == 0 ? "Событий нет." : string.Join("\n", ev.Select(e => e.Start.ToString(e.AllDay ? "d MMMM" : "d MMMM HH:mm") + " — " + e.Title + (string.IsNullOrEmpty(e.Location) ? "" : " · " + e.Location))));
                    }
            }
            return no("Нет такого инструмента: " + tool);
        }
    }
}
