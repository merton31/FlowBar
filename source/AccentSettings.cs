using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static LumaDock.Visuals;

namespace LumaDock;

internal sealed partial class DockWindow
{
    private static readonly (string Name, string Color, string Previous)[] AccentPalette =
    [
        ("Blue", "#0A84FF", "#0A88FF"), ("Indigo", "#5E5CE6", "#6552E7"), ("Purple", "#BF5AF2", "#B951E7"),
        ("Pink", "#FF375F", "#FF3164"), ("Red", "#FF453A", "#FF453A"), ("Orange", "#ED6812", "#EF6E13"),
        ("Green", "#29A84A", "#27AA52"), ("Teal", "#1A9FB5", "#159EB3"), ("Graphite", "#8E8E93", "#8D8D94")
    ];

    private void AccentSettings(StackPanel content, Action onChange)
    {
        var colours = SettingsGroup(content);
        var swatches = RowStack();
        var markers = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        var picker = Stack();
        picker.Visibility = Visibility.Collapsed;
        var customMark = Card(null, 4, Brushes.White);
        bool isCustom = P.UseCustomAccent || !AccentPalette.Any(value => Matches(value.Color) || Matches(value.Previous));
        foreach (var (name, color, previous) in AccentPalette)
        {
            var swatch = Swatch(name, Brush(color), () =>
            {
                if (isCustom && P.CustomAccent is null) P.CustomAccent = P.Accent;
                picker.Visibility = Visibility.Collapsed;
                SetAccent(color, false);
            });
            markers.Add(color, (Border)swatch.Content);
            swatch.Margin = new(0, 0, 7, 0);
            swatches.Children.Add(swatch);
        }
        var custom = Swatch("Custom accent", HueGradient(), () =>
        {
            if (picker.Visibility == Visibility.Visible) { picker.Visibility = Visibility.Collapsed; return; }
            var initial = P.CustomAccent ?? P.Accent;
            picker.Children.Clear();
            picker.Children.Add(AccentPicker(initial, value => SetAccent(value, true)));
            picker.Visibility = Visibility.Visible;
        }, customMark);
        swatches.Children.Add(custom);
        SettingsRow(colours, Text("Accent", 14, true), swatches);
        colours.Children.Add(picker);
        ShowSelected();

        bool Matches(string color) => string.Equals(P.Accent, color, StringComparison.OrdinalIgnoreCase);

        void ShowSelected()
        {
            foreach (var (_, color, previous) in AccentPalette)
                markers[color].Opacity = !isCustom && (Matches(color) || Matches(previous)) ? 1 : 0;
            customMark.Opacity = isCustom ? 1 : 0;
        }

        void SetAccent(string color, bool customColor)
        {
            if (customColor) P.CustomAccent = color;
            P.Accent = color;
            P.UseCustomAccent = isCustom = customColor;
            model.Save();
            onChange();
            ShowSelected();
        }

        static Button Swatch(string label, Brush face, Action action, Border? mark = null)
        {
            mark ??= Card(null, 4, Brushes.White);
            mark.Width = mark.Height = 7;
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            var button = Button(mark, action, face, 10);
            button.Width = button.Height = 20;
            button.Padding = new(0);
            button.ToolTip = L10n.T(label);
            AutomationProperties.SetName(button, L10n.T(label));
            return button;
        }
    }

    private static LinearGradientBrush HueGradient()
    {
        var rainbow = new GradientStopCollection();
        var colors = new[] { Colors.Red, Colors.Yellow, Colors.Lime, Colors.Cyan, Colors.Blue, Colors.Magenta, Colors.Red };
        for (int index = 0; index < colors.Length; index++)
            rainbow.Add(new GradientStop(colors[index], index / 6d));
        return new(rainbow, 0);
    }

    private static UIElement AccentPicker(string initial, Action<string> save)
    {
        Color color = ParseAccent(initial) ?? Colors.DodgerBlue;
        var (hue, saturation, brightness) = ToHsv(color);
        var sample = Card(null, 14, new SolidColorBrush(color));
        sample.Width = sample.Height = 28;
        sample.Margin = new(0, 0, 10, 0);
        var hex = Input("Hex colour", Hex(color));
        hex.MaxLength = 7;
        hex.Width = 98;
        hex.FontFamily = new FontFamily("Consolas");
        AutomationProperties.SetName(hex, L10n.T("Hex colour"));
        var saturationFill = new LinearGradientBrush(Colors.White, Colors.Red, 0);
        var value = Card(null, 9, saturationFill);
        var shade = Card(null, 9, new LinearGradientBrush(Colors.Transparent, Colors.Black, 90));
        var point = Marker();
        var square = new Grid { Height = 120, Margin = new(0, 0, 0, 10), Cursor = Cursors.Cross };
        square.Children.Add(value);
        square.Children.Add(shade);
        square.Children.Add(point);
        var hueMarker = Marker();
        var strip = new Grid { Height = 14, Margin = new(0, 0, 0, 9), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        strip.Children.Add(Card(null, 7, HueGradient()));
        strip.Children.Add(hueMarker);

        void Paint()
        {
            saturationFill.GradientStops[1].Color = FromHsv(hue, 1, 1);
            sample.Background = new SolidColorBrush(color);
            point.Background = sample.Background;
            Move(point, saturation * square.ActualWidth, (1 - brightness) * square.ActualHeight);
            Move(hueMarker, hue * strip.ActualWidth / 360, strip.ActualHeight / 2);
            if (!hex.IsKeyboardFocused) hex.Text = Hex(color);
        }
        square.SizeChanged += (_, _) => Paint();
        strip.SizeChanged += (_, _) => Paint();
        Drag(square, fraction => { saturation = fraction.X; brightness = 1 - fraction.Y; });
        Drag(strip, fraction => hue = fraction.X * 359.99);
        hex.LostKeyboardFocus += (_, _) => ApplyHex();
        hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; ApplyHex(); } };
        return new Border { Child = Stack(square, strip, RowStack(sample, hex)), Padding = new(14, 1, 14, 13) };

        void ApplyHex()
        {
            if (ParseAccent(hex.Text) is { } parsed)
            {
                color = parsed;
                (hue, saturation, brightness) = ToHsv(color);
                save(Hex(color));
            }
            hex.Text = Hex(color);
            Paint();
        }

        void Drag(Grid element, Action<Point> change)
        {
            void Read(MouseEventArgs e)
            {
                if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return;
                var p = e.GetPosition(element);
                change(new Point(Math.Clamp(p.X / element.ActualWidth, 0, 1), Math.Clamp(p.Y / element.ActualHeight, 0, 1)));
                color = FromHsv(hue, saturation, brightness);
                Paint();
            }
            element.MouseLeftButtonDown += (_, e) =>
            {
                if (!element.CaptureMouse()) return;
                Read(e);
                e.Handled = true;
            };
            element.MouseMove += (_, e) => { if (element.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) Read(e); };
            element.MouseLeftButtonUp += (_, e) =>
            {
                if (!element.IsMouseCaptured) return;
                Read(e);
                element.ReleaseMouseCapture();
                save(Hex(color));
            };
        }

        static Border Marker() => new() { Width = 13, Height = 13, CornerRadius = new(7), BorderBrush = Brushes.White,
            BorderThickness = new(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false };
        static void Move(Border marker, double x, double y) => marker.RenderTransform = new TranslateTransform(x - 6.5, y - 6.5);
    }

    private static Color? ParseAccent(string input)
    {
        string text = input.Trim();
        if (text.StartsWith('#')) text = text[1..];
        return text.Length == 6 && uint.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb)
            ? Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb) : null;
    }

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static (double Hue, double Saturation, double Value) ToHsv(Color color)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        double hue = delta == 0 ? 0 : max == r ? (g - b) / delta : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
        return ((hue * 60 + 360) % 360, max == 0 ? 0 : delta / max, max);
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        byte Channel(double n)
        {
            double k = (n + hue / 60) % 6;
            return (byte)Math.Round(255 * (value - value * saturation * Math.Max(0, Math.Min(k, Math.Min(4 - k, 1)))));
        }
        return Color.FromRgb(Channel(5), Channel(3), Channel(1));
    }
}
