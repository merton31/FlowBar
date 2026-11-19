using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using static LumaDock.Visuals;

namespace LumaDock;

internal sealed record Widget(string Id, string Category, string Glyph, double Width, string? Label = null)
{
    public string Name => Label ?? Id;
    public string GalleryCaption => Id == "Now Playing Artwork" && !L10n.Russian ? "Now Playing" : L10n.T(Name);
}

internal static partial class Widgets
{
    public static readonly (string Name, string Glyph, string Catalog)[] Categories =
    [
        ("Clocks", "\uE823", "Clock|Flip Clock|World Clocks|7 Days|World Time|Today|Date|Day Number|Analog Minimal|Segment Clock|Clock & Weather|Day Progress|Month Progress|Year Progress"),
        ("Productivity", "\uE73A", "Day Timeline|Screen Time|Pomodoro|Timer|Stopwatch|Todos|Notes|Calendar|Gmail|WhatsApp"),
        ("System", "\uE713", "Battery|Battery Ring|Battery Core|Battery Dots|Battery Gauge|Tray|Language|Quick Toggles|Brightness|Theme Toggle|Show Desktop|Notifications|Keyboard|Do Not Disturb|CPU|RAM|Temperature|Network|Wallpaper Gallery"),
        ("Apps & Files", "\uE8B7", "Apps|Recently Opened|Recycle Bin|Search|Storage|Screenshot Capture"),
        ("Media & Audio", "\uE7F5", "Now Playing|Audio Output|Microphone|Now Playing Artwork|Now Playing Mini|Now Playing Bar"),
        ("Developer", "\uE756", "GitHub Activity|GitHub|GitHub Overview|LeetCode Stats|Commands|Supabase|Linear|Resend"),
        ("AI", "\uE735", "AI Limits|Claude Usage|Claude Plan Usage|ChatGPT Usage|Gemini Usage|Grok Usage|DeepSeek Balance|GLM Usage|OpenCode Usage|OpenCode Plan Usage|Cursor Usage"),
        ("Analytics", "\uE9D2", "Search Console|DataFast Chart|Plausible|Dub"),
        ("Social", "\uE716", "X Profile|TikTok|YouTube|Instagram"),
        ("Stocks", "\uE9F9", "Crypto|Stock Chart|Stock Watchlist"),
        ("Finance", "\uE8C7", "Currency|Total Revenue|Polar|Polar Chart|Stripe|Stripe Chart|Dodo Payments|Dodo Chart|Shopify|Shopify Chart|Lemon Squeezy|Lemon Squeezy Chart|Paddle|Paddle Chart|Gumroad|Gumroad Chart"),
        ("Weather", "\uE706", "Weather|Weather Details|Today & Forecast|Weather Status|Hourly|Sunrise & Sunset"),
        // функции FlowBar: отдельная категория, в сверке с каталогом эталона Lumabar (106 позиций) не участвует
        (FlowBarCategory, "\uE720", "Dictation")
    ];

    public const string FlowBarCategory = "FlowBar";

    // Виджеты, которые не снимаются с дока, пока включено закрепление системных виджетов
    // Размеры, которые можно выбрать в меню виджета: название и ширина плитки. Пусто — размер виджета менять нельзя
    public static (string Label, double Width)[] SizeChoices(string id) => id == "Audio Output"
        ? [("Narrow", 170), ("Standard", 242), ("Wide", 330), ("Extra wide", 440)] : [];

    // Ширина плитки в доке с учётом выбранного размера
    public static double WidthOf(Preferences p, Widget widget)
    {
        // быстрые настройки без кнопки Wi-Fi (на компьютере нет адаптера) уже на ширину этой кнопки
        if (widget.Id == "Quick Toggles" && !WifiNetworks.HasAdapterCached) return widget.Width - WifiTileWidth;
        return SizeChoices(widget.Id).Length > 0 && p.WidgetWidths.TryGetValue(widget.Id, out double chosen) ? Math.Clamp(chosen, 120, 600) : widget.Width;
    }

    // ширина кнопки Wi-Fi в быстрых настройках вместе с полями: 47 + 2 * 2
    internal const double WifiTileWidth = 51;

    public static readonly string[] SystemPinned = ["Audio Output", "Quick Toggles", "Tray"];

    // Позиции каталога эталона Lumabar без функций FlowBar
    public static IEnumerable<(string Name, string Glyph, string Catalog)> ReferenceCategories => Categories.Where(category => category.Name != FlowBarCategory);

    public static readonly string[] WindowsFolders = ["Administrative Tools", "Desktop", "Downloads", "Documents", "Pictures", "Music"];

    public static readonly IReadOnlyDictionary<string, Widget> Available = Categories
        .SelectMany(category => category.Catalog.Split('|').Select(id => new Widget(id, category.Name,
            id switch
            {
                "Todos" => "\uE73A", "Theme Toggle" => "\uE708", "Quick Toggles" => "\uE701", "Audio Output" => "\uE767",
                "Pomodoro" or "Timer" or "Stopwatch" => "\uE916", "Now Playing" or "Now Playing Artwork" or "Now Playing Mini" or "Now Playing Bar" => "\uE768",
                "Notes" => "\uE70B", "Dictation" => "\uE720", "Calendar" => "\uE787", "CPU" => "\uE950", "RAM" => "\uE964",
                "Battery" or "Battery Ring" or "Battery Gauge" or "Battery Core" or "Battery Dots" => "\uE83F",
                "Notifications" => "\uE7E7", "Show Desktop" => "\uE8B9", "Tray" => "\uE70E",
                "Apps" => "\uE71D", "Recently Opened" => "\uE81C", "Recycle Bin" => "\uE74D", "Search" => "\uE721",
                "Storage" => "\uEDA2", "Screenshot Capture" => "\uE722", "Wallpaper Gallery" => "\uE91B", "WhatsApp" => "\uE8BD", _ => category.Glyph
            },
            id switch
            {
                "Todos" => 176, "WhatsApp" => 140, "Now Playing" => 308, "Now Playing Bar" => 329, "Now Playing Artwork" => 242, "Quick Toggles" => 240,
                "Audio Output" => 242, "Microphone" => 115, "Pomodoro" => 144, "Clock" => 111, "7 Days" => 252,
                "Weather" => 128, "Weather Details" => 226, "Today & Forecast" => 271, "Weather Status" => 230, "Hourly" => 281, "Sunrise & Sunset" => 205,
                "Currency" or "Crypto" or "Day Timeline" or "GitHub Activity" or "GitHub" or "GitHub Overview" or "LeetCode Stats" => 122,
                "Clock & Weather" or "Screen Time" => 91, "Brightness" => 151, "Temperature" or "Network" => 174,
                _ when ConnectedData.Supports(id) => 137,
                _ when AiMetrics.Supports(id) => 137,
                "Flip Clock" or "World Clocks" or "Day Progress" or "Month Progress" or "Year Progress" => 152,
                "Today" or "Date" or "World Time" => 168, "Timer" or "Stopwatch" or "Calendar" => 102,
                "Segment Clock" => 137, _ => 48
            })))
        .Where(widget => widget.Category == "Clocks" || IsLocal(widget.Id) || LiveData.PublicIds.Contains(widget.Id)
            || ConnectedData.Supports(widget.Id) || AiMetrics.Supports(widget.Id))
        .Concat(WindowsFolders.Select(id => new Widget(id, "Apps & Files", "\uE8B7", 48)))
        .ToDictionary(widget => widget.Id);

    private static bool IsLocal(string id) => id is
        "Todos" or "Notes" or "Dictation" or "Pomodoro" or "Timer" or "Stopwatch" or "Calendar" or "CPU" or "RAM" or "Battery"
        or "Battery Ring" or "Battery Core" or "Battery Dots" or "Battery Gauge" or "Quick Toggles" or "Theme Toggle"
        or "Audio Output" or "Microphone" or "Now Playing" or "Now Playing Artwork" or "Now Playing Mini" or "Now Playing Bar" or "Notifications" or "Show Desktop"
        or "Keyboard" or "WhatsApp" or "Search" or "Storage" or "Screenshot Capture" or "Wallpaper Gallery" or "Recycle Bin" or "Apps"
        or "Recently Opened" or "Brightness" or "Network" or "Temperature" or "Language" or "Tray"
        or "Day Timeline" or "Screen Time" or "Do Not Disturb" or "Commands";

    public static FrameworkElement Face(Widget widget, DockModel model, bool gallery, out Action update, Action<string>? quickAction = null)
    {
        var p = model.Settings;
        var ink = !gallery && LightTiles(p) ? Brush("#1D1D1F") : Paper;
        var muted = !gallery && LightTiles(p) ? Brush("#55555A") : Muted;
        var accent = Accent(p);
        // кружки с кнопками и дорожки индикаторов: на светлых плитках полупрозрачные тёмные (светло-серые), иначе тёмный значок пропадает на тёмном кружке
        bool lightTile = !gallery && LightTiles(p);
        var chip = lightTile ? Brush("#26000000") : Brush("#2C2C2F");
        var track = lightTile ? Brush("#2E000000") : Brush("#2A2A2D");
        var value = Text("", gallery ? 32 : 22, true, ink);
        var detail = Text("", gallery ? 13 : 10, true, muted);
        update = () => { };
        switch (widget.Id)
        {
            case "Dictation":
                // шарик диктовки из FlowBar: движется всегда, во время записи светится ярче
                var dictationOrb = new VoiceOrb { Width = gallery ? 46 : 38, Height = gallery ? 46 : 38 };
                dictationOrb.SetState(Dictation.IsRecording, true);
                update = () => dictationOrb.SetState(Dictation.IsRecording, true);
                return dictationOrb;
            case "Clock":
                var seconds = Text("", gallery ? 19 : 13, true, muted);
                seconds.VerticalAlignment = VerticalAlignment.Bottom;
                seconds.Margin = new(0, 0, 0, gallery ? 5 : 3);
                update = () =>
                {
                    var now = DateTime.Now;
                    value.Text = now.ToString(p.Hour24 ? "HH:mm" : "h:mm");
                    seconds.Text = now.ToString(":ss");
                    detail.Text = now.ToString("ddd dd MMM", L10n.Culture);
                };
                return Stack(RowStack(value, seconds), detail);
            case "Flip Clock":
                var digits = Enumerable.Range(0, 4).Select(_ => new FlipDigit(gallery, ink, !gallery && LightTiles(p))).ToArray();
                var strip = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                for (int index = 0; index < digits.Length; index++)
                {
                    digits[index].Margin = new(2, 0, index == 1 ? 6 : 2, 0);
                    strip.Children.Add(digits[index]);
                }
                update = () =>
                {
                    var time = DateTime.Now.ToString(p.Hour24 ? "HHmm" : "hhmm");
                    for (int index = 0; index < digits.Length; index++) digits[index].SetDigit(time[index]);
                };
                return strip;
            case "Segment Clock":
                var segmentClock = new SegmentFace { Width = gallery ? 172 : 108, Height = gallery ? 41 : 26, Ink = ink };
                var segmentDay = Text("", gallery ? 11 : 8, true, Brush("#FF5835"));
                var segmentDate = Text("", gallery ? 13 : 9, true, ink);
                var dayBadge = Card(segmentDay, 8, Brushes.Transparent, Brush("#FF5835"));
                dayBadge.Padding = new(5, 0, 5, 0);
                dayBadge.Margin = new(0, 0, 5, 0);
                var segmentHeader = RowStack(dayBadge, segmentDate);
                segmentHeader.Margin = new(0, 0, 0, 2);
                update = () =>
                {
                    var now = DateTime.Now;
                    segmentDay.Text = now.ToString("ddd", L10n.Culture).ToUpperInvariant();
                    segmentDate.Text = now.ToString("dd MMM", L10n.Culture).ToUpperInvariant();
                    string time = now.ToString(p.Hour24 ? "HHmm" : "hhmm");
                    if (segmentClock.Digits != time) { segmentClock.Digits = time; segmentClock.InvalidateVisual(); }
                };
                return Stack(segmentHeader, segmentClock);
            case "Analog Minimal":
                var face = new ClockFace { Width = gallery ? 53 : 37, Height = gallery ? 53 : 37 };
                update = face.InvalidateVisual;
                return face;
            case "World Clocks":
                var clocks = RowStack();
                clocks.HorizontalAlignment = HorizontalAlignment.Center;
                var worldFaces = new List<(ClockFace Face, TextBlock Tag)>();
                for (int slot = 0; slot < 3; slot++)
                {
                    var city = SelectedCity(p, slot);
                    var cityClock = new ClockFace { Width = gallery ? 38 : 24, Height = gallery ? 38 : 24,
                        Zone = city.Zone, LightFace = slot < 2 };
                    var name = Text(city.Key, gallery ? 13 : 9, true, Muted);
                    worldFaces.Add((cityClock, name));
                    name.HorizontalAlignment = HorizontalAlignment.Center;
                    var clock = Stack(cityClock, name);
                    clock.Margin = new(5, 0, 5, 0);
                    clocks.Children.Add(clock);
                }
                update = () =>
                {
                    for (int slot = 0; slot < worldFaces.Count; slot++)
                    {
                        var city = SelectedCity(p, slot);
                        var (clock, label) = worldFaces[slot];
                        clock.Zone = city.Zone;
                        label.Text = city.Key;
                        clock.InvalidateVisual();
                    }
                };
                return clocks;
            case "World Time":
                return WorldTimeFace(model, gallery, ink, muted, out update);
            case "7 Days":
                var days = Enumerable.Range(0, 7).Select(_ => Text("", gallery ? 14 : 10, true, ink)).ToArray();
                var week = new UniformGrid();
                week.Rows = 1;
                for (int index = 0; index < days.Length; index++)
                {
                    var day = days[index];
                    day.TextAlignment = TextAlignment.Center;
                    var cell = Card(day, 9, index == 0 ? accent : Brushes.Transparent);
                    cell.Margin = new(0, 1, 2, 0);
                    week.Children.Add(cell);
                }
                update = () =>
                {
                    var now = DateTime.Today;
                    detail.Text = now.ToString("MMMM", L10n.Culture);
                    for (int index = 0; index < days.Length; index++)
                        days[index].Text = now.AddDays(index).ToString("ddd\nd", L10n.Culture);
                };
                var weekWeather = Text("", gallery ? 13 : 10, true, ink);
                var weekIcon = new WeatherMark(-1, true, gallery ? 14 : 11) { Margin = new(0, 0, 3, 0) };
                var weekUpdate = update;
                update = () =>
                {
                    weekUpdate();
                    var forecast = model.Data.Forecast;
                    weekWeather.Text = forecast is null ? "" : WeatherDegrees((double)forecast.Temp, p);
                    weekIcon.Visibility = forecast is null ? Visibility.Collapsed : Visibility.Visible;
                    if (forecast is not null) weekIcon.SetConditions(forecast.Code, forecast.IsDay);
                };
                return Stack(Right(detail, RowStack(weekIcon, weekWeather)), week);
            case "Date":
            case "Today":
            case "Day Number":
                return DateFace(widget.Id, model, gallery, ink, muted, out update);
            case "Day Progress":
            case "Month Progress":
            case "Year Progress":
                var dots = new UniformGrid { Rows = 3, Columns = 10 };
                var lights = new Border[30];
                for (int index = 0; index < lights.Length; index++)
                {
                    var lamp = Card(null, 8, Brushes.Transparent);
                    lamp.Width = lamp.Height = gallery ? 10 : 6;
                    lamp.Margin = new(1);
                    lights[index] = lamp;
                    dots.Children.Add(lamp);
                }
                var unlit = Brush("#45454A");
                var percent = Text("", gallery ? 13 : 10, true, ink);
                update = () =>
                {
                    var now = DateTime.Now;
                    var fraction = widget.Id == "Day Progress" ? now.TimeOfDay.TotalDays
                        : widget.Id == "Month Progress" ? (now.Day - 1 + now.TimeOfDay.TotalDays) / DateTime.DaysInMonth(now.Year, now.Month)
                        : (now.DayOfYear - 1 + now.TimeOfDay.TotalDays) / (DateTime.IsLeapYear(now.Year) ? 366 : 365);
                    for (int index = 0; index < lights.Length; index++)
                        lights[index].Background = index < fraction * lights.Length ? ink : unlit;
                    detail.Text = now.ToString(widget.Id == "Day Progress" ? L10n.Pair("MMM d", "d MMM") : widget.Id == "Month Progress" ? "MMM yyyy" : "yyyy", L10n.Culture);
                    percent.Text = fraction.ToString("0%", CultureInfo.InvariantCulture);
                };
                return Stack(dots, Right(detail, percent));
            case "CPU":
            case "RAM":
            case "Battery":
            case "Battery Ring":
            case "Battery Core":
            case "Battery Dots":
            case "Battery Gauge":
                var ring = new Meter { Width = gallery ? 48 : 35, Height = gallery ? 48 : 35,
                    Stroke = widget.Id == "CPU" ? Brush("#40D0F4") : accent, Variant = widget.Id };
                var meterValue = Text("", gallery ? 14 : 10, true, ink);
                meterValue.HorizontalAlignment = HorizontalAlignment.Center;
                var meter = Stack(Symbol(widget.Glyph, gallery ? 12 : 9, muted), meterValue);
                meter.VerticalAlignment = VerticalAlignment.Center;
                var grid = new Grid();
                grid.Children.Add(ring);
                grid.Children.Add(meter);
                update = () =>
                {
                    var usage = model.Usage;
                    int? amount = widget.Id switch
                    {
                        "CPU" => usage.Cpu, "RAM" => usage.Ram, _ => usage.HasBattery ? usage.Battery : null
                    };
                    meterValue.Text = amount?.ToString() ?? "—";
                    ring.Value = amount ?? 0;
                    ring.InvalidateVisual();
                };
                return grid;
            case "Todos":
                value.FontSize = gallery ? 15 : 11;
                var count = Text("", gallery ? 16 : 12, true, ink);
                count.HorizontalAlignment = HorizontalAlignment.Center;
                var badge = Card(count, 18, Brushes.Transparent, Brush("#555557"));
                badge.Width = badge.Height = gallery ? 34 : 26;
                badge.Margin = new(0, 0, 9, 0);
                update = () =>
                {
                    var remaining = model.Settings.Todos.Count(todo => !todo.Done);
                    count.Text = remaining.ToString();
                    value.Text = remaining == 0 ? L10n.T("No todos") : L10n.F($"{remaining} to do");
                    detail.Text = L10n.T(remaining == 0 ? "Click to add one" : "Todos");
                };
                return RowStack(badge, Stack(value, detail));
            case "Pomodoro":
            case "Timer":
            case "Stopwatch":
                var status = Text("", gallery ? 13 : 10, true, widget.Id == "Pomodoro" ? Brush("#FF4B44") : accent);
                var play = Symbol("\uE768", gallery ? 16 : 12);
                var circle = Card(play, 20, widget.Id == "Pomodoro" ? Brush("#FF443B") : accent);
                circle.Width = circle.Height = gallery ? 32 : 25;
                circle.VerticalAlignment = VerticalAlignment.Center;
                if (!gallery) circle.PreviewMouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    if (widget.Id == "Stopwatch")
                    {
                        if (model.Stopwatch.IsRunning) model.Stopwatch.Stop(); else model.Stopwatch.Start();
                    }
                    else (widget.Id == "Pomodoro" ? model.Focus : model.Timer).Toggle();
                };
                update = () =>
                {
                    var runtime = widget.Id == "Pomodoro" ? model.Focus : model.Timer;
                    status.Text = widget.Id == "Pomodoro"
                        ? $"{L10n.T(model.OnBreak ? "Break" : "Focus")}  {model.FocusRound}/4" : L10n.T(widget.Id);
                    value.Text = Duration(widget.Id == "Stopwatch" ? model.Stopwatch.Elapsed : runtime.Left);
                    play.Text = (widget.Id == "Stopwatch" ? model.Stopwatch.IsRunning : runtime.Running) ? "\uE769" : "\uE768";
                };
                return Right(Stack(status, value), circle);
            case "Microphone":
                // у Lumabar микрофон — две круглые кнопки: микрофон (серый, красный при отключении звука) и наушники
                var micIcon = Symbol("", gallery ? 19 : 14, ink);
                micIcon.HorizontalAlignment = HorizontalAlignment.Center;
                micIcon.VerticalAlignment = VerticalAlignment.Center;
                var micButton = Card(micIcon, 30, chip);
                var headsetIcon = Symbol("", gallery ? 19 : 14, ink);
                headsetIcon.HorizontalAlignment = HorizontalAlignment.Center;
                headsetIcon.VerticalAlignment = VerticalAlignment.Center;
                var headsetButton = Card(headsetIcon, 30, chip);
                micButton.Width = micButton.Height = headsetButton.Width = headsetButton.Height = gallery ? 48 : 30;
                headsetButton.Margin = new(gallery ? 10 : 6, 0, 0, 0);
                update = () => micButton.Background = model.Microphone is { Muted: true } ? Brush("#7A2E2E") : chip;
                var micRow = RowStack(micButton, headsetButton);
                micRow.HorizontalAlignment = HorizontalAlignment.Center;
                micRow.VerticalAlignment = VerticalAlignment.Center;
                return micRow;
            case "Audio Output":
                if (!gallery) return AudioFace(widget, model, ink, muted, accent, chip, track, out update);
                var volume =FilledMeter(Accent(p), gallery ? 24 : 14, widget.Id, track);
                volume.Margin = new(0, 6, 0, 0);
                var speaker = Symbol("", gallery ? 15 : 11, muted);
                speaker.Margin = new(0, 0, 8, 0);
                detail = Text("", gallery ? 14 : 11, true, ink);
                detail.Text = L10n.T(widget.Id);
                var audioState = Text("", gallery ? 14 : 10, true, muted);
                update = () =>
                {
                    var audio = widget.Id == "Microphone" ? model.Microphone : model.Audio;
                    volume.Value = 100 * (audio?.Volume ?? 0);
                    audioState.Text = audio is null ? "—" : audio.Value.Muted ? L10n.T("Muted") : $"{volume.Value:F0}%";
                };
                return Stack(Right(RowStack(speaker, detail), audioState), volume);
            case "Now Playing":
            case "Now Playing Artwork":
            case "Now Playing Mini":
            case "Now Playing Bar":
                return MediaFace(widget.Id, model, gallery, ink, muted, out update);
            case "Quick Toggles":
                var toggles = RowStack();
                toggles.HorizontalAlignment = HorizontalAlignment.Center;
                var radios = new List<(string Name, Border Tile)>();
                var neutral = lightTile ? Brush("#26000000") : Brush("#28292B");
                foreach (var (name, glyph, _) in QuickLinks)
                {
                    if (!gallery && name == "Wi-Fi" && !WifiNetworks.HasAdapterCached) continue; // адаптера Wi-Fi нет — кнопки нет
                    bool wide = name is "Wi-Fi" or "Bluetooth";
                    var icon = Symbol(glyph, gallery ? 17 : 13, ink);
                    FrameworkElement symbol = wide ? RowStack(icon, Text("  ›", gallery ? 19 : 14, true, muted)) : icon;
                    symbol.HorizontalAlignment = HorizontalAlignment.Center;
                    var tile = Card(symbol, gallery ? 27 : 18, neutral);
                    tile.Height = gallery ? 49 : 30;
                    tile.Width = wide ? gallery ? 85 : 47 : gallery ? 48 : 29;
                    tile.Margin = new(2, 0, 2, 0);
                    tile.ToolTip = L10n.T(name);
                    if (wide) radios.Add((name, tile));
                    if (quickAction is not null)
                    {
                        tile.PreviewMouseLeftButtonDown += (_, e) => e.Handled = tile.CaptureMouse();
                        tile.PreviewMouseLeftButtonUp += (_, e) =>
                        {
                            if (!tile.IsMouseCaptured) return;
                            bool activate = tile.IsMouseOver;
                            tile.ReleaseMouseCapture();
                            e.Handled = true;
                            if (activate) quickAction(name);
                        };
                    }
                    toggles.Children.Add(tile);
                }
                update = () =>
                {
                    foreach (var (name, tile) in radios)
                        tile.Background = model.Radios.Read(name) == RadioController.State.On ? accent : neutral;
                };
                return toggles;
            case "Brightness":
                var light = Text("", gallery ? 14 : 11, true, ink);
                var bar = new MetricBar { Height = gallery ? 22 : 11, Fill = accent, Margin = new(0, 5, 0, 0) };
                update = () =>
                {
                    light.Text = model.BrightnessLevel is double brightness ? $"{brightness:P0}" : "—";
                    bar.Fraction = model.BrightnessLevel ?? 0;
                };
                return Stack(Right(Text("☼  Brightness", gallery ? 14 : 11, true, ink), light), bar);
            case "Screen Time":
                update = () =>
                {
                    var today = DateTime.Today;
                    value.Text = CompactDuration(model.Activity.Between(today, today.AddDays(1)).Sum(slice => slice.Seconds));
                    detail.Text = L10n.T("Screen Time");
                };
                return Stack(value, detail);
            case "Day Timeline":
                update = () =>
                {
                    var now = DateTime.Now;
                    var next = p.Events.Where(item => item.End >= now && item.Start.Date == now.Date).OrderBy(item => item.Start).FirstOrDefault();
                    value.Text = next is null ? now.ToString("HH:mm") : next.AllDay ? L10n.T("All day") : next.Start.ToString("HH:mm");
                    detail.Text = next?.Title ?? L10n.T("Day Timeline");
                };
                return Stack(value, detail);
            case "Language":
                var language = Text("", 14, true, ink);
                language.HorizontalAlignment = HorizontalAlignment.Center;
                update = () => language.Text = WindowsShell.InputLanguage();
                return language;
            case "Temperature":
                return TemperatureFace(model, gallery, ink, muted, out update);
            case "Network":
                return NetworkFace(model, gallery, ink, out update);
            case "WhatsApp":
                return UnreadFace("WhatsApp", "WhatsApp", gallery, ink, muted, out update);
            case "Tray": case "Theme Toggle": case "Show Desktop": case "Notifications": case "Keyboard": case "Do Not Disturb":
                var shortcut = Card(Symbol(widget.Glyph, gallery ? 22 : 17, ink), 28,
                    widget.Id == "Do Not Disturb" ? Brush("#6051DA") : lightTile ? Brush("#26000000") : Brush("#26262A"));
                shortcut.Width = shortcut.Height = gallery ? 49 : 31;
                return shortcut;
            case "Weather":
            case "Weather Details":
            case "Today & Forecast":
            case "Weather Status":
            case "Hourly":
            case "Sunrise & Sunset":
            case "Clock & Weather":
                return WeatherFace(widget.Id, model, gallery, ink, muted, out update);
            case "Currency":
            case "Crypto":
            case "GitHub":
            case "GitHub Overview":
            case "GitHub Activity":
            case "LeetCode Stats":
                value.FontSize = gallery ? 20 : 15;
                update = () =>
                {
                    var quote = model.Data.Quotes.GetValueOrDefault(widget.Id);
                    value.Text = L10n.Metric(quote?.Value ?? "—");
                    detail.Text = quote?.Title ?? L10n.T(widget.Name);
                };
                return Stack(value, detail);
            default:
                if (AiMetrics.Supports(widget.Id))
                {
                    value.FontSize = gallery ? 21 : 15;
                    update = () =>
                    {
                        var quota = p.Quotas.GetValueOrDefault(widget.Id);
                        value.Text = widget.Id switch
                        {
                            "AI Limits" => L10n.F($"{p.Quotas.Count} tracked"),
                            "Claude Usage" => model.ClaudeUsage.Length == 0 ? "—" : $"{(model.ClaudeUsage.Sum(item => item.Input + item.Output) / 1000.0):0.#}k",
                            "OpenCode Usage" => model.OpenCodeUsage.Length == 0 ? "—" : $"{(model.OpenCodeUsage.Sum(item => item.Input + item.Output) / 1000.0):0.#}k",
                            _ => quota is null ? "—" : $"{quota.SessionPercent}%"
                        };
                        detail.Text = L10n.T(widget.Name);
                    };
                    return Stack(value, detail);
                }
                if (ConnectedData.Supports(widget.Id))
                {
                    if (widget.Id == "Stock Chart") return StockFace(model, gallery, ink, muted, out update);
                    if (widget.Id.EndsWith(" Chart", StringComparison.Ordinal) && ConnectedData.Finance.Contains(ConnectedData.BaseId(widget.Id)))
                        return RevenueFace(widget.Id, model, gallery, ink, muted, out update);
                    value.FontSize = gallery ? 21 : 15;
                    update = () =>
                    {
                        var snapshot = model.Connected.Dashboards.GetValueOrDefault(ConnectedData.BaseId(widget.Id));
                        value.Text = L10n.Metric(snapshot?.Value ?? "—");
                        detail.Text = L10n.T(widget.Name);
                    };
                    return Stack(value, detail);
                }
                return Symbol(widget.Glyph, gallery ? 24 : 17, ink);
        }
    }

    public static string Duration(TimeSpan left) => $"{(int)left.TotalMinutes:00}:{left.Seconds:00}";
    public static string CompactDuration(int seconds) => seconds >= 3600 ? L10n.F($"{seconds / 3600}h {seconds % 3600 / 60}m")
        : seconds >= 60 ? L10n.F($"{seconds / 60}m") : L10n.T(seconds > 0 ? "<1m" : "0m");
    public static string Rate(double speed) => speed >= 1024 * 1024 ? L10n.F($"{speed / (1024 * 1024):0.0} MB/s") : L10n.F($"{speed / 1024:0.0} KB/s");

    private sealed class Meter : FrameworkElement
    {
        public int Value { get; set; }
        public required Brush Stroke { get; init; }
        public string Variant { get; init; } = "";
        protected override void OnRender(DrawingContext drawing)
        {
            var radius = ActualWidth / 2 - 1.6;
            if (radius <= 0) return;
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            var fraction = Value / 100.0;
            if (Variant == "Battery")
            {
                DrawBattery(drawing, new Rect(2, 3, ActualWidth - 6, ActualHeight - 6), fraction, false);
                drawing.DrawRoundedRectangle(Stroke, null, new Rect(ActualWidth - 2, center.Y - 3, 2, 6), 1, 1);
                return;
            }
            if (Variant == "Battery Core")
            {
                DrawBattery(drawing, new Rect(5, 5, ActualWidth - 10, ActualHeight - 7), fraction, true);
                drawing.DrawRoundedRectangle(Stroke, null, new Rect(center.X - 5, 2, 10, 2), 1, 1);
                return;
            }
            if (Variant == "Battery Dots")
            {
                for (int index = 0; index < 3; index++)
                {
                    var pen = index < fraction * 3 ? Stroke : Line;
                    drawing.DrawEllipse(pen, null, new Point(ActualWidth * (.2 + index * .3), ActualHeight - 3), 2.3, 2.3);
                }
                return;
            }
            if (Variant == "Battery Gauge")
            {
                for (int index = 0; index <= 8; index++)
                {
                    double angle = (index / 8.0 - .5) * Math.PI;
                    var direction = new Vector(Math.Sin(angle), -Math.Cos(angle));
                    drawing.DrawLine(new Pen(index <= fraction * 8 ? Stroke : Line, 1.5),
                        new Point(center.X, center.Y + 7) + direction * radius,
                        new Point(center.X, center.Y + 7) + direction * (radius - 3));
                }
                return;
            }
            drawing.DrawEllipse(null, new Pen(Brush("#38383B"), 2.4), center, radius, radius);
            if (Value <= 0) return;
            var end = Math.Min(99.999, Value) * Math.PI / 50;
            var path = new StreamGeometry();
            using (var context = path.Open())
            {
                context.BeginFigure(new Point(center.X, center.Y - radius), false, false);
                context.ArcTo(new Point(center.X + Math.Sin(end) * radius, center.Y - Math.Cos(end) * radius),
                    new Size(radius, radius), 0, Value > 50, SweepDirection.Clockwise, true, false);
            }
            drawing.DrawGeometry(null, new Pen(Stroke, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, path);
        }

        private void DrawBattery(DrawingContext drawing, Rect shell, double fraction, bool vertical)
        {
            drawing.DrawRoundedRectangle(null, new Pen(Line, 1.5), shell, 5, 5);
            if (fraction <= 0) return;
            shell.Inflate(-2, -2);
            var fill = vertical ? new Rect(shell.Left, shell.Bottom - shell.Height * fraction, shell.Width, shell.Height * fraction)
                : new Rect(shell.Left, shell.Top, shell.Width * fraction, shell.Height);
            var tint = Stroke.Clone();
            tint.Opacity = .46;
            drawing.DrawRoundedRectangle(tint, null, fill, 3, 3);
        }
    }

    private sealed class ClockFace : FrameworkElement
    {
        public string? Zone { get; set; }
        public bool LightFace { get; init; }
        protected override void OnRender(DrawingContext drawing)
        {
            double radius = ActualWidth / 2;
            var center = new Point(radius, radius);
            var ink = LightFace ? Brush("#242427") : Paper;
            drawing.DrawEllipse(LightFace ? Brush("#F4F4F6") : Brush("#29292C"), new Pen(Line, 1), center, radius - 1, radius - 1);
            var now = Zone is null ? DateTime.Now : TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, Zone);
            for (int tick = 0; tick < 12; tick++)
            {
                double angle = tick * Math.Tau / 12;
                var vector = new Vector(Math.Sin(angle), -Math.Cos(angle));
                drawing.DrawLine(new Pen(ink, tick % 3 == 0 ? 1.2 : .5), center + vector * (radius - 4),
                    center + vector * (radius - (tick % 3 == 0 ? 8 : 6)));
            }
            void Hand(double turn, double length, Brush ink, double width)
            {
                var radians = turn * 2 * Math.PI;
                drawing.DrawLine(new Pen(ink, width), center,
                    new Point(radius + Math.Sin(radians) * radius * length, radius - Math.Cos(radians) * radius * length));
            }
            Hand(now.Hour / 12.0 + now.Minute / 720.0, .52, ink, 2.2);
            Hand(now.Minute / 60.0, .72, ink, 1.5);
            Hand(now.Second / 60.0, .8, Brush("#FF9B38"), 1);
        }
    }

    private sealed class SegmentFace : FrameworkElement
    {
        private static readonly byte[] Segments = [0x3f, 0x06, 0x5b, 0x4f, 0x66, 0x6d, 0x7d, 0x07, 0x7f, 0x6f];
        public required Brush Ink { get; init; }
        public string Digits { get; set; } = "0000";

        protected override void OnRender(DrawingContext drawing)
        {
            var pitch = ActualWidth / 4.25;
            var height = ActualHeight;
            var dim = new Pen(Brush("#3D3D40"), 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var on = new Pen(Ink, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            for (int index = 0; index < Digits.Length; index++)
            {
                var mask = Segments[Digits[index] - '0'];
                double left = index * pitch + (index >= 2 ? pitch * .25 : 0) + 2;
                double right = left + pitch - 7;
                void Segment(int segment, double x1, double y1, double x2, double y2) =>
                    drawing.DrawLine((mask & (1 << segment)) != 0 ? on : dim, new(x1, y1), new(x2, y2));
                Segment(0, left + 3, 3, right - 3, 3);
                Segment(1, right, 6, right, height / 2 - 3);
                Segment(2, right, height / 2 + 3, right, height - 6);
                Segment(3, left + 3, height - 3, right - 3, height - 3);
                Segment(4, left, height / 2 + 3, left, height - 6);
                Segment(5, left, 6, left, height / 2 - 3);
                Segment(6, left + 3, height / 2, right - 3, height / 2);
            }
        }
    }
}
