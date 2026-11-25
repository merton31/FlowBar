using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LumaDock;

internal sealed class Preferences
{
    // версия программы, которая записывала профиль: при смене версии рядом остаётся копия прежнего профиля
    public string LastVersion { get; set; } = "";
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
    // Степень преломления живого стекла, 0–100 (50 — как было) и рамки вокруг плиток: у Windows их нет, поэтому можно убрать
    public int Refraction { get; set; } = 50;
    public bool TileBorders { get; set; } = true;
    // Толщина границы по краям панели, px (0 — без границы)
    public double DockBorder { get; set; } = 1;
    // Размер окна ленты TikTok (запоминается при закрытии)
    public double TikTokWidth { get; set; } = 380;
    public double TikTokHeight { get; set; } = 680;
    // Свой набор виджетов у дополнительного монитора: имя устройства → настройка (как в 1.x: «как на основном», «своя раскладка», «без виджетов»)
    public Dictionary<string, MonitorSetup> Monitors { get; set; } = [];
    // Где показывать значки открытых окон: all — на всех панелях (по умолчанию, как в Windows); own — только на панели монитора, где окно открыто; main — только на основной
    public string WindowsOnDocks { get; set; } = "all"; // как в Windows: открытые программы видны на панелях всех мониторов
    // Закреплённые приложения всегда на месте: когда док не вмещает всё, колесом мыши прокручиваются только открытые приложения (выключено — прокручиваются все)
    public bool StickyPinnedApps { get; set; }
    // Программы из панели задач Windows, которые док уже видел и добавил (или они уже стояли): убранные из дока обратно не возвращаются (WindowsPins.Merge)
    public List<string> SeenWindowsPins { get; set; } = [];
    // Ширина виджетов, размер которых можно менять (пока только «Вывод звука»): идентификатор виджета -> ширина плитки
    public Dictionary<string, double> WidgetWidths { get; set; } = [];
    // Плеер в доке по умолчанию в виде обложки с кнопками; у уже существующего профиля меняется один раз
    public bool PlayerArtworkApplied { get; set; }
    public bool SearchButtonApplied { get; set; }
    // Виджет переключения темы убран из дока по умолчанию; у существующего профиля снимается один раз (в галерее остаётся, добавить можно снова)
    public bool ThemeToggleRemoved { get; set; }
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
        ["Todos", "Start", "Search", "app:explorer.exe", "Now Playing Artwork", "CPU", "RAM", "Pomodoro", "Language", "Audio Output", "Quick Toggles", "Tray", "Clock"];
    public List<Todo> Todos { get; set; } = [];
    public string Notes { get; set; } = "";
}

// Раскладка дополнительного монитора: same — как на основном, own — свой список Items, none — только значки программ
internal sealed class MonitorSetup
{
    public string Widgets { get; set; } = "same";
    public List<string> Items { get; set; } = [];
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
    public readonly AppTraffic NetworkApps = new();
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
        var loaded = ReadProfile(Profile, out bool damaged);
        if (exists && loaded is null)
        {
            // профиль не читается: оставляем его копию рядом (только если он действительно повреждён, а не просто занят) и берём последнюю удачную, а не затираем пустыми настройками
            if (damaged)
            {
                try { File.Copy(Profile, Profile + ".broken", true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex, "Settings quarantine"); }
            }
            loaded = ReadProfile(Profile + ".bak", out _);
            backedUp = true; // прежняя копия .bak хорошая — первое сохранение её не трогает
        }
        Settings = loaded ?? new();
        string version = Updates.Current.ToString();
        if (loaded is not null && Settings.LastVersion != version)
        {
            // первый запуск после обновления: профиль прежней версии лежит рядом, если новая что-то прочла не так
            try { File.Copy(Profile, Path.Combine(Path.GetDirectoryName(Profile)!, $"profile.before-{(Settings.LastVersion.Length > 0 ? Settings.LastVersion : "older")}.json"), true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex, "Settings version copy"); }
            // копий прошлых версий хранится не больше пяти, самые свежие по времени записи
            try
            {
                foreach (var old in new DirectoryInfo(Path.GetDirectoryName(Profile)!).GetFiles("profile.before-*.json").OrderByDescending(file => file.LastWriteTimeUtc).Skip(5))
                    old.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex, "Settings version cleanup"); }
        }
        Settings.LastVersion = version;
        if (!exists && path is null)
        {
            // Первый запуск на этом устройстве: ярлыки как на панели задач Windows 11 — Edge, Проводник, Microsoft Store (какие из них есть в меню «Пуск»)
            var shortcuts = WindowsShell.StartMenuShortcuts();
            string? Find(string name) => shortcuts.FirstOrDefault(shortcut => name.Equals(Path.GetFileNameWithoutExtension(shortcut), StringComparison.OrdinalIgnoreCase));
            int explorer = Settings.Items.IndexOf("app:explorer.exe");
            if (Find("Microsoft Edge") is { } edge) { Settings.Items.Insert(explorer, "app:" + edge); explorer++; }
            if (Find("Microsoft Store") is { } store) Settings.Items.Insert(explorer + 1, "app:" + store);
        }
        LocalVoice.Pins = Settings.VoiceModelPins;
        if (!Settings.ThemeToggleRemoved)
        {
            Settings.ThemeToggleRemoved = true;
            Settings.Items.Remove("Theme Toggle");
        }
        if (!Settings.PlayerArtworkApplied)
        {
            Settings.PlayerArtworkApplied = true;
            int player = Settings.Items.IndexOf("Now Playing");
            if (player >= 0 && !Settings.Items.Contains("Now Playing Artwork")) Settings.Items[player] = "Now Playing Artwork";
        }
        if (!Settings.SearchButtonApplied)
        {
            // один раз: кнопка поиска рядом с «Пуск», как в Windows; убранную позже обратно не возвращаем
            Settings.SearchButtonApplied = true;
            int start = Settings.Items.IndexOf("Start");
            if (start >= 0 && !Settings.Items.Contains("Search")) Settings.Items.Insert(start + 1, "Search");
        }
        EnsurePinned();
        savedSummary = BugReport.SettingsSummary(Settings); // исходное состояние для сравнения при первом сохранении
        savedItems = Settings.Items.Select(ActivityLog.Tile).ToArray();
    }

    private string? savedSummary;
    private string[]? savedItems;
    private bool backedUp; // копия профиля перед первым сохранением за запуск уже сделана

    // damaged = true, когда файл прочитан, но не разобран (JSON повреждён); занятый файл (антивирус, синхронизация) сначала перечитывается с короткой паузой
    private static Preferences? ReadProfile(string file, out bool damaged)
    {
        damaged = false;
        for (int attempt = 1; ; attempt++)
        {
            try { return File.Exists(file) ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(file)) : null; }
            catch (JsonException ex) { damaged = true; ErrorLog.Write(ex, "Settings load"); return null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt < 3) { System.Threading.Thread.Sleep(200); continue; }
                ErrorLog.Write(ex, "Settings load");
                return null;
            }
        }
    }

    // В журнал действий попадает, что именно изменилось: настройки (имя=значение) и состав виджетов в доке
    private void NoteChanges()
    {
        var summary = BugReport.SettingsSummary(Settings);
        if (savedSummary is not null && summary != savedSummary)
        {
            var before = savedSummary.Split(", ").ToHashSet();
            ActivityLog.Record("настройки изменены", string.Join(", ", summary.Split(", ").Where(part => !before.Contains(part))));
        }
        savedSummary = summary;
        var items = Settings.Items.Select(ActivityLog.Tile).ToArray();
        if (savedItems is not null && !items.SequenceEqual(savedItems))
        {
            var added = items.Except(savedItems).ToArray();
            var removed = savedItems.Except(items).ToArray();
            ActivityLog.Record("виджеты в доке изменены", added.Length + removed.Length == 0 ? "порядок или число программ" :
                (added.Length > 0 ? "добавлены " + string.Join(", ", added) : "") + (removed.Length > 0 ? " убраны " + string.Join(", ", removed) : ""));
        }
        savedItems = items;
    }

    public void Save(bool redraw = true)
    {
        NoteChanges();
        var pending = Profile + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Profile)!);
            // перед первым сохранением за запуск прежний (читавшийся без ошибок) профиль сохраняется как .bak
            if (!backedUp && File.Exists(Profile))
            {
                try { File.Copy(Profile, Profile + ".bak", true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLog.Write(ex, "Settings backup"); }
            }
            backedUp = true;
            File.WriteAllText(pending, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(pending, Profile, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex, "Settings save");
            SaveFailed?.Invoke(ex.Message);
        }
        if (redraw) Changed?.Invoke();
    }

    // Раскладка монитора по умолчанию своя: при первом обращении берётся копия виджетов основного дока, дальше монитор живёт сам по себе.
    // Явно выбранные «Как на основной» / «Только приложения» остаются как были
    public MonitorSetup SetupFor(string device)
    {
        if (!Settings.Monitors.TryGetValue(device, out var setup))
            Settings.Monitors[device] = setup = new MonitorSetup { Widgets = "own", Items = [.. Settings.Items] };
        return setup;
    }

    // Раскладка дополнительного монитора: same / own / none; при первом включении «своей» берётся копия основного набора
    public void SetMonitorWidgets(string device, string mode)
    {
        var setup = SetupFor(device);
        if (mode == "own" && setup.Items.Count == 0) setup.Items = [.. Settings.Items];
        setup.Widgets = mode;
        Save();
    }

    // Системные виджеты (звук, переключатели сети, тема) остаются в доке всегда, пока включён «Keep system widgets»
    public bool IsPinned(string id) => Settings.PinSystem && Widgets.SystemPinned.Contains(id);

    // Закреплённые в панели задач Windows программы добавляются и в док (ChatGPT, WhatsApp, Claude из магазина тоже); если что-то добавилось, док перерисуется
    public void ImportWindowsPins(List<string> pins)
    {
        if (WindowsPins.Merge(Settings.Items, Settings.SeenWindowsPins, pins, id => WindowsShell.MatchApp(id[4..]))) Save();
    }

    // Возвращает в док закреплённые виджеты, которых там нет (например, после старого профиля), перед часами или в конце
    public void EnsurePinned()
    {
        if (!Settings.PinSystem) return;
        foreach (var id in Widgets.SystemPinned)
        {
            if (Settings.Items.Contains(id)) continue;
            // встаёт перед уже стоящими системными виджетами (язык ввода, как в Windows, слева от звука и сети), иначе перед часами или в конце
            int first = Settings.Items.FindIndex(item => Widgets.SystemPinned.Contains(item));
            int clock = Settings.Items.IndexOf("Clock");
            Settings.Items.Insert(first >= 0 ? first : clock >= 0 ? clock : Settings.Items.Count, id);
        }
    }

    // items — список виджетов того дока, где сделано действие (у дополнительного монитора со своей раскладкой он свой); без него — основной
    public void ToggleWidget(string id, List<string>? items = null)
    {
        var list = items ?? Settings.Items;
        if (id.StartsWith("app:", StringComparison.Ordinal))
        {
            SetAppPinned(id, !IsAppPinned(id, list), list);
            return;
        }
        if (list == Settings.Items && list.Contains(id) && IsPinned(id)) return;
        if (!list.Remove(id)) list.Add(id);
        Save();
    }

    // Ярлык, exe и регистр пути могут различаться, хотя плитка относится к одной программе.
    // Храним исходный ярлык (с его параметрами запуска), сопоставляем по той же цели, что и открытые окна.
    internal bool IsAppPinned(string id, List<string>? items = null)
        => AppPins(id, items ?? Settings.Items).Any();

    private static IEnumerable<string> AppPins(string id, List<string> items)
    {
        string key = WindowsShell.MatchApp(id[4..]);
        return items.Where(item => item.StartsWith("app:", StringComparison.Ordinal)
            && string.Equals(WindowsShell.MatchApp(item[4..]), key, StringComparison.OrdinalIgnoreCase));
    }

    internal void SetAppPinned(string id, bool pin, List<string>? items = null)
    {
        if (!id.StartsWith("app:", StringComparison.Ordinal) || id.Length <= 4) return;
        var list = items ?? Settings.Items;
        var matches = AppPins(id, list).ToArray();
        bool changed = false;
        if (pin && matches.Length == 0)
        {
            int at = list.FindLastIndex(item => item.StartsWith("app:", StringComparison.Ordinal) || item is "Start" or "Search");
            list.Insert(at + 1, id);
            changed = true;
        }
        else
        {
            // Убираем и старые дубли; при закреплении оставляем первый ярлык и его место.
            foreach (string match in matches.Skip(pin ? 1 : 0)) changed |= list.Remove(match);
        }
        if (!pin && ReferenceEquals(list, Settings.Items))
        {
            string key = WindowsShell.MatchApp(id[4..]);
            if (!Settings.SeenWindowsPins.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                Settings.SeenWindowsPins.Add(key);
                changed = true;
            }
        }
        if (changed) Save();
    }

    public bool SelectWidgetStyle(string from, string to, List<string>? items = null)
    {
        var list = items ?? Settings.Items;
        int source = list.IndexOf(from);
        if (source < 0 || from == to || !Widgets.Styles(from).Contains(to)) return false;
        int target = list.IndexOf(to);
        list[source] = to;
        if (target >= 0) list[target] = from;
        Save();
        return true;
    }

    // before == null: в конец списка (перетаскивание правее последней плитки)
    // Меняет ярлык местами с соседним (delta -1 — влево, +1 — вправо); у края и при закреплённой панели ничего не делает
    public bool Shift(string item, int delta, List<string>? items = null)
    {
        var list = items ?? Settings.Items;
        int from = list.IndexOf(item), to = from + delta;
        if (Settings.Locked || from < 0 || to < 0 || to >= list.Count) return false;
        (list[from], list[to]) = (list[to], list[from]);
        Save();
        return true;
    }

    public void MoveItem(string item, string? before, List<string>? items = null)
    {
        var list = items ?? Settings.Items;
        if (Settings.Locked || item == before || before is not null && !list.Contains(before) || !list.Remove(item)) return;
        if (before is null) list.Add(item); else list.Insert(list.IndexOf(before), item);
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
