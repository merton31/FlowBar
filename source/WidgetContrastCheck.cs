using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LumaDock;

internal static class WidgetContrastCheck
{
    internal static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        try
        {
            var app = new Application();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/FlowBar;component/DockControls.xaml", UriKind.Relative) });
            ErrorLog.Disabled = true;
            Dictation.Fixture = true;
            var model = new DockModel(Path.Combine(folder, "test-profile.json"));
            foreach (bool light in new[] { false, true })
            {
                for (int step = 0; step <= 255; step++)
                {
                    double background = step / 255d;
                    double opacity = DockWindow.WidgetBackingOpacity(light, background);
                    double result = (light ? opacity : 0) + background * (1 - opacity);
                    double muted = light ? 85 / 255d : 161 / 255d;
                    double ratio = (Math.Max(Linear(result), Linear(muted)) + .05) / (Math.Min(Linear(result), Linear(muted)) + .05);
                    if (ratio < 4.5) throw new InvalidOperationException("Insufficient secondary text contrast: " + ratio);
                }
                model.Settings.Style = light ? "White" : "Liquid";
                model.Settings.Transparency = 100;
                var page = new StackPanel();
                foreach (byte brightness in new byte[] { 255, 0, 128 })
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10) };
                    foreach (string id in new[] { "Network", "Audio Output", "Today", "Language", "Quick Toggles" })
                    {
                        var spec = Widgets.Available[id];
                        var face = Widgets.Face(spec, model, false, out var update);
                        update();
                        var color = light ? Colors.White : Colors.Black;
                        double opacity = DockWindow.WidgetBackingOpacity(light, brightness / 255d);
                        var button = Visuals.Button(face, () => { }, new SolidColorBrush(color) { Opacity = opacity }, 13);
                        var tile = Visuals.Card(button, 14, Brushes.Transparent);
                        tile.Width = Widgets.WidthOf(model.Settings, spec);
                        tile.Height = 58; tile.Margin = new Thickness(2);
                        row.Children.Add(tile);
                    }
                    page.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(brightness, brightness, brightness)), Child = row });
                }
                page.Measure(new Size(1800, 500));
                page.Arrange(new Rect(0, 0, page.DesiredSize.Width, page.DesiredSize.Height));
                page.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(page.ActualWidth), (int)Math.Ceiling(page.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(page);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(folder, light ? "light-tiles.png" : "dark-tiles.png"));
                encoder.Save(stream);
            }
            File.WriteAllText(Path.Combine(folder, "result.txt"), "Secondary text contrast >= 4.5:1 at 256 background levels for both palettes; widget renders saved.\nOK");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(folder, "result.txt"), ex.ToString());
            return 1;
        }
    }

    private static double Linear(double value) => value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
}
