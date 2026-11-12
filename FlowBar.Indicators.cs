using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PixelPoint = System.Drawing.Point;
using PixelRect = System.Drawing.Rectangle;

namespace FlowBar
{
    partial class Bar
    {
        sealed class AppWindowIndicator : FrameworkElement
        {
            static readonly double[] linear = MakeLinearTable();
            readonly int local, remote;
            readonly double localWidth;
            readonly Color accent;
            readonly bool active;
            Color markColor;
            Brush mark, rim;
            Pen stroke, borderStroke;

            public AppWindowIndicator(int local, int remote, bool active, double tileHeight, Color accent, Color background)
            {
                this.local = Math.Min(2, local); this.remote = Math.Min(2, remote);
                this.active = active; this.accent = accent;
                localWidth = active ? Math.Max(6, Math.Round(tileHeight * (this.local == 1 ? 0.34 : 0.17))) : 6;
                if (remote > 0) localWidth = Math.Min(localWidth, 8);
                Width = 2 + this.local * localWidth + this.remote * 7 + Math.Max(0, this.local + this.remote - 1) * 2;
                Height = 6;
                Margin = new Thickness(0, 0, 0, 1);
                HorizontalAlignment = HorizontalAlignment.Center;
                VerticalAlignment = VerticalAlignment.Bottom;
                IsHitTestVisible = false;
                SnapsToDevicePixels = true;
                var level = Luminance(background.R << 16 | background.G << 8 | background.B);
                UpdateContrast(level, level);
            }

            public void UpdateContrast(double darkest, double lightest)
            {
                double light = 1.05 / (lightest + 0.05);
                double dark = (darkest + 0.05) / 0.05;
                var next = light >= dark ? Colors.White : Colors.Black;
                // Keep the previous tone around the crossover to avoid flicker on moving glass.
                if ((markColor == Colors.White || markColor == Colors.Black) &&
                    Math.Abs(light - dark) < 0.45 && Math.Min(light, dark) >= 3) next = markColor;
                double value = Luminance(accent.R << 16 | accent.G << 8 | accent.B);
                double contrast = value < darkest ? (darkest + 0.05) / (value + 0.05)
                    : value > lightest ? (value + 0.05) / (lightest + 0.05) : 1;
                if (active && contrast >= (markColor == accent ? 3 : 3.4)) next = accent;
                if (mark != null && next == markColor) return;
                markColor = next;
                mark = Frozen(next, 255);
                rim = Frozen(Luminance(next.R << 16 | next.G << 8 | next.B) >= 0.18 ? Colors.Black : Colors.White, 255);
                stroke = new Pen(mark, 1.25); stroke.Freeze();
                borderStroke = new Pen(rim, 2.65); borderStroke.Freeze();
                InvalidateVisual();
            }

            protected override void OnRender(DrawingContext dc)
            {
                double x = 1;
                for (int i = 0; i < local; i++)
                {
                    dc.DrawRoundedRectangle(rim, null, new Rect(x - 0.75, 1, localWidth + 1.5, 4), 2, 2);
                    dc.DrawRoundedRectangle(mark, null, new Rect(x, 1.75, localWidth, 2.5), 1.25, 1.25);
                    x += localWidth + 2;
                }
                for (int i = 0; i < remote; i++)
                {
                    var ring = new Rect(x + 0.5, 1.4, 6, 3.2);
                    dc.DrawRoundedRectangle(null, borderStroke, ring, 1.6, 1.6);
                    dc.DrawRoundedRectangle(null, stroke, ring, 1.6, 1.6);
                    x += 9;
                }
            }

            static double[] MakeLinearTable()
            {
                var values = new double[256];
                for (int i = 0; i < values.Length; i++)
                {
                    double s = i / 255.0;
                    values[i] = s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
                }
                return values;
            }

            internal static double Luminance(int rgb)
            {
                return 0.2126 * linear[rgb >> 16 & 255] + 0.7152 * linear[rgb >> 8 & 255] + 0.0722 * linear[rgb & 255];
            }
        }

        sealed class IndicatorSample
        {
            public AppWindowIndicator Indicator;
            public PixelRect Rect;
            public double Darkest = 1, Lightest;
            public bool Valid;
        }

        readonly HashSet<AppWindowIndicator> liveIndicators = new HashSet<AppWindowIndicator>();
        DispatcherTimer indicatorTimer;
        bool indicatorReadPending;

        FrameworkElement Indicator(bool active, int local, int remote, bool live = true)
        {
            var marker = new AppWindowIndicator(local, remote, active, TileH, AccentColor, Cur.Bg);
            if (live)
            {
                marker.Loaded += (s, e) =>
                {
                    liveIndicators.Add(marker);
                    if (indicatorTimer == null)
                    {
                        indicatorTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(160) };
                        indicatorTimer.Tick += (sender, args) => SampleIndicators();
                        Closed += (sender, args) => { indicatorTimer.Stop(); liveIndicators.Clear(); };
                    }
                    indicatorTimer.Start();
                };
                marker.Unloaded += (s, e) =>
                {
                    liveIndicators.Remove(marker);
                    if (liveIndicators.Count == 0) indicatorTimer.Stop();
                };
            }
            return marker;
        }

        void SampleIndicators()
        {
            if (indicatorReadPending || !IsVisible || root == null || !root.IsLoaded) return;
            var area = PixelRect.Intersect(IndicatorScreenRect(root), scr.Bounds);
            if (stripScroll != null) area.Intersect(IndicatorScreenRect(stripScroll));
            if (area.Width < 1 || area.Height < 1) return;
            var samples = new List<IndicatorSample>();
            var band = PixelRect.Empty;
            foreach (var marker in liveIndicators)
            {
                if (!marker.IsVisible || marker.ActualWidth < 1 || !marker.IsArrangeValid) continue;
                var position = IndicatorScreenRect(marker);
                if (position.Width < 1 || !area.Contains(position)) continue;
                var padding = position;
                padding.Inflate(3, 3);
                padding.Intersect(area);
                band = band.IsEmpty ? padding : PixelRect.Union(band, padding);
                samples.Add(new IndicatorSample { Indicator = marker, Rect = position });
            }
            if (samples.Count == 0) return;
            indicatorReadPending = true;
            Task.Run(() =>
            {
                try { ReadIndicatorBand(band, samples); }
                catch (Exception) { /* Keep the outlined theme color if the desktop cannot be read. */ }
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    indicatorReadPending = false;
                    if (!IsVisible) return;
                    foreach (var sample in samples)
                        if (sample.Valid && liveIndicators.Contains(sample.Indicator) && sample.Indicator.IsVisible &&
                            IndicatorScreenRect(sample.Indicator) == sample.Rect)
                            sample.Indicator.UpdateContrast(sample.Darkest, sample.Lightest);
                }));
            });
        }

        static PixelRect IndicatorScreenRect(FrameworkElement element)
        {
            try
            {
                var start = element.PointToScreen(new Point());
                var end = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
                return PixelRect.FromLTRB((int)Math.Floor(start.X), (int)Math.Floor(start.Y),
                    (int)Math.Ceiling(end.X), (int)Math.Ceiling(end.Y));
            }
            catch (InvalidOperationException) { return PixelRect.Empty; }
        }

        static void ReadIndicatorBand(PixelRect band, List<IndicatorSample> samples)
        {
            using (var image = new LensDib())
            {
                if (!image.CopyFromDesktop(band)) return;
                foreach (var sample in samples)
                {
                    var r = sample.Rect;
                    // Samples lie outside the entire marker, so a dot never sees its previous color.
                    for (int i = 1; i <= 3; i++)
                    {
                        int x = r.Left + r.Width * i / 4;
                        ReadIndicatorPixel(image, band, sample, x, r.Top - 2);
                        ReadIndicatorPixel(image, band, sample, x, r.Bottom + 1);
                    }
                    ReadIndicatorPixel(image, band, sample, r.Left - 2, r.Top + r.Height / 2);
                    ReadIndicatorPixel(image, band, sample, r.Right + 1, r.Top + r.Height / 2);
                }
            }
        }

        static void ReadIndicatorPixel(LensDib image, PixelRect band, IndicatorSample sample, int x, int y)
        {
            if (!band.Contains(new PixelPoint(x, y))) return;
            int offset = ((y - band.Top) * band.Width + x - band.Left) * 4;
            double level = AppWindowIndicator.Luminance(Marshal.ReadInt32(image.Pixels, offset));
            sample.Darkest = Math.Min(sample.Darkest, level);
            sample.Lightest = Math.Max(sample.Lightest, level);
            sample.Valid = true;
        }

        static string WindowIndicatorStatus(int local, int remote)
        {
            return "Окон на этом мониторе: " + local + " · На других: " + remote;
        }
    }
}
