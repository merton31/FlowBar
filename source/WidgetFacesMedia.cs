using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shapes = System.Windows.Shapes;
using static LumaDock.Visuals;

namespace LumaDock;

// Лица виджетов погоды и музыки по образцу галереи Lumabar: значок слева, крупный текст, пояснения бледным.
// Когда данных ещё нет (галерея без сети, ничего не играет), галерея показывает образец, как у Lumabar; в панели образец не используется.
internal static partial class Widgets
{
    private static LiveData.Weather? sampleWeather;

    // Образец погоды для превью в галерее: дождь, 12°, три дня вперёд, 6 часов
    private static LiveData.Weather SampleWeather()
    {
        if (sampleWeather is not null) return sampleWeather;
        var now = DateTime.Now;
        var today = now.Date;
        LiveData.ForecastDay Day(int offset, double low, double high, int code) =>
            new(today.AddDays(offset), low, high, code, today.AddDays(offset).AddHours(8).AddMinutes(10), today.AddDays(offset).AddHours(17).AddMinutes(50));
        var start = today.AddHours(now.Hour + 1);
        double[] temps = [12, 11, 10, 10, 11, 11, 12];
        var hours = temps.Select((degrees, index) => new LiveData.ForecastHour(start.AddHours(index), degrees, 30, 61, false)).ToArray();
        return sampleWeather = new LiveData.Weather(12, 10, 80, 12, 61, true, now,
            [Day(0, 7, 16, 61), Day(1, 10, 16, 61), Day(2, 13, 16, 95), Day(3, 11, 14, 61)], hours);
    }

    // Логотипы сервисов лежат в папке Services проекта и вшиты в exe; ключ — название сервиса так, как его считает ServicePreview
    private static readonly Dictionary<string, string> ServiceLogos = new(StringComparer.Ordinal)
    {
        ["GitHub"] = "github", ["LeetCode"] = "leetcode", ["Gmail"] = "gmail", ["Linear"] = "linear", ["Supabase"] = "supabase",
        ["Resend"] = "resend", ["Search Console"] = "search-console", ["DataFast Chart"] = "datafast", ["Plausible"] = "plausible",
        ["Dub"] = "dub", ["X Profile"] = "x", ["TikTok"] = "tiktok", ["YouTube"] = "youtube", ["Instagram"] = "instagram",
        ["Stripe"] = "stripe", ["Lemon Squeezy"] = "lemon-squeezy", ["Polar"] = "polar", ["Shopify"] = "shopify", ["Paddle"] = "paddle",
        ["Dodo Payments"] = "dodo", ["Gumroad"] = "gumroad", ["Claude"] = "claude", ["ChatGPT"] = "chatgpt", ["Gemini"] = "gemini",
        ["Grok"] = "grok", ["DeepSeek Balance"] = "deepseek", ["GLM"] = "glm", ["OpenCode"] = "opencode", ["Cursor"] = "cursor",
        ["Stock Watchlist"] = "finnhub", ["WhatsApp"] = "whatsapp"
    };
    private static readonly Dictionary<string, BitmapSource?> serviceLogoCache = new(StringComparer.Ordinal);

    internal static BitmapSource? ServiceLogo(string brand)
    {
        if (!ServiceLogos.TryGetValue(brand, out var file)) return null;
        if (serviceLogoCache.TryGetValue(brand, out var cached)) return cached;
        BitmapSource? image = null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri($"pack://application:,,,/Services/{file}.png", UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            image = bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException) { }
        return serviceLogoCache[brand] = image;
    }

    // Виджеты подключаемых сервисов: пока данных нет, галерея показывает размытый образец и название сервиса со значком, как Lumabar
    internal static (FrameworkElement Face, Action Update) ServicePreview(Widget widget, DockModel model, FrameworkElement face, Action update)
    {
        string id = widget.Id;
        bool github = id is "GitHub" or "GitHub Overview" or "GitHub Activity" or "LeetCode Stats";
        bool connected = ConnectedData.Supports(id) && id != "Stock Chart";
        if (!github && !connected && !AiMetrics.Supports(id)) return (face, update);
        string brand = connected ? ConnectedData.BaseId(id) : id.Split(' ')[0];
        bool HasData() => id switch
        {
            "GitHub" or "GitHub Overview" or "GitHub Activity" or "LeetCode Stats" => model.Data.Quotes.ContainsKey(id),
            "AI Limits" => model.Settings.Quotas.Count > 0,
            "Claude Usage" => model.ClaudeUsage.Length > 0,
            "OpenCode Usage" => model.OpenCodeUsage.Length > 0,
            _ when AiMetrics.Supports(id) => model.Settings.Quotas.ContainsKey(id),
            _ => model.Connected.Dashboards.ContainsKey(ConnectedData.BaseId(id))
        };
        var wave = new Shapes.Path
        {
            Data = Geometry.Parse("M0,12 C10,2 20,22 30,12 S50,2 60,12 S80,22 90,12 S110,2 120,12"), Stroke = Brush("#2FBF71"), StrokeThickness = 2.4,
            Stretch = Stretch.Fill, Height = 22, Margin = new(0, 4, 0, 0)
        };
        var sample = Stack(Right(RawText("Net revenue", 11, true, Muted), RawText("$788", 18, true, Paper)), wave);
        sample.VerticalAlignment = VerticalAlignment.Center;
        sample.Margin = new(4, 0, 4, 0);
        sample.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 7 };
        (string back, string front) = brand switch
        {
            "GitHub" => ("#F2F2F4", "#111111"), "Gumroad" => ("#FF90E8", "#111111"), "Stripe" => ("#635BFF", "#FFFFFF"),
            "Shopify" => ("#95BF47", "#FFFFFF"), "Polar" => ("#2F6BFF", "#FFFFFF"), "Paddle" => ("#FFFFFF", "#111111"),
            "LeetCode" => ("#FFA116", "#111111"), "Claude" => ("#D97757", "#FFFFFF"), _ => ("#3A3A3F", "#FFFFFF")
        };
        var logo = ServiceLogo(brand);
        Border badge;
        if (logo is not null)
        {
            // Настоящий логотип сервиса; значок обрезается скруглением, как плитка приложения
            var picture = new Image { Source = logo, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            badge = Card(picture, 7, Brushes.Transparent);
            badge.Width = badge.Height = 28;
            badge.Clip = new RectangleGeometry(new Rect(0, 0, 28, 28), 7, 7);
        }
        else
        {
            badge = Card(Centered(RawText(brand[..1].ToUpperInvariant(), 14, true, Brush(front))), 7, Brush(back));
            badge.Width = badge.Height = 26;
        }
        var name = RawText(brand, 12, true, Paper);
        name.Margin = new(0, 3, 0, 0);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        var overlay = Centered(Stack(Centered(badge), name));
        var host = new Grid();
        host.Children.Add(sample);
        host.Children.Add(overlay);
        host.Children.Add(face);
        return (host, () =>
        {
            bool has = HasData();
            face.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            sample.Visibility = overlay.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
            update();
        });
    }

    // Виджет «значок приложения и число непрочитанных» (WhatsApp): число — уведомления приложения в центре уведомлений Windows
    private static FrameworkElement UnreadFace(string brand, string app, bool gallery, Brush ink, Brush muted, out Action update)
    {
        double size = gallery ? 46 : 32;
        double radius = size * .24;
        FrameworkElement icon;
        var logo = ServiceLogo(brand);
        if (logo is not null)
        {
            var picture = new Image { Source = logo, Width = size, Height = size, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            icon = Card(picture, radius, Brushes.Transparent);
            icon.Clip = new RectangleGeometry(new Rect(0, 0, size, size), radius, radius);
        }
        else icon = Card(Centered(RawText(brand[..1].ToUpperInvariant(), size * .45, true, ink)), radius, Brush("#3A3A3F"));
        icon.Width = icon.Height = size;
        var count = RawText("0", gallery ? 26 : 18, true, ink);
        var caption = Text("Unread", gallery ? 14 : 10, true, muted);
        var text = Stack(count, caption);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Margin = new(gallery ? 14 : 8, 0, 0, 0);
        var row = RowStack(icon, text);
        row.HorizontalAlignment = HorizontalAlignment.Center;
        row.VerticalAlignment = VerticalAlignment.Center;
        update = () =>
        {
            _ = UnreadNotifications.Refresh();
            bool denied = UnreadNotifications.State == UnreadNotifications.Access.Denied;
            int unread = UnreadNotifications.Count(app);
            count.Text = denied ? "—" : unread > 99 ? "99+" : unread.ToString();
            caption.Text = L10n.T(denied ? "Allow access" : "Unread");
        };
        return row;
    }

    private static T Centered<T>(T element) where T : FrameworkElement
    {
        element.HorizontalAlignment = HorizontalAlignment.Center;
        element.VerticalAlignment = VerticalAlignment.Center;
        return element;
    }

    // Маленький значок из штрихов: ветер, капля, восход и закат
    private static FrameworkElement Glyph(string data, string colour, double size, double thickness = 1.4) => new Shapes.Path
    {
        Data = Geometry.Parse(data), Stroke = Brush(colour), StrokeThickness = thickness, Stretch = Stretch.Uniform,
        Width = size, Height = size, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = false
    };

    private const string WindShape = "M1,5 H9.5 A2.2,2.2 0 1 0 7.3,2.8 M1,9 H12.5 A2.4,2.4 0 1 1 10.1,11.4 M1,13 H6";
    private const string DropShape = "M6,1 C6,1 1.5,6 1.5,9 A4.5,4.5 0 0 0 10.5,9 C10.5,6 6,1 6,1 Z";
    private const string SunriseShape = "M1,12 H13 M3.5,12 A3.5,3.5 0 0 1 10.5,12 M7,1.5 V5 M5.3,3.2 L7,1.5 L8.7,3.2";
    private const string SunsetShape = "M1,12 H13 M3.5,12 A3.5,3.5 0 0 1 10.5,12 M7,1.5 V5 M5.3,3.3 L7,5 L8.7,3.3";

    private static FrameworkElement WeatherFace(string id, DockModel model, bool gallery, Brush ink, Brush muted, out Action update)
    {
        if (id == "Clock & Weather") return ClockWeatherFace(model, gallery, out update);
        var host = new Border();
        LiveData.Weather? shown = null;
        string place = "";
        bool built = false;
        update = () =>
        {
            var forecast = model.Data.Forecast ?? (gallery ? SampleWeather() : null);
            string city = model.Data.Forecast is null && gallery ? "Tbilisi" : model.Settings.WeatherPlace?.Name ?? "";
            if (built && ReferenceEquals(forecast, shown) && city == place) return;
            (built, shown, place) = (true, forecast, city);
            host.Child = forecast is null ? EmptyWeather(id, gallery, ink, muted) : WeatherLayout(id, forecast, model.Settings, city, gallery, ink, muted);
        };
        return host;
    }

    private static FrameworkElement EmptyWeather(string id, bool gallery, Brush ink, Brush muted) =>
        Centered(RowStack(new WeatherMark(-1, true, gallery ? 28 : 17) { Margin = new(0, 0, 8, 0) }, Stack(Text("—", gallery ? 20 : 13, true, ink), Text(id, gallery ? 12 : 9, false, muted))));

    private static FrameworkElement WeatherLayout(string id, LiveData.Weather f, Preferences p, string city, bool gallery, Brush ink, Brush muted)
    {
        double s = gallery ? 1 : .72;
        string Deg(double celsius) => WeatherDegrees(celsius, p);
        var day = f.Days.FirstOrDefault();
        string condition = L10n.Condition(LiveData.Condition(f.Code));
        FrameworkElement Mark(double size, double gap) => new WeatherMark(f.Code, f.IsDay, size * s) { Margin = new(0, 0, gap * s, 0) };
        FrameworkElement Divider() => new Border { Width = 1, Height = 46 * s, Background = Brush("#2E2E31"), Margin = new(12 * s, 0, 12 * s, 0), VerticalAlignment = VerticalAlignment.Center };
        var temp = RawText(Deg((double)f.Temp), 24 * s, true, ink);
        switch (id)
        {
            case "Weather Details":
            {
                int chance = LiveData.UpcomingHours(f).FirstOrDefault()?.Chance ?? 0;
                FrameworkElement Row(FrameworkElement icon, string value) { icon.Margin = new(0, 0, 6 * s, 0); return RowStack(icon, RawText(value, 13 * s, true, ink)); }
                var range = RawText(L10n.Pair("H/L", "Макс/Мин"), 11 * s, true, Brush("#FF9F0A"));
                range.Margin = new(0, 0, 6 * s, 0);
                var rows = Stack(Row(Glyph(WindShape, "#41CEF4", 14 * s), $"{f.Wind} km/h"), Row(Glyph(DropShape, "#41A6FF", 14 * s), $"{chance}%"),
                    RowStack(range, RawText(day is null ? "—" : $"{Deg(day.High)} / {Deg(day.Low)}", 13 * s, true, ink)));
                return Centered(RowStack(Mark(32, 8), Stack(RawText(Deg((double)f.Temp), 22 * s, true, ink), RawText(condition, 12 * s, false, muted)), Divider(), rows));
            }
            case "Today & Forecast":
            {
                var columns = RowStack();
                foreach (var next in f.Days.Skip(1).Take(3))
                {
                    var column = Stack(Centered(RawText(next.Date.ToString("ddd", L10n.Culture), 12 * s, true, muted)),
                        Centered(new WeatherMark(next.Code, true, 22 * s) { Margin = new(0, 2 * s, 0, 2 * s) }),
                        Centered(RowStack(RawText(Deg(next.High), 13 * s, true, ink), RawText(" " + Deg(next.Low), 13 * s, true, muted))));
                    column.Margin = new(0, 0, 14 * s, 0);
                    columns.Children.Add(column);
                }
                return Centered(RowStack(Mark(38, 10), Stack(temp, RawText(L10n.T("Today"), 13 * s, true, muted)), Divider(), columns));
            }
            case "Weather Status":
            {
                var left = Stack(RawText(condition, 21 * s, true, Brushes.White), RawText(city, 16 * s, false, Brush("#C9D2E3")));
                left.VerticalAlignment = VerticalAlignment.Center;
                var degrees = RawText(Deg((double)f.Temp), 46 * s, false, Brushes.White);
                degrees.FontWeight = FontWeights.Light;
                var right = RowStack(degrees, new WeatherMark(f.Code, f.IsDay, 42 * s) { Margin = new(2, 0, 0, 0) });
                right.HorizontalAlignment = HorizontalAlignment.Right;
                right.VerticalAlignment = VerticalAlignment.Center;
                var content = new Grid { Margin = new(14 * s, 0, 10 * s, 0) };
                content.Children.Add(left);
                content.Children.Add(right);
                var card = new Border
                {
                    CornerRadius = new(gallery ? 17 : 12), Child = content, ClipToBounds = true,
                    Background = new LinearGradientBrush(Color.FromRgb(0x4B, 0x5C, 0x7A), Color.FromRgb(0x1D, 0x23, 0x31), 35),
                    HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch
                };
                if (gallery) { card.Margin = new(-8, -2, -8, -2); card.Height = 70; card.VerticalAlignment = VerticalAlignment.Center; }
                return card;
            }
            case "Hourly":
            {
                var grid = new Grid();
                var items = new[] { ("Now", (double)f.Temp, f.Code, f.IsDay, true) }
                    .Concat(LiveData.UpcomingHours(f).Take(5).Select(hour => (hour.At.ToString("HH:mm"), hour.Temp, hour.Code, hour.IsDay, false)));
                int index = 0;
                foreach (var (label, degrees, code, isDay, now) in items)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 46 * s });
                    var column = Stack(Centered(RawText(label == "Now" ? L10n.T("Now") : label, 12 * s, true, now ? ink : muted)),
                        Centered(new WeatherMark(code, isDay, 22 * s) { Margin = new(0, 3 * s, 0, 3 * s) }),
                        Centered(RawText(Deg(degrees), 13 * s, true, ink)));
                    Grid.SetColumn(column, index++);
                    grid.Children.Add(column);
                }
                return Centered(grid);
            }
            case "Sunrise & Sunset":
            {
                double progress = .5;
                if (day?.Sunrise is { } rise && day.Sunset is { } set && set > rise)
                    progress = Math.Clamp((DateTime.Now - rise).TotalSeconds / (set - rise).TotalSeconds, 0, 1);
                if (day is null) progress = .5;
                var arc = new SunArc(progress) { Height = 28 * s, Margin = new(2, 0, 2, 4) };
                FrameworkElement Time(DateTime? at, bool rising)
                {
                    var icon = Glyph(rising ? SunriseShape : SunsetShape, "#FF9F0A", 14 * s);
                    var text = RawText(at?.ToString("HH:mm") ?? "—", 14 * s, true, ink);
                    icon.Margin = new(rising ? 0 : 6 * s, 0, rising ? 6 * s : 0, 0);
                    return rising ? RowStack(icon, text) : RowStack(text, icon);
                }
                var panel = Stack(arc, Right(Time(day?.Sunrise, true), Time(day?.Sunset, false)));
                panel.VerticalAlignment = VerticalAlignment.Center;
                panel.Margin = new(6 * s, 0, 6 * s, 0);
                return panel;
            }
            default:
            {
                var range = RawText(day is null ? "" : $"H {Deg(day.High)} L {Deg(day.Low)}", 12 * s, true, muted);
                return Centered(RowStack(Mark(38, 10), Stack(temp, range)));
            }
        }
    }

    private static FrameworkElement ClockWeatherFace(DockModel model, bool gallery, out Action update)
    {
        var time = RawText("", gallery ? 19 : 16, true, Brushes.Black);
        time.HorizontalAlignment = HorizontalAlignment.Center;
        var date = RawText("", gallery ? 13 : 10, true, Paper);
        var degrees = Centered(RawText("", gallery ? 11 : 9, true, Brushes.Black));
        var badge = Card(degrees, 14, Brush("#D4D4D8"));
        badge.Width = badge.Height = gallery ? 26 : 20;
        update = () =>
        {
            var now = DateTime.Now;
            var forecast = model.Data.Forecast ?? (gallery ? SampleWeather() : null);
            time.Text = now.ToString(model.Settings.Hour24 ? "HH:mm" : "h:mm");
            date.Text = now.ToString("ddd d", L10n.Culture);
            degrees.Text = forecast is null ? "—" : WeatherDegrees((double)forecast.Temp, model.Settings);
        };
        return Stack(Card(time, 9, Brush("#FF5835")), Right(date, badge));
    }

    // Дуга солнца от восхода к закату с точкой на нынешнем положении
    private sealed class SunArc(double progress) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            var start = new Point(0, h * .78);
            var control = new Point(w / 2, -h * .62);
            var end = new Point(w, h * .78);
            var line = new StreamGeometry();
            using (var path = line.Open())
            {
                path.BeginFigure(start, false, false);
                path.QuadraticBezierTo(control, end, true, false);
            }
            dc.DrawGeometry(null, new Pen(Brush("#FFB43C"), 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, line);
            double t = progress, u = 1 - t;
            var dot = new Point(u * u * start.X + 2 * u * t * control.X + t * t * end.X, u * u * start.Y + 2 * u * t * control.Y + t * t * end.Y);
            dc.DrawEllipse(Brush("#FFC52E"), null, dot, 4.2, 4.2);
        }
    }

    private static FrameworkElement MediaFace(string id, DockModel model, bool gallery, Brush ink, Brush muted, out Action update)
    {
        double s = gallery ? 1 : .72;
        bool lightTile = !gallery && LightTiles(model.Settings); // светлая плитка: кружки и дорожки светлые, полоса прогресса тёмная
        var title = RawText("", 14 * s, true, ink);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var artist = RawText("", 12 * s, false, muted);
        artist.TextTrimming = TextTrimming.CharacterEllipsis;
        var image = new Image { Stretch = Stretch.UniformToFill };
        // Подставной «обложкой» служит градиент, пока нет настоящей: в галерее он выглядит как картинка
        var sampleCover = new Border { Background = new LinearGradientBrush(Color.FromRgb(0x7A, 0x52, 0x9E), Color.FromRgb(0x2A, 0x1D, 0x3C), 55) };
        var idle = Symbol("", 15 * s, muted);
        idle.HorizontalAlignment = HorizontalAlignment.Center;
        idle.VerticalAlignment = VerticalAlignment.Center;
        var art = new Grid();
        art.Children.Add(idle);
        art.Children.Add(sampleCover);
        art.Children.Add(image);
        TextBlock? playIcon = null;
        var progress = FilledMeter(lightTile ? ink : Brushes.White, 3, "Now Playing", lightTile ? Brush("#2E000000") : Brush("#3A3A3D"));
        progress.Value = 55;
        progress.VerticalAlignment = VerticalAlignment.Center;
        progress.Margin = new(0, 5 * s, 0, 0);

        // Круглая кнопка или значок управления: в панели нажатие шлёт клавишу мультимедиа
        FrameworkElement Control(string glyph, byte key, double size, double glyphSize, bool round)
        {
            var icon = Symbol(glyph, glyphSize * s, ink);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            if (key == 0xB3) playIcon = icon;
            FrameworkElement element = round ? Card(icon, size * s / 2, lightTile ? Brush("#26000000") : Brush("#2C2C2F")) : icon;
            if (round) element.Width = element.Height = size * s;
            else icon.Margin = new(6 * s, 0, 6 * s, 0);
            if (!gallery) element.PreviewMouseLeftButtonUp += (_, e) => { e.Handled = true; WindowsShell.Key(key); };
            return element;
        }

        bool sample = false;
        update = () =>
        {
            var track = model.Media.Current;
            sample = gallery && track is null;
            title.Text = !string.IsNullOrWhiteSpace(track?.Title) ? track.Title : sample ? "Midnight City" : L10n.T("Now Playing");
            artist.Text = track?.Artist ?? (sample ? "M83" : "");
            image.Source = model.Media.Artwork;
            sampleCover.Visibility = sample ? Visibility.Visible : Visibility.Collapsed;
            idle.Visibility = track is null && !sample ? Visibility.Visible : Visibility.Collapsed;
            progress.Visibility = sample ? Visibility.Visible : Visibility.Collapsed;
            if (playIcon is not null) playIcon.Text = track?.State == "Playing" ? "" : "";
        };

        FrameworkElement Cover(double size, double radius, bool circle = false)
        {
            var holder = new Border { Width = size * s, Height = size * s, ClipToBounds = true, Child = art };
            holder.CornerRadius = new(circle ? size * s / 2 : radius * s);
            holder.Clip = circle ? new EllipseGeometry(new Rect(0, 0, size * s, size * s)) : new RectangleGeometry(new Rect(0, 0, size * s, size * s), radius * s, radius * s);
            return holder;
        }

        switch (id)
        {
            case "Now Playing Mini":
            {
                var play = Control("", 0xB3, 0, 13, false);
                ((TextBlock)play).Margin = new();
                var cover = Cover(46, 0, true);
                var veil = new Border { Width = 46 * s, Height = 46 * s, CornerRadius = new(23 * s), Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)) };
                var ring = new Shapes.Ellipse { Width = 52 * s, Height = 52 * s, Stroke = Brush("#3A3A3D"), StrokeThickness = 2 };
                var grid = new Grid { Width = 52 * s, Height = 52 * s };
                grid.Children.Add(ring);
                grid.Children.Add(Centered(cover));
                grid.Children.Add(Centered(veil));
                grid.Children.Add(play);
                return Centered(grid);
            }
            case "Now Playing Artwork":
            {
                var overlay = new Border { Background = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)) };
                title.HorizontalAlignment = HorizontalAlignment.Center;
                if (!gallery) title.FontSize = 13;
                var transport = Centered(RowStack(Control("", 0xB1, 0, gallery ? 13 : 18, false), Control("", 0xB3, 0, gallery ? 15 : 20, false), Control("", 0xB0, 0, gallery ? 13 : 18, false)));
                var texts = Centered(Stack(title, transport));
                var back = new Grid { ClipToBounds = true };
                art.Width = double.NaN;
                back.Children.Add(art);
                back.Children.Add(overlay);
                back.Children.Add(texts);
                var card = new Border { CornerRadius = new(gallery ? 17 : 12), Child = back, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
                // Рамка не обрезает содержимое по скруглению: обрезаем сами по размеру карточки
                card.SizeChanged += (_, e) => card.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), card.CornerRadius.TopLeft, card.CornerRadius.TopLeft);
                if (gallery) { card.Margin = new(-8, -2, -8, -2); card.Height = 70; card.VerticalAlignment = VerticalAlignment.Center; }
                // в доке карточка заполняет плитку целиком (без полей кнопки), как в Lumabar
                else { card.Margin = new(-8, -1, -8, -1); card.CornerRadius = new(13); }
                return card;
            }
            case "Now Playing Bar":
            {
                var cover = Cover(46, 8);
                cover.Margin = new(0, 0, 10 * s, 0);
                var text = Stack(title, artist, progress);
                text.VerticalAlignment = VerticalAlignment.Center;
                var buttons = RowStack(Control("", 0xB3, 38, 15, true), Control("", 0xB0, 0, 13, false));
                buttons.Margin = new(8 * s, 0, 0, 0);
                var row = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(cover, Dock.Left);
                DockPanel.SetDock(buttons, Dock.Right);
                row.Children.Add(cover);
                row.Children.Add(buttons);
                row.Children.Add(text);
                return row;
            }
            default:
            {
                var cover = Cover(46, 8);
                cover.Margin = new(0, 0, 10 * s, 0);
                var text = Stack(title, artist);
                text.VerticalAlignment = VerticalAlignment.Center;
                var play = Control("", 0xB3, 36, 14, true);
                play.Margin = new(8 * s, 0, 0, 0);
                var row = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(cover, Dock.Left);
                DockPanel.SetDock(play, Dock.Right);
                row.Children.Add(cover);
                row.Children.Add(play);
                row.Children.Add(text);
                return row;
            }
        }
    }
}
