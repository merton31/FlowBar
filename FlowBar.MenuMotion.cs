using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace FlowBar
{
    partial class Bar
    {
        // Квадраты повторяют одну траекторию в обе стороны. Окно эффекта пропускает ввод.
        public static class MenuMotion
        {
            public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
                "Enabled", typeof(bool), typeof(MenuMotion), new PropertyMetadata(false, Attach));
            static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
                "State", typeof(Surface), typeof(MenuMotion));
            static int menuGeneration;
            static readonly Brush[] WaveColors =
            {
                Brushes.DeepSkyBlue, Brushes.LimeGreen, Brushes.White, Brushes.Gray, Brushes.Black
            };
            static int nextWaveColor;

            public static void SetEnabled(DependencyObject element, bool value) { element.SetValue(EnabledProperty, value); }
            public static bool GetEnabled(DependencyObject element) { return (bool)element.GetValue(EnabledProperty); }

            static void Attach(DependencyObject element, DependencyPropertyChangedEventArgs e)
            {
                var item = element as MenuItem;
                var menu = element as ContextMenu;
                if (item == null && menu == null) return;
                var previous = (Surface)element.GetValue(StateProperty);
                if (previous != null) previous.Detach();
                if ((bool)e.NewValue) element.SetValue(StateProperty, new Surface(item, menu));
                else element.ClearValue(StateProperty);
            }

            sealed class Surface
            {
                readonly MenuItem item;
                readonly ContextMenu menu;
                FrameworkElement panel;
                Rect pixels;
                double dpi;
                Point source;
                Rect sourceBounds;
                bool opening;
                int generation;
                int depth;
                BitmapSource snapshot;
                Brush waveColor;

                public Surface(MenuItem item, ContextMenu menu)
                {
                    this.item = item; this.menu = menu;
                    if (item != null) { item.SubmenuOpened += OnOpened; item.SubmenuClosed += OnClosed; }
                    else { menu.Opened += OnOpened; menu.Closed += OnClosed; }
                }

                public void Detach()
                {
                    if (item != null) { item.SubmenuOpened -= OnOpened; item.SubmenuClosed -= OnClosed; }
                    else { menu.Opened -= OnOpened; menu.Closed -= OnClosed; }
                    Forget();
                }

                bool IsOpen { get { return item != null ? item.IsSubmenuOpen : menu.IsOpen; } }

                void OnOpened(object sender, RoutedEventArgs e)
                {
                    if (e.OriginalSource != sender) return;
                    Forget();
                    TileBurst.StopCurrent();
                    if (menu != null) menuGeneration++;
                    generation = menuGeneration;
                    depth = 0;
                    for (ItemsControl parent = item; parent is MenuItem; parent = ItemsControl.ItemsControlFromItemContainer(parent))
                        depth++;
                    if (!Motion) return;
                    Control control = item != null ? (Control)item : menu;
                    control.ApplyTemplate();
                    panel = control.Template.FindName("MotionSurface", control) as FrameworkElement;
                    if (panel == null) return;
                    opening = true;
                    panel.Opacity = 0;
                    panel.SizeChanged += Resized;
                    panel.LayoutUpdated += OnLayout;
                    panel.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(StartOpening));
                }

                void OnLayout(object sender, EventArgs e) { StartOpening(); }

                void StartOpening()
                {
                    if (!opening || panel == null) return;
                    Measure();
                    if (pixels.IsEmpty) return;
                    opening = false;
                    panel.LayoutUpdated -= OnLayout;
                    if (!Motion || !TileBurst.Show(pixels, sourceBounds, dpi, ExpressiveMotion,
                        WaveColors[nextWaveColor], panel, null, RememberSurface))
                        panel.Opacity = 1;
                    else
                    {
                        waveColor = WaveColors[nextWaveColor];
                        nextWaveColor = (nextWaveColor + 1) % WaveColors.Length;
                    }
                }

                void RememberSurface()
                {
                    if (!IsOpen || panel == null || PresentationSource.FromVisual(panel) == null) return;
                    var scale = VisualTreeHelper.GetDpi(panel);
                    int width = (int)Math.Ceiling(panel.ActualWidth * scale.DpiScaleX);
                    int height = (int)Math.Ceiling(panel.ActualHeight * scale.DpiScaleY);
                    if (width <= 0 || height <= 0 || width > 1800 || height > 1600) return;
                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen())
                        dc.DrawRectangle(new VisualBrush(panel) { Stretch = Stretch.None }, null, new Rect(panel.RenderSize));
                    var image = new RenderTargetBitmap(width, height, scale.PixelsPerInchX, scale.PixelsPerInchY, PixelFormats.Pbgra32);
                    image.Render(visual);
                    image.Freeze();
                    snapshot = image;
                }

                void Resized(object sender, SizeChangedEventArgs e) { Measure(); }

                void Measure()
                {
                    if (!IsOpen || panel == null || PresentationSource.FromVisual(panel) == null ||
                        panel.ActualWidth < 12 || panel.ActualHeight < 12) return;
                    var origin = panel.PointToScreen(new Point(0, 0));
                    var end = panel.PointToScreen(new Point(panel.ActualWidth, panel.ActualHeight));
                    pixels = new Rect(origin, end);
                    dpi = VisualTreeHelper.GetDpi(panel).DpiScaleX;
                    var trigger = item != null ? (UIElement)item : menu.PlacementTarget;
                    if (trigger != null && PresentationSource.FromVisual(trigger) != null)
                    {
                        var bounds = new Rect(trigger.PointToScreen(new Point()), trigger.PointToScreen(
                            new Point(trigger.RenderSize.Width, trigger.RenderSize.Height)));
                        var center = new Point(pixels.Left + pixels.Width / 2, pixels.Top + pixels.Height / 2);
                        source = new Point(Math.Max(bounds.Left, Math.Min(bounds.Right, center.X)),
                            Math.Max(bounds.Top, Math.Min(bounds.Bottom, center.Y)));
                        sourceBounds = item != null
                            ? new Rect(source.X, bounds.Top, 0, bounds.Height)
                            : new Rect(bounds.Left, source.Y, bounds.Width, 0);
                    }
                    else
                    {
                        var cursor = Forms.Cursor.Position;
                        source = new Point(cursor.X, cursor.Y);
                        sourceBounds = new Rect(source, source);
                    }
                }

                void OnClosed(object sender, RoutedEventArgs e)
                {
                    if (e.OriginalSource != sender) return;
                    var bounds = pixels;
                    var surface = panel;
                    var image = snapshot;
                    var color = waveColor;
                    Forget();
                    if (!Motion || color == null || generation != menuGeneration || bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 ||
                        Application.Current.Dispatcher.HasShutdownStarted) return;
                    TileBurst.Show(bounds, sourceBounds, dpi, ExpressiveMotion, color, null, surface, null, image, depth);
                }

                void Forget()
                {
                    if (panel != null)
                    {
                        panel.SizeChanged -= Resized;
                        panel.LayoutUpdated -= OnLayout;
                        panel.Opacity = 1;
                    }
                    panel = null;
                    snapshot = null;
                    waveColor = null;
                    opening = false;
                    pixels = Rect.Empty;
                }
            }

            // Замер для самопроверки: сколько миллисекунд занимает один кадр полной волны у большого меню (программная отрисовка — это верхняя оценка)
            internal static double BenchFrameMs(bool full)
            {
                var burst = new TileBurst(new Rect(10, 10, 300, 520), new Rect(10, 10, 300, 0), 1.25, full, Brushes.White, null, 0.5, null, null);
                burst.Width = 340; burst.Height = 560; burst.Measure(new Size(340, 560)); burst.Arrange(new Rect(0, 0, 340, 560));
                var bmp = new RenderTargetBitmap(425, 700, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(burst); // прогрев
                var sw = Stopwatch.StartNew(); const int frames = 20;
                for (int i = 0; i < frames; i++) { burst.InvalidateVisual(); bmp.Render(burst); }
                burst.window.Close();
                return sw.Elapsed.TotalMilliseconds / frames;
            }

            sealed class TileBurst : FrameworkElement
            {
                struct Tile
                {
                    public Point Origin, Entry;
                    public Vector Drift;
                    public double Size, Delay, Lane, Depth, Grain;
                }

                static TileBurst current;
                internal readonly Window window;
                readonly Tile[] tiles;
                readonly Stopwatch watch = new Stopwatch();
                TimeSpan lastRender;
                readonly bool full;
                readonly double duration;
                readonly Point source;
                readonly Rect sheet;
                readonly BitmapSource snapshot;
                readonly Action remember;
                readonly LinearGradientBrush mask;
                readonly Brush waveColor;
                readonly Brush[] levels = new Brush[17]; // кисти с прозрачностью i/16: квадрат — одна команда рисования вместо трёх (Push, Draw, Pop)
                readonly double pixelScale;
                int closingDepth;
                readonly FrameworkElement reveal;
                double start, end;
                double Phase { get { return start + (end - start) * Math.Min(1, watch.Elapsed.TotalSeconds / duration); } }

                internal TileBurst(Rect sheet, Rect edge, double pixelScale, bool full, Brush waveColor, FrameworkElement reveal, double start, Action remember, BitmapSource snapshot)
                {
                    this.full = full;
                    this.sheet = sheet;
                    this.snapshot = snapshot;
                    this.waveColor = waveColor;
                    var baseColor = waveColor is SolidColorBrush ? ((SolidColorBrush)waveColor).Color : Colors.White;
                    for (int i = 1; i < levels.Length; i++) { var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255.0 * i / 16), baseColor.R, baseColor.G, baseColor.B)); b.Freeze(); levels[i] = b; }
                    this.pixelScale = pixelScale;
                    this.remember = remember;
                    source = new Point(edge.X + edge.Width / 2, edge.Y + edge.Height / 2);
                    this.reveal = reveal;
                    this.start = start;
                    end = reveal == null ? 0 : 1;
                    duration = full ? 0.44 : 0.32; // полное раскрытие 0,44 с, частичное и закрытие 0,32 с (было 0,58 и 0,42)
                    bool sideways = edge.Height > 0;
                    bool reverse = sideways ? source.X > sheet.Left + sheet.Width / 2 : source.Y > sheet.Top + sheet.Height / 2;
                    var near = sideways ? new Point(reverse ? 1 : 0, 0.5) : new Point(0.5, reverse ? 1 : 0);
                    var far = sideways ? new Point(reverse ? 0 : 1, 0.5) : new Point(0.5, reverse ? 0 : 1);
                    mask = new LinearGradientBrush(Colors.White, Colors.Transparent, near, far);
                    var random = new Random(71);
                    // Мелкая сетка сохраняет плотный фронт, а лимит ограничивает работу на длинных меню.
                    int budget = (full ? 3600 : 2400) / (Environment.ProcessorCount <= 2 ? 2 : 1); // на слабом процессоре (1–2 потока) квадратов вдвое меньше
                    double step = Math.Max(2.4, Math.Sqrt(sheet.Width * sheet.Height / budget));
                    int cols = Math.Max(1, Math.Min(budget, (int)Math.Round(sheet.Width / step)));
                    int rows = Math.Max(1, Math.Min(budget / cols, (int)Math.Round(sheet.Height / step)));
                    double dx = sheet.Width / cols, dy = sheet.Height / rows;
                    tiles = new Tile[cols * rows];
                    for (int row = 0; row < rows; row++)
                    for (int col = 0; col < cols; col++)
                    {
                        var target = new Point(sheet.X + (col + 0.5 + (random.NextDouble() - 0.5) * 0.54) * dx,
                            sheet.Y + (row + 0.5 + (random.NextDouble() - 0.5) * 0.54) * dy);
                        double lane = sideways ? (row + 0.5) / rows : (col + 0.5) / cols;
                        double depth = sideways ? (col + 0.5) / cols : (row + 0.5) / rows;
                        if (reverse) depth = 1 - depth;
                        double center = Math.Sin(lane * Math.PI);
                        double cross = (lane - 0.5) * (full ? 34 : 25);
                        double forward = (6 + center * (full ? 30 : 23)) * (reverse ? -1 : 1);
                        tiles[row * cols + col] = new Tile
                        {
                            Origin = target,
                            Entry = new Point(edge.Left + edge.Width * lane, edge.Top + edge.Height * lane),
                            Lane = lane,
                            Depth = depth,
                            Grain = random.NextDouble(),
                            Drift = (sideways ? new Vector(forward, cross) : new Vector(cross, forward)) +
                                new Vector((random.NextDouble() - 0.5) * 14, (random.NextDouble() - 0.5) * 14),
                            Size = 1.15 + random.NextDouble() * (full ? 1.20 : 1.35),
                            Delay = depth * 0.26 + (1 - center) * 0.12 + random.NextDouble() * 0.065
                        };
                    }
                    IsHitTestVisible = false;
                    ClipToBounds = true;
                    window = new Window
                    {
                        Content = this, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                        AllowsTransparency = true, Background = Brushes.Transparent, ShowActivated = false,
                        ShowInTaskbar = false, Topmost = true
                    };
                    window.SourceInitialized += (s, e) =>
                    {
                        var h = new WindowInteropHelper(window).Handle;
                        const int transparent = 0x20, toolWindow = 0x80, noActivate = 0x08000000;
                        N.SetWindowLongPtr(h, -20, new IntPtr(N.GetWindowLongPtr(h, -20).ToInt64() | transparent | toolWindow | noActivate));
                    };
                    window.Closed += (s, e) =>
                    {
                        CompositionTarget.Rendering -= OnRendering;
                        if (reveal != null)
                        {
                            reveal.OpacityMask = null;
                            reveal.Opacity = 1;
                            if (remember != null) remember();
                        }
                        if (current == this) current = null;
                    };
                }

                public static bool Show(Rect sheet, Rect edge, double scale, bool full, Brush waveColor,
                    FrameworkElement reveal, FrameworkElement closing = null, Action remember = null, BitmapSource snapshot = null,
                    int closingDepth = 0)
                {
                    // Меню панели растёт от всей её ширины; окно эффекта по объединению с кромкой на широкой панели закрывало бы пол-экрана и
                    // каждый кадр перерисовывало миллионы пикселей. Берём только часть кромки вокруг меню.
                    if (edge.Height == 0 && edge.Width > sheet.Width + 120 * scale)
                    {
                        double l = Math.Max(edge.Left, sheet.Left - 60 * scale), r = Math.Min(edge.Right, sheet.Right + 60 * scale);
                        edge = new Rect(l, edge.Top, Math.Max(0, r - l), 0);
                    }
                    double from = reveal == null ? 1 : 0;
                    // При закрытии всей цепочки дочерние события не перекрывают волну родительского меню.
                    if (reveal == null && current != null && current.reveal == null &&
                        current.closingDepth < closingDepth && current.watch.ElapsedMilliseconds < 140) return false;
                    if (closing != null && current != null && current.reveal == closing) from = current.Phase;
                    StopCurrent();
                    if (reveal == null && from <= 0.015) return false;
                    var screen = Forms.Screen.FromPoint(new System.Drawing.Point((int)sheet.Left, (int)sheet.Top)).Bounds;
                    var area = new Rect(screen.Left, screen.Top, screen.Width, screen.Height);
                    var outer = sheet;
                    outer.Union(edge);
                    outer.Inflate(38 * scale, 26 * scale);
                    outer.Intersect(area);
                    if (outer.IsEmpty || outer.Width <= 0 || outer.Height <= 0) return false;
                    var burst = new TileBurst(new Rect((sheet.X - outer.X) / scale, (sheet.Y - outer.Y) / scale,
                        sheet.Width / scale, sheet.Height / scale), new Rect((edge.X - outer.X) / scale,
                        (edge.Y - outer.Y) / scale, edge.Width / scale, edge.Height / scale), scale, full, waveColor, reveal, from, remember, snapshot);
                    current = burst;
                    burst.closingDepth = closingDepth;
                    var host = burst.window;
                    host.Width = outer.Width / scale; host.Height = outer.Height / scale;
                    // Создаём окно сразу на нужном мониторе, затем закрепляем прямоугольник в физических пикселях.
                    host.Left = outer.X / scale; host.Top = outer.Y / scale;
                    var h = new WindowInteropHelper(host).EnsureHandle();
                    N.SetWindowPos(h, new IntPtr(-1), (int)outer.X, (int)outer.Y,
                        (int)Math.Ceiling(outer.Width), (int)Math.Ceiling(outer.Height), 0x10);
                    host.Show();
                    if (reveal != null)
                    {
                        burst.SetMask(0);
                        reveal.OpacityMask = burst.mask;
                        reveal.Opacity = 1;
                    }
                    burst.watch.Start();
                    CompositionTarget.Rendering += burst.OnRendering;
                    return true;
                }

                public static void StopCurrent()
                {
                    if (current != null) current.window.Close();
                }

                // кадр — по событию отрисовки WPF (60 в секунду): DispatcherTimer на 16 мс упирался в шаг системного таймера и давал около 40
                void OnRendering(object sender, EventArgs e)
                {
                    var rt = ((RenderingEventArgs)e).RenderingTime;
                    if (rt == lastRender) return; // WPF иногда зовёт событие дважды за один кадр
                    lastRender = rt;
                    Frame(sender, e);
                }

                void Frame(object sender, EventArgs e)
                {
                    if (!Motion || watch.Elapsed.TotalSeconds >= duration) window.Close();
                    else
                    {
                        if (reveal != null) SetMask(Phase);
                        InvalidateVisual();
                    }
                }

                static double Smooth(double value)
                {
                    double t = Math.Max(0, Math.Min(1, value));
                    return t * t * (3 - 2 * t);
                }

                void SetMask(double phase)
                {
                    double front = Front(phase);
                    mask.GradientStops[0].Offset = front - 0.20;
                    mask.GradientStops[1].Offset = front;
                }

                static double Front(double phase) { return Smooth((phase - 0.23) / 0.77) * 1.28 - 0.06; }

                protected override void OnRender(DrawingContext dc)
                {
                    double phase = Phase;
                    double front = Front(phase);
                    if (snapshot != null)
                    {
                        SetMask(phase);
                        dc.PushOpacityMask(mask.CloneCurrentValue());
                        dc.DrawImage(snapshot, sheet);
                        dc.Pop();
                    }
                    foreach (var tile in tiles)
                    {
                        double t = Smooth((phase - tile.Delay) / (1 - tile.Delay));
                        double opacity = Smooth(t * 8);
                        if (reveal != null || snapshot != null)
                        {
                            // Плотная кромка впереди и отдельные квадраты там, где уже проявляются строки.
                            double behind = front - tile.Depth + 0.08 * Math.Sin(Math.PI * tile.Lane);
                            opacity *= 1 - Smooth((behind - tile.Grain * 0.25) / 0.25);
                            opacity *= 1 - Smooth((phase - 0.88) / 0.12);
                        }
                        int level = (int)Math.Round(Math.Min(1, opacity) * 16);
                        if (level <= 0) continue;
                        double swell = Math.Sin(Math.PI * t) * (full ? 1 : 0.65);
                        double ripple = 0.84 + 0.16 * Math.Sin(tile.Lane * 12 - phase * 8);
                        var p = tile.Entry + (tile.Origin - tile.Entry) * t + tile.Drift * (swell * ripple);
                        double size = Math.Max(1, Math.Round(tile.Size * (0.62 + t * 0.38) * pixelScale)) / pixelScale;
                        double x = Math.Round((p.X - size / 2) * pixelScale) / pixelScale;
                        double y = Math.Round((p.Y - size / 2) * pixelScale) / pixelScale;
                        dc.DrawRectangle(levels[level], null, new Rect(x, y, size, size));
                    }
                }
            }
        }
    }
}
