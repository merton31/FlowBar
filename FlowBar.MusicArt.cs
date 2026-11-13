using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FlowBar
{
    partial class Bar
    {
        static readonly string[] DemoTrackTitles = { "Северное сияние", "После заката", "Глубина" };
        static int demoMusicIndex;
        static string DemoMusicAlbum(int index) { return index == 0 ? "Aurora" : index == 1 ? "Afterglow" : "Deep"; }

        static ImageSource DemoMusicCover(int index)
        {
            var dark = index == 0 ? Color.FromRgb(18, 20, 79) : index == 1 ? Color.FromRgb(72, 14, 42) : Color.FromRgb(13, 55, 57);
            var light = index == 0 ? Color.FromRgb(103, 132, 255) : index == 1 ? Color.FromRgb(255, 128, 65) : Color.FromRgb(75, 229, 175);
            var art = new DrawingGroup();
            using (var dc = art.Open())
            {
                dc.DrawRectangle(new LinearGradientBrush(dark, light, 55 + index * 35), null, new Rect(0, 0, 256, 256));
                var center = new Point(128 + (index - 1) * 24, 120);
                dc.DrawEllipse(new RadialGradientBrush(light, dark), null, center, 84, 84);
                var ring = new Pen(Frozen(light, 0xA8), 1);
                for (int i = 0; i < 9; i++) dc.DrawEllipse(null, ring, center, 30 + i * 7, 30 + i * 7);
                dc.DrawEllipse(Frozen(light, 0xEC), null, center, 16, 16);
                dc.DrawEllipse(Frozen(dark, 255), null, center, 3, 3);
            }
            var bitmap = new RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) dc.DrawDrawing(art);
            bitmap.Render(visual); bitmap.Freeze();
            return bitmap;
        }

        static void DemoMusicCommand(string cmd, double fraction)
        {
            var s = live;
            var oldTrack = s.MusicTrack;
            switch (cmd)
            {
                case "toggle": s.Playing = !s.Playing; break;
                case "seek": s.Pos = s.Dur * fraction; break;
                case "next":
                case "prev":
                    skipFrom = oldTrack; skipDirection = cmd == "prev" ? -1 : 1;
                    skipUntil = DateTime.UtcNow.AddSeconds(3);
                    demoMusicIndex = (demoMusicIndex + skipDirection + DemoTrackTitles.Length) % DemoTrackTitles.Length;
                    s.Title = DemoTrackTitles[demoMusicIndex]; s.Album = DemoMusicAlbum(demoMusicIndex);
                    s.Cover = DemoMusicCover(demoMusicIndex); s.Pos = 0;
                    break;
            }
            PublishMusicTrack(s, oldTrack);
        }

        sealed class MusicTrackCardData
        {
            public readonly string Key, Source, Title, Artist, Album;
            public readonly ImageSource Cover;

            public MusicTrackCardData(string key, string source, string title, string artist, string album, ImageSource cover)
            { Key = key; Source = source; Title = title; Artist = artist; Album = album; Cover = cover; }
        }

        // Сохраняем соседние треки, уже увиденные в этом сеансе. Новый источник начинает свою историю.
        static readonly List<MusicTrackCardData> musicHistory = new List<MusicTrackCardData>();
        static int musicHistoryAt;
        static void RememberMusicTrack(Snap s)
        {
            if (!s.HasMedia) { musicHistory.Clear(); musicHistoryAt = 0; return; }
            var item = new MusicTrackCardData(s.MusicTrack, s.App, s.Title, s.Artist, s.Album, s.Cover);
            if (musicHistory.Count > 0 && musicHistory[musicHistoryAt].Source != item.Source) { musicHistory.Clear(); musicHistoryAt = 0; }
            if (musicHistory.Count == 0) { musicHistory.Add(item); return; }
            if (musicHistory[musicHistoryAt].Key == item.Key) { musicHistory[musicHistoryAt] = item; return; }
            int adjacent = musicHistoryAt + s.CoverDirection;
            if (adjacent >= 0 && adjacent < musicHistory.Count && musicHistory[adjacent].Key == item.Key)
            {
                musicHistoryAt = adjacent; musicHistory[musicHistoryAt] = item; return;
            }
            if (s.CoverDirection < 0)
            {
                musicHistory.RemoveRange(0, musicHistoryAt);
                musicHistory.Insert(0, item); musicHistoryAt = 0;
                if (musicHistory.Count > 8) musicHistory.RemoveAt(8);
            }
            else
            {
                musicHistory.RemoveRange(musicHistoryAt + 1, musicHistory.Count - musicHistoryAt - 1);
                musicHistory.Add(item);
                if (musicHistory.Count > 8) musicHistory.RemoveAt(0);
                musicHistoryAt = musicHistory.Count - 1;
            }
        }

        static readonly Dictionary<int, MusicTrackCardData> demoCardData = new Dictionary<int, MusicTrackCardData>();
        static MusicTrackCardData NeighborTrack(Snap s, int direction)
        {
            if (!s.HasMedia) return null;
            if (DemoMode)
            {
                int i = (demoMusicIndex + direction + DemoTrackTitles.Length) % DemoTrackTitles.Length;
                MusicTrackCardData demo;
                if (!demoCardData.TryGetValue(i, out demo))
                    demoCardData[i] = demo = new MusicTrackCardData("demo:" + i, null, DemoTrackTitles[i], s.Artist, DemoMusicAlbum(i), DemoMusicCover(i));
                return demo;
            }
            int next = musicHistoryAt + direction;
            return musicHistory.Count > 0 && musicHistory[musicHistoryAt].Key == s.MusicTrack && next >= 0 && next < musicHistory.Count ? musicHistory[next] : null;
        }

        sealed class MusicCarousel : Grid
        {
            const double CardWidth = 196, CardHeight = 210, SideOffset = CardWidth + 22;
            TrackCard left = new TrackCard(), center = new TrackCard(), right = new TrackCard();
            TrackCard outgoing = new TrackCard { Visibility = Visibility.Hidden };
            readonly Stopwatch slide = new Stopwatch();
            int direction, duration;
            MusicTrackCardData current;
            Action pending;

            public MusicCarousel()
            {
                Height = CardHeight + 12;
                ClipToBounds = true;
                Children.Add(left); Children.Add(right); Children.Add(outgoing); Children.Add(center);
                Unloaded += (s, e) => Stop();
            }

            public void Update(Snap s, string idleTitle, string idleHint)
            {
                string next = s.MusicTrack;
                string key = current == null ? null : current.Key;
                var previous = NeighborTrack(s, -1);
                var upcoming = NeighborTrack(s, 1);
                var incoming = current;
                if (!s.HasMedia) incoming = null;
                else if (key != next || current.Cover != s.Cover || current.Album != s.Album)
                    incoming = new MusicTrackCardData(next, s.App, s.Title, s.Artist, s.Album, s.Cover);
                int travel = s.CoverDirection < 0 ? -1 : 1;
                bool canPrev = s.CanPrev, canNext = s.CanNext;
                if (slide.IsRunning && Motion && incoming != null)
                {
                    // побеждает последнее событие: если пользователь вернулся к показанному треку, старый отложенный переход на другой трек не должен остаться
                    pending = () => ShowTracks(previous, incoming, upcoming, idleTitle, idleHint, canPrev, canNext, travel);
                    return;
                }
                if (slide.IsRunning) Stop();
                ShowTracks(previous, incoming, upcoming, idleTitle, idleHint, canPrev, canNext, travel);
            }

            void ShowTracks(MusicTrackCardData previous, MusicTrackCardData incoming, MusicTrackCardData upcoming,
                string idleTitle, string idleHint, bool canPrev, bool canNext, int travel)
            {
                bool animate = Motion && IsLoaded && current != null && incoming != null && current.Key != incoming.Key;
                if (animate)
                {
                    // Сами карточки меняются местами. Уже видимая обложка продолжает движение со своей позиции.
                    var spare = outgoing;
                    if (travel > 0) { outgoing = left; left = center; center = right; right = spare; }
                    else { outgoing = right; right = center; center = left; left = spare; }
                    outgoing.SetAction(0, false);
                    direction = travel;
                    duration = ExpressiveMotion ? 550 : 385;
                }
                left.Show(previous, "Предыдущий трек", "Нет данных о треке", -1, canPrev, incoming != null);
                right.Show(upcoming, "Следующий трек", "Очередь недоступна", 1, canNext, incoming != null);
                center.Show(incoming, idleTitle, idleHint, 0, false, true);
                current = incoming;
                PlaceCards(animate ? direction : 0);
                if (!animate) return;
                slide.Restart();
                CompositionTarget.Rendering += Frame;
            }

            void PlaceCards(double offset)
            {
                left.Place(-1 + offset);
                center.Place(offset);
                right.Place(1 + offset);
                if (outgoing.Visibility == Visibility.Visible) outgoing.Place(-2 * direction + offset);
            }

            void Frame(object sender, EventArgs args)
            {
                double t = Math.Min(1, slide.Elapsed.TotalMilliseconds / duration);
                if (t < 1 && Motion)
                {
                    double eased = t * t * t * (10 + t * (6 * t - 15));
                    PlaceCards(direction * (1 - eased));
                    return;
                }
                var next = pending;
                Stop();
                PlaceCards(0);
                if (next != null) next();
            }

            void Stop()
            {
                CompositionTarget.Rendering -= Frame;
                slide.Reset();
                pending = null;
                outgoing.Visibility = Visibility.Hidden;
            }

            sealed class TrackCard : Border
            {
                readonly MusicArt cover = new MusicArt(196, 15, true);
                readonly TextBlock title = Txt("", 15, true), artist = Txt("", 12, false), album = Txt("", 11, false);
                readonly StackPanel labels;
                readonly Border shade;
                readonly ScaleTransform labelScale = new ScaleTransform(1, 1);
                readonly TranslateTransform labelShift = new TranslateTransform();
                readonly TranslateTransform position = new TranslateTransform();
                readonly Image ribbon = new Image { Width = CardWidth, Height = CardWidth, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
                ImageSource artwork, ribbonArtwork;
                int ribbonSide;
                readonly Press button;
                int action;
                bool canClick;

                public TrackCard()
                {
                    Width = CardWidth; Height = CardHeight;
                    HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Center;
                    CornerRadius = new CornerRadius(15);
                    RenderTransform = position;
                    var stack = new Grid();
                    cover.VerticalAlignment = VerticalAlignment.Top; stack.Children.Add(cover);
                    stack.Children.Add(ribbon);
                    var shades = new LinearGradientBrush(); shades.StartPoint = new Point(0.5, 0); shades.EndPoint = new Point(0.5, 1);
                    shades.GradientStops.Add(new GradientStop(Colors.Transparent, 0.33));
                    shades.GradientStops.Add(new GradientStop(Color.FromArgb(155, 12, 13, 17), 0.64));
                    shades.GradientStops.Add(new GradientStop(Color.FromRgb(12, 13, 17), 1));
                    shade = new Border { Background = shades, CornerRadius = CornerRadius };
                    stack.Children.Add(shade);
                    title.Foreground = Brushes.White; artist.Foreground = Frozen(Colors.White, 0xE5); album.Foreground = Frozen(Colors.White, 0xBE);
                    title.Margin = new Thickness(0, 0, 0, 3); album.Margin = new Thickness(0, 4, 0, 0);
                    labels = VS(title, artist, album);
                    labels.VerticalAlignment = VerticalAlignment.Bottom; labels.Margin = new Thickness(12, 0, 12, 13);
                    labels.RenderTransformOrigin = new Point(0, 1);
                    labels.RenderTransform = new TransformGroup { Children = { labelScale, labelShift } };
                    stack.Children.Add(labels);
                    button = Act(stack, null, () => { if (canClick) MediaCmd(action < 0 ? "prev" : "next"); });
                    button.CornerRadius = CornerRadius; Child = button;
                }

                public void Show(MusicTrackCardData data, string emptyTitle, string hint, int side, bool clickable, bool visible)
                {
                    Visibility = visible ? Visibility.Visible : Visibility.Hidden;
                    title.Text = data == null ? emptyTitle : data.Title;
                    artist.Text = data == null ? side == 0 ? hint : "" : data.Artist;
                    album.Text = data == null ? "" : string.IsNullOrWhiteSpace(data.Album) ? "Альбом не указан" : data.Album;
                    artwork = data == null ? null : data.Cover;
                    cover.SetTrack(data == null ? null : data.Key, artwork, 1, false);
                    SetAction(side, clickable);
                    ToolTip = side == 0 ? null : data == null ? emptyTitle + " · " + hint : data.Title + " · " + album.Text;
                }

                public void SetAction(int side, bool clickable)
                {
                    action = side;
                    canClick = side != 0 && clickable;
                    button.Focusable = canClick;
                    button.Cursor = canClick ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
                    System.Windows.Automation.AutomationProperties.SetName(button, side < 0 ? "Предыдущий трек" : side > 0 ? "Следующий трек" : "");
                }

                void SetRibbon(int edge)
                {
                    if (ribbonSide == edge && ribbonArtwork == artwork) return;
                    ribbon.Source = artwork == null ? null : StretchCover(artwork, edge);
                    ribbonSide = edge; ribbonArtwork = artwork;
                }

                public void Place(double slot)
                {
                    position.X = SideOffset * slot;
                    double edge = Math.Min(1, Math.Abs(slot));
                    double blur = edge * edge * (3 - 2 * edge);
                    if (blur > 0) SetRibbon(Math.Sign(slot));
                    ribbon.Opacity = artwork == null ? 0 : blur;
                    cover.Opacity = 1 - ribbon.Opacity;
                    Opacity = 1 - 0.26 * blur;
                    shade.Opacity = 1 - 0.32 * blur;
                    // Меняем только визуальный масштаб, чтобы не пересчитывать окно на каждом кадре.
                    labelScale.ScaleX = labelScale.ScaleY = 1 - 0.2 * blur;
                    labelShift.X = slot < 0 ? 78 * blur : 0;
                }
            }

            static readonly ConditionalWeakTable<ImageSource, ImageSource[]> ribbons = new ConditionalWeakTable<ImageSource, ImageSource[]>();
            static ImageSource StretchCover(ImageSource artwork, int side)
            {
                var pair = ribbons.GetValue(artwork, key => new ImageSource[2]);
                int index = side < 0 ? 0 : 1;
                return pair[index] ?? (pair[index] = RenderRibbon(artwork, side));
            }

            static ImageSource RenderRibbon(ImageSource artwork, int side)
            {
                const int size = 160, stride = size * 4;
                var art = new DrawingVisual();
                using (var drawing = art.RenderOpen())
                    drawing.DrawRectangle(new ImageBrush(artwork) { Stretch = Stretch.UniformToFill }, null, new Rect(0, 0, size, size));
                var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(art);
                var source = new byte[size * stride];
                var output = new byte[source.Length];
                bitmap.CopyPixels(source, stride, 0);
                var offsets = new int[11];
                for (int x = 0; x < size; x++)
                {
                    double u = x / (size - 1.0);
                    double distance = side > 0 ? u : 1 - u;
                    double height = 0.75 + 0.85 * Math.Pow(distance, 1.5);
                    double sample = 0.5 + (u - 0.5) * (1 - 0.66 * distance);
                    double blur = 1 + 42 * Math.Pow(distance, 0.95);
                    for (int i = 0; i < offsets.Length; i++)
                        offsets[i] = 4 * Math.Max(0, Math.Min(size - 1, (int)Math.Round(sample * (size - 1) + (i - 5) * blur / 5)));
                    for (int y = 0; y < size; y++)
                    {
                        double v = 0.5 + (y / (size - 1.0) - 0.5) / height;
                        double coverage = Math.Max(0, Math.Min(1, 0.5 + Math.Min(v, 1 - v) * size * height));
                        if (coverage == 0) continue;
                        int row = (int)Math.Round(Math.Max(0, Math.Min(1, v)) * (size - 1)) * stride;
                        int dst = y * stride + x * 4;
                        for (int channel = 0; channel < 4; channel++)
                        {
                            int sum = 0;
                            for (int i = 0; i < offsets.Length; i++) sum += source[row + offsets[i] + channel] * (6 - Math.Abs(i - 5));
                            output[dst + channel] = (byte)(sum * coverage / 36);
                        }
                    }
                }
                var result = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, output, stride);
                result.Freeze();
                return result;
            }
        }

        sealed class MusicArt : FrameworkElement
        {
            readonly bool expanded;
            readonly double radius;
            readonly Stopwatch clock = new Stopwatch();
            Brush current, previous;
            ImageSource image;
            Color accent;
            string track;
            int direction, duration;
            double progress;
            static readonly Rect Unit = new Rect(0, 0, 1, 1);

            public MusicArt(double size, double cornerRadius, bool expanded = false)
            {
                this.expanded = expanded;
                radius = cornerRadius;
                Width = Height = size;
                Unloaded += (s, e) => Stop();
            }

            public void SetTrack(string key, ImageSource cover, int travel, bool transition = true)
            {
                var color = AccentColor;
                if (!Motion && previous != null) Stop();
                if (key == track && cover == image && (cover != null || accent == color)) return;
                bool animate = transition && Motion && IsLoaded && IsVisible && !string.IsNullOrEmpty(track) && !string.IsNullOrEmpty(key) && key != track;
                if (animate)
                {
                    // При быстром пропуске берём обложку, которая уже занимает большую часть перехода.
                    previous = previous != null && progress < 0.40 ? previous : current;
                    direction = travel < 0 ? -1 : 1;
                    duration = expanded ? (ExpressiveMotion ? 470 : 320) : (ExpressiveMotion ? 325 : 230);
                    progress = 0;
                    if (!clock.IsRunning) CompositionTarget.Rendering += Frame;
                    clock.Restart();
                }
                else if (key != track) Stop();
                current = cover == null ? Placeholder(color) : new ImageBrush(cover) { Stretch = Stretch.UniformToFill };
                if (current.CanFreeze) current.Freeze();
                track = key; image = cover; accent = color;
                InvalidateVisual();
            }

            static Brush Placeholder(Color color)
            {
                var art = new DrawingGroup();
                using (var dc = art.Open())
                {
                    dc.DrawRectangle(new LinearGradientBrush(Frozen(color, 0xA8).Color, Frozen(color, 0x28).Color, 45), null, Unit);
                    var mark = new FormattedText("\uE8D6", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(Icons, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 42, Brushes.White, 1);
                    dc.PushTransform(new ScaleTransform(0.01, 0.01));
                    dc.DrawText(mark, new Point((100 - mark.Width) / 2, (100 - mark.Height) / 2));
                    dc.Pop();
                }
                return new DrawingBrush(art);
            }

            void Frame(object sender, EventArgs e)
            {
                progress = Math.Min(1, clock.Elapsed.TotalMilliseconds / duration);
                if (progress >= 1 || !Motion || !IsVisible) Stop();
                InvalidateVisual();
            }

            void Stop()
            {
                if (clock.IsRunning) CompositionTarget.Rendering -= Frame;
                clock.Reset();
                previous = null;
                InvalidateVisual();
            }

            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);
                if (current == null || ActualWidth <= 0 || ActualHeight <= 0) return;
                var bounds = new Rect(RenderSize);
                dc.PushClip(new RectangleGeometry(bounds, radius, radius));
                if (previous == null) dc.DrawRectangle(current, null, bounds);
                else
                {
                    dc.PushTransform(new ScaleTransform(ActualWidth, ActualHeight));
                    double t = progress * progress * (3 - 2 * progress);
                    dc.DrawRectangle(previous, null, Unit);
                    dc.PushOpacity(t);
                    dc.DrawRectangle(current, null, Unit);
                    dc.Pop();
                    DrawRibbon(dc, previous, -direction, t, 1 - t);
                    DrawRibbon(dc, current, direction, 1 - t, Math.Min(1, t * 1.45));
                    dc.Pop();
                }
                dc.Pop();
            }

            void DrawRibbon(DrawingContext dc, Brush art, int side, double bend, double opacity)
            {
                if (opacity < 0.01) return;
                int strips = expanded ? 20 : 12;
                double stretch = bend * (expanded ? 0.92 : 0.62);
                double x0 = side * bend * 0.88 - (side < 0 ? stretch : 0);
                double width = 1 + stretch;
                var outline = new StreamGeometry();
                using (var path = outline.Open())
                {
                    path.BeginFigure(new Point(x0, RibbonY(side, bend, 0, false)), true, true);
                    for (int i = 1; i <= strips; i++)
                    {
                        double u = i / (double)strips;
                        path.LineTo(new Point(x0 + width * u, RibbonY(side, bend, u, false)), true, false);
                    }
                    for (int i = strips; i >= 0; i--)
                    {
                        double u = i / (double)strips;
                        path.LineTo(new Point(x0 + width * u, RibbonY(side, bend, u, true)), true, false);
                    }
                }
                // Расширяем вертикальные полосы к уходящей кромке, сохраняя узкую шейку у центра.
                dc.PushOpacity(opacity);
                dc.PushClip(outline);
                for (int i = 0; i < strips; i++)
                {
                    double u = (i + 0.5) / strips;
                    double x = x0 + width * i / strips;
                    if (x >= 1 || x + width / strips <= 0) continue;
                    double y = RibbonY(side, bend, u, false) - 0.04;
                    double height = RibbonY(side, bend, u, true) - y + 0.04;
                    dc.PushClip(new RectangleGeometry(new Rect(x, 0, width / strips + 0.008, 1)));
                    dc.DrawRectangle(art, null, new Rect(x0, y, width, height));
                    if (bend > 0.1)
                    {
                        double smear = bend * (expanded ? 0.14 : 0.09);
                        dc.PushOpacity(0.28);
                        dc.DrawRectangle(art, null, new Rect(x0 + side * smear, y, width + smear, height));
                        dc.Pop();
                    }
                    dc.Pop();
                }
                dc.Pop();
                dc.Pop();
            }

            static double RibbonY(int side, double bend, double u, bool bottom)
            {
                double edge = side < 0 ? 1 - u : u;
                double height = 1 - bend * 0.45 + bend * Math.Pow(edge, 2.7) * 1.45;
                return 0.5 + (bottom ? height : -height) / 2 + Math.Sin(Math.PI * u) * bend * 0.09 * side;
            }
        }
    }
}
