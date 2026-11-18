using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static LumaDock.Visuals;

namespace LumaDock;

internal static partial class Widgets
{
    internal sealed record ClockCity(string Key, string Name, string Zone);
    internal static readonly ClockCity[] ClockCities =
    [
        new("NYC", "New York", "Eastern Standard Time"), new("LON", "London", "GMT Standard Time"),
        new("TYO", "Tokyo", "Tokyo Standard Time"), new("PAR", "Paris", "Romance Standard Time"),
        new("MSK", "Moscow", "Russian Standard Time"), new("TBS", "Tbilisi", "Georgian Standard Time"),
        new("LAX", "Los Angeles", "Pacific Standard Time"), new("CHI", "Chicago", "Central Standard Time"),
        new("DXB", "Dubai", "Arabian Standard Time"), new("DEL", "Delhi", "India Standard Time"),
        new("SIN", "Singapore", "Singapore Standard Time"), new("SYD", "Sydney", "AUS Eastern Standard Time"),
        new("IST", "Istanbul", "Turkey Standard Time"), new("HKG", "Hong Kong", "China Standard Time")
    ];

    internal static ClockCity SelectedCity(Preferences preferences, int slot)
    {
        string? id = slot < 0 ? preferences.WorldTimeCity : preferences.WorldClockCities?.ElementAtOrDefault(slot);
        return ClockCities.FirstOrDefault(city => city.Key == id) ?? ClockCities[Math.Max(0, slot)];
    }

    internal static string WeatherDegrees(double celsius, Preferences preferences, bool unit = false)
    {
        double value = preferences.WeatherFahrenheit ? celsius * 1.8 + 32 : celsius;
        return $"{value:F0}°" + (unit ? preferences.WeatherFahrenheit ? "F" : "C" : "");
    }

    internal static readonly (string Name, string Glyph, string Address)[] QuickLinks =
    [
        ("Wi-Fi", "\uE701", "ms-settings:network-wifi"),
        ("Ethernet", "\uE839", "ms-settings:network-ethernet"),
        ("Bluetooth", "\uE702", "ms-settings:bluetooth"),
        ("Mobile hotspot", "\uE704", "ms-settings:network-mobilehotspot"),
        ("Airplane mode", "\uE709", "ms-settings:network-airplanemode")
    ];

    internal static readonly string[][] StyleFamilies =
    [
        ["Clock", "Flip Clock", "World Clocks", "World Time", "Analog Minimal", "Segment Clock", "Clock & Weather"],
        ["Today", "Date", "Day Number", "7 Days"],
        ["Day Progress", "Month Progress", "Year Progress"],
        ["Battery", "Battery Ring", "Battery Core", "Battery Dots", "Battery Gauge"],
        ["Now Playing", "Now Playing Artwork", "Now Playing Mini", "Now Playing Bar"],
        ["Weather", "Weather Details", "Today & Forecast", "Weather Status", "Hourly", "Sunrise & Sunset"]
    ];

    internal static string[] Styles(string id)
    {
        var family = StyleFamilies.FirstOrDefault(group => group.Contains(id));
        if (family is not null) return family;
        string provider = ConnectedData.BaseId(id);
        if (!ConnectedData.Finance.Contains(provider)) return [];
        return Available.Keys.Where(key => ConnectedData.BaseId(key) == provider).ToArray();
    }

    private static FrameworkElement WorldTimeFace(DockModel model, bool gallery, Brush ink, Brush muted, out Action update)
    {
        var time = Text("", gallery ? 30 : 18, true, ink);
        var subtitle = Text("", gallery ? 13 : 10, true, muted);
        var cityName = Text("", gallery ? 14 : 11, true, ink);
        var city = RowStack(Text("●", gallery ? 12 : 10, true, Brush("#FFB900")), cityName);
        update = () =>
        {
            var now = DateTimeOffset.Now;
            var configured = SelectedCity(model.Settings, -1);
            cityName.Text = "  " + L10n.T(configured.Name);
            var selected = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, configured.Zone);
            time.Text = selected.ToString(model.Settings.Hour24 ? "HH:mm" : "h:mm", CultureInfo.InvariantCulture);
            var difference = selected.Offset - now.Offset;
            var day = selected.Date == now.Date ? "Today" : selected.Date < now.Date ? "Yesterday" : "Tomorrow";
            var offset = difference.ToString(@"h\:mm", CultureInfo.InvariantCulture);
            if (difference.Minutes == 0) offset = $"{Math.Abs(difference.Hours)}{L10n.Pair("h", " ч")}";
            subtitle.Text = $"{L10n.T(day)}, {(difference < TimeSpan.Zero ? "−" : "+")}{offset}";
        };
        return Right(Stack(city, subtitle), time);
    }

    private static FrameworkElement DateFace(string id, DockModel model, bool gallery, Brush ink, Brush muted, out Action update)
    {
        var red = Brush("#FF514C");
        var label = Text("", gallery ? 14 : 10, true, id == "Date" ? muted : red);
        var date = Text("", gallery ? 23 : 16, true, ink);
        var time = Text("", gallery ? 22 : 15, true, ink);
        var conditions = Text("", gallery ? 13 : 10, true, muted);
        var conditionsIcon = new WeatherMark(-1, true, gallery ? 14 : 11) { Margin = new(0, 0, 3, 0) };
        if (id == "Day Number")
        {
            label.HorizontalAlignment = date.HorizontalAlignment = HorizontalAlignment.Center;
            date.FontSize = gallery ? 27 : 18;
        }
        update = () =>
        {
            var now = DateTime.Now;
            label.Text = now.ToString(id == "Day Number" ? "ddd" : "dddd", L10n.Culture);
            if (id != "Date") label.Text = label.Text.ToUpperInvariant();
            date.Text = now.ToString(id == "Day Number" ? "dd" : id == "Today" ? L10n.Pair("MMM d", "d MMM") : L10n.Pair("MMMM d", "d MMMM"), L10n.Culture);
            time.Text = now.ToString(model.Settings.Hour24 ? "HH:mm" : "h:mm");
            var forecast = model.Data.Forecast;
            conditions.Text = forecast is null ? "" : WeatherDegrees((double)forecast.Temp, model.Settings);
            conditionsIcon.Visibility = forecast is null ? Visibility.Collapsed : Visibility.Visible;
            if (forecast is not null) conditionsIcon.SetConditions(forecast.Code, forecast.IsDay);
        };
        var left = Stack(label, date);
        if (id != "Today") return left;
        var right = Stack(time, RowStack(conditionsIcon, conditions));
        right.Margin = new(15, 0, 0, 0);
        return Right(left, right);
    }

    private static FrameworkElement TemperatureFace(DockModel model, bool gallery, Brush ink, Brush muted, out Action update)
    {
        var face = new Grid { Width = gallery ? 51 : 37, Height = gallery ? 51 : 37, Margin = new(0, 0, 10, 0) };
        var ring = new Meter { Width = face.Width, Height = face.Height, Stroke = Brush("#52D478") };
        var temp = Text("", gallery ? 15 : 11, true, ink);
        temp.HorizontalAlignment = HorizontalAlignment.Center;
        face.Children.Add(ring);
        face.Children.Add(temp);
        var readings = Stack();
        var cpu = Text("", gallery ? 13 : 10, true, ink);
        var ram = Text("", gallery ? 13 : 10, true, ink);
        var cpuBar = new MetricBar { Width = gallery ? 42 : 31, Height = 4, Fill = Brush("#45B7FF"), Margin = new(7, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
        var ramBar = new MetricBar { Width = cpuBar.Width, Height = 4, Fill = Brush("#1488FF"), Margin = cpuBar.Margin, VerticalAlignment = VerticalAlignment.Center };
        readings.Children.Add(Right(RowStack(Text("CPU", 11, true, muted), cpuBar), cpu));
        readings.Children.Add(Right(RowStack(Text("RAM", 11, true, muted), ramBar), ram));
        readings.VerticalAlignment = VerticalAlignment.Center;
        var group = new DockPanel();
        DockPanel.SetDock(face, Dock.Left);
        group.Children.Add(face);
        group.Children.Add(readings);
        update = () =>
        {
            temp.Text = model.Temperature is double degrees ? $"{degrees:F0}°" : "—";
            ring.Value = (int)Math.Clamp(model.Temperature ?? 0, 0, 100);
            ring.InvalidateVisual();
            cpu.Text = $"{model.Usage.Cpu}%";
            ram.Text = $"{model.Usage.Ram}%";
            cpuBar.Fraction = model.Usage.Cpu / 100d;
            ramBar.Fraction = model.Usage.Ram / 100d;
        };
        return group;
    }

    private static FrameworkElement NetworkFace(DockModel model, bool gallery, Brush ink, out Action update)
    {
        var down = Text("", gallery ? 14 : 11, true, ink);
        var up = Text("", gallery ? 14 : 11, true, ink);
        var chart = new TraceView { Height = gallery ? 27 : 16, Margin = new(0, 4, 0, 0) };
        update = () =>
        {
            down.Text = $"↓ {Rate(model.Network.Download)}";
            up.Text = $"{Rate(model.Network.Upload)} ↑";
            // Пока истории нет, галерея показывает образец волны, как Lumabar
            if (gallery && model.Network.History.Count() < 2)
                chart.Set(Enumerable.Range(0, 24).Select(i => 5 + 4 * Math.Sin(i / 2.6)).ToArray(), Enumerable.Range(0, 24).Select(i => 5 + 3 * Math.Cos(i / 2.1)).ToArray());
            else chart.Set(model.Network.History.Select(value => value.Down).ToArray(), model.Network.History.Select(value => value.Up).ToArray());
        };
        return Stack(Right(down, up), chart);
    }

    private static FrameworkElement StockFace(DockModel model, bool gallery, Brush ink, Brush muted, out Action update)
    {
        var symbol = Text("Stock Chart", gallery ? 12 : 10, false, muted);
        var quote = Text("—", gallery ? 14 : 11, true, ink);
        var chart = new StockTrace(true) { Height = gallery ? 27 : 16 };
        ConnectedData.Snapshot? last = null;
        update = () =>
        {
            var current = model.Connected.Dashboards.GetValueOrDefault("Stock Chart");
            if (ReferenceEquals(current, last)) return;
            last = current;
            symbol.Text = current?.Entries.FirstOrDefault()?.Title ?? L10n.T("Stock Chart");
            quote.Text = L10n.Metric(current?.Value ?? "—");
            chart.Set(current?.Prices?.TakeLast(22).ToArray() ?? []);
        };
        return Stack(Right(symbol, quote), chart);
    }

    private static FrameworkElement RevenueFace(string id, DockModel model, bool gallery, Brush ink, Brush muted, out Action update)
    {
        var amount = Text("—", gallery ? 15 : 12, true, ink);
        var status = Text(id, gallery ? 12 : 9, false, muted);
        var trend = new TraceView { Height = gallery ? 25 : 13 };
        ConnectedData.Snapshot? previous = null;
        update = () =>
        {
            var result = model.Connected.Dashboards.GetValueOrDefault(ConnectedData.BaseId(id));
            if (ReferenceEquals(result, previous)) return;
            previous = result;
            amount.Text = result?.Value ?? "—";
            var lines = result?.Revenue?.GroupBy(line => line.Currency).FirstOrDefault();
            if (lines is null) { trend.Set([], []); return; }
            var start = DateTime.Today.AddDays(-29);
            var groups = lines.GroupBy(line => line.At.Date).ToDictionary(group => group.Key, group => (double)group.Sum(line => line.Amount));
            trend.Set(Enumerable.Range(0, 30).Select(day => Math.Max(0, groups.GetValueOrDefault(start.AddDays(day)))).ToArray(), []);
        };
        return Stack(Right(status, amount), trend);
    }

    private sealed class MetricBar : FrameworkElement
    {
        private double fraction;
        public Brush Fill { get; init; } = Brushes.White;
        public double Fraction { set { if (fraction != value) { fraction = value; InvalidateVisual(); } } }
        protected override void OnRender(DrawingContext dc)
        {
            var area = new Rect(0, 0, ActualWidth, ActualHeight);
            var geometry = new RectangleGeometry(area, ActualHeight / 2, ActualHeight / 2);
            dc.DrawGeometry(Brush("#343439"), null, geometry);
            dc.PushClip(geometry);
            dc.DrawRectangle(Fill, null, new Rect(0, 0, ActualWidth * Math.Clamp(fraction, 0, 1), ActualHeight));
            dc.Pop();
        }
    }

    private sealed class TraceView : FrameworkElement
    {
        private double[] first = [], second = [];
        public void Set(double[] a, double[] b)
        {
            if (first.SequenceEqual(a) && second.SequenceEqual(b)) return;
            first = a;
            second = b;
            InvalidateVisual();
        }
        protected override void OnRender(DrawingContext dc)
        {
            double peak = Math.Max(1, Math.Max(first.DefaultIfEmpty().Max(), second.DefaultIfEmpty().Max()));
            var floor = ActualHeight - 2;
            dc.DrawLine(new Pen(Brush("#39393E"), 1), new Point(0, floor), new Point(ActualWidth, floor));
            Plot(first, Brush("#41CEF4"));
            Plot(second, Brush("#9B7BFE"));
            void Plot(double[] points, Brush colour)
            {
                if (points.Length < 2 || floor < 3) return;
                var line = new StreamGeometry();
                using (var path = line.Open())
                {
                    for (int index = 0; index < points.Length; index++)
                    {
                        var point = new Point(index * ActualWidth / (points.Length - 1), floor - Math.Clamp(points[index] / peak, 0, 1) * (floor - 2));
                        if (index == 0) path.BeginFigure(point, false, false);
                        else path.LineTo(point, true, false);
                    }
                }
                dc.DrawGeometry(null, new Pen(colour, 1.8) { LineJoin = PenLineJoin.Round }, line);
            }
        }
    }
}
