using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace LumaDock;

// Доступ для агентов (Claude Code, Codex и других) через MCP, как во FlowBar: запуск «FlowBar.exe --mcp» — сервер MCP по stdio,
// он пересылает вызовы работающему приложению через именованный канал (только для текущего пользователя).
// Задачи и заметки меняет само приложение, поэтому интерфейс обновляется сразу. Включается в «Настройки → Общие»;
// чтение задач и заметок разрешается отдельным переключателем, запись — только при включённом доступе.
internal static class Agent
{
    private const int MaxLine = 65536, MaxReply = 1_000_000, MaxText = 20000;
    internal static string PipeName = "FlowBar2Agent";
    // русский текст в ответе не превращается в \uXXXX, иначе длинные заметки не помещались бы в ответ
    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly (string Name, string Title, string Schema)[] Tools =
    [
        ("add_task", "Add a task to the FlowBar to-do list. Optional due date and time.",
            """{"type":"object","properties":{"text":{"type":"string","maxLength":240},"due":{"type":"string","description":"yyyy-MM-dd or yyyy-MM-dd HH:mm"}},"required":["text"]}"""),
        ("list_tasks", "List the FlowBar tasks (needs read access).",
            """{"type":"object","properties":{"include_done":{"type":"boolean"}}}"""),
        ("complete_task", "Mark the first open task with this exact text as done.",
            """{"type":"object","properties":{"text":{"type":"string","maxLength":240}},"required":["text"]}"""),
        ("add_note", "Append a dated entry to the FlowBar notes.",
            """{"type":"object","properties":{"text":{"type":"string","maxLength":4000}},"required":["text"]}"""),
        ("read_notes", "Read the FlowBar notes (needs read access).", """{"type":"object","properties":{}}""")
    ];

    internal static int ToolCount => Tools.Length;

    // ---- Работающее приложение: принимает вызовы из канала и выполняет их в потоке интерфейса ----

    public static void Start(DockModel model, Dispatcher ui)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync();
                    _ = Task.Run(() => ServeConnection(pipe, model, ui));
                }
                catch (Exception ex)
                {
                    // любая ошибка приёма не должна убивать цикл, иначе инструменты агентов молча замолчат до перезапуска
                    ErrorLog.Write(ex, "Agent pipe");
                    await Task.Delay(1000);
                }
            }
        });
    }

    private static async Task ServeConnection(NamedPipeServerStream pipe, DockModel model, Dispatcher ui)
    {
        using (pipe)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                string? line = await ReadLine(pipe, timeout.Token);
                string reply;
                try
                {
                    var request = JsonNode.Parse(line ?? "")?.AsObject() ?? throw new JsonException();
                    string tool = request["tool"]?.GetValue<string>() ?? "";
                    var args = request["args"] as JsonObject ?? new JsonObject();
                    var (ok, text) = await ui.InvokeAsync(() => Run(model, tool, args));
                    reply = new JsonObject { ["ok"] = ok, ["text"] = text }.ToJsonString(Relaxed);
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
                {
                    reply = new JsonObject { ["ok"] = false, ["text"] = "Bad request." }.ToJsonString();
                }
                var bytes = Encoding.UTF8.GetBytes(reply + "\n");
                await pipe.WriteAsync(bytes, timeout.Token);
                await pipe.FlushAsync(timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    // Одна строка до перевода строки, не длиннее MaxLine байт
    private static async Task<string?> ReadLine(Stream stream, CancellationToken stop, int limit = MaxLine)
    {
        var buffer = new MemoryStream();
        var one = new byte[1];
        while (buffer.Length < limit && await stream.ReadAsync(one, stop) == 1)
        {
            if (one[0] == (byte)'\n') return Encoding.UTF8.GetString(buffer.ToArray());
            buffer.WriteByte(one[0]);
        }
        return buffer.Length >= limit || buffer.Length == 0 ? null : Encoding.UTF8.GetString(buffer.ToArray());
    }

    // Выполняет инструмент; вызывать только в потоке интерфейса
    internal static (bool Ok, string Text) Run(DockModel model, string tool, JsonObject args)
    {
        var p = model.Settings;
        if (!p.Agent) return (false, "Agent access is turned off in the FlowBar settings.");
        string Arg(string name) => args[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text.Trim() : "";
        switch (tool)
        {
            case "add_task":
            {
                string text = Arg("text").Replace('\r', ' ').Replace('\n', ' ');
                if (text.Length is 0 or > 240) return (false, "The task text must be 1 to 240 characters.");
                DateTime? due = null;
                string dueText = Arg("due");
                if (dueText.Length > 0)
                {
                    if (!DateTime.TryParseExact(dueText, ["yyyy-MM-dd", "yyyy-MM-dd HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                        return (false, "The due date must look like 2026-11-18 or 2026-11-18 09:30.");
                    due = parsed;
                }
                model.AddTodo(text, due);
                return (true, "Task added.");
            }
            case "list_tasks":
            {
                if (!p.AgentRead) return (false, "Reading tasks is turned off in the FlowBar settings.");
                bool all = args["include_done"] is JsonValue flag && flag.TryGetValue<bool>(out var include) && include;
                var lines = p.Todos.Where(todo => all || !todo.Done)
                    .Select(todo => (todo.Done ? "[x] " : "[ ] ") + todo.Text + (todo.Due is { } due ? " (due " + due.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ")" : ""));
                string list = string.Join("\n", lines);
                return (true, list.Length == 0 ? "No tasks." : list.Length > MaxText ? list[..MaxText] : list);
            }
            case "complete_task":
            {
                string text = Arg("text");
                var todo = p.Todos.FirstOrDefault(item => !item.Done && string.Equals(item.Text, text, StringComparison.OrdinalIgnoreCase));
                if (todo is null) return (false, "No open task with this text.");
                model.ToggleTodo(todo);
                return (true, "Task completed.");
            }
            case "add_note":
            {
                string text = Arg("text");
                if (text.Length is 0 or > 4000) return (false, "The note must be 1 to 4000 characters.");
                Dictation.AppendToNotes(model, text);
                model.Save();
                return (true, "Note added.");
            }
            case "read_notes":
                if (!p.AgentRead) return (false, "Reading notes is turned off in the FlowBar settings.");
                return (true, p.Notes.Length == 0 ? "No notes." : p.Notes.Length > MaxText ? p.Notes[^MaxText..] : p.Notes);
            default:
                return (false, "Unknown tool.");
        }
    }

    // ---- Процесс «--mcp»: JSON-RPC по stdio, вызовы идут в приложение через канал ----

    public static int ServeMcp()
    {
        string? pipe = Environment.GetEnvironmentVariable("LUMADOCK_AGENT_PIPE");
        if (pipe is not null && Regex.IsMatch(pipe, "^[A-Za-z0-9-]{1,64}$")) PipeName = pipe;
        using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        string? line;
        while ((line = input.ReadLine()) is not null)
        {
            string? reply = HandleMcp(line, Forward);
            if (reply is not null) output.WriteLine(reply);
        }
        return 0;
    }

    // Один запрос JSON-RPC; null — уведомление, ответа не нужно. call — как выполнить инструмент (в тестах подменяется)
    internal static string? HandleMcp(string line, Func<string, JsonObject, (bool Ok, string Text)> call)
    {
        try { return HandleMcpCore(line, call); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            // поле не того типа (например, число вместо строки): процесс не падает, агент получает ошибку запроса
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = null, ["error"] = new JsonObject { ["code"] = -32600, ["message"] = "Invalid request" } }.ToJsonString();
        }
    }

    private static string? HandleMcpCore(string line, Func<string, JsonObject, (bool Ok, string Text)> call)
    {
        JsonObject request;
        try { request = JsonNode.Parse(line)?.AsObject() ?? throw new JsonException(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = null, ["error"] = new JsonObject { ["code"] = -32700, ["message"] = "Parse error" } }.ToJsonString();
        }
        var id = request["id"]?.DeepClone();
        string method = request["method"]?.GetValue<string>() ?? "";
        if (id is null) return null;
        JsonNode? result;
        switch (method)
        {
            case "initialize":
                result = new JsonObject
                {
                    ["protocolVersion"] = request["params"]?["protocolVersion"]?.GetValue<string>() ?? "2025-06-18",
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "lumadock", ["version"] = typeof(Agent).Assembly.GetName().Version?.ToString(3) ?? "0" }
                };
                break;
            case "ping": result = new JsonObject(); break;
            case "tools/list":
                result = new JsonObject
                {
                    ["tools"] = new JsonArray(Tools.Select(tool => (JsonNode)new JsonObject
                    {
                        ["name"] = tool.Name, ["description"] = tool.Title, ["inputSchema"] = JsonNode.Parse(tool.Schema)
                    }).ToArray())
                };
                break;
            case "tools/call":
            {
                string name = request["params"]?["name"]?.GetValue<string>() ?? "";
                var args = request["params"]?["arguments"] as JsonObject ?? new JsonObject();
                if (!Tools.Any(tool => tool.Name == name))
                    return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = -32602, ["message"] = "Unknown tool" } }.ToJsonString();
                var (ok, text) = call(name, args);
                result = new JsonObject
                {
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
                    ["isError"] = !ok
                };
                break;
            }
            default:
                return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Method not found" } }.ToJsonString();
        }
        return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToJsonString();
    }

    // Передача вызова работающему приложению; если оно не запущено, агент получает понятную ошибку
    private static (bool Ok, string Text) Forward(string tool, JsonObject args)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            pipe.Connect(2000);
            var request = Encoding.UTF8.GetBytes(new JsonObject { ["tool"] = tool, ["args"] = args.DeepClone() }.ToJsonString() + "\n");
            pipe.Write(request, 0, request.Length);
            pipe.Flush();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            string? line = ReadLine(pipe, timeout.Token, MaxReply).GetAwaiter().GetResult();
            if (line is null) return (false, "FlowBar returned no answer.");
            var reply = JsonNode.Parse(line)?.AsObject() ?? throw new JsonException();
            return (reply["ok"]?.GetValue<bool>() ?? false, reply["text"]?.GetValue<string>() ?? "");
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or JsonException or OperationCanceledException or InvalidOperationException)
        {
            return (false, "FlowBar is not running or agent access is unavailable.");
        }
    }
}
