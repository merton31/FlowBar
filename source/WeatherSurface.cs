using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using static LumaDock.Visuals;

namespace LumaDock;

internal sealed partial class DockWindow
{
    private void Weather(Border shell, Window window, string id)
    {
        var panel = Stack();
        var city = Text(P.WeatherPlace?.Name ?? "Weather", 14, true);
        var query = Input("Search for a city", P.WeatherPlace?.Name ?? "");
        var matches = Stack();
        var searchStatus = Text("", 12, false, Muted);
        var forecastStatus = Text("", 12, false, Muted);
        var rows = Stack();
        var searchPanel = Stack();
        LiveData.Weather? painted = null;
        string? paintedError = null;
        int searchRevision = 0;

        async Task Search()
        {
            int revision = ++searchRevision;
            string term = query.Text.Trim();
            matches.Children.Clear();
            searchStatus.Text = L10n.T(term.Length < 2 ? "Enter at least two letters." : "Searching…");
            if (term.Length < 2) return;
            try
            {
                var results = await LiveData.FindPlaces(term);
                if (!window.IsLoaded || revision != searchRevision) return;
                foreach (var result in results)
                    AddRow(matches, result.Name, Button(result.Region.Length > 0 ? result.Region : "Choose", () => SelectPlace(result)));
                searchStatus.Text = results.Length == 0 ? L10n.T("No matching cities") : "";
            }
            catch (Exception ex) when (LiveData.IsDataError(ex))
            {
                if (revision == searchRevision) searchStatus.Text = L10n.T("City search unavailable.");
            }
        }

        var searchButton = Button("Search", () => _ = Search());
        searchButton.Margin = new(6, 0, 0, 0);
        query.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; _ = Search(); } };
        query.TextChanged += (_, _) =>
        {
            searchRevision++;
            matches.Children.Clear();
            searchStatus.Text = "";
        };
        searchPanel.Children.Add(Right(query, searchButton));
        searchPanel.Children.Add(matches);
        searchPanel.Children.Add(searchStatus);
        searchPanel.Margin = new(0, 8, 0, 10);
        var change = Button(Text("Change", 12, true, Accent(P)), () =>
        {
            bool open = searchPanel.Visibility != Visibility.Visible;
            searchPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            searchRevision++;
            if (open) { query.Focus(); query.SelectAll(); }
        }, Brushes.Transparent);
        change.Padding = new(10, 5, 5, 5);
        System.Windows.Automation.AutomationProperties.SetName(change, L10n.T("Change weather city"));
        searchPanel.Visibility = P.WeatherPlace is null ? Visibility.Visible : Visibility.Collapsed;
        AddRow(searchPanel, "Units", Segments(P.WeatherFahrenheit, fahrenheit =>
        {
            P.WeatherFahrenheit = fahrenheit;
            model.Save();
            Paint();
        }, ("°C", false), ("°F", true)));

        var close = Button(Symbol("\uE711", 12, Muted), window.Close, Brushes.Transparent);
        close.Width = close.Height = 25;
        close.Padding = new(0);
        System.Windows.Automation.AutomationProperties.SetName(close, L10n.T("Close forecast"));
        var header = (FrameworkElement)Right(RowStack(city, change), close);
        header.Margin = new(3, 0, 1, 3);
        panel.Children.Add(header);
        panel.Children.Add(searchPanel);
        panel.Children.Add(rows);
        panel.Children.Add(forecastStatus);
        panel.Margin = new(12, 12, 12, 16);
        shell.Child = Scroll(panel);

        void SelectPlace(LiveData.Place place)
        {
            P.WeatherPlace = place;
            model.Save(false);
            city.Text = place.Name;
            query.Text = place.Name;
            searchPanel.Visibility = Visibility.Collapsed;
            _ = Refresh(place, true);
        }

        async Task Refresh(LiveData.Place place, bool force)
        {
            var loading = model.Data.RefreshWeather(place, force);
            Paint();
            if (model.Data.Forecast is null && !loading.IsCompleted) forecastStatus.Text = L10n.T("Loading weather…");
            await loading;
            if (window.IsLoaded) Paint();
        }

        void Paint()
        {
            rows.Children.Clear();
            var data = painted = model.Data.Forecast;
            paintedError = model.Data.WeatherError;
            forecastStatus.Text = L10n.T(paintedError ?? (data is null && P.WeatherPlace is not null ? "Loading weather…" : ""));
            if (data is null) return;

            var today = data.Days.FirstOrDefault(day => day.Date.Date == data.Updated.Date);
            var description = L10n.Condition(LiveData.Condition(data.Code));
            if (today is not null) description += L10n.F($" · H {Degrees(today.High)} L {Degrees(today.Low)}");
            var summary = RowStack(new WeatherMark(data.Code, data.IsDay, 44) { Margin = new(3, 0, 13, 0) },
                Stack(Text(Degrees((double)data.Temp), 35, true), Text(description, 13, false, Muted)));
            summary.Margin = new(4, 1, 0, 13);
            rows.Children.Add(summary);

            var upcoming = LiveData.UpcomingHours(data);
            if (id is "Weather Details" or "Weather Status")
            {
                AddRow(rows, "Feels like", Text(Degrees((double)data.Feels)));
                AddRow(rows, "Humidity", Text($"{data.Humidity}%"));
                AddRow(rows, "Wind", Text(L10n.F($"{data.Wind} km/h")));
            }
            else if (id == "Hourly")
            {
                foreach (var hour in upcoming.Take(24))
                    AddRow(rows, Time(hour.At), Right(new WeatherMark(hour.Code, hour.IsDay, 18),
                        Text(hour.Chance is int chance ? L10n.F($"  {Degrees(hour.Temp)}   Rain {chance}%") : "  " + Degrees(hour.Temp))));
            }
            else if (id == "Sunrise & Sunset")
            {
                foreach (var day in data.Days) AddRow(rows, Day(day, data.Updated), Text($"{Time(day.Sunrise)}   /   {Time(day.Sunset)}"));
            }
            else
            {
                var hourly = new UniformGrid { Rows = 1 };
                hourly.Children.Add(HourColumn("Now", (double)data.Temp, data.Code, data.IsDay));
                foreach (var hour in upcoming.Take(6))
                    hourly.Children.Add(HourColumn(Time(hour.At), hour.Temp, hour.Code, hour.IsDay));
                var band = Card(hourly, 18, Row);
                band.Padding = new(5, 8, 5, 8);
                band.Margin = new(0, 0, 0, 7);
                rows.Children.Add(band);
                foreach (var (day, index) in data.Days.Select((day, index) => (day, index)))
                {
                    var icon = new WeatherMark(day.Code, true, 20) { Margin = new(7, 0, 0, 0) };
                    var label = Text(Day(day, data.Updated), 13, true);
                    label.Width = 54;
                    var high = Text(Degrees(day.High), 13, true);
                    high.Width = 53;
                    high.TextAlignment = TextAlignment.Right;
                    var dayRow = Right(RowStack(label, icon), RowStack(Text(Degrees(day.Low), 13, false, Muted), high));
                    var line = new Border { Child = dayRow, Height = 33, BorderBrush = Line,
                        BorderThickness = new(0, index == 0 ? 0 : 1, 0, 0), Margin = new(4, 0, 4, 0) };
                    rows.Children.Add(line);
                }
            }
        }
        FrameworkElement HourColumn(string label, double temperature, int code, bool daylight)
        {
            var title = Text(label, 12, true, label == "Now" ? Paper : Muted);
            var degrees = Text(Degrees(temperature), 13, true);
            title.HorizontalAlignment = degrees.HorizontalAlignment = HorizontalAlignment.Center;
            return Stack(title, new WeatherMark(code, daylight, 19)
            {
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 3, 0, 2)
            }, degrees);
        }
        string Time(DateTime? value) => value?.ToString(P.Hour24 ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture) ?? "—";
        string Degrees(double value) => Widgets.WeatherDegrees(value, P, P.WeatherFahrenheit);
        static string Day(LiveData.ForecastDay day, DateTime current) =>
            day.Date.Date == current.Date ? "Today" : day.Date.ToString("ddd", L10n.Culture);

        Paint();
        Live(window, () =>
        {
            if (!ReferenceEquals(painted, model.Data.Forecast) || paintedError != model.Data.WeatherError) Paint();
        });
        if (P.WeatherPlace is { } saved && !smoke) _ = Refresh(saved, false);
    }
}

internal sealed class WeatherMark : FrameworkElement
{
    private static readonly Brush Sun = Brush("#FFCC21"), Moon = Brush("#FFE892"), Cloud = Brush("#F1F3F7"),
        DarkCloud = Brush("#B0B7C4"), Rain = Brush("#52AFFF");
    private int code;
    private bool daylight;

    public WeatherMark(int code, bool daylight, double size)
    {
        this.code = code;
        this.daylight = daylight;
        Width = Height = size;
        VerticalAlignment = VerticalAlignment.Center;
        ToolTip = L10n.Condition(LiveData.Condition(code));
    }

    public void SetConditions(int weatherCode, bool isDay)
    {
        if (code == weatherCode && daylight == isDay) return;
        code = weatherCode;
        daylight = isDay;
        ToolTip = L10n.Condition(LiveData.Condition(code));
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(ActualWidth / 32, ActualHeight / 32));
        if (code is 0 or 1 or 2)
        {
            if (code != 0) dc.PushTransform(new TranslateTransform(4, -5));
            if (daylight) DrawSun(dc); else DrawMoon(dc);
            if (code != 0) { dc.Pop(); DrawCloud(dc, Cloud, 6); }
        }
        else if (code < 0)
            dc.DrawRoundedRectangle(Muted, null, new Rect(9, 15, 14, 2.5), 1.25, 1.25);
        else
        {
            DrawCloud(dc, code >= 61 ? DarkCloud : Cloud, 0);
            if (code is 45 or 48)
                for (int i = 0; i < 2; i++) dc.DrawRoundedRectangle(Muted, null, new Rect(5 + 2 * i, 22 + 4 * i, 22 - 4 * i, 2), 1, 1);
            else if (code >= 95)
                dc.DrawGeometry(Sun, null, Geometry.Parse("M16,17 L23,17 18,23 22,23 13,31 15,25 12,25 Z"));
            else if (code is >= 71 and <= 77 or 85 or 86)
                for (int i = 0; i < 3; i++) dc.DrawEllipse(Cloud, null, new Point(10 + 6 * i, 24 + i % 2 * 3), 1.4, 1.4);
            else if (code >= 51)
                for (int i = 0; i < 3; i++) dc.DrawLine(new Pen(Rain, 2), new(11 + 6 * i, 22), new(8 + 6 * i, 28));
        }
        dc.Pop();
    }

    private static void DrawCloud(DrawingContext dc, Brush ink, double lower)
    {
        dc.DrawRoundedRectangle(ink, null, new Rect(4, 12 + lower, 24, 8), 4, 4);
        dc.DrawEllipse(ink, null, new Point(14, 11 + lower), 6, 6);
        dc.DrawEllipse(ink, null, new Point(21, 13 + lower), 5, 5);
    }

    private static void DrawSun(DrawingContext dc)
    {
        dc.DrawEllipse(Sun, null, new Point(16, 16), 8, 8);
        var ray = new Pen(Sun, 2.1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        for (int i = 0; i < 8; i++)
        {
            double angle = i * Math.PI / 4;
            double x = Math.Cos(angle), y = Math.Sin(angle);
            dc.DrawLine(ray, new(16 + 11 * x, 16 + 11 * y), new(16 + 14.4 * x, 16 + 14.4 * y));
        }
    }

    private static void DrawMoon(DrawingContext dc)
    {
        var crescent = new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(new Point(15, 17), 10, 10),
            new EllipseGeometry(new Point(21, 12), 9, 9));
        dc.DrawGeometry(Moon, null, crescent);
        dc.DrawEllipse(Moon, null, new Point(25, 7), 1.3, 1.3);
    }
}
