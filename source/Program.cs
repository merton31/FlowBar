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
                        foreach (string tab in new[] { "Dock", "Sounds", "Automations" })
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
                        model.ToggleWidget("Theme Toggle");
                        if (model.Settings.Items.Contains("Theme Toggle")) throw new InvalidOperationException("Theme Toggle stayed after unpinning.");
                        model.Settings.PinSystem = true;
                        model.EnsurePinned();
                        if (!Widgets.SystemPinned.All(model.Settings.Items.Contains)) throw new InvalidOperationException("Pinned widgets were not restored.");
                        // значок трея закреплён вместе с системными и есть в списке нового профиля
                        if (!Widgets.SystemPinned.Contains("Tray") || !new DockModel(Path.Combine(args[1], "fresh-profile.json")).Settings.Items.Contains("Tray"))
                            throw new InvalidOperationException("The tray icon is not pinned with the system widgets.");
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
                        await VerifyAgent(dock, model);
                        await VerifyEndTask(dock);
                        await VerifyLocalVoice();
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
                            "Stock Chart", "Quick Toggles", "Bluetooth", "Tray" })
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
                            if (id == "Tray" && (FindControl(surface, "Sample tray icon") is not Button
                                || !WindowsShell.IsOverflowButton("SystemTray.ChevronIconView", "", "")
                                || WindowsShell.IsOverflowButton("SystemTray.NotifyIconView", "SystemTrayIcon", "Example sync")))
                                throw new InvalidOperationException("Tray icon layout or chevron classification failed.");
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
                        var reloaded = new DockModel(model.Profile);
                        if (!reloaded.Settings.Todos.Last().Done || reloaded.Settings.Items.IndexOf("Notes") != 0)
                            throw new InvalidOperationException("Profile round trip failed");
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
            await WaitFor(() => !menu.IsOpen, 2000);
        }
        finally
        {
            (MenuWatch.ButtonDown, MenuWatch.Cursor) = (savedDown, savedCursor);
            menu.IsOpen = false;
        }
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
