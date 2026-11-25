using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LumaDock;

internal sealed partial class DockWindow
{
    private readonly List<(Border Tile, SolidColorBrush Fill, bool Light)> contrastTiles = [];
    private long nextContrastSample;

    // Keep the theme's ink and accent colors. Adapt the backing beneath each
    // widget to the brightest/darkest sampled pixels, including mixed wallpapers.
    internal static double WidgetBackingOpacity(bool light, double background)
    {
        background = Math.Clamp(background, 0, 1);
        // Dark tiles stay below sRGB 0.20 so muted text remains legible.
        // Light tiles stay above sRGB 0.90 for both primary and secondary ink.
        return light
            ? Math.Clamp((.90 - background) / Math.Max(.001, 1 - background), .30, .96)
            : Math.Clamp(1 - .20 / Math.Max(.001, background), .30, .96);
    }

    private void RegisterWidgetContrast(Border tile, Button button, bool light)
    {
        var fill = new SolidColorBrush(light ? Colors.White : Colors.Black) { Opacity = .96 };
        button.Background = fill;
        // Fading the entire button also fades its text into the desktop.
        button.MouseEnter += (_, _) => button.Opacity = 1;
        contrastTiles.Add((tile, fill, light));
        nextContrastSample = 0;
    }

    private void RefreshWidgetContrast()
    {
        long now = Environment.TickCount64;
        if (!IsVisible || WindowState == WindowState.Minimized || contrastTiles.Count == 0 || now < nextContrastSample) return;
        nextContrastSample = now + 500;
        try
        {
            var origin = PointToScreen(new System.Windows.Point(0, 0));
            var dpi = VisualTreeHelper.GetDpi(this);
            int width = Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX));
            int height = Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));
            if (width > 16384 || height > 2048) return;
            using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            // Standard SRCCOPY omits layered windows, as in the existing glass capture.
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen((int)origin.X, (int)origin.Y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
            foreach (var (tile, fill, light) in contrastTiles)
            {
                if (!tile.IsVisible || tile.ActualWidth < 1 || tile.ActualHeight < 1) continue;
                var location = tile.PointToScreen(new System.Windows.Point(0, 0));
                double extreme = light ? 1 : 0;
                for (int row = 0; row < 3; row++)
                    for (int column = 0; column < 7; column++)
                    {
                        int x = Math.Clamp((int)(location.X - origin.X + tile.ActualWidth * dpi.DpiScaleX * (column + .5) / 7), 0, width - 1);
                        int y = Math.Clamp((int)(location.Y - origin.Y + tile.ActualHeight * dpi.DpiScaleY * (row + .5) / 3), 0, height - 1);
                        var pixel = bitmap.GetPixel(x, y);
                        double sample = light ? Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) / 255d :
                            Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) / 255d;
                        extreme = light ? Math.Min(extreme, sample) : Math.Max(extreme, sample);
                    }
                double target = WidgetBackingOpacity(light, extreme);
                // Restore contrast immediately. Ease only toward more transparency.
                double opacity = target >= fill.Opacity ? target : Math.Max(target, fill.Opacity - .10);
                if (Math.Abs(fill.Opacity - opacity) > .005) fill.Opacity = opacity;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            foreach (var entry in contrastTiles) entry.Fill.Opacity = .96;
        }
    }
}
