using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LumaDock;

internal static partial class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--check-wallpapers")
            return CheckWallpapers(args[1]);
        if (args.Length == 2 && args[0] == "--check-jumplist")
            return JumpListCheck.Run(args[1]);
        if (args.Length == 2 && args[0] == "--check-places")
            return PlaceCheck.Run(args[1]);
        if (args.Length == 2 && args[0] == "--check-dictation")
            return DictationCheck.Run(args[1]);
        if (args.Length == 1 && args[0] == "--mcp")
            return Agent.ServeMcp();
        if (args.Length == 2 && args[0] == "--check-install")
            return InstallCheck.Run(args[1]);
        if (args.Length == 2 && args[0] == "--check-network")
            return NetworkCheck.Run(args[1]);
        if (args.Length == 2 && args[0] == "--check-tiktok")
            return TikTokFeed.Run(args[1]);
        if (args.Length == 2 && args[0] == "--check-connections")
            return ConnectionCheck.Run(args[1]);
        if (args.Length == 0 && Installer.RunFromElsewhere())
            return 0; // запущен не из папки установки: поставил себя и запустил установленную копию
        if (args.Length == 4 && args[0] == "--voice-local")
            return LocalVoice.RunWorker(args);
        if (args.Contains("--restore-taskbar"))
        {
            WindowsShell.RestoreTaskbarFromReceipt();
            return 0;
        }
        if (args.Length == 2 && args[0] == "--watch-taskbar" && int.TryParse(args[1], out int parent))
        {
            WindowsShell.WatchTaskbar(parent);
            return 0;
        }

        var shellCheck = args.Length == 2 && args[0] == "--check-taskbar";
        var liveCheck = args.Length == 2 && args[0] == "--check-live";
        var uiAudit = args.Length == 2 && args[0] == "--audit-ui";
        var glassPreview = args.Length == 2 && args[0] == "--preview-glass";
        var testing = args.Length == 2 && (args[0] == "--smoke" || shellCheck || liveCheck || uiAudit || glassPreview);
        bool smoke = testing;
        using var instance = new Mutex(true, "Local\\FlowBar2.Desktop", out bool first);
        if (!first && !smoke) return 0;
        // WPF по умолчанию рисует не больше 60 кадров в секунду; поднимаем до частоты монитора дока, чтобы живое стекло в прозрачном режиме не отставало на быстрых мониторах
        if (!testing)
            System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(System.Windows.Media.Animation.Timeline),
                new FrameworkPropertyMetadata(Math.Max(60, Displays.Hz(System.Windows.Forms.Screen.PrimaryScreen?.DeviceName))));

        var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        // необработанные ошибки пишутся в error.log: их потом можно приложить к отчёту (Report a problem); сами ошибки не гасим
        ErrorLog.Disabled = testing;
        if (!testing)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) => ErrorLog.Write(e.ExceptionObject as Exception, "Crash");
            application.DispatcherUnhandledException += (_, e) => ErrorLog.Write(e.Exception, "UI");
            // ошибки в фоновых задачах без await (`_ = Task`) иначе пропадают без следа
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => ErrorLog.Write(e.Exception.GetBaseException(), "Task");
        }
        // общие тёмные стили (в том числе полоса прокрутки для всех окон) подключаются ко всему приложению
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/FlowBar;component/DockControls.xaml", UriKind.Relative) });
        var model = new DockModel(testing ? Path.Combine(args[1], "test-profile.json") : null);
        Dictation.Fixture = testing; // в проверках микрофон и Handy не нужны
        UnreadNotifications.Fixture = testing; // и уведомления Windows: счётчик WhatsApp в проверках фиксированный
        L10n.SetLanguage(testing ? "en" : model.Settings.UiLanguage);
        int result = 0;
        try
        {
            var dock = new DockWindow(model, smoke, liveCheck || glassPreview);
            if (!testing) dock.Loaded += (_, _) => Monitors.Attach(model, dock);
            if (testing)
            {
                dock.ContentRendered += (_, _) => dock.Dispatcher.BeginInvoke(async () =>
                {
                    Window? surface = null;
                    try
                    {
                        if (uiAudit)
                        {
                            L10n.TrackMissing = true;
                            AuditInterface(dock, model, args[1]);
                            File.WriteAllLines(Path.Combine(args[1], "missing-translations.txt"), L10n.Missing);
                            File.WriteAllText(Path.Combine(args[1], "result.txt"), "OK");
                            return;
                        }
                        if (glassPreview)
                        {
                            await GlassPreview.Run(dock, model, args[1]);
                            File.WriteAllText(Path.Combine(args[1], "result.txt"), "OK");
                            return;
                        }
                        if (liveCheck)
                        {
                            await System.Threading.Tasks.Task.Delay(3800);
                            Capture(dock, Path.Combine(args[1], "dock-live.png"));
                            if (!dock.HasLiveBackdrop) throw new InvalidOperationException("The Windows session did not supply desktop pixels.");
                            File.WriteAllText(Path.Combine(args[1], "result.txt"), "OK");
                            return;
                        }
                        if (shellCheck)
                        {
                            var original = WindowsShell.InspectTaskbar();
                            if (!original.Found) throw new InvalidOperationException("Windows Explorer taskbar is not available in this session.");
                            try
                            {
                                var root = new System.Windows.Interop.WindowInteropHelper(dock).Handle;
                                var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
                                bool hidden = WindowsShell.HideTaskbar();
                                if (!hidden) throw new InvalidOperationException("Taskbar recovery watcher could not be created.");
                                for (int attempt = 0; attempt < 6 && WindowsShell.TaskbarOccupiesScreen(); attempt++)
                                {
                                    await System.Threading.Tasks.Task.Delay(250);
                                    WindowsShell.HideTaskbar();
                                }
                                if (WindowsShell.TaskbarOccupiesScreen()) throw new InvalidOperationException("Explorer taskbar still occupies the screen.");
                                var area = WindowsShell.Reserve(root, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height), 52, true);
                                if (!area.HasValue || area.Value.Height != 52)
                                    throw new InvalidOperationException("Explorer rejected the AppBar rectangle.");
                                WindowsShell.Release(root);
                            }
                            finally { WindowsShell.RestoreTaskbar(); }
                            await System.Threading.Tasks.Task.Delay(400);
                            var restored = WindowsShell.InspectTaskbar();
                            if (restored.State != original.State || (original.Visible && !restored.Visible))
                                throw new InvalidOperationException("Explorer taskbar state could not be restored.");
                            File.WriteAllText(Path.Combine(args[1], "result.txt"), "OK");
                            return;
                        }
                        Capture(dock, Path.Combine(args[1], "dock.png"));
                        surface = dock.OpenSettings();
                        Capture(surface, Path.Combine(args[1], "settings.png"));
                        VerifyAccentControls(surface, model, args[1]);
                        surface.Close();
                        foreach (string tab in new[] { "Dock", "Sounds", "Automations", "General" })
                        {
                            surface = dock.OpenSettings(tab);
                            Capture(surface, Path.Combine(args[1], $"settings-{tab}.png"));
                            if (tab == "Dock") VerifyDockControls(surface, model);
                            surface.Close();
                        }
                        surface = dock.OpenGallery();
                        Capture(surface, Path.Combine(args[1], "gallery.png"));
                        surface.Close();
                        foreach (string category in new[] { "System", "Media & Audio", "Weather", "Analytics", "Social", "AI", "Apps & Files", "Productivity" })
                        {
                            surface = dock.OpenGallery(category);
                            Capture(surface, Path.Combine(args[1], $"gallery-{category.Split(' ')[0]}.png"));
                            surface.Close();
                        }
                        surface = dock.OpenWidgetStyles("Clock");
                        Capture(surface, Path.Combine(args[1], "widget-styles.png"));
                        surface.Close();
                        surface = dock.OpenFolderPin();
                        var folder = (Button?)FindControl(surface, "Downloads") ?? throw new InvalidOperationException("Windows folder shortcuts are missing.");
                        folder.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, folder));
                        if (!new DockModel(model.Profile).Settings.Items.Contains("Downloads"))
                            throw new InvalidOperationException("Windows folder could not be pinned.");
                        model.ToggleWidget("Downloads");
                        // закрепление системных виджетов: не снимаются, возвращаются, а при выключенном закреплении снимаются
                        foreach (var pinned in Widgets.SystemPinned)
                        {
                            model.ToggleWidget(pinned);
                            if (!model.Settings.Items.Contains(pinned)) throw new InvalidOperationException(pinned + " was removed while pinned.");
                        }
                        model.Settings.PinSystem = false;
                        model.ToggleWidget("Tray");
                        if (model.Settings.Items.Contains("Tray")) throw new InvalidOperationException("The tray icon stayed after unpinning.");
                        if (model.Settings.Items.Contains("Theme Toggle") || new DockModel(Path.Combine(args[1], "fresh-theme.json")).Settings.Items.Contains("Theme Toggle"))
                            throw new InvalidOperationException("The theme toggle must not be in the dock by default.");
                        model.Settings.PinSystem = true;
                        model.EnsurePinned();
                        if (!Widgets.SystemPinned.All(model.Settings.Items.Contains)) throw new InvalidOperationException("Pinned widgets were not restored.");
                        // значок трея закреплён вместе с системными и есть в списке нового профиля
                        if (!Widgets.SystemPinned.Contains("Tray") || !new DockModel(Path.Combine(args[1], "fresh-profile.json")).Settings.Items.Contains("Tray"))
                            throw new InvalidOperationException("The tray icon is not pinned with the system widgets.");
                        // кнопка поиска стоит сразу после «Пуск»: в новом профиле и один раз в старом, убранная позже не возвращается
                        var oldProfile = Path.Combine(args[1], "old-search-profile.json");
                        File.WriteAllText(oldProfile, "{\"Items\":[\"Todos\",\"Start\",\"app:x.exe\"]}");
                        var oldItems = new DockModel(oldProfile).Settings.Items;
                        var freshItems = new DockModel(Path.Combine(args[1], "fresh-profile.json")).Settings.Items;
                        if (!oldItems.Take(3).SequenceEqual(["Todos", "Start", "Search"]) || freshItems.IndexOf("Search") != freshItems.IndexOf("Start") + 1)
                            throw new InvalidOperationException("The search button does not follow the Start button.");
                        var keptSearch = new DockModel(oldProfile); keptSearch.Settings.Items.Remove("Search"); keptSearch.Save();
                        if (new DockModel(oldProfile).Settings.Items.Contains("Search")) throw new InvalidOperationException("A removed search button came back.");
                        // виджет WhatsApp: в проверках счётчик фиксированный (3), подпись «Unread»
                        var whatsapp = Widgets.Face(Widgets.Available["WhatsApp"], model, true, out var whatsappUpdate);
                        whatsappUpdate();
                        string whatsappText = FaceText(whatsapp);
                        if (!whatsappText.Contains('3') || !whatsappText.Contains("Unread"))
                            throw new InvalidOperationException("WhatsApp widget shows: " + whatsappText);
                        // у виджетов «Приложения и файлы» свои значки, а не одна папка
                        string[] fileWidgets = ["Apps", "Recently Opened", "Recycle Bin", "Search", "Storage", "Screenshot Capture"];
                        if (fileWidgets.Select(id => Widgets.Available[id].Glyph).Distinct().Count() != fileWidgets.Length)
                            throw new InvalidOperationException("Apps & Files widgets share an icon.");
                        // окно миниатюр нескольких окон: карточки с иконкой и заголовком, перенос на следующий ряд
                        var previewWindow = dock.PreviewTest(7);
                        if (previewWindow is not null)
                        {
                            await System.Threading.Tasks.Task.Delay(400);
                            Capture(previewWindow, Path.Combine(args[1], "window-previews.png"));
                            if (dock.LiveThumbnails == 0) throw new InvalidOperationException("Windows accepted none of the live window thumbnails.");
                            dock.ClosePreviewTest();
                        }
                        // увеличение как в Dock на Mac
                        var magnifyProblem = dock.MagnifyCheck();
                        if (!string.IsNullOrEmpty(magnifyProblem)) throw new InvalidOperationException("Magnification: " + magnifyProblem);
                        surface = dock.OpenWidget("Todos") ?? throw new InvalidOperationException("Todos did not open");
                        Capture(surface, Path.Combine(args[1], "todos.png"));
                        await VerifyDateControl(surface, args[1]);
                        VerifyTaskCheckBox(surface, model, args[1]);
                        surface.Close();
                        VerifyNotesScrollBar(dock, model, args[1]);
                        await VerifyDictation(dock, model, args[1]);
                        await VerifyTaskDictation(dock, model, args[1]);
                        await VerifyMonitors(model);
                        dock.CaptureRealApps(Path.Combine(args[1], "dock-real-apps.png"));
                        var reopenProblem = dock.ReopenCheck();
                        if (!string.IsNullOrEmpty(reopenProblem)) throw new InvalidOperationException("Widget toggle: " + reopenProblem);
                        var scrollProblem = dock.ScrollCheck(args[1]);
                        if (!string.IsNullOrEmpty(scrollProblem)) throw new InvalidOperationException("App list: " + scrollProblem);
                        await VerifyBugReport(dock, args[1]);
                        VerifyQuickPanel(dock, args[1]);
                        VerifyFullscreenReveal();
                        await VerifyWidgetSize(dock, model);
                        await VerifyDeviceLists(dock, args[1]);
                        await VerifyPlayer(dock, model, args[1]);
                        VerifyIcons();
                        if (WindowsPins.Verify() is { Length: > 0 } pinProblem) throw new InvalidOperationException("Windows pins: " + pinProblem);
                        await VerifyAgent(dock, model);
                        await VerifyEndTask(dock);
                        await VerifyLocalVoice();
                        await VerifyVoiceTools(dock, args[1]);
                        await VerifyHotkeys(dock, model);
                        await VerifyAppMenu(dock, args[1]);
                        await VerifyJumpList();
                        VerifyLookCode(model);
                        await VerifyStyleMixer(dock, model, args[1]);
                        var sample = DateTime.Today.AddHours(12);
                        var domain = BrowserSites.Domain("https://www.example.com/article?q=private#fragment");
                        if (domain != "example.com" || BrowserSites.Domain("file:///C:/temp/article.html") is not null ||
                            BrowserSites.Domain("localhost:8080") is not null || BrowserSites.Domain("https://user@site.example") is not null)
                            throw new InvalidOperationException("Browser address handling failed.");
                        model.Activity.Sample(sample.AddSeconds(-2), "fixture-app", TimeSpan.Zero, domain);
                        model.Activity.Sample(sample, "fixture-app", TimeSpan.Zero, domain);
                        if (model.Activity.Between(sample.Date, sample.Date.AddDays(1)).Single().Site != "example.com")
                            throw new InvalidOperationException("Website activity did not aggregate.");
                        await VerifyBrowserAccessibility(dock);
                        var forecast = WeatherFixture();
                        model.Data.Forecast = forecast;
                        model.Settings.WeatherPlace = new LiveData.Place("Sokhumi", "Georgia", 43.0, 41.0);
                        model.Connected.Dashboards["Stock Chart"] = StockFixture();
                        var radioError = await model.Radios.Refresh();
                        File.WriteAllText(Path.Combine(args[1], "radios.txt"), radioError ??
                            $"Wi-Fi: {model.Radios.Read("Wi-Fi")}; Bluetooth: {model.Radios.Read("Bluetooth")}");
                        foreach (var id in new[] { "Calendar", "Screen Time", "Day Timeline", "Weather", "Audio Output", "World Clocks", "World Time", "Network",
                            "Now Playing", "AI Limits", "Total Revenue", "Storage", "Wallpaper Gallery", "Hourly", "Sunrise & Sunset",
                            "Stock Chart", "Quick Toggles", "Bluetooth" })
                        {
                            surface = dock.OpenWidget(id) ?? throw new InvalidOperationException(id + " did not open");
                            Capture(surface, Path.Combine(args[1], id.Replace(' ', '-') + ".png"));
                            if (id == "Audio Output") VerifyMixerControls(surface, args[1]);
                            if (id == "World Clocks") await VerifyCityDropdown(surface, model, args[1]);
                            if (id == "World Time")
                            {
                                // выбор города у «Мирового времени» тоже в общем тёмном стиле, а не белый список Windows
                                var single = FindControl(surface, L10n.T("City")) as ComboBox ?? throw new InvalidOperationException("World Time city dropdown is missing.");
                                single.ApplyTemplate();
                                if (single.Template.FindName("Field", single) is not Border { CornerRadius.TopLeft: > 8 })
                                    throw new InvalidOperationException("World Time city dropdown did not receive the rounded frame.");
                            }
                            if (id == "Weather" && Environment.GetEnvironmentVariable("LUMADOCK_LIVE_SEARCH") == "1")
                                await VerifyLiveCitySearch(surface, args[1]);
                            if (id == "Weather")
                            {
                                Click(surface, "Change weather city");
                                surface.UpdateLayout();
                                Click(surface, "°F");
                                if (!new DockModel(model.Profile).Settings.WeatherFahrenheit)
                                    throw new InvalidOperationException("Weather unit selection did not persist.");
                                Click(surface, "°C");
                            }
                            if (id == "Stock Chart") Click(surface, "1W");
                            surface.Close();
                        }
                        surface = null;
                        model.Data.Forecast = null;
                        model.Settings.WeatherPlace = null;
                        var listed = Widgets.ReferenceCategories.SelectMany(category => category.Catalog.Split('|')).ToArray();
                        if (listed.Length != 107 || listed.Any(id => !Widgets.Available.ContainsKey(id)) || listed.Distinct().Count() != listed.Length
                            || Widgets.WindowsFolders.Any(id => listed.Contains(id) || !Widgets.Available.ContainsKey(id)))
                            throw new InvalidOperationException("Widget catalog is incomplete or contains duplicate IDs.");
                        foreach (string id in listed)
                        {
                            Widgets.Face(Widgets.Available[id], model, true, out var render);
                            render();
                        }
                        foreach (var city in Widgets.ClockCities) _ = TimeZoneInfo.FindSystemTimeZoneById(city.Zone);
                        model.Settings.WeatherFahrenheit = true;
                        if (Widgets.WeatherDegrees(0, model.Settings, true) != "32°F")
                            throw new InvalidOperationException("Temperature units did not convert.");
                        int clockPosition = model.Settings.Items.IndexOf("Clock");
                        if (!model.SelectWidgetStyle("Clock", "Flip Clock") || model.Settings.Items[clockPosition] != "Flip Clock" ||
                            model.Settings.Items.Contains("Clock") || model.SelectWidgetStyle("Flip Clock", "CPU"))
                            throw new InvalidOperationException("Widget style replacement did not preserve the dock position.");
                        if (!model.SelectWidgetStyle("Flip Clock", "Clock"))
                            throw new InvalidOperationException("Original clock style could not be restored.");
                        VerifyWallpaperResponse();
                        var date = DateTime.Today;
                        var next = date.AddDays(1);
                        var fixture = "BEGIN:VCALENDAR\nBEGIN:VEVENT\nSUMMARY:Test\\, repeated\n" +
                            $"DTSTART:{date:yyyyMMdd}T100000\nDTEND:{date:yyyyMMdd}T110000\nRRULE:FREQ=DAILY;COUNT=3\nEXDATE:{next:yyyyMMdd}T100000\nEND:VEVENT\nEND:VCALENDAR";
                        var events = Calendars.Parse(fixture, "fixture.ics", date, date.AddDays(5));
                        if (events.Length != 2 || events[1].Start != date.AddDays(2).AddHours(10) || events[0].Title != "Test, repeated")
                            throw new InvalidOperationException("Calendar recurrence failed.");
                        if (!AutomationRunner.IsQuiet(TimeSpan.FromHours(2), model.Settings.Automations) ||
                            AutomationRunner.IsQuiet(TimeSpan.FromHours(12), model.Settings.Automations))
                            throw new InvalidOperationException("Quiet-hours interval failed.");
                        string project = Path.Combine(args[1], "claude-fixture");
                        Directory.CreateDirectory(project);
                        var record = System.Text.Json.JsonSerializer.Serialize(new { type = "assistant", timestamp = DateTimeOffset.Now,
                            message = new { id = "fixture", model = "fixture-model", usage = new { input_tokens = 10, output_tokens = 20 } } });
                        File.WriteAllLines(Path.Combine(project, "sample.jsonl"), [record, record]);
                        var tokens = await AiMetrics.Claude(project);
                        if (tokens.Length != 1 || tokens[0].Input != 10 || tokens[0].Output != 20)
                            throw new InvalidOperationException("Local token totals failed.");
                        model.AddTodo("smoke");
                        model.ToggleTodo(model.Settings.Todos.Last());
                        model.ToggleWidget("Notes");
                        model.MoveItem("Notes", "Todos");
                        model.MoveItem("Notes", null);
                        if (model.Settings.Items[^1] != "Notes") throw new InvalidOperationException("Moving a tile to the end failed.");
                        model.MoveItem("Notes", "Todos");
                        var order = new System.Collections.Generic.List<string> { "a", "b", "c" };
                        if (!model.Shift("b", 1, order) || !order.SequenceEqual(new[] { "a", "c", "b" }) || model.Shift("b", 1, order) || model.Shift("a", -1, order))
                            throw new InvalidOperationException("Swapping a tile with its neighbour failed.");
                        // рогатка: значение под точкой дорожки, крайние значения достижимы, цель летит туда же, куда ушла мышь
                        if (SlingSlider.ValueAt(10, 180, 0, 100, false, 5) != 0 || SlingSlider.ValueAt(999, 180, 0, 100, false, 5) != 100
                            || SlingSlider.ValueAt(10, 180, 0, 100, true, 5) != 100 || SlingSlider.ValueAt(100, 180, 0, 100, false, 5) is < 49.9 or > 50.1
                            || SlingSlider.Shot(new System.Windows.Vector(-40, 0), 180, out _) >= 0 || SlingSlider.Shot(new System.Windows.Vector(40, 0), 180, out double pull) <= 0 || pull is <= 0 or > 1)
                            throw new InvalidOperationException("Slingshot slider maths failed.");
                        var secondLayout = new System.Collections.Generic.List<string> { "Start", "app:a.exe" };
                        model.ToggleWidget("app:b.exe", secondLayout);
                        model.MoveItem("app:b.exe", "Start", secondLayout);
                        if (!secondLayout.SequenceEqual(new[] { "app:b.exe", "Start", "app:a.exe" }) || model.Settings.Items.Contains("app:b.exe"))
                            throw new InvalidOperationException("A monitor's own layout must pin and move apps without touching the main one.");
                        model.ToggleWidget("app:b.exe", secondLayout);
                        if (secondLayout.Contains("app:b.exe")) throw new InvalidOperationException("Unpin failed.");
                        var reloaded = new DockModel(model.Profile);
                        if (!reloaded.Settings.Todos.Last().Done || reloaded.Settings.Items.IndexOf("Notes") != 0)
                            throw new InvalidOperationException("Profile round trip failed");
                        // обновление не должно терять закреплённые программы, порядок, размеры и стили виджетов: всё это переживает перезапуск
                        static double Wide(Preferences settings) => settings.WidgetWidths.TryGetValue("Audio Output", out double width) ? width : 0;
                        var keepPath = Path.Combine(args[1], "keep-profile.json");
                        var keep = new DockModel(keepPath);
                        keep.ToggleWidget("app:keep.exe");
                        keep.Settings.WidgetWidths["Audio Output"] = 330;
                        keep.Settings.Style = "Clear";
                        keep.MoveItem("app:keep.exe", "Start");
                        keep.Save(false);
                        var keepItems = keep.Settings.Items.ToArray();
                        keep = new DockModel(keepPath); // «перезапуск» той же версии
                        if (!keep.Settings.Items.SequenceEqual(keepItems) || Wide(keep.Settings) != 330 || keep.Settings.Style != "Clear")
                            throw new InvalidOperationException("Pinned apps, order or widget size were lost on restart.");
                        // профиль, записанный прежней версией: читается целиком, рядом остаётся его копия
                        keep.Settings.LastVersion = "0.0.1";
                        keep.Save(false);
                        keep = new DockModel(keepPath);
                        if (!keep.Settings.Items.SequenceEqual(keepItems) || !File.Exists(Path.Combine(args[1], "profile.before-0.0.1.json")) || keep.Settings.LastVersion != Updates.Current.ToString())
                            throw new InvalidOperationException("Update from an older version lost the profile or did not keep a copy.");
                        // повреждённый профиль не затирается пустым: копия .broken и возврат к последнему удачному (.bak)
                        keep.Settings.Items.Add("Notes");
                        keep.Save(false); // .bak — состояние до этого запуска без «Notes»
                        File.WriteAllText(keepPath, "{ not json");
                        var recovered = new DockModel(keepPath);
                        if (!File.Exists(keepPath + ".broken") || !recovered.Settings.Items.Contains("app:keep.exe") || Wide(recovered.Settings) != 330)
                            throw new InvalidOperationException("A broken profile was not quarantined or the backup was not used.");
                        recovered.Save(false);
                        if (new DockModel(keepPath).Settings.Items.Contains("app:keep.exe") == false || !File.ReadAllText(keepPath + ".bak").Contains("keep.exe"))
                            throw new InvalidOperationException("Saving after recovery damaged the backup.");
                        // занятый другим процессом профиль не считается повреждённым: после короткого ожидания читается как обычно, .broken не создаётся
                        File.Delete(keepPath + ".broken");
                        using (var lockedProfile = new FileStream(keepPath, FileMode.Open, FileAccess.Read, FileShare.None))
                        {
                            _ = System.Threading.Tasks.Task.Run(() => { System.Threading.Thread.Sleep(300); lockedProfile.Dispose(); });
                            var afterLock = new DockModel(keepPath);
                            if (File.Exists(keepPath + ".broken") || !afterLock.Settings.Items.Contains("app:keep.exe") || Wide(afterLock.Settings) != 330)
                                throw new InvalidOperationException("A briefly locked profile was treated as broken.");
                        }
                        // копий прошлых версий остаётся не больше пяти
                        for (int copy = 0; copy < 8; copy++)
                        {
                            var oldCopy = Path.Combine(args[1], $"profile.before-9.{copy}.json");
                            File.WriteAllText(oldCopy, "{}");
                            File.SetLastWriteTimeUtc(oldCopy, DateTime.UtcNow.AddDays(-30 + copy));
                        }
                        keep.Settings.LastVersion = "0.0.2";
                        keep.Save(false);
                        _ = new DockModel(keepPath);
                        if (Directory.GetFiles(args[1], "profile.before-*.json").Length != 5)
                            throw new InvalidOperationException("Copies of older versions were not trimmed to five.");
                        // квитанции панели задач от упавшего запуска: берётся самая свежая после загрузки Windows, все старые удаляются, своя не трогается
                        var receipts = Path.Combine(args[1], "receipts");
                        Directory.CreateDirectory(receipts);
                        File.WriteAllText(Path.Combine(receipts, "taskbar-2147483000.restore"), "1|True");
                        File.WriteAllText(Path.Combine(receipts, "taskbar-2147483001.restore"), "3|False");
                        File.SetLastWriteTimeUtc(Path.Combine(receipts, "taskbar-2147483000.restore"), DateTime.UtcNow.AddMinutes(-2));
                        File.WriteAllText(Path.Combine(receipts, $"taskbar-{Environment.ProcessId}.restore"), "0|True");
                        var prior = WindowsShell.TakeOverStaleReceipts(receipts);
                        if (prior != (3u, false) || Directory.GetFiles(receipts).Length != 1 || !File.Exists(Path.Combine(receipts, $"taskbar-{Environment.ProcessId}.restore")))
                            throw new InvalidOperationException("Stale taskbar receipts were not taken over correctly.");
                        // при обновлении прежний запуск ещё жив и держит панель скрытой: его квитанция берётся для нового запуска, но не удаляется
                        using (var alive = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c ping -n 6 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!)
                        {
                            var handoverFile = Path.Combine(receipts, $"taskbar-{alive.Id}.restore");
                            File.WriteAllText(handoverFile, "2|True");
                            bool onlyDead = WindowsShell.TakeOverStaleReceipts(receipts) is null;
                            var handed = WindowsShell.TakeOverStaleReceipts(receipts, true);
                            if (!onlyDead || handed != (2u, true) || !File.Exists(handoverFile))
                                throw new InvalidOperationException("A running instance's taskbar receipt was handled wrongly.");
                            alive.Kill();
                            File.Delete(handoverFile);
                        }
                        // виджет звука: свой вид на каждый размер, число громкости видно, выключенный звук не ломает вид
                        var savedAudio = model.Audio;
                        var savedWidth = Wide(model.Settings);
                        var looks = new System.Collections.Generic.List<Widgets.VolumeLook>();
                        foreach (var (_, audioWidth) in Widgets.SizeChoices("Audio Output"))
                        {
                            model.Settings.WidgetWidths["Audio Output"] = audioWidth;
                            looks.Add(Widgets.VolumeLookFor(audioWidth));
                            foreach (bool silent in new[] { false, true })
                            {
                                model.Audio = (.5, silent);
                                var audioFace = Widgets.Face(Widgets.Available["Audio Output"], model, false, out var audioUpdate);
                                audioUpdate();
                                if (!FaceText(audioFace).Contains("50%") && !silent) throw new InvalidOperationException($"Sound widget {audioWidth} does not show the volume.");
                                // снимок для глаз: плитка 47 px высотой на тёмном фоне, как в доке
                                var holder = new System.Windows.Controls.Border { Width = audioWidth, Height = 47, Background = System.Windows.Media.Brushes.Black, Padding = new System.Windows.Thickness(10, 1, 10, 1), Child = audioFace };
                                holder.Measure(new System.Windows.Size(audioWidth, 47));
                                holder.Arrange(new System.Windows.Rect(0, 0, audioWidth, 47));
                                holder.UpdateLayout();
                                var shot = new System.Windows.Media.Imaging.RenderTargetBitmap((int)audioWidth * 2, 94, 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
                                shot.Render(holder);
                                var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(shot));
                                using (var pngFile = File.Create(Path.Combine(args[1], $"sound-{audioWidth}-{(silent ? "muted" : "on")}.png"))) png.Save(pngFile);
                            }
                        }
                        if (looks.Distinct().Count() != 4) throw new InvalidOperationException("Every sound widget size must have its own style.");
                        model.Audio = savedAudio;
                        if (savedWidth == 0) model.Settings.WidgetWidths.Remove("Audio Output"); else model.Settings.WidgetWidths["Audio Output"] = savedWidth;
                        model.Activity.ClearSites();
                        reloaded = new DockModel(model.Profile);
                        if (reloaded.Settings.TrackWebsites || reloaded.Activity.Between(sample.Date, sample.Date.AddDays(1)).Single().Site is not null)
                            throw new InvalidOperationException("Website history could not be removed.");
                        await VerifyLocalization(dock, model, args[1]);
                        File.WriteAllText(Path.Combine(args[1], "widgets.txt"), $"{listed.Length} catalog entries");
                        File.WriteAllText(Path.Combine(args[1], "result.txt"), "OK");
                    }
                    catch (Exception ex)
                    {
                        result = 1;
                        File.WriteAllText(Path.Combine(args[1], "result.txt"), ex.ToString());
                    }
                    finally
                    {
                        surface?.Close();
                        dock.Close();
                    }
                }, DispatcherPriority.ApplicationIdle);
            }
            else Agent.Start(model, dock.Dispatcher);
            application.Run(dock);
            return result;
        }
        catch (Exception ex) when (testing)
        {
            File.WriteAllText(Path.Combine(args[1], "result.txt"), ex.ToString());
            return 1;
        }
        finally
        {
            if (!smoke) WindowsShell.RestoreTaskbar();
        }
    }

    private static async System.Threading.Tasks.Task VerifyLocalization(DockWindow dock, DockModel model, string folder)
    {
        var settings = dock.OpenSettings("General");
        Click(settings, "Русский");
        settings = dock.OpenSettings("General");
        if (settings.Title != "Настройки" || !L10n.Russian || new DockModel(model.Profile).Settings.UiLanguage != "ru"
            || L10n.T("Clear") == L10n.Condition("Clear") || L10n.Metric("15 followers") != "Подписчиков: 15")
            throw new InvalidOperationException("Russian interface or profile preference did not update.");
        Capture(settings, Path.Combine(folder, "settings-ru.png"));
        settings.Close();
        var gallery = dock.OpenGallery();
        var search = (TextBox?)FindControl(gallery, "Поиск виджетов") ?? throw new InvalidOperationException("Russian gallery search is missing.");
        search.Text = "Подробная погода";
        gallery.UpdateLayout();
        if (FindControl(gallery, "Добавить · Подробная погода") is not Button)
            throw new InvalidOperationException("Russian search did not find the widget.");
        Capture(gallery, Path.Combine(folder, "gallery-ru.png"));
        gallery.Close();
        var calendar = dock.OpenWidget("Calendar") ?? throw new InvalidOperationException("Calendar did not open in Russian.");
        if (calendar.Title != "Календарь") throw new InvalidOperationException("Widget title did not translate.");
        Capture(calendar, Path.Combine(folder, "calendar-ru.png"));
        calendar.Close();
        var todos = dock.OpenWidget("Todos") ?? throw new InvalidOperationException("Todos did not open in Russian.");
        await VerifyDateControl(todos, folder);
        VerifyTaskCheckBox(todos, model, folder);
        todos.Close();
        await VerifyDictation(dock, model, folder);
        await VerifyStyleMixer(dock, model, folder);
        var audio = dock.OpenWidget("Audio Output") ?? throw new InvalidOperationException("Sound did not open in Russian.");
        VerifyMixerControls(audio, folder);
        audio.Close();
        Click(dock.OpenSettings("General"), "English");
        settings = dock.OpenSettings("General");
        if (settings.Title != "Settings" || L10n.Russian || new DockModel(model.Profile).Settings.UiLanguage != "en")
            throw new InvalidOperationException("English interface did not restore.");
        settings.Close();
    }

    private static void VerifyDockControls(Window settings, DockModel model)
    {
        var size = (Slider?)FindControl(settings, "Size") ?? throw new InvalidOperationException("Size slider is missing.");
        var cards = (ToggleButton?)FindControl(settings, "App cards") ?? throw new InvalidOperationException("App cards switch is missing.");
        double initialSize = size.Value;
        bool initialCards = cards.IsChecked == true;
        size.Value = 80;
        cards.IsChecked = !initialCards;
        var reloaded = new DockModel(model.Profile).Settings;
        if (reloaded.Size != 80 || reloaded.AppCards == initialCards)
            throw new InvalidOperationException("Dock controls did not save size and app cards.");
        size.Value = initialSize;
        cards.IsChecked = initialCards;

    }

    // Подставной приёмник отчётов: настоящий адрес в проверках не трогаем
    private sealed class FakeReceiver(Func<System.Net.Http.HttpRequestMessage, string?, System.Net.Http.HttpResponseMessage> reply) : System.Net.Http.HttpMessageHandler
    {
        public string? Body, ContentType;
        protected override async System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken token)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);
            ContentType = request.Content?.Headers.ContentType?.ToString();
            return reply(request, Body);
        }
    }

    // Правила показа дока: поверх полноэкранного окна он выезжает только после задержки у нижнего края
    private static void VerifyFullscreenReveal()
    {
        bool Show(bool fullscreen, bool autoHide = false, bool hover = false, bool edge = false, int ticks = 0, bool open = false) =>
            DockWindow.BarShouldShow(fullscreen, autoHide, false, hover, edge, ticks, open);
        if (!Show(false) || Show(false, autoHide: true) || !Show(false, autoHide: true, edge: true))
            throw new InvalidOperationException("Fullscreen: the normal auto-hide rules changed.");
        if (Show(true) || Show(true, edge: true, ticks: 1) || !Show(true, edge: true, ticks: 2) || !Show(true, hover: true) || !Show(true, open: true))
            throw new InvalidOperationException("Fullscreen: the dock does not reveal at the bottom edge or hides over an open panel.");
    }

    // Панель быстрых настроек как в Windows: плитки сети, громкость, снимок для просмотра (радио и громкость в проверке не трогаем)
    private static void VerifyQuickPanel(DockWindow dock, string folder)
    {
        var surface = dock.OpenWidget("Quick Toggles") ?? throw new InvalidOperationException("The quick settings panel did not open.");
        try
        {
            foreach (var name in new[] { "Bluetooth", "Ethernet", "Mobile hotspot", "Airplane mode" })
                if (FindControl(surface, L10n.T(name)) is not Button) throw new InvalidOperationException("Quick settings tile is missing: " + name);
            if (FindControl(surface, L10n.F($"{L10n.T("Bluetooth")} list")) is not Button) throw new InvalidOperationException("The Bluetooth list arrow is missing.");
            if (FindControl(surface, L10n.T("Volume")) is not Slider || FindControl(surface, L10n.T("Mute")) is not Button)
                throw new InvalidOperationException("Quick settings volume controls are missing.");
            surface.UpdateLayout();
            Capture(surface, Path.Combine(folder, "quick-panel.png"));
        }
        finally { surface.Close(); }
    }

    // Отправка отчёта: адрес, разбор подтверждения, ответы 200/429/500, лимит 32 КиБ, очистка журнала от личного и окно (без настоящей отправки)
    private static async System.Threading.Tasks.Task VerifyBugReport(DockWindow dock, string folder)
    {
        void Need(bool condition, string text) { if (!condition) throw new InvalidOperationException("Bug report: " + text); }
        Need(BugReport.Endpoint(BugReport.Url) is not null, "the receiver address is rejected");
        foreach (var bad in new[] { "http://example.com/x", "https://user:pw@example.com/x", "https://example.com/x?a=1", "https://example.com/x#a", "nonsense" })
            Need(BugReport.Endpoint(bad) is null, "unsafe address accepted: " + bad);
        Need(BugReport.ReadReceipt("{\"ok\":true,\"report_id\":\"abc-123_X\"}") == "abc-123_X", "good receipt rejected");
        foreach (var bad in new[] { "{\"ok\":false,\"report_id\":\"a\"}", "{\"report_id\":\"a\"}", "{\"ok\":true}", "{\"ok\":true,\"report_id\":\"a b\"}", "{\"ok\":true,\"report_id\":\"\"}", "[]" })
        {
            bool rejected = false;
            try { BugReport.ReadReceipt(bad); } catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException) { rejected = true; }
            Need(rejected, "bad receipt accepted: " + bad);
        }
        // журнал действий и настройки в диагностике: имена программ не попадают, настройки видны, свежие события есть
        Need(ActivityLog.Tile("app:C:\\Games\\secret.exe") == "app" && ActivityLog.Tile("CPU") == "CPU", "an app name reached the activity log");
        ActivityLog.Record("нажатие на плитку", "ПроверкаЖурнала");
        var prefs = new Preferences { AutoHide = true, Items = ["CPU", "app:C:\\Games\\secret.exe"], WorldTimeCity = "SecretCity" };
        var diag = BugReport.Diagnostics(prefs, ["Settings"]);
        Need(diag.Contains("AutoHide=True") && diag.Contains("Открыто сейчас: Settings") && diag.Contains("нажатие на плитку ПроверкаЖурнала"), "settings, open windows or activity are missing");
        Need(!diag.Contains("secret") && !diag.Contains("SecretCity"), "private data reached the diagnostics");
        Need(diag.Length <= BugReport.DiagnosticsLimit, "diagnostics exceed the limit");
        var endpoint = new Uri(BugReport.Url);
        string Json(string id) => "{\"ok\":true,\"report_id\":\"" + id + "\"}";
        System.Net.Http.HttpResponseMessage Answer(System.Net.HttpStatusCode code, string body) =>
            new(code) { Content = new System.Net.Http.StringContent(body) };
        var good = new FakeReceiver((_, body) => Answer(System.Net.HttpStatusCode.OK, Json(System.Text.Json.JsonDocument.Parse(body!).RootElement.GetProperty("report_id").GetString()!)));
        Need(await BugReport.SendAsync(endpoint, "id1", "Не работает «кнопка»", "Строка отчёта", good) == "id1", "delivery was not confirmed");
        using (var sent = System.Text.Json.JsonDocument.Parse(good.Body!))
        {
            var root = sent.RootElement;
            Need(root.GetProperty("description").GetString() == "Не работает «кнопка»" && root.GetProperty("report").GetString() == "Строка отчёта"
                && root.GetProperty("version").GetString() == BugReport.VersionText && root.EnumerateObject().Count() == 4, "payload fields are wrong");
        }
        Need(good.ContentType?.Contains("utf-8", StringComparison.OrdinalIgnoreCase) == true, "payload is not UTF-8 JSON");
        async System.Threading.Tasks.Task<Exception?> Fails(System.Net.Http.HttpMessageHandler handler, string description = "d")
        {
            try { await BugReport.SendAsync(endpoint, "id2", description, "r", handler); return null; }
            catch (Exception ex) { return ex; }
        }
        Need(await Fails(new FakeReceiver((_, _) => Answer(System.Net.HttpStatusCode.OK, Json("other")))) is InvalidDataException, "another report number was accepted");
        Need(await Fails(new FakeReceiver((_, _) => Answer(System.Net.HttpStatusCode.OK, "{\"ok\":true"))) is System.Text.Json.JsonException, "broken receipt was accepted");
        Need(await Fails(new FakeReceiver((_, _) => Answer((System.Net.HttpStatusCode)429, ""))) is BugReport.DeliveryException { Limited: true }, "limit answer is not recognised");
        Need(await Fails(new FakeReceiver((_, _) => Answer(System.Net.HttpStatusCode.InternalServerError, ""))) is BugReport.DeliveryException { Limited: false }, "server error is not a failure");
        var tooBig = new FakeReceiver((_, _) => Answer(System.Net.HttpStatusCode.OK, Json("id2")));
        Need(await Fails(tooBig, new string('я', 20000)) is InvalidDataException && tooBig.Body is null, "oversized report was sent");

        string clean = BugReport.CleanLog([
            "2026-11-18 10:00:00 System.IO.IOException: Cannot read C:\\Users\\Ivan\\Secret\\file.txt from https://example.com/a?b=1 for ivan@example.com",
            "   at LumaDock.DockWindow.Tick() in C:\\src\\DockWindow.cs:line 5",
            "   at System.Windows.Threading.Dispatcher.Invoke()",
            "2026-11-18 10:00:05 System.IO.IOException: Cannot read C:\\Users\\Ivan\\Secret\\file.txt from https://example.com/a?b=1 for ivan@example.com",
            "   at LumaDock.DockWindow.Tick() in C:\\src\\DockWindow.cs:line 5"]);
        Need(clean.Contains("System.IO.IOException") && clean.Contains("×2") && clean.Contains("LumaDock.DockWindow.Tick"), "log lost the error or did not merge repeats");
        Need(!clean.Contains("Ivan") && !clean.Contains("example.com") && !clean.Contains("C:\\") && !clean.Contains("DockWindow.cs"), "log kept personal data");
        string tagged = BugReport.CleanLog([
            "2026-11-18 11:00:00 [Radios] System.InvalidOperationException: radio failed for ivan@example.com",
            "   at LumaDock.RadioController.Refresh() in C:\\src\\RadioController.cs:line 9",
            "2026-11-18 11:00:03 [Radios] FlowBar.Note: Windows radio control did not respond in time"]);
        Need(tagged.Contains("[Radios] System.InvalidOperationException") && tagged.Contains("[Radios] FlowBar.Note: Windows radio control did not respond")
            && tagged.Contains("LumaDock.RadioController.Refresh") && !tagged.Contains("example.com"), "tagged log lines are lost or not cleaned");

        var surface = dock.OpenBugReport();
        try
        {
            var send = (Button?)FindControl(surface, L10n.T("Send")) ?? throw new InvalidOperationException("Bug report: Send button is missing.");
            var text = (TextBox?)FindControl(surface, L10n.T("Problem description")) ?? throw new InvalidOperationException("Bug report: description box is missing.");
            Need(!send.IsEnabled, "Send is enabled without a description");
            text.Text = "Тест";
            Need(send.IsEnabled, "Send stays disabled after typing");
            text.Text = "   ";
            Need(!send.IsEnabled, "Send is enabled for blank text");
            text.Text = "Проверка окна";
            surface.UpdateLayout();
            Capture(surface, Path.Combine(folder, "bug-report.png"));
        }
        finally { surface.Close(); }
    }

    private static DependencyObject? FindControl(DependencyObject node, string name)
    {
        if (AutomationProperties.GetName(node) == name) return node;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
        {
            var match = FindControl(VisualTreeHelper.GetChild(node, index), name);
            if (match is not null) return match;
        }
        return null;
    }

    private static void VerifyAccentControls(Window settings, DockModel model, string folder)
    {
        var custom = (Button?)FindControl(settings, "Custom accent") ?? throw new InvalidOperationException("Custom accent is missing.");
        var blue = (Button?)FindControl(settings, "Blue") ?? throw new InvalidOperationException("Blue preset is missing.");
        custom.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, custom));
        settings.UpdateLayout();
        var hex = (TextBox?)FindControl(settings, "Hex colour") ?? throw new InvalidOperationException("Inline accent editor did not open.");
        hex.Text = "#27A94C";
        hex.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, hex, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        var saved = new DockModel(model.Profile).Settings;
        if (saved.Accent != "#27A94C" || !saved.UseCustomAccent)
            throw new InvalidOperationException("Custom accent did not persist.");
        Capture(settings, Path.Combine(folder, "settings-custom-accent.png"));
        blue.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, blue));
        saved = new DockModel(model.Profile).Settings;
        if (saved.Accent != "#0A84FF" || saved.CustomAccent != "#27A94C" || saved.UseCustomAccent)
            throw new InvalidOperationException("The custom accent was lost when switching to a preset.");
    }

    private static void Click(Window window, string name)
    {
        var button = (Button?)FindControl(window, name) ?? throw new InvalidOperationException($"Missing button: {name}.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    }

    private static ConnectedData.Snapshot StockFixture()
    {
        using var response = System.Text.Json.JsonDocument.Parse("""
            {"s":"ok","t":[1763337600,1763078400,1762905600,1762646400,1763337600,1763596800],"c":[101.5,98.1,103.2,100.7,101.75,null]}
            """);
        using var empty = System.Text.Json.JsonDocument.Parse("""{"s":"no_data"}""");
        var prices = ConnectedData.ParseStockHistory(response.RootElement);
        if (prices.Length != 4 || prices[0].Close != 100.7m || prices[^1].Close != 101.75m
            || ConnectedData.ParseStockHistory(empty.RootElement).Length != 0)
            throw new InvalidOperationException("Historical stock prices were not parsed and ordered correctly.");
        return new("101.75  ·  +3.72%", "Latest provider quote", [new("EXAMPLE", "101.75  ·  +3.72%")], Prices: prices);
    }

    private static LiveData.Weather WeatherFixture()
    {
        using var response = System.Text.Json.JsonDocument.Parse("""
            {"current":{"time":"2026-11-16T16:15","temperature_2m":13,"apparent_temperature":12,"relative_humidity_2m":71,"wind_speed_10m":8,"weather_code":0,"is_day":1},
             "daily":{"time":["2026-11-16","2026-11-17","2026-11-18","2026-11-19","2026-11-20","2026-11-21","2026-11-22"],
              "temperature_2m_min":[5,6,11,11,9,9,8],"temperature_2m_max":[13,16,16,14,14,16,17],"weather_code":[1,3,61,61,61,95,61],
              "sunrise":["2026-11-16T07:54","2026-11-17T07:55",null,null,null,null,null],"sunset":["2026-11-16T17:55","2026-11-17T17:54",null,null,null,null,null]},
             "hourly":{"time":["2026-11-16T16:00","2026-11-16T17:00","2026-11-16T18:00","2026-11-16T19:00","2026-11-16T20:00","2026-11-16T21:00","2026-11-16T22:00"],
              "temperature_2m":[13,12,10,7,7,6,6],"precipitation_probability":[15,null,0,0,0,0,0],"weather_code":[0,0,0,0,0,0,0],"is_day":[1,1,0,0,0,0,0]}}
            """);
        var weather = LiveData.ParseWeather(response.RootElement);
        var hours = LiveData.UpcomingHours(weather).ToArray();
        if (weather.Days.Length != 7 || hours.Length != 6 || hours[0].At.Hour != 17 || hours[0].Chance is not null
            || !weather.IsDay || hours[1].IsDay || weather.Days[2].Sunrise.HasValue)
            throw new InvalidOperationException("Weather forecast parse or city-local hours failed.");
        return weather;
    }

    private static void VerifyWallpaperResponse()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""
            {"query":{"pages":[
                {"index":1,"pageid":12,"title":"File:Mountain_lake.jpg","imageinfo":[{
                    "mime":"image/jpeg","width":2400,"height":1500,
                    "thumburl":"https://upload.wikimedia.org/wikipedia/commons/thumb/file.jpg",
                    "extmetadata":{"Artist":{"value":"<a>Example &amp; Co</a>"},"LicenseShortName":{"value":"CC BY 4.0"}}
                }]},
                {"index":2,"pageid":13,"title":"File:External.jpg","imageinfo":[{
                    "mime":"image/jpeg","width":2400,"height":1500,
                    "thumburl":"https://example.invalid/file.jpg",
                    "extmetadata":{"LicenseShortName":{"value":"CC BY 4.0"}}
                }]}
            ]}}
            """);
        var photo = OnlineWallpapers.Parse(document.RootElement).Single();
        if (photo.Id != 12 || photo.Title != "Mountain lake" || photo.Artist != "Example & Co")
            throw new InvalidOperationException("Wallpaper metadata parsing failed.");
    }

    private static async System.Threading.Tasks.Task VerifyBrowserAccessibility(Window owner)
    {
        var controls = new System.Windows.Controls.StackPanel();
        var address = new System.Windows.Controls.TextBox { Text = "https://www.example.com/article" };
        System.Windows.Automation.AutomationProperties.SetAutomationId(address, "omnibox");
        controls.Children.Add(address);
        var fixture = new Window { Title = "Browser fixture", Width = 280, Height = 100, ShowActivated = false,
            ShowInTaskbar = false, Content = controls, Owner = owner, Left = -2000, Top = -2000 };
        try
        {
            fixture.Show();
            var handle = new System.Windows.Interop.WindowInteropHelper(fixture).Handle;
            var read = await System.Threading.Tasks.Task.Run(() => BrowserSites.Read(handle)).WaitAsync(TimeSpan.FromSeconds(5));
            if (read != "example.com") throw new InvalidOperationException("Browser address control not read through Windows UI Automation.");
            fixture.Title = "Browser fixture - Incognito";
            var privateDomain = await System.Threading.Tasks.Task.Run(() => BrowserSites.Read(handle)).WaitAsync(TimeSpan.FromSeconds(5));
            if (privateDomain is not null) throw new InvalidOperationException("Private browser mode was not excluded.");
        }
        finally { fixture.Close(); }
    }

    private static int CheckWallpapers(string folder)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(32));
            var photo = OnlineWallpapers.Search("mountain lake landscape", deadline.Token).GetAwaiter().GetResult().First();
            var preview = OnlineWallpapers.Thumbnail(photo, deadline.Token).GetAwaiter().GetResult();
            var filename = OnlineWallpapers.Save(photo, folder, deadline.Token).GetAwaiter().GetResult();
            if (preview.PixelWidth < 100 || new FileInfo(filename).Length < 1024)
                throw new InvalidOperationException("Downloaded image was empty.");
            File.WriteAllText(Path.Combine(folder, "result.txt"), "OK");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(folder, "result.txt"), ex.GetType().Name + " · " + ex.Message);
            return 1;
        }
    }

    private static async System.Threading.Tasks.Task VerifyDateControl(Window surface, string folder)
    {
        string suffix = L10n.Russian ? "-ru" : "";
        Click(surface, L10n.T("Schedule reminder"));
        surface.UpdateLayout();
        var date = FindControl(surface, L10n.T("Reminder date")) as DatePicker
            ?? throw new InvalidOperationException("Reminder date is missing.");
        var editor = date.Template.FindName("PART_TextBox", date) as DatePickerTextBox
            ?? throw new InvalidOperationException("Date entry part is missing.");
        var popup = date.Template.FindName("PART_Popup", date) as Popup
            ?? throw new InvalidOperationException("Calendar popup part is missing.");
        var open = date.Template.FindName("PART_Button", date) as Button
            ?? throw new InvalidOperationException("Calendar button part is missing.");
        Capture(surface, Path.Combine(folder, $"themed-date-field{suffix}.png"));
        open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, open));
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        if (!date.IsDropDownOpen || popup.Child is not Calendar calendar)
            throw new InvalidOperationException("The styled calendar did not open.");
        calendar.ApplyTemplate();
        calendar.UpdateLayout();
        var item = calendar.Template.FindName("PART_CalendarItem", calendar) as CalendarItem
            ?? throw new InvalidOperationException("The styled calendar item is missing.");
        item.ApplyTemplate();
        if (item.Template.FindName("PART_MonthView", item) is not Grid { Children.Count: 49 })
            throw new InvalidOperationException("Calendar did not render all date cells.");
        calendar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        calendar.Arrange(new Rect(calendar.DesiredSize));
        CaptureElement(calendar, Path.Combine(folder, $"themed-date-popup{suffix}.png"));
        DateTime picked = DateTime.Today.AddDays(3);
        calendar.SelectedDate = picked;
        if (date.SelectedDate != picked) throw new InvalidOperationException("Calendar date did not reach the form.");
        date.IsDropDownOpen = false;
        DateTime typed = picked.AddDays(2);
        editor.Text = typed.ToString("d", L10n.Culture);
        var source = PresentationSource.FromVisual(editor) ?? throw new InvalidOperationException("Date editor has no input source.");
        editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
        if (date.SelectedDate != typed) throw new InvalidOperationException("Typed date did not reach the form.");
    }

    // Флажок задач в общем стиле: шаблон на месте, клик отмечает задачу в профиле, галочка появляется, снимок состояний
    private static void VerifyTaskCheckBox(Window surface, DockModel model, string folder)
    {
        string suffix = L10n.Russian ? "-ru" : "";
        string first = "Checkbox task A" + suffix, second = "Checkbox task B" + suffix;
        var input = FindControl(surface, L10n.T("New todo")) as TextBox
            ?? throw new InvalidOperationException("New task field is missing.");
        foreach (string name in new[] { first, second })
        {
            input.Text = name;
            Click(surface, L10n.T("Add task"));
        }
        surface.UpdateLayout();
        var check = FindControl(surface, first) as CheckBox
            ?? throw new InvalidOperationException("Task checkbox is missing.");
        check.ApplyTemplate();
        if (check.Template.FindName("Box", check) is not Border || check.Template.FindName("Tick", check) is not System.Windows.Shapes.Path tick)
            throw new InvalidOperationException("Checkbox template parts are missing.");
        if (check.IsChecked == true || tick.Visibility == Visibility.Visible)
            throw new InvalidOperationException("A new task must start unchecked.");
        // клик мыши и клавиатуры в WPF сводится к ButtonBase.OnClick
        typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(check, null);
        if (!model.Settings.Todos.Single(t => t.Text == first).Done)
            throw new InvalidOperationException("Checkbox click did not complete the task.");
        surface.UpdateLayout();
        var again = FindControl(surface, first) as CheckBox ?? throw new InvalidOperationException("Checked task checkbox is missing.");
        again.ApplyTemplate();
        again.UpdateLayout();
        if (again.IsChecked != true || again.Template.FindName("Tick", again) is not System.Windows.Shapes.Path { Visibility: Visibility.Visible })
            throw new InvalidOperationException("The checked checkbox did not show its tick.");
        Capture(surface, Path.Combine(folder, $"themed-checkbox{suffix}.png"));
        model.Settings.Todos.RemoveAll(t => t.Text == first || t.Text == second);
        model.Save();
    }

    // Выпадающий список городов в общем стиле: список открывается скруглённым окном, выбор доходит до профиля
    private static async System.Threading.Tasks.Task VerifyCityDropdown(Window surface, DockModel model, string folder)
    {
        var combo = FindControl(surface, L10n.F($"Clock {1}")) as ComboBox
            ?? throw new InvalidOperationException("City dropdown is missing.");
        combo.ApplyTemplate();
        var popup = combo.Template.FindName("PART_Popup", combo) as Popup
            ?? throw new InvalidOperationException("City dropdown popup part is missing.");
        if (combo.Template.FindName("Field", combo) is not Border { CornerRadius.TopLeft: > 8 })
            throw new InvalidOperationException("City dropdown did not receive the rounded frame.");
        Capture(surface, Path.Combine(folder, "themed-city-dropdown.png"));
        combo.IsDropDownOpen = true;
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        if (!combo.IsDropDownOpen || popup.Child is not Border list)
            throw new InvalidOperationException("The styled city list did not open.");
        list.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        list.Arrange(new Rect(list.DesiredSize));
        if (combo.ItemContainerGenerator.ContainerFromIndex(0) is not ComboBoxItem)
            throw new InvalidOperationException("City list items were not created.");
        CaptureElement(list, Path.Combine(folder, "themed-city-list.png"));
        int next = (combo.SelectedIndex + 1) % combo.Items.Count;
        combo.SelectedIndex = next;
        combo.IsDropDownOpen = false;
        if (model.Settings.WorldClockCities[0] != Widgets.ClockCities[next].Key)
            throw new InvalidOperationException("City choice did not reach the profile.");
    }

    // Длинная заметка: полоса прокрутки получает общий тёмный шаблон (PART_Track с тонким скруглённым ползунком)
    private static void VerifyNotesScrollBar(DockWindow dock, DockModel model, string folder)
    {
        string suffix = L10n.Russian ? "-ru" : "";
        string saved = model.Settings.Notes;
        var notes = dock.OpenWidget("Notes") ?? throw new InvalidOperationException("Notes did not open.");
        var field = FindControl(notes, L10n.T("Notes")) as TextBox ?? throw new InvalidOperationException("Notes field is missing.");
        field.Text = string.Join("\n", Enumerable.Range(1, 60).Select(i => $"Line {i}"));
        notes.UpdateLayout();
        field.ApplyTemplate();
        var bar = FindScrollBar(field) ?? throw new InvalidOperationException("Notes scrollbar is missing.");
        bar.ApplyTemplate();
        if (bar.Template.FindName("PART_Track", bar) is not System.Windows.Controls.Primitives.Track { Thumb: not null })
            throw new InvalidOperationException("The scrollbar did not receive the styled track.");
        Capture(notes, Path.Combine(folder, $"themed-scrollbar{suffix}.png"));
        field.Text = saved;
        notes.Close();
    }

    private static System.Windows.Controls.Primitives.ScrollBar? FindScrollBar(DependencyObject node)
    {
        if (node is System.Windows.Controls.Primitives.ScrollBar { Orientation: Orientation.Vertical, Visibility: Visibility.Visible } bar) return bar;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
        {
            var match = FindScrollBar(VisualTreeHelper.GetChild(node, index));
            if (match is not null) return match;
        }
        return null;
    }

    // Диктовка: шарик движется, нажатие начинает и останавливает запись, распознанный текст попадает в заметки и сохраняется
    // (в проверке запись и Handy заменены тестовыми: Dictation.Fixture)
    // Списки в окнах Wi-Fi и Bluetooth: на образцах видны сети и устройства, состояние, заряд и кнопки
    private static async System.Threading.Tasks.Task VerifyDeviceLists(DockWindow dock, string folder)
    {
        WifiNetworks.Fixture = BluetoothDevices.Fixture = true;
        try
        {
            var wifi = dock.OpenWidget("Wi-Fi") ?? throw new InvalidOperationException("The Wi-Fi window did not open.");
            await WaitFor(() => FaceText(wifi).Contains("Home Wi-Fi"));
            string text = FaceText(wifi);
            if (!text.Contains(L10n.T("Available networks")) || !text.Contains("Guest") || !text.Contains(L10n.T("Connected")) || !text.Contains("82%") || !text.Contains(L10n.T("Saved")))
                throw new InvalidOperationException("The Wi-Fi list is incomplete: " + text);
            if (FindControl(wifi, L10n.T("Disconnect") + " Home Wi-Fi") is not Button || FindControl(wifi, L10n.T("Connect") + " Guest") is not Button)
                throw new InvalidOperationException("The Wi-Fi buttons are missing.");
            Capture(wifi, Path.Combine(folder, "wifi-list.png"));
            wifi.Close();
            var bluetooth = dock.OpenWidget("Bluetooth") ?? throw new InvalidOperationException("The Bluetooth window did not open.");
            await WaitFor(() => FaceText(bluetooth).Contains("Headphones"));
            text = FaceText(bluetooth);
            if (!text.Contains(L10n.T("Paired devices")) || !text.Contains("Speaker") || !text.Contains(L10n.T("Paired")) || !text.Contains("80%"))
                throw new InvalidOperationException("The Bluetooth list is incomplete: " + text);
            Capture(bluetooth, Path.Combine(folder, "bluetooth-list.png"));
            // «Добавить устройство»: поиск показывает устройства поблизости, «Сопрячь» сопрягает
            var addButton = FindControl(bluetooth, L10n.T("Add a device")) as Button ?? throw new InvalidOperationException("The add device button is missing.");
            addButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, addButton));
            await WaitFor(() => FaceText(bluetooth).Contains("Gamepad"));
            if (!FaceText(bluetooth).Contains(L10n.T("Nearby devices")) || FindControl(bluetooth, L10n.T("Pair") + " Mouse") is not Button)
                throw new InvalidOperationException("The nearby devices list is incomplete.");
            Capture(bluetooth, Path.Combine(folder, "bluetooth-add.png"));
            var pairButton = (Button)FindControl(bluetooth, L10n.T("Pair") + " Mouse")!;
            pairButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, pairButton));
            await WaitFor(() => FaceText(bluetooth).Contains(L10n.T("The device was paired.")));
            if (FindControl(bluetooth, L10n.T("Pair") + " Mouse") is not null) throw new InvalidOperationException("A paired device stayed in the nearby list.");
            bluetooth.Close();
        }
        finally { WifiNetworks.Fixture = BluetoothDevices.Fixture = false; }

        // без адаптера Wi-Fi в быстрых настройках нет кнопки Wi-Fi, и виджет уже на её ширину
        var quick = Widgets.Available["Quick Toggles"];
        try
        {
            WifiNetworks.AdapterOverride = true;
            int with = ((Panel)Widgets.Face(quick, dock.Model, false, out _)).Children.Count;
            double withWidth = Widgets.WidthOf(dock.Model.Settings, quick);
            WifiNetworks.AdapterOverride = false;
            int without = ((Panel)Widgets.Face(quick, dock.Model, false, out _)).Children.Count;
            double withoutWidth = Widgets.WidthOf(dock.Model.Settings, quick);
            if (with != 5 || without != 4 || Math.Abs(withWidth - withoutWidth - Widgets.WifiTileWidth) > .01)
                throw new InvalidOperationException($"The Wi-Fi button hiding is wrong: {with}/{without} buttons, widths {withWidth}/{withoutWidth}.");
        }
        finally { WifiNetworks.AdapterOverride = null; }
    }

    // Окно плеера: обложка, название, исполнитель, источник, время и кнопки; перемотка и время считаются по данным сеанса
    private static async System.Threading.Tasks.Task VerifyPlayer(DockWindow dock, DockModel model, string folder)
    {
        if (DockWindow.SourceName("Spotify.exe") != "Spotify" || DockWindow.SourceName("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic") != "ZuneMusic" || DockWindow.SourceName("Chrome") != "Chrome")
            throw new InvalidOperationException("The media source name is wrong.");
        var audio = new[] { new WindowsShell.AudioSession("a", "Spotify", .5, false, "Spotify"), new WindowsShell.AudioSession("b", "Chrome", .5, false, "chrome"), new WindowsShell.AudioSession("c", "System", .5, false, "") };
        if (DockWindow.SourceSessions("Spotify.exe", audio).Select(session => session.Id).SequenceEqual(["a"]) is false || DockWindow.SourceSessions("Chrome", audio).Select(session => session.Id).SequenceEqual(["b"]) is false || DockWindow.SourceSessions("Nothing.exe", audio).Length != 0)
            throw new InvalidOperationException("The player audio session is not found by its source.");
        // из заголовка окна док использует только число непрочитанного в начале: остальные смены заголовка плитки не пересобирают
        if (DockWindow.UnreadPrefix.Match("(3) Inbox - Mail").Value != "(3) " || DockWindow.UnreadPrefix.Match("(99+) Chat").Value != "(99+) " ||
            DockWindow.UnreadPrefix.Match("Inbox (3)").Success || DockWindow.UnreadPrefix.Match("Claude Code").Success)
            throw new InvalidOperationException("The unread title prefix is matched wrongly.");
        WindowsShell.ReadEconomy(); // режим экономии читается без ошибок на любой машине
        var pixels = new byte[200 * 200 * 4];
        for (int y = 0; y < 200; y++)
            for (int x = 0; x < 200; x++) { int i = (y * 200 + x) * 4; pixels[i] = (byte)(40 + x / 2); pixels[i + 1] = (byte)(80 + y / 3); pixels[i + 2] = 180; pixels[i + 3] = 255; }
        var art = System.Windows.Media.Imaging.BitmapSource.Create(200, 200, 96, 96, PixelFormats.Bgra32, null, pixels, 800);
        art.Freeze();
        model.Media.SetForTest(new MediaSession.Track("Disturbia", "Rihanna", "Playing", "Spotify.exe", null, 83, 227), art);
        try
        {
            var window = dock.OpenWidget("Now Playing Artwork") ?? throw new InvalidOperationException("The player window did not open.");
            await WaitFor(() => FaceText(window).Contains("Disturbia"));
            string text = FaceText(window);
            if (!text.Contains("Rihanna") || !text.Contains("Spotify") || !text.Contains("1:2") || !text.Contains("3:47"))
                throw new InvalidOperationException("The player window is incomplete: " + text);
            if (FindControl(window, L10n.T("Pause")) is not Button || FindControl(window, L10n.T("Prev")) is not Button || FindControl(window, L10n.T("Next")) is not Button)
                throw new InvalidOperationException("The player buttons are missing.");
            Capture(window, Path.Combine(folder, "player.png"));
            window.Close();
        }
        finally { model.Media.SetForTest(null!, null); }
    }

    // Размер виджета звука: по умолчанию 242, выбранная ширина применяется к плитке и сохраняется в профиле, у других виджетов размера нет
    private static async System.Threading.Tasks.Task VerifyWidgetSize(DockWindow dock, DockModel model)
    {
        var audio = Widgets.Available["Audio Output"];
        if (Widgets.WidthOf(model.Settings, audio) != audio.Width || Widgets.SizeChoices("Audio Output").Length != 4 || Widgets.SizeChoices("Clock").Length != 0)
            throw new InvalidOperationException("The widget size choices are wrong.");
        if (!model.Settings.Items.Contains("Audio Output")) throw new InvalidOperationException("Audio Output must be in the dock for the size check.");
        double saved = model.Settings.WidgetWidths.TryGetValue("Audio Output", out double earlier) ? earlier : 0;
        model.Settings.WidgetWidths["Audio Output"] = 330;
        model.Save();
        await WaitFor(() => dock.HasTileWidth(330));
        if (!(new DockModel(model.Profile).Settings.WidgetWidths.TryGetValue("Audio Output", out double kept) && kept == 330)) throw new InvalidOperationException("The widget size was not saved.");
        model.Settings.WidgetWidths["Clock"] = 999; // чужой виджет: ширина игнорируется
        if (Widgets.WidthOf(model.Settings, Widgets.Available["Clock"]) != Widgets.Available["Clock"].Width) throw new InvalidOperationException("A fixed widget was resized.");
        model.Settings.WidgetWidths.Remove("Clock");
        if (saved == 0) model.Settings.WidgetWidths.Remove("Audio Output"); else model.Settings.WidgetWidths["Audio Output"] = saved;
        model.Save();
    }

    // Значки программ высокого разрешения: оболочка отдаёт 256 px с прозрачностью (углы прозрачные, середина непрозрачная)
    private static void VerifyIcons()
    {
        var icon = WindowsShell.HighResIcon(Path.Combine(Environment.SystemDirectory, "notepad.exe")) ?? WindowsShell.HighResIcon(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
        if (icon is null || icon.PixelWidth < 128 || icon.PixelHeight < 128) throw new InvalidOperationException("The high resolution icon is missing or small.");
        var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(icon, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        bool transparent = false, opaque = false;
        for (int i = 3; i < pixels.Length; i += 4) { transparent |= pixels[i] == 0; opaque |= pixels[i] == 255; }
        if (!transparent || !opaque) throw new InvalidOperationException("The icon has no transparency.");
        // значок приложения из магазина берётся из «Приложений» по идентификатору (у exe таких приложений своего значка нет)
        var packaged = WindowsShell.ShellImage(@"shell:AppsFolder\windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel", 256);
        if (packaged is null || packaged.PixelWidth < 64) throw new InvalidOperationException("The Store app icon from the Apps folder is missing.");
        // exe без значка среди ресурсов (как Ollama) узнаётся, чтобы вместо серой заглушки оболочки взять значок окна
        var bare = Path.Combine(Path.GetTempPath(), "flowbar-no-icon-" + Environment.ProcessId + ".exe");
        File.WriteAllBytes(bare, new byte[2048]);
        try
        {
            if (WindowsShell.HasOwnIcon(bare)) throw new InvalidOperationException("A file without icon resources was taken for an app with its own icon.");
            if (!WindowsShell.HasOwnIcon(Path.Combine(Environment.SystemDirectory, "notepad.exe"))) throw new InvalidOperationException("Notepad has its own icon, but it was not found.");
        }
        finally { File.Delete(bare); }
        // значок, который задан в самом ярлыке, читается; пустой — нет (иначе у Ollama остаётся серая заглушка вместо app.ico)
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        string notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe"), withIcon = Path.Combine(Path.GetTempPath(), "flowbar-icon-" + Environment.ProcessId + ".lnk"), without = Path.ChangeExtension(withIcon, ".b.lnk");
        try
        {
            dynamic a = ((dynamic)shell).CreateShortcut(withIcon); a.TargetPath = Path.Combine(Environment.SystemDirectory, "cmd.exe"); a.IconLocation = notepad + ",0"; a.Save();
            dynamic b = ((dynamic)shell).CreateShortcut(without); b.TargetPath = notepad; b.Save();
            if (!string.Equals(WindowsShell.LinkIconFile(withIcon), notepad, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The icon set in a shortcut was not read.");
            if (WindowsShell.LinkIconFile(without) is not null) throw new InvalidOperationException("A shortcut without its own icon reported one.");
        }
        finally { File.Delete(withIcon); File.Delete(without); System.Runtime.InteropServices.Marshal.ReleaseComObject(shell); }
        // Discord: запущенный «папка\app-1.0.9\X.exe» и ярлык на «папка\X.exe» — одна программа, а без заглушки путь остаётся как есть
        var root = Path.Combine(Path.GetTempPath(), "flowbar-stub-" + Environment.ProcessId);
        Directory.CreateDirectory(Path.Combine(root, "app-1.0.9"));
        try
        {
            string running = Path.Combine(root, "app-1.0.9", "X.exe"), stub = Path.Combine(root, "X.exe");
            if (WindowsShell.ResolveApp(running) != running) throw new InvalidOperationException("A versioned exe without a stub was changed.");
            File.WriteAllBytes(stub, [0]);
            if (WindowsShell.ResolveApp(running) != stub) throw new InvalidOperationException("A versioned exe was not matched to its launcher.");
            // ярлык из панели Windows «Update.exe --processStart X.exe» и ярлык на сам X.exe — одна программа, иначе в доке появится второй значок
            File.WriteAllBytes(Path.Combine(root, "Update.exe"), [0]);
            File.WriteAllBytes(running, [0]);
            var link = ((dynamic)Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!).CreateShortcut(Path.Combine(root, "X.lnk"));
            link.TargetPath = Path.Combine(root, "Update.exe"); link.Arguments = "--processStart X.exe"; link.Save();
            var direct = ((dynamic)Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!).CreateShortcut(Path.Combine(root, "Direct.lnk"));
            direct.TargetPath = running; direct.Save();
            if (WindowsShell.MatchApp(Path.Combine(root, "X.lnk")) != stub || WindowsShell.MatchApp(Path.Combine(root, "Direct.lnk")) != stub)
                throw new InvalidOperationException("Shortcuts to one Squirrel app resolve to different programs.");
        }
        finally { Directory.Delete(root, true); }
        // окно у помощника (steamwebhelper) относится к главной программе (steam.exe) выше по папке, но не к Проводнику и не к программе рядом
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!WindowsShell.IsHostOf(@"C:\Games\Steam\bin\cef\steamwebhelper.exe", @"C:\Games\Steam\steam.exe")
            || WindowsShell.IsHostOf(@"C:\Games\Steam\bin\cef\steamwebhelper.exe", @"C:\Games\Other\other.exe")
            || WindowsShell.IsHostOf(Path.Combine(windows, @"System32\notepad.exe"), Path.Combine(windows, "explorer.exe"))
            || WindowsShell.IsHostOf(@"C:\Games\a.exe", @"C:\Games\a.exe"))
            throw new InvalidOperationException("The rule for helper processes of a pinned app is wrong.");
    }

    // Док на втором мониторе: настройка включена по умолчанию, дополнительный док стоит внизу своего монитора и целиком в его границах (на одном мониторе проверка пропускается)
    private static async System.Threading.Tasks.Task VerifyMonitors(DockModel model)
    {
        if (!new Preferences().AllMonitors) throw new InvalidOperationException("The dock must be shown on all monitors by default.");
        if (new Preferences().WindowsOnDocks != "all") throw new InvalidOperationException("Open windows must be shown on every dock by default.");
        var other = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(screen => !screen.Primary);
        if (other is null) return;
        var extra = new DockWindow(model, true, false, other);
        try
        {
            extra.Show();
            await System.Threading.Tasks.Task.Delay(500);
            var bounds = extra.ScreenBounds();
            var area = other.Bounds;
            if (bounds.Width < 100 || bounds.Left < area.Left - 2 || bounds.Right > area.Right + 2 || bounds.Bottom > area.Bottom + 2 || bounds.Bottom < area.Bottom - area.Height / 4.0)
                throw new InvalidOperationException($"The dock on the second monitor is misplaced: {bounds} in {area}.");
        }
        finally { extra.Close(); }
    }

    // Кнопка записи в окне задач: первое нажатие начинает запись, второе распознаёт и добавляет задачу
    private static async System.Threading.Tasks.Task VerifyTaskDictation(DockWindow dock, DockModel model, string folder)
    {
        var saved = model.Settings.Todos.ToList();
        model.Settings.Todos.Clear();
        var surface = dock.OpenWidget("Todos") ?? throw new InvalidOperationException("Todos did not open.");
        surface.UpdateLayout();
        var start = FindControl(surface, L10n.T("Dictate a task")) as Button ?? throw new InvalidOperationException("The task record button is missing.");
        start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, start));
        await WaitFor(() => Dictation.IsRecording);
        var stop = FindControl(surface, L10n.T("Stop dictation")) as Button ?? throw new InvalidOperationException("The task stop button is missing.");
        Capture(surface, Path.Combine(folder, "tasks-recording.png"));
        stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, stop));
        await WaitFor(() => model.Settings.Todos.Count == 1);
        if (Dictation.IsRecording || model.Settings.Todos[0].Text != "Тестовая запись диктовки." || model.Settings.Todos[0].Done)
            throw new InvalidOperationException("The dictated task was not added.");
        if (new DockModel(model.Profile).Settings.Todos.Count != 1) throw new InvalidOperationException("The dictated task was not saved.");
        await WaitFor(() => FindControl(surface, L10n.T("Dictate a task")) is Button { IsEnabled: true });
        Capture(surface, Path.Combine(folder, "tasks-recorded.png"));
        surface.Close();
        model.Settings.Todos.Clear();
        model.Settings.Todos.AddRange(saved);
        model.Save(false);
    }

    private static async System.Threading.Tasks.Task VerifyDictation(DockWindow dock, DockModel model, string folder)
    {
        string suffix = L10n.Russian ? "-ru" : "";
        string before = model.Settings.Notes;
        var surface = dock.OpenWidget("Dictation") ?? throw new InvalidOperationException("Dictation did not open.");
        surface.UpdateLayout();
        var start = FindControl(surface, L10n.T("Start dictation")) as Button ?? throw new InvalidOperationException("Dictation start button is missing.");
        var orb = FindVisual<VoiceOrb>(surface) ?? throw new InvalidOperationException("Dictation orb is missing.");
        var seen = new System.Collections.Generic.HashSet<int>();
        for (int index = 0; index < 16; index++) { await System.Threading.Tasks.Task.Delay(60); seen.Add(orb.CurrentFrame); }
        if (!orb.Running || seen.Count < 4) throw new InvalidOperationException("The dictation orb did not animate.");
        start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, start));
        await WaitFor(() => Dictation.IsRecording);
        var stop = FindControl(surface, L10n.T("Stop dictation")) as Button ?? throw new InvalidOperationException("Dictation stop button is missing.");
        Capture(surface, Path.Combine(folder, $"dictation-recording{suffix}.png"));
        stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, stop));
        await WaitFor(() => model.Settings.Notes != before);
        if (Dictation.IsRecording || !model.Settings.Notes.Contains("Тестовая запись диктовки."))
            throw new InvalidOperationException("The recognised text did not reach the notes.");
        if (new DockModel(model.Profile).Settings.Notes != model.Settings.Notes)
            throw new InvalidOperationException("The dictated note was not saved.");
        await WaitFor(() => FindControl(surface, L10n.T("Start dictation")) is Button { IsEnabled: true });
        Capture(surface, Path.Combine(folder, $"dictation-result{suffix}.png"));
        surface.Close();
        model.Settings.Notes = before;
        model.Save(false);
        await VerifyAssistant(dock, model, folder, before);
        VerifyAssistantJson();
        if (!Widgets.Available.TryGetValue("Dictation", out var spec) || Widgets.ReferenceCategories.Any(category => category.Catalog.Split('|').Contains("Dictation")))
            throw new InvalidOperationException("Dictation must be a FlowBar widget outside the reference catalog.");
        Widgets.Face(spec, model, false, out _);
    }

    // Помощник: с выбранным помощником диктовка не пишет в заметки сразу, а показывает список задач и заметку; Save записывает отмеченное
    // (в проверке ответ помощника заменён VoiceAssistant.Fixture, настоящие Codex, Claude и Gemini не вызываются)
    private static async System.Threading.Tasks.Task VerifyAssistant(DockWindow dock, DockModel model, string folder, string notesBefore)
    {
        string suffix = L10n.Russian ? "-ru" : "";
        var p = model.Settings;
        string providerBefore = p.VoiceProvider;
        int todosBefore = p.Todos.Count;
        p.VoiceProvider = "codex";
        VoiceAssistant.Fixture = true;
        try
        {
            var surface = dock.OpenWidget("Dictation") ?? throw new InvalidOperationException("Dictation did not open.");
            surface.UpdateLayout();
            var start = FindControl(surface, L10n.T("Start dictation")) as Button ?? throw new InvalidOperationException("Dictation start button is missing.");
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, start));
            await WaitFor(() => Dictation.IsRecording);
            var stop = FindControl(surface, L10n.T("Stop dictation")) as Button ?? throw new InvalidOperationException("Dictation stop button is missing.");
            stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, stop));
            await WaitFor(() => FindControl(surface, L10n.T("Save")) is Button);
            if (p.Notes != notesBefore || p.Todos.Count != todosBefore)
                throw new InvalidOperationException("The assistant result was saved before it was reviewed.");
            surface.UpdateLayout();
            Capture(surface, Path.Combine(folder, $"dictation-assistant{suffix}.png"));
            var save = FindControl(surface, L10n.T("Save")) as Button ?? throw new InvalidOperationException("The Save button is missing.");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, save));
            await WaitFor(() => p.Todos.Count == todosBefore + 2);
            if (!p.Todos.Any(todo => todo.Text == "Купить хлеб и молоко" && todo.Due is not null) || !p.Todos.Any(todo => todo.Text == "Позвонить заказчику" && todo.Due is null)
                || !p.Notes.Contains("Идея для заметки из тестовой диктовки."))
                throw new InvalidOperationException("The assistant tasks or note were not saved.");
            if (new DockModel(model.Profile).Settings.Todos.Count != todosBefore + 2) throw new InvalidOperationException("The assistant tasks were not stored on disk.");
            surface.Close();

            // Окно закрыто, пока результат не сохранён: продиктованный текст целиком попадает в заметки, задачи не добавляются
            p.Notes = notesBefore;
            surface = dock.OpenWidget("Dictation") ?? throw new InvalidOperationException("Dictation did not open.");
            surface.UpdateLayout();
            start = FindControl(surface, L10n.T("Start dictation")) as Button ?? throw new InvalidOperationException("Dictation start button is missing.");
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, start));
            await WaitFor(() => Dictation.IsRecording);
            stop = FindControl(surface, L10n.T("Stop dictation")) as Button ?? throw new InvalidOperationException("Dictation stop button is missing.");
            stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, stop));
            await WaitFor(() => FindControl(surface, L10n.T("Save")) is Button);
            surface.Close();
            if (!p.Notes.Contains("Тестовая запись диктовки.") || p.Todos.Count != todosBefore + 2)
                throw new InvalidOperationException("The dictated text was lost when the window closed before saving.");
        }
        finally
        {
            VoiceAssistant.Fixture = false;
            p.VoiceProvider = providerBefore;
            p.Todos.RemoveRange(todosBefore, p.Todos.Count - todosBefore);
            p.Notes = notesBefore;
            model.Save(false);
        }
    }

    // Доступ для агентов: настоящий процесс «--mcp» по stdio, вызовы идут в приложение через канал (в проверке со своим именем канала);
    // чтение закрыто, пока его не разрешили, а при выключенном доступе запись тоже отклоняется
    private static async System.Threading.Tasks.Task VerifyAgent(DockWindow dock, DockModel model)
    {
        var p = model.Settings;
        var saved = (p.Agent, p.AgentRead, p.Notes, Todos: p.Todos.Count);
        string pipe = "FlowBar2Agent-smoke-" + Guid.NewGuid().ToString("N")[..12];
        Agent.PipeName = pipe;
        Agent.Start(model, dock.Dispatcher);
        var info = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--mcp")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            StandardInputEncoding = new System.Text.UTF8Encoding(false), StandardOutputEncoding = new System.Text.UTF8Encoding(false)
        };
        info.Environment["LUMADOCK_AGENT_PIPE"] = pipe;
        using var process = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("The MCP process did not start.");
        try
        {
            async System.Threading.Tasks.Task<System.Text.Json.Nodes.JsonObject> Ask(string json)
            {
                await process.StandardInput.WriteLineAsync(json);
                await process.StandardInput.FlushAsync();
                var read = process.StandardOutput.ReadLineAsync();
                if (await System.Threading.Tasks.Task.WhenAny(read, System.Threading.Tasks.Task.Delay(15000)) != read)
                    throw new InvalidOperationException("The MCP process did not answer.");
                return System.Text.Json.Nodes.JsonNode.Parse(await read ?? throw new InvalidOperationException("The MCP process closed."))!.AsObject();
            }
            string Call(int id, string tool, string arguments) =>
                "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"tools/call\",\"params\":{\"name\":\"" + tool + "\",\"arguments\":" + arguments + "}}";
            string TextOf(System.Text.Json.Nodes.JsonObject reply) => reply["result"]!["content"]![0]!["text"]!.GetValue<string>();
            bool IsError(System.Text.Json.Nodes.JsonObject reply) => reply["result"]!["isError"]!.GetValue<bool>();

            var hello = await Ask("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}");
            if (hello["result"]!["serverInfo"]!["name"]!.GetValue<string>() != "lumadock") throw new InvalidOperationException("MCP initialize failed.");
            await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
            var list = await Ask("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}");
            if (list["result"]!["tools"]!.AsArray().Count != Agent.ToolCount) throw new InvalidOperationException("MCP tool list is wrong.");

            p.Agent = true; p.AgentRead = false;
            var added = await Ask(Call(3, "add_task", "{\"text\":\"Агент: купить чай\",\"due\":\"2026-11-20 10:00\"}"));
            if (IsError(added) || !p.Todos.Any(todo => todo.Text == "Агент: купить чай" && todo.Due == new DateTime(2026, 11, 20, 10, 0, 0)))
                throw new InvalidOperationException("The agent could not add a task.");
            if (!IsError(await Ask(Call(4, "list_tasks", "{}"))) || !IsError(await Ask(Call(5, "read_notes", "{}"))))
                throw new InvalidOperationException("Agent reading worked while it was turned off.");
            if (!IsError(await Ask(Call(6, "add_task", "{\"text\":\"x\",\"due\":\"завтра\"}"))))
                throw new InvalidOperationException("A bad due date was accepted.");

            p.AgentRead = true;
            if (!TextOf(await Ask(Call(7, "list_tasks", "{}"))).Contains("Агент: купить чай")) throw new InvalidOperationException("The agent could not read tasks.");
            if (IsError(await Ask(Call(8, "add_note", "{\"text\":\"Заметка агента\"}"))) || !TextOf(await Ask(Call(9, "read_notes", "{}"))).Contains("Заметка агента"))
                throw new InvalidOperationException("The agent could not write and read notes.");
            if (IsError(await Ask(Call(10, "complete_task", "{\"text\":\"агент: купить чай\"}"))) || !p.Todos.Single(todo => todo.Text == "Агент: купить чай").Done)
                throw new InvalidOperationException("The agent could not complete a task.");

            int count = p.Todos.Count;
            p.Agent = false;
            if (!IsError(await Ask(Call(11, "add_task", "{\"text\":\"Запрещено\"}"))) || p.Todos.Count != count)
                throw new InvalidOperationException("Agent writing worked while it was turned off.");
            if ((await Ask("{\"jsonrpc\":\"2.0\",\"id\":12,\"method\":\"nope\"}"))["error"]!["code"]!.GetValue<int>() != -32601)
                throw new InvalidOperationException("An unknown MCP method was not rejected.");
            // поле не того типа не роняет процесс, он отвечает ошибкой и продолжает работать
            if ((await Ask("{\"jsonrpc\":\"2.0\",\"id\":13,\"method\":5}"))["error"]!["code"]!.GetValue<int>() != -32600 || process.HasExited)
                throw new InvalidOperationException("A malformed MCP request was not rejected.");
            // длинные заметки по-русски помещаются в ответ целиком
            p.Agent = true; p.AgentRead = true;
            p.Notes = string.Concat(Enumerable.Repeat("Длинная заметка для проверки ответа. ", 400));
            string longNotes = TextOf(await Ask(Call(14, "read_notes", "{}")));
            if (longNotes.Length < 14000 || !longNotes.StartsWith("Длинная заметка")) throw new InvalidOperationException("Long Russian notes were not returned.");
        }
        finally
        {
            try { process.StandardInput.Close(); if (!process.WaitForExit(3000)) process.Kill(); } catch (InvalidOperationException) { }
            p.Todos.RemoveRange(saved.Todos, p.Todos.Count - saved.Todos);
            (p.Agent, p.AgentRead, p.Notes) = (saved.Agent, saved.AgentRead, saved.Notes);
            model.Save(false);
        }
    }

    // Живой поиск города в окне погоды (только при LUMADOCK_LIVE_SEARCH=1, нужен интернет): русское название вводится в поле, нажимается «Search», в списке появляется Россия
    private static async System.Threading.Tasks.Task VerifyLiveCitySearch(Window surface, string folder)
    {
        Click(surface, "Change weather city");
        surface.UpdateLayout();
        var field = FindControl(surface, L10n.T("Search for a city")) as TextBox ?? throw new InvalidOperationException("The city search field is missing.");
        field.Text = "Новосибирск";
        Click(surface, "Search");
        string Texts(DependencyObject node)
        {
            var text = node is TextBlock block ? block.Text + "\n" : "";
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++) text += Texts(VisualTreeHelper.GetChild(node, index));
            return text;
        }
        await WaitFor(() => Texts(surface).Contains("Россия") || Texts(surface).Contains("City search unavailable"), 12000);
        surface.UpdateLayout();
        Capture(surface, Path.Combine(folder, "weather-search-russian.png"));
        string all = Texts(surface);
        if (!all.Contains("Новосибирск") || !all.Contains("Россия")) throw new InvalidOperationException("The Russian city name was not found in the live search.");
        field.Text = "";
    }

    // Меню программы по правому щелчку: состав и порядок пунктов как у Lumabar (Widgets…, Settings, разделы списка переходов, программа, Pin, красные End task и Close window),
    // меню закрывается щелчком вне его и остаётся при щелчке внутри (мышь в проверке подменена, настоящие окна пользователя не нажимаются)
    private static async System.Threading.Tasks.Task VerifyAppMenu(DockWindow dock, string folder)
    {
        string app = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var running = new System.Collections.Generic.List<WindowsShell.WindowInfo> { new(0, "Test", app, "notepad") };
        var pixels = new byte[16 * 16 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 60; pixels[i + 1] = 120; pixels[i + 2] = 230; pixels[i + 3] = 255; }
        var icon = System.Windows.Media.Imaging.BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 64);
        icon.Freeze();
        var groups = new System.Collections.Generic.List<JumpList.Group>
        {
            new("Новая сессия Code", [new("FlowBar", []), new("Проект Альфа", []), new("Проект_Бета_2026-11-17_очень_длинное_название_сессии_для_обрезки", [])]),
            new("Tasks", [new("Новый чат", []), new("Новая сессия Code", [])])
        };
        var menu = Visuals.DockMenu();
        dock.FillAppMenu(menu, "app:" + app, app, running, icon, groups);
        var items = menu.Items.Cast<object>().ToList();
        if (items.Count != 15 || items[2] is not Separator || items[3] is not Separator || items[7] is not Separator || items[10] is not Separator)
            throw new InvalidOperationException("The app menu layout is wrong: " + items.Count + " items.");
        var red = (items[^1] as MenuItem)?.Foreground as SolidColorBrush;
        if (items[0] is not MenuItem first || (string)first.Header != L10n.T("Widgets…") || first.Icon is null
            || red is not { Color.R: 0xFF } || (items[^2] as MenuItem)?.Foreground is not SolidColorBrush { Color.R: 0xFF }
            || (string)((MenuItem)items[^1]).Header != L10n.T("Close window") || (string)((MenuItem)items[^2]).Header != L10n.T("End task")
            || (string)((MenuItem)items[^3]).Header != L10n.T("Pin to dock") || ((MenuItem)items[11]).Icon is null || ((MenuItem)items[4]).Icon is null)
            throw new InvalidOperationException("The app menu items are wrong.");

        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute;
        menu.HorizontalOffset = 140;
        menu.VerticalOffset = 60;
        menu.IsOpen = true;
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        await System.Threading.Tasks.Task.Delay(200);
        if (menu.ActualWidth < 240 || menu.ActualWidth > 420) throw new InvalidOperationException("The app menu has an unexpected width: " + menu.ActualWidth);
        CaptureElement(menu, Path.Combine(folder, "app-menu" + (L10n.Russian ? "-ru" : "") + ".png"));

        bool down = false;
        var cursor = new Point();
        var (savedDown, savedCursor) = (MenuWatch.ButtonDown, MenuWatch.Cursor);
        MenuWatch.ButtonDown = () => down;
        MenuWatch.Cursor = () => cursor;
        try
        {
            var bounds = MenuWatch.Bounds(menu);
            if (!MenuWatch.ShouldClose(true, false, new Point(bounds.Right + 50, bounds.Top), bounds) || MenuWatch.ShouldClose(true, true, new Point(bounds.Right + 50, bounds.Top), bounds)
                || MenuWatch.ShouldClose(true, false, new Point(bounds.Left + 5, bounds.Top + 5), bounds) || MenuWatch.ShouldClose(false, false, new Point(bounds.Right + 50, bounds.Top), bounds))
                throw new InvalidOperationException("The menu close rule is wrong.");
            cursor = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            down = true; // щелчок внутри меню: оно остаётся
            await System.Threading.Tasks.Task.Delay(250);
            if (!menu.IsOpen) throw new InvalidOperationException("The menu closed after a click inside it.");
            down = false;
            await System.Threading.Tasks.Task.Delay(120);
            cursor = new Point(bounds.Right + 400, bounds.Bottom + 400);
            down = true; // щелчок в другой программе: меню закрывается
            await WaitFor(() => !menu.IsOpen, 8000); // таймер слежения за меню под нагрузкой (сразу после сборки) иногда опаздывает больше 2 секунд
            down = false;
            await VerifyPopoverWatch(v => down = v, p => cursor = p);
        }
        finally
        {
            (MenuWatch.ButtonDown, MenuWatch.Cursor) = (savedDown, savedCursor);
            menu.IsOpen = false;
        }
    }

    // Карточка виджета закрывается новым нажатием вне неё, остаётся при нажатии внутри и при кнопке, зажатой ещё до начала слежения
    private static async System.Threading.Tasks.Task VerifyPopoverWatch(Action<bool> press, Action<Point> moveTo)
    {
        var window = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            Width = 200, Height = 100, Left = 300, Top = 300 };
        window.Show();
        try
        {
            int closed = 0;
            var inside = window.PointToScreen(new Point(100, 50));
            moveTo(new Point(-6000, -6000));
            press(true); // кнопка зажата ещё до слежения: это не новое нажатие
            PopoverWatch.Attach(window, () => closed++);
            await System.Threading.Tasks.Task.Delay(250);
            if (closed != 0) throw new InvalidOperationException("The card closed on a button held before watching began.");
            press(false);
            await System.Threading.Tasks.Task.Delay(120);
            moveTo(inside);
            press(true);
            await System.Threading.Tasks.Task.Delay(250);
            if (closed != 0) throw new InvalidOperationException("The card closed after a click inside it.");
            press(false);
            await System.Threading.Tasks.Task.Delay(120);
            moveTo(new Point(-6000, -6000));
            press(true);
            await WaitFor(() => closed > 0, 8000);
        }
        finally { press(false); window.Close(); }
    }

    // Список переходов: имена файлов по AppUserModelID совпадают с настоящими файлами Windows, разделы читаются из файла, пункт запускается
    private static void VerifyJumpListNames()
    {
        if (JumpList.Hash("OpenAI.Codex_2p2nqsd0c76g0!App") != "3c6e5c18e9f546e6" || JumpList.Hash("Microsoft.ScreenSketch_8wekyb3d8bbwe!App") != "1c7a9be1b15a03ba"
            || !JumpList.Hash("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App").StartsWith("ff99ba") || !JumpList.Hash("5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App").StartsWith("c83076"))
            throw new InvalidOperationException("The jump list file name hash is wrong.");
    }

    private static async System.Threading.Tasks.Task VerifyJumpList()
    {
        VerifyJumpListNames();
        string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var clsid = new byte[] { 0x01, 0x14, 0x02, 0, 0, 0, 0, 0, 0xC0, 0, 0, 0, 0, 0, 0, 0x46 };
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        writer.Write(2); writer.Write(3); writer.Write(0);
        writer.Write(0); // свой раздел «Недавние» с двумя пунктами
        string title = "Недавние";
        writer.Write((ushort)title.Length); writer.Write(System.Text.Encoding.Unicode.GetBytes(title)); writer.Write(2);
        foreach (string name in new[] { "Первый", "Второй" }) { writer.Write(clsid); writer.Write(JumpList.MakeTestLink(name, cmd, "/c exit")); }
        writer.Write(new byte[] { 0xAB, 0xFB, 0xBF, 0xBA });
        writer.Write(1); writer.Write(uint.MaxValue); writer.Write(new byte[] { 0xAB, 0xFB, 0xBF, 0xBA }); // пустой раздел частых
        writer.Write(2); writer.Write(1); writer.Write(clsid); writer.Write(JumpList.MakeTestLink("Новый чат", cmd, "/c exit")); // задачи
        writer.Write(new byte[] { 0xAB, 0xFB, 0xBF, 0xBA });
        var groups = JumpList.Parse(stream.ToArray());
        if (groups.Count != 2 || groups[0].Title != "Недавние" || groups[0].Entries.Select(entry => entry.Title).SequenceEqual(["Первый", "Второй"]) is false
            || groups[1].Title != "Tasks" || groups[1].Entries.Count != 1 || groups[1].Entries[0].Title != "Новый чат")
            throw new InvalidOperationException("The jump list file was parsed incorrectly.");
        if (JumpList.Parse([1, 2, 3]).Count != 0 || JumpList.Parse(new byte[64]).Count != 0) throw new InvalidOperationException("A broken jump list file was not rejected.");

        string marker = Path.Combine(Path.GetTempPath(), "LumaDock-jump-marker-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var launch = new JumpList.Entry("Запуск", JumpList.MakeTestLink("Запуск", cmd, "/c echo ok>\"" + marker + "\""));
            if (!JumpList.Launch(launch)) throw new InvalidOperationException("The jump list entry did not start.");
            await WaitFor(() => File.Exists(marker), 10000);
        }
        finally { try { File.Delete(marker); } catch (IOException) { } }
    }

    // Горячие клавиши: разбор и подписи сочетаний, запись из нажатых клавиш, занятость, а сочетание диктовки открывает окно и сразу начинает запись
    // (сами сочетания Windows в проверке не регистрируются, чтобы не мешать настоящему запущенному LumaDock)
    private static async System.Threading.Tasks.Task VerifyHotkeys(DockWindow dock, DockModel model)
    {
        var p = model.Settings;
        var saved = p.Hotkeys;
        try
        {
            // язык поиска городов: русские буквы или русский интерфейс дают ru (иначе «Москва» ничего не находит)
            if (LiveData.SearchLanguage("Москва", false) != "ru" || LiveData.SearchLanguage("Ёлки", false) != "ru" || LiveData.SearchLanguage("Moscow", false) != "en"
                || LiveData.SearchLanguage("Moscow", true) != "ru")
                throw new InvalidOperationException("The city search language was chosen incorrectly.");
            if (!Hotkeys.TryParse("1,84", out uint modifiers, out uint key) || modifiers != 1 || key != 84)
                throw new InvalidOperationException("A valid shortcut was not parsed.");
            foreach (string bad in new[] { "", "x", "0,84", "16,84", "1,300", "1,5", "1,84,2" })
                if (Hotkeys.TryParse(bad, out _, out _)) throw new InvalidOperationException("An invalid shortcut was accepted: " + bad);
            if (Hotkeys.Label("1,84") != "Alt+T" || Hotkeys.Label("3,32") != "Ctrl+Alt+Space" || Hotkeys.Label("") != "Not set")
                throw new InvalidOperationException("A shortcut label is wrong.");
            if (Hotkeys.FromKeys(ModifierKeys.Control | ModifierKeys.Alt, Key.D) != "3,68" || Hotkeys.FromKeys(ModifierKeys.None, Key.D) is not null
                || Hotkeys.FromKeys(ModifierKeys.Control, Key.LeftCtrl) is not null)
                throw new InvalidOperationException("Pressed keys were converted incorrectly.");
            p.Hotkeys = ["1,84", "3,68", ""];
            if (!Hotkeys.Taken(p, 2, "1,84") || Hotkeys.Taken(p, 0, "1,84") || Hotkeys.Get(p, 2) != "")
                throw new InvalidOperationException("A taken shortcut was not detected.");
            Hotkeys.Set(p, 2, "9,77");
            if (p.Hotkeys.Length != 3 || p.Hotkeys[2] != "9,77" || p.Hotkeys[0] != "1,84") throw new InvalidOperationException("A shortcut was not stored.");

            dock.RunHotkey(1);
            await WaitFor(() => Dictation.IsRecording);
            var surface = dock.OpenWidget("Dictation") ?? throw new InvalidOperationException("The dictation window did not open from the shortcut.");
            surface.Close();
            await WaitFor(() => !Dictation.IsRecording);
        }
        finally { p.Hotkeys = saved; model.Save(false); }
    }

    // Встроенный движок распознавания без настоящих скачиваний (сотни мегабайт): нарезка звука, проверка скачивания по сумме и размеру
    // на локальном сервере, распаковка tar.gz, запуск процесса --voice-local без библиотеки (должен отказать с понятной ошибкой)
    // Проверка связи с помощниками и обновление модели распознавания: разбор ответа Hugging Face, закреплённая версия, проверка файла по сумме и раздел в настройках
    private static async System.Threading.Tasks.Task VerifyVoiceTools(DockWindow dock, string folder)
    {
        string hello = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("hello"u8.ToArray())).ToLowerInvariant();
        string revision = new('a', 40);
        string json = "{\"sha\":\"" + revision + "\",\"siblings\":[{\"rfilename\":\"readme.md\",\"size\":10},{\"rfilename\":\"m-Q8_0.gguf\",\"size\":5,\"lfs\":{\"sha256\":\"" + hello.ToUpperInvariant() + "\"}}]}";
        var remote = LocalVoice.ParseRemote(json, "m-Q8_0.gguf");
        if (remote.Revision != revision || remote.Size != 5 || remote.Digest != hello) throw new InvalidOperationException("The repository answer was parsed wrongly.");
        foreach (string broken in new[] { json.Replace(hello.ToUpperInvariant(), "abc"), json.Replace("m-Q8_0.gguf", "other.gguf"), json.Replace(revision, "abc") })
        {
            try { LocalVoice.ParseRemote(broken, "m-Q8_0.gguf"); throw new InvalidOperationException("A broken repository answer was accepted."); }
            catch (InvalidDataException) { }
        }

        string root = Path.Combine(Path.GetTempPath(), "FlowBarVoiceTools-" + Guid.NewGuid().ToString("N"));
        string? savedRoot = LocalVoice.RootOverride;
        var savedPins = LocalVoice.Pins;
        LocalVoice.RootOverride = root;
        try
        {
            var original = LocalVoice.Selected("whisper-small");
            LocalVoice.Pins = new() { ["whisper-small"] = new LocalVoice.ModelPin { Revision = revision, Digest = hello, Size = 5 } };
            var pinned = LocalVoice.Selected("whisper-small");
            if (pinned.Digest != hello || pinned.Size != 5 || pinned.Id != original.Id || LocalVoice.Selected("gigaam-v3-e2e-rnnt").Digest == hello)
                throw new InvalidOperationException("A pinned model version was not applied.");
            Directory.CreateDirectory(LocalVoice.ModelsFolder);
            var none = System.Threading.CancellationToken.None;
            if (await LocalVoice.VerifyAsync(pinned, none) != LocalVoice.ModelState.Missing) throw new InvalidOperationException("A missing model was not reported.");
            await File.WriteAllTextAsync(LocalVoice.ModelPath(pinned), "hello");
            if (await LocalVoice.VerifyAsync(pinned, none) != LocalVoice.ModelState.Ok) throw new InvalidOperationException("An intact model was not recognised.");
            await File.WriteAllTextAsync(LocalVoice.ModelPath(pinned), "hellp");
            if (await LocalVoice.VerifyAsync(pinned, none) != LocalVoice.ModelState.Corrupt) throw new InvalidOperationException("A damaged model was not recognised.");
            await File.WriteAllTextAsync(LocalVoice.ModelPath(pinned), "hi");
            if (await LocalVoice.VerifyAsync(pinned, none) != LocalVoice.ModelState.WrongSize) throw new InvalidOperationException("A model of another size was not recognised.");
        }
        finally
        {
            LocalVoice.RootOverride = savedRoot;
            LocalVoice.Pins = savedPins;
            try { Directory.Delete(root, true); } catch (IOException) { }
        }

        // связь с помощником: в проверке ответ подменён, раздел «Диктовка» в настройках и кнопки проверки
        VoiceAssistant.Fixture = true;
        try
        {
            var connection = await VoiceAssistant.CheckAsync("codex", System.Threading.CancellationToken.None);
            if (!connection.Ok) throw new InvalidOperationException("The assistant connection check failed in fixture mode.");
            var settings = dock.OpenSettings("General");
            settings.UpdateLayout();
            foreach (string name in new[] { "Codex", "Claude Code", "Gemini (Antigravity CLI)" })
                if (FindControl(settings, L10n.F($"Check connection: {name}")) is not Button) throw new InvalidOperationException("The connection check button is missing for " + name + ".");
            var button = (Button)FindControl(settings, L10n.F($"Check connection: Codex"))!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            await WaitFor(() => FaceText(settings).Contains(L10n.F($"Codex: connected, answered in {0.0:0.0} s.")[..22]));
            string text = FaceText(settings);
            if (!text.Contains("Check the model") || !text.Contains("Handy keeps its own models")) throw new InvalidOperationException("The model section is missing in the settings.");
            FindVisual<ScrollViewer>(settings)?.ScrollToEnd();
            settings.UpdateLayout();
            Capture(settings, Path.Combine(folder, "settings-dictation.png"));
            settings.Close();
        }
        finally { VoiceAssistant.Fixture = false; }
    }

    private static async System.Threading.Tasks.Task VerifyLocalVoice()
    {
        string root = Path.Combine(Path.GetTempPath(), "LumaDockLocalVoice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        LocalVoice.RootOverride = Path.Combine(root, "speech");
        try
        {
            foreach (var item in LocalVoice.Models)
                if (item.Digest.Length != 64 || !item.Digest.All(Uri.IsHexDigit) || item.Size <= 0 || LocalVoice.Ready(item))
                    throw new InvalidOperationException("A local voice model description is invalid.");
            // звук: 30 секунд режутся на отрывки 24 + 6 секунд, длинный отрывок движок не принимает
            string wav = Dictation.FixtureWave(30);
            var parts = Dictation.SliceWave(wav, root, CancellationToken.None, 24);
            if (parts.Count != 2 || LocalVoice.ReadSlice(parts[0]).Length != 24 * 16000 || LocalVoice.ReadSlice(parts[1]).Length != 6 * 16000)
                throw new InvalidOperationException("The recording was sliced incorrectly.");
            var longParts = Dictation.SliceWave(wav, Directory.CreateDirectory(Path.Combine(root, "long")).FullName, CancellationToken.None, 160);
            try { LocalVoice.ReadSlice(longParts[0]); throw new InvalidOperationException("A long slice was accepted."); }
            catch (InvalidDataException) { }
            File.Delete(wav);

            // скачивание: верная сумма проходит, неверная сумма и превышение размера отвергаются, файл при этом удаляется
            var body = Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray();
            string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)).ToLowerInvariant();
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    var stream = client.GetStream();
                    var request = new byte[4096];
                    _ = await stream.ReadAsync(request); // сам запрос серверу не нужен
                    await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n"));
                    await stream.WriteAsync(body);
                }
            });
            string url = "http://127.0.0.1:" + ((System.Net.IPEndPoint)listener.LocalEndpoint).Port + "/file";
            string target = Path.Combine(root, "asset.bin");
            var texts = new System.Collections.Generic.List<string>();
            await LocalVoice.FetchAsync(url, target, digest, 10000, texts.Add, "test", CancellationToken.None);
            if (!File.Exists(target) || new FileInfo(target).Length != body.Length || texts.Count == 0) throw new InvalidOperationException("The test download failed.");
            File.Delete(target);
            foreach (var (badDigest, limit) in new[] { (new string('0', 64), 10000L), (digest, 100L) })
            {
                try { await LocalVoice.FetchAsync(url, target, badDigest, limit, _ => { }, "test", CancellationToken.None); }
                catch (InvalidDataException) { if (File.Exists(target)) throw new InvalidOperationException("A rejected download was kept."); continue; }
                throw new InvalidOperationException("A bad download was accepted.");
            }
            listener.Stop();

            // распаковка архива системной tar.exe
            string source = Directory.CreateDirectory(Path.Combine(root, "pack")).FullName;
            File.WriteAllText(Path.Combine(source, "transcribe.dll"), "x");
            string archive = Path.Combine(root, "engine.tar.gz"), unpacked = Directory.CreateDirectory(Path.Combine(root, "unpacked")).FullName;
            await LocalVoice.RunAsync(Path.Combine(Environment.SystemDirectory, "tar.exe"), ["-czf", archive, "-C", source, "."], TimeSpan.FromMinutes(1), CancellationToken.None);
            await LocalVoice.UnpackAsync(archive, unpacked, CancellationToken.None);
            if (!File.Exists(Path.Combine(unpacked, "transcribe.dll"))) throw new InvalidOperationException("The archive was not unpacked.");

            // процесс распознавания без библиотеки отказывает с понятной ошибкой
            try { await LocalVoice.RunAsync(Environment.ProcessPath!, ["--voice-local", Path.Combine(root, "nothing"), "model.gguf", root], TimeSpan.FromSeconds(30), CancellationToken.None); throw new InvalidOperationException("The recognition process worked without its library."); }
            catch (InvalidOperationException ex) when (ex.Message != "The recognition process worked without its library.") { }
        }
        finally
        {
            LocalVoice.RootOverride = null;
            try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // Код оформления: экспорт и импорт возвращают то же оформление, неверные коды отвергаются и ничего не меняют
    private static void VerifyLookCode(DockModel model)
    {
        var p = model.Settings;
        var saved = (p.Style, p.Material, p.Transparency, p.CustomAccent, p.UseCustomAccent, p.Accent);
        try
        {
            (p.Style, p.Material, p.Transparency, p.CustomAccent, p.UseCustomAccent) = ("Midnight", "prism", 37, "#12AB34", true);
            string code = Looks.Export(p);
            (p.Style, p.Material, p.Transparency, p.CustomAccent, p.UseCustomAccent) = ("Black", "theme", 0, null, false);
            if (!Looks.TryImport(p, code) || (p.Style, p.Material, p.Transparency, p.CustomAccent, p.UseCustomAccent) != ("Midnight", "prism", 37, "#12AB34", true) || p.Accent != "#12AB34")
                throw new InvalidOperationException("The look code did not round-trip.");
            string Make(string json) => "LD1:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            var state = (p.Style, p.Material, p.Transparency, p.CustomAccent, p.UseCustomAccent);
            foreach (var bad in new[] { null, "", "LD1:", "LD2:" + code[4..], "LD1:не base64", Make("{}"),
                Make("{\"style\":\"Neon\",\"material\":\"theme\",\"transparency\":10,\"accent\":\"#112233\"}"),
                Make("{\"style\":\"Black\",\"material\":\"theme\",\"transparency\":150,\"accent\":\"#112233\"}"),
                Make("{\"style\":\"Black\",\"material\":\"theme\",\"transparency\":10,\"accent\":\"red\"}"),
                Make("{\"style\":\"Black\",\"material\":\"theme\",\"transparency\":10,\"accent\":\"#112233\\n\"}") })
                if (Looks.TryImport(p, bad) || (p.Style, p.Material, p.Transparency, p.CustomAccent, p.UseCustomAccent) != state)
                    throw new InvalidOperationException("An invalid look code was accepted.");
        }
        finally { (p.Style, p.Material, p.Transparency, p.CustomAccent, p.UseCustomAccent, p.Accent) = saved; model.Save(false); }
    }

    // Завершение задачи и прыжок значка: чужой путь и системные процессы не завершаются, свой тестовый процесс завершается; значок Explorer прыгает
    private static async System.Threading.Tasks.Task VerifyEndTask(DockWindow dock)
    {
        string exe = Environment.ProcessPath!, cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var info = new System.Diagnostics.ProcessStartInfo(cmd, "/c ping -n 60 127.0.0.1 > nul") { UseShellExecute = false, CreateNoWindow = true };
        using var victim = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("The test process did not start.");
        try
        {
            uint pid = (uint)victim.Id;
            if (WindowsShell.EndProcess(pid, @"C:\Windows\notepad.exe") || victim.HasExited)
                throw new InvalidOperationException("A process with another path was ended.");
            if (!WindowsShell.IsProtected(@"C:\Windows\System32\ApplicationFrameHost.exe")) throw new InvalidOperationException("The UWP frame host must be protected.");
            if (WindowsShell.EndProcess(pid, @"C:\Windows\explorer.exe") || WindowsShell.EndProcess((uint)Environment.ProcessId, exe) || !WindowsShell.IsProtected("explorer.exe") || !WindowsShell.IsProtected(exe))
                throw new InvalidOperationException("A protected process was not protected.");
            if (!WindowsShell.EndProcess(pid, cmd)) throw new InvalidOperationException("The test process was not ended.");
            if (!await System.Threading.Tasks.Task.Run(() => victim.WaitForExit(5000))) throw new InvalidOperationException("The ended process is still running.");
        }
        finally { try { if (!victim.HasExited) victim.Kill(true); } catch (InvalidOperationException) { } }
        if (dock.FlashApp("") || dock.FlashApp("no-such-app.exe")) throw new InvalidOperationException("An unknown app was animated.");
        if (!dock.FlashApp("explorer.exe")) throw new InvalidOperationException("The dock icon did not bounce.");
        await System.Threading.Tasks.Task.Delay(1);
    }

    // Проверка ответа помощника: допустимый ответ разбирается, неверный срок отбрасывается, пустое название и слишком длинная заметка отвергаются
    private static void VerifyAssistantJson()
    {
        var good = VoiceAssistant.FromJson("{\"tasks\":[{\"text\":\"Позвонить\",\"due\":\"2026-11-18\",\"time\":\"09:30\"},{\"text\":\"Купить\",\"due\":\"бред\",\"time\":null}],\"note\":\"Идея\"}", false);
        if (good.Tasks.Count != 2 || good.Tasks[0].Due != new DateTime(2026, 11, 18, 9, 30, 0) || good.Tasks[1].Due is not null || good.Note != "Идея")
            throw new InvalidOperationException("The assistant answer was parsed incorrectly.");
        foreach (string bad in new[] { "{\"tasks\":[{\"text\":\"\"}],\"note\":\"\"}", "{\"note\":\"\"}", "{\"tasks\":[],\"note\":\"" + new string('я', 4001) + "\"}" })
        {
            try { VoiceAssistant.FromJson(bad, false); }
            catch (System.IO.InvalidDataException) { continue; }
            throw new InvalidOperationException("An invalid assistant answer was accepted.");
        }
    }

    private static async System.Threading.Tasks.Task WaitFor(Func<bool> condition, int milliseconds = 4000)
    {
        for (int attempt = 0; attempt < milliseconds / 50 && !condition(); attempt++) await System.Threading.Tasks.Task.Delay(50);
        if (!condition()) throw new InvalidOperationException("A background step did not finish in time.");
    }

    // Весь текст лица виджета (логическое дерево: лицо ещё не показано и не имеет визуального дерева)
    private static string FaceText(DependencyObject node) =>
        (node is TextBlock block ? block.Text + " " : "") +
        string.Concat(LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>().Select(FaceText));

    private static T? FindVisual<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T match) return match;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
        {
            var found = FindVisual<T>(VisualTreeHelper.GetChild(node, index));
            if (found is not null) return found;
        }
        return null;
    }

    // Миксер стилей: выбор стиля и материала доходит до профиля, прозрачность сохраняется, плитка и кисти дока строятся для каждого сочетания
    private static async System.Threading.Tasks.Task VerifyStyleMixer(DockWindow dock, DockModel model, string folder)
    {
        string suffix = L10n.Russian ? "-ru" : "";
        var p = model.Settings;
        var original = (p.Style, p.Material, p.Transparency);
        var settings = dock.OpenSettings("Appearance");
        settings.UpdateLayout();
        var mixer = FindVisual<StyleMixer>(settings) ?? throw new InvalidOperationException("The style mixer is missing.");
        string Word(string group, string name, bool selected = false) => L10n.T(group) + ": " + name + (selected ? ", " + L10n.T("selected") : "");
        if (FindControl(settings, Word("Style", L10n.T(original.Style), true)) is not Button || FindControl(settings, Word("Material", L10n.T(Looks.MaterialName(Looks.Material(p))), true)) is not Button)
            throw new InvalidOperationException("The mixer did not mark the current style and material.");
        Click(settings, Word("Style", L10n.T("Midnight")));
        if (p.Style != "Midnight") throw new InvalidOperationException("The mixer did not select the style.");
        settings.UpdateLayout();
        Click(settings, Word("Material", L10n.T("Prism")));
        if (p.Material != "prism") throw new InvalidOperationException("The mixer did not select the material.");
        settings.UpdateLayout();
        var slider = FindControl(settings, L10n.T("Transparency")) as Slider ?? throw new InvalidOperationException("The transparency slider is missing.");
        slider.Value = 60;
        await WaitFor(() => new DockModel(model.Profile).Settings.Transparency == 60);
        var saved = new DockModel(model.Profile).Settings;
        if (saved.Style != "Midnight" || saved.Material != "prism") throw new InvalidOperationException("The look was not saved.");
        if (!Looks.Glassy(p) || Looks.Detail(p) != 1 || Looks.Dispersion(p) != 3) throw new InvalidOperationException("Prism did not switch the glass to dispersion.");
        mixer = FindVisual<StyleMixer>(settings) ?? throw new InvalidOperationException("The mixer disappeared after the choice.");
        mixer.UpdateLayout();
        CaptureDetached(mixer, Path.Combine(folder, $"style-mixer{suffix}.png"));
        // материалы строят разные кисти, у металла нет прозрачности, у матового стекла вчетверо крупнее размытие
        p.Material = "metal"; p.Transparency = 80;
        if (Looks.Glassy(p) || Looks.Surface(p, false) is not DrawingBrush) throw new InvalidOperationException("Metal must stay opaque.");
        p.Material = "frost"; p.Style = "Black";
        if (!Looks.Glassy(p) || Looks.Detail(p) != 4) throw new InvalidOperationException("Frosted glass must blur four times wider.");
        foreach (string style in Looks.Styles)
            foreach (string material in Looks.Materials)
                if (Looks.Tile(style, material, 40, Colors.DodgerBlue, 108, false) is not Grid { Children.Count: > 3 })
                    throw new InvalidOperationException("The mixer tile was not built for " + style + " / " + material + ".");
        (p.Style, p.Material, p.Transparency) = original;
        model.Save();
        settings.Close();
    }

    private static void VerifyMixerControls(Window surface, string folder)
    {
        string suffix = L10n.Russian ? "-ru" : "";
        var slider = FindControl(surface, L10n.F($"Volume · {"Example player"}")) as Slider
            ?? throw new InvalidOperationException("Application volume slider is missing.");
        slider.UpdateLayout();
        if (slider.Template.FindName("PART_Track", slider) is not Track || slider.Value != 64d)
            throw new InvalidOperationException("The styled slider did not receive its track or value.");
        slider.Value = 37d;
        Capture(surface, Path.Combine(folder, $"themed-mixer{suffix}.png"));
        Click(surface, L10n.T("Mute"));
        if (FindControl(surface, L10n.T("Unmute")) is not Button)
            throw new InvalidOperationException("Mute status did not update.");
    }

    // Снимок элемента, который лежит внутри прокручиваемой страницы: рисуется кистью, без смещения и обрезки родителя
    private static void CaptureDetached(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        int width = (int)Math.Ceiling(element.ActualWidth), height = (int)Math.Ceiling(element.ActualHeight);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            context.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    internal static void Capture(Window window, string path) => CaptureElement((FrameworkElement)window.Content, path);

    private static void CaptureElement(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),
            (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
