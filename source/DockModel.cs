using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LumaDock;

internal sealed class Preferences
{
    public string UiLanguage { get; set; } = "system";
    public string Style { get; set; } = "Liquid";
    public string ClearWidgets { get; set; } = "Black";
    // материал дока (theme, frost, metal, prism) и прозрачность 0–100: открывают живое стекло под доком, см. Looks.cs
    public string Material { get; set; } = "theme";
    public int Transparency { get; set; }
    // помощник, который разбирает продиктованный текст на задачи и заметку: none, codex, claude, agy (см. VoiceAssistant.cs)
    public string VoiceProvider { get; set; } = "none";
    // глобальные сочетания клавиш для задач, диктовки и заметок: «модификаторы,код клавиши» (см. Hotkeys.cs)
    public string[] Hotkeys { get; set; } = ["1,84", "", ""];
    // распознавание речи: handy или встроенный движок local (LocalVoice.cs) и выбранная модель
    public string VoiceEngine { get; set; } = "handy";
    public string VoiceModel { get; set; } = "";
    // Версии моделей распознавания, найденные проверкой обновления (LocalVoice.ModelPin)
    public Dictionary<string, LocalVoice.ModelPin> VoiceModelPins { get; set; } = [];
    // доступ агентов через MCP (AgentServer.cs): запись задач и заметок включена, чтение задач и заметок разрешается отдельно
    public bool Agent { get; set; } = true;
    public bool AgentRead { get; set; }
    public string Accent { get; set; } = "#0A88FF";
    public string? CustomAccent { get; set; }
    public bool UseCustomAccent { get; set; }
    public double Size { get; set; } = 75;
    public double Spacing { get; set; } = 3;
    public double Padding { get; set; } = 3;
    public double Edge { get; set; }
    public int IconSize { get; set; } = 46;
    public bool FullWidth { get; set; }
    public bool Names { get; set; } = true;
    public bool WindowPreviews { get; set; } = true;
    public bool AppCards { get; set; } = true;
    public bool AppBadges { get; set; } = true;
    // Если подключено несколько мониторов, на каждом из них свой док (по умолчанию включено)
    public bool AllMonitors { get; set; } = true;
    // Плеер в доке по умолчанию в виде обложки с кнопками; у уже существующего профиля меняется один раз
    public bool PlayerArtworkApplied { get; set; }
    public bool Magnification { get; set; }
    public bool AutoHide { get; set; }
    public bool ShowOnDesktop { get; set; } = true;
    public bool TrackScreenTime { get; set; } = true;
    public bool TrackWebsites { get; set; }
    public LiveData.Place? WeatherPlace { get; set; }
    public bool WeatherFahrenheit { get; set; }
    public string WorldTimeCity { get; set; } = "NYC";
    public string[] WorldClockCities { get; set; } = ["NYC", "LON", "TYO"];
    public Dictionary<string, string> DataInputs { get; set; } = new();
    public Dictionary<string, string> ProviderAccounts { get; set; } = new();
    public Dictionary<string, AiQuota> Quotas { get; set; } = new();
    public string? ClaudeProjects { get; set; }
    public string? OpenCodeDatabase { get; set; }
    public List<CalendarEvent> Events { get; set; } = [];
    public List<string> CalendarSources { get; set; } = [];
    public Dictionary<string, FileGroup> FileGroups { get; set; } = new();
    public AutomationPreferences Automations { get; set; } = new();
    public double MagnificationScale { get; set; } = 1.4;
    public bool Locked { get; set; }
    public bool PinSystem { get; set; } = true;
    public bool Hour24 { get; set; } = true;
    public bool ReplaceTaskbar { get; set; }
    public bool AutoHideWindows { get; set; }
    public bool AlertSound { get; set; } = true;
    public string Sound { get; set; } = "Crystal";
    public List<string> Items { get; set; } =
        ["Todos", "Start", "app:explorer.exe", "Now Playing Artwork", "CPU", "RAM", "Pomodoro", "Audio Output", "Quick Toggles", "Theme Toggle", "Tray", "Clock"];
    public List<Todo> Todos { get; set; } = [];
    public string Notes { get; set; } = "";
}

internal sealed class FileGroup
{
    public string Name { get; set; } = "Files";
    public List<string> Paths { get; set; } = [];
}

internal sealed class Todo
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public DateTime? Due { get; set; }
    public bool Reminded { get; set; }
}

internal sealed class DockModel
{
    public string Profile { get; }
    public Preferences Settings { get; }
    public event Action? Changed;
    internal RadioController Radios { get; } = new();
    public event Action<string>? SaveFailed;
    public readonly Countdown Focus = new(TimeSpan.FromMinutes(25));
    public readonly Countdown Timer = new(TimeSpan.FromMinutes(5));
    public readonly System.Diagnostics.Stopwatch Stopwatch = new();
    public WindowsShell.Usage Usage { get; set; }
    public (double Volume, bool Muted)? Audio { get; set; }
    public readonly MediaSession Media = new();
    public readonly LinkTraffic Network = new();
    public readonly LiveData Data = new();
    public readonly ConnectedData Connected = new();
    public AiMetrics.ModelUsage[] ClaudeUsage { get; set; } = [];
    public AiMetrics.ModelUsage[] OpenCodeUsage { get; set; } = [];
    public (double Volume, bool Muted)? Microphone { get; set; }
    public double? Temperature { get; set; }
    public double? BrightnessLevel { get; set; }
    public DesktopActivity Activity { get; }
    public AutomationRunner Automation { get; }
    public int FocusRound { get; private set; } = 1;
    public bool OnBreak { get; private set; }

    public DockModel(string? path = null)
    {
        Profile = path ?? Path.Combine(AppData.Root, "profile.json");
        Activity = new DesktopActivity(Path.Combine(Path.GetDirectoryName(Profile)!, "desktop-activity.json"));
        Automation = new AutomationRunner(this);
        bool exists = File.Exists(Profile);
        try
        {
            Settings = exists
                ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Profile)) ?? new()
                : new();
        }
        catch (JsonException) { Settings = new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Settings = new(); }
        if (!exists && path is null)
        {
            var shortcuts = WindowsShell.StartMenuShortcuts();
            var apps = new[] { "WhatsApp", "Telegram", "Spotify", "Discord", "Google Chrome", "Steam", "Codex",
                "Claude", "Gemini", "Ollama", "Happ", "Hermes" };
            var available = apps.Select(name => shortcuts.FirstOrDefault(shortcut =>
                name.Equals(Path.GetFileNameWithoutExtension(shortcut), StringComparison.OrdinalIgnoreCase)))
                .OfType<string>().Select(shortcut => "app:" + shortcut);
            Settings.Items.InsertRange(Settings.Items.IndexOf("app:explorer.exe") + 1, available);
        }
        LocalVoice.Pins = Settings.VoiceModelPins;
        if (!Settings.PlayerArtworkApplied)
        {
            Settings.PlayerArtworkApplied = true;
            int player = Settings.Items.IndexOf("Now Playing");
            if (player >= 0 && !Settings.Items.Contains("Now Playing Artwork")) Settings.Items[player] = "Now Playing Artwork";
        }
        EnsurePinned();
    }

    public void Save(bool redraw = true)
    {
        var pending = Profile + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Profile)!);
            File.WriteAllText(pending, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(pending, Profile, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveFailed?.Invoke(ex.Message);
        }
        if (redraw) Changed?.Invoke();
    }

    // Системные виджеты (звук, переключатели сети, тема) остаются в доке всегда, пока включён «Keep system widgets»
    public bool IsPinned(string id) => Settings.PinSystem && Widgets.SystemPinned.Contains(id);

    // Возвращает в док закреплённые виджеты, которых там нет (например, после старого профиля), перед часами или в конце
    public void EnsurePinned()
    {
        if (!Settings.PinSystem) return;
        foreach (var id in Widgets.SystemPinned)
        {
            if (Settings.Items.Contains(id)) continue;
            int clock = Settings.Items.IndexOf("Clock");
            Settings.Items.Insert(clock >= 0 ? clock : Settings.Items.Count, id);
        }
    }

    public void ToggleWidget(string id)
    {
        if (Settings.Items.Contains(id) && IsPinned(id)) return;
        if (!Settings.Items.Remove(id)) Settings.Items.Add(id);
        Save();
    }

    public bool SelectWidgetStyle(string from, string to)
    {
        int source = Settings.Items.IndexOf(from);
        if (source < 0 || from == to || !Widgets.Styles(from).Contains(to)) return false;
        int target = Settings.Items.IndexOf(to);
        Settings.Items[source] = to;
        if (target >= 0) Settings.Items[target] = from;
        Save();
        return true;
    }

    public void MoveItem(string item, string before)
    {
        if (Settings.Locked || item == before || !Settings.Items.Contains(before) || !Settings.Items.Remove(item)) return;
        Settings.Items.Insert(Settings.Items.IndexOf(before), item);
        Save();
    }

    public void AddTodo(string text, DateTime? due = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Settings.Todos.Add(new Todo { Text = text.Trim(), Due = due });
        Save();
    }

    public void ToggleTodo(Todo todo)
    {
        todo.Done = !todo.Done;
        Save();
    }

    public void NextFocus()
    {
        var running = Focus.Running;
        if (OnBreak) FocusRound = FocusRound % 4 + 1;
        OnBreak = !OnBreak;
        Focus.Reset(TimeSpan.FromMinutes(OnBreak ? 5 : 25));
        if (running) Focus.Toggle();
        Automation.FocusChanged();
    }

    public void Tick()
    {
        if (Focus.Finished)
        {
            PlaySound();
            NextFocus();
        }
        if (Timer.Finished)
        {
            PlaySound();
            Timer.StopAtZero();
        }
    }

    public void PlaySound()
    {
        if (!Settings.AlertSound) return;
        DockSounds.Play(Settings.Sound);
    }
}

internal sealed class Countdown
{
    private readonly System.Diagnostics.Stopwatch elapsed = new();
    private TimeSpan remaining, duration;
    public Countdown(TimeSpan duration) => this.duration = remaining = duration;
    public bool Running => elapsed.IsRunning;
    public TimeSpan Duration => duration;
    public TimeSpan Left => remaining > elapsed.Elapsed ? remaining - elapsed.Elapsed : TimeSpan.Zero;
    public bool Finished => Running && Left == TimeSpan.Zero;

    public void Toggle()
    {
        if (Running) elapsed.Stop();
        else
        {
            if (Left == TimeSpan.Zero) Reset(duration);
            elapsed.Start();
        }
    }

    public void Reset(TimeSpan value)
    {
        duration = remaining = value;
        elapsed.Reset();
    }

    public void StopAtZero()
    {
        remaining = TimeSpan.Zero;
        elapsed.Reset();
    }
}
