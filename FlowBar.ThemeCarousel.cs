using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using InputKey = System.Windows.Input.Key;

namespace FlowBar
{
    partial class Bar
    {
        sealed class ThemeCapsule : Press
        {
            public readonly string ThemeKey;
            public readonly Color Tint;
            readonly Border pill;
            readonly Image sculpture, bloom;
            readonly Canvas face;
            readonly Rectangle reflection;
            readonly Ellipse appliedDot;
            readonly ScaleTransform pressScale = new ScaleTransform(1, 1);
            readonly TranslateTransform slide = new TranslateTransform();
            readonly DispatcherTimer frames = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            readonly Stopwatch animationTime = new Stopwatch();
            readonly Func<bool> selected;
            readonly ThemeMotif motif;
            bool hovered, held, highlighted;
            ImageSource[] images;
            int currentFrame = -1;

            public ThemeCapsule(string key, string label, string subtitle, Color tint,
                Func<bool> selected, Action pick, Action<bool> preview, bool palette)
            {
                ThemeKey = key; Tint = tint; this.selected = selected;
                motif = ThemeArt(key, tint);
                Width = 210; Height = 270;
                Background = Brushes.Transparent;
                RenderTransform = slide;
                BorderBrush = Brushes.Transparent;
                BorderThickness = new Thickness(1);
                CornerRadius = new CornerRadius(18);
                Cursor = Cursors.Hand;
                ToolTip = label;
                Invoke = pick;
                Tag = "settings-theme:" + key;
                var content = new Canvas { IsHitTestVisible = false };
                face = new Canvas { Width = 208, Height = 182, RenderTransformOrigin = new Point(0.5, 0.58), RenderTransform = pressScale };

                pill = new Border
                {
                    Width = 65, Height = 108, CornerRadius = new CornerRadius(32.5), BorderThickness = new Thickness(1),
                    BorderBrush = Frozen(Colors.White, 0x1E),
                    Background = new LinearGradientBrush(new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(20, 20, 22), 0),
                        new GradientStop(Color.FromRgb(2, 2, 3), 0.33),
                        new GradientStop(Color.FromRgb((byte)(tint.R / 5), (byte)(tint.G / 6), (byte)(tint.B / 6)), 1)
                    }, 90)
                };
                AddAt(face, pill, 71.5, 49);
                bloom = new Image { Width = 216, Height = 216, Opacity = 0.43, Effect = new BlurEffect { Radius = 15 }, Visibility = Visibility.Hidden };
                sculpture = new Image { Width = 216, Height = 216, Visibility = Visibility.Hidden };
                RenderOptions.SetBitmapScalingMode(sculpture, BitmapScalingMode.HighQuality);
                AddAt(face, bloom, -4, -17);
                AddAt(face, sculpture, -4, -17);
                var icon = ThemeLineIcon(motif);
                AddAt(face, icon, 93, 86);
                var title = Txt(label, 9.2, true);
                title.Width = 59; title.TextAlignment = TextAlignment.Center;
                title.TextTrimming = TextTrimming.CharacterEllipsis;
                title.Foreground = Frozen(Colors.White, 0xF8);
                AddAt(face, title, 74.5, 119);
                if (subtitle != null)
                {
                    var description = Txt(subtitle, 6.3, false);
                    description.Width = 59; description.TextAlignment = TextAlignment.Center;
                    description.TextTrimming = TextTrimming.CharacterEllipsis;
                    description.Foreground = Frozen(Colors.White, 0xB8);
                    AddAt(face, description, 74.5, 132);
                }
                if (palette)
                {
                    var style = Styles[key];
                    int i = 0;
                    foreach (var color in new[] { style.Bg, style.Fg, style.Ac })
                        AddAt(face, new Ellipse { Width = 4, Height = 4, Fill = Frozen(color, 255) }, 97 + i++ * 6, 137);
                }
                appliedDot = new Ellipse { Width = 3, Height = 3, Fill = Frozen(Colors.White, 0xA0) };
                AddAt(face, appliedDot, 102.5, 62);

                var mirrored = new VisualBrush(face) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 208, 182) };
                reflection = new Rectangle
                {
                    Width = 208, Height = 182, Fill = mirrored, Opacity = 0.18,
                    OpacityMask = new LinearGradientBrush(Colors.Transparent, Colors.White, 90),
                    RenderTransform = new MatrixTransform(1, 0, 0, -0.56, 0, 102),
                    Effect = new BlurEffect { Radius = 4 }
                };
                AddAt(content, reflection, 0, 160);
                AddAt(content, face, 0, 0);
                Child = content;
                frames.Tick += (s, e) => { if (Motion && IsVisible) SetFrame(); else frames.Stop(); };
                MouseEnter += (s, e) => { hovered = true; Paint(true); preview(true); };
                MouseLeave += (s, e) => { hovered = held = false; Paint(true); preview(false); };
                MouseLeftButtonDown += (s, e) => { held = true; Paint(true); };
                MouseLeftButtonUp += (s, e) => { held = false; Paint(true); e.Handled = true; pick(); };
                IsVisibleChanged += (s, e) => Paint(false);
                IsKeyboardFocusedChanged += (s, e) => Paint(true);
                Unloaded += (s, e) => frames.Stop();
                System.Windows.Automation.AutomationProperties.SetName(this, label + (subtitle == null ? "" : ", " + subtitle));
            }

            static Lazy<Task<Dictionary<ThemeMotif, ImageSource[]>>> artwork = NewArtwork();
            static Lazy<Task<Dictionary<ThemeMotif, ImageSource[]>>> NewArtwork()
            {
                return new Lazy<Task<Dictionary<ThemeMotif, ImageSource[]>>>(() => Task.Run(() =>
                {
                    var result = new Dictionary<ThemeMotif, ImageSource[]>();
                    foreach (ThemeMotif kind in Enum.GetValues(typeof(ThemeMotif))) result.Add(kind, LoadImages(kind));
                    return result;
                }));
            }
            // режим экономии памяти: кадры (~21 МБ) не держатся после закрытия настроек, при следующем открытии загрузятся снова
            public static void ReleaseArtwork() { artwork = NewArtwork(); }

            public static Task WarmArtwork() { return artwork.Value; }

            static ImageSource[] LoadImages(ThemeMotif kind)
            {
                string prefix = "FlowBar.Elemental." + kind.ToString().ToLowerInvariant() + "-";
                var assembly = typeof(ThemeCapsule).Assembly;
                return assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
                    .OrderBy(n => n, StringComparer.Ordinal).Select(n =>
                    {
                        using (var input = assembly.GetManifestResourceStream(n))
                        {
                            var bitmap = BitmapFrame.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                            bitmap.Freeze();
                            return (ImageSource)bitmap;
                        }
                    }).ToArray();
            }

            void SetFrame()
            {
                if (images == null || images.Length == 0) return;
                int index = (int)((animationTime.ElapsedMilliseconds / 50) % images.Length);
                if (index == currentFrame) return;
                currentFrame = index;
                sculpture.Source = bloom.Source = images[index];
            }

            public void Paint(bool animate)
            {
                bool show = selected() && IsVisible;
                if (show && images == null && artwork.Value.Status == TaskStatus.RanToCompletion)
                    images = artwork.Value.Result[motif];
                bool art = show && images != null && images.Length > 0;
                if (art && !highlighted)
                {
                    animationTime.Restart();
                    currentFrame = -1;
                    SetFrame();
                    if (animate && Motion)
                    {
                        sculpture.BeginAnimation(OpacityProperty, SettingsMotion(0, 1, 290));
                        bloom.BeginAnimation(OpacityProperty, SettingsMotion(0, 0.43, 360));
                    }
                }
                else if (!art && highlighted)
                {
                    sculpture.BeginAnimation(OpacityProperty, null);
                    bloom.BeginAnimation(OpacityProperty, null);
                }
                highlighted = art;
                sculpture.Visibility = bloom.Visibility = art ? Visibility.Visible : Visibility.Hidden;
                if (art && Motion) frames.Start(); else frames.Stop();
                reflection.Opacity = art ? 0.48 : 0.14;
                bool applied = C("style", "Black") == ThemeKey;
                appliedDot.Visibility = applied ? Visibility.Visible : Visibility.Hidden;
                System.Windows.Automation.AutomationProperties.SetItemStatus(this, applied ? "Применена" : "");
                pill.BorderBrush = Frozen(Colors.White, hovered || IsKeyboardFocused ? (byte)0x60 : (byte)0x1E);
                double scale = Motion && held ? 0.95 : 1;
                double from = pressScale.ScaleX;
                pressScale.ScaleX = pressScale.ScaleY = scale;
                pressScale.BeginAnimation(ScaleTransform.ScaleXProperty, animate && Motion ? SettingsMotion(from, scale, 110) : null);
                pressScale.BeginAnimation(ScaleTransform.ScaleYProperty, animate && Motion ? SettingsMotion(from, scale, 110) : null);
            }

            public void SlideTo(double target, bool animate, double width, double spacing)
            {
                if (animate && Math.Abs((double)slide.GetAnimationBaseValue(TranslateTransform.XProperty) - target) < 0.5) return;
                double from = slide.X;
                slide.BeginAnimation(TranslateTransform.XProperty, null);
                slide.X = target;
                if (!animate || !Motion || Math.Abs(from - target) < 0.5) return;
                // Jump only to the entry side of the viewport when a cyclic item wraps.
                if (Math.Abs(from - target) > spacing * 1.65)
                {
                    bool visible = from < width && from + Width > 0;
                    if (!visible) from = target < width / 2 ? -Width : width;
                    else if (target <= -Width || target >= width)
                        return;
                }
                slide.BeginAnimation(TranslateTransform.XProperty, SettingsMotion(from, target, 350));
            }
        }

        static void AddAt(Canvas panel, UIElement item, double x, double y)
        {
            Canvas.SetLeft(item, x); Canvas.SetTop(item, y); panel.Children.Add(item);
        }

        sealed class ThemeCarousel : Canvas
        {
            readonly List<ThemeCapsule> cards;
            readonly Func<string> selected;
            readonly Rectangle floor;
            readonly Ellipse glow;
            int center, centered = -1;

            public ThemeCarousel(List<Press> buttons, Func<string> selected)
            {
                this.selected = selected;
                cards = buttons.Cast<ThemeCapsule>().ToList();
                Height = 290; ClipToBounds = true;
                Background = Frozen(Color.FromRgb(8, 7, 9), 255);
                glow = new Ellipse { Height = 282, IsHitTestVisible = false };
                floor = new Rectangle { Height = 16, IsHitTestVisible = false };
                AddAt(this, glow, 0, 1);
                AddAt(this, floor, 0, 198);
                foreach (var card in cards) AddAt(this, card, 0, 7);
                SizeChanged += (s, e) => Refresh(false);
                IsVisibleChanged += (s, e) => { if (IsVisible) Refresh(false); };
                var loading = ThemeCapsule.WarmArtwork();
                if (!loading.IsCompleted)
                    loading.ContinueWith(task =>
                    {
                        if (task.IsFaulted) { Log(task.Exception); return; }
                        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                        {
                            if (IsLoaded && IsVisible) foreach (var card in cards) card.Paint(true);
                        }));
                    }, TaskScheduler.Default);
                PreviewKeyDown += (s, e) =>
                {
                    if (e.Key != InputKey.Left && e.Key != InputKey.Right) return;
                    int next = (center + (e.Key == InputKey.Right ? 1 : cards.Count - 1)) % cards.Count;
                    cards[next].Focus(); cards[next].Invoke();
                    e.Handled = true;
                };
            }

            public void Refresh(bool animate)
            {
                if (ActualWidth < 1 || cards.Count == 0) return;
                int wanted = cards.FindIndex(c => c.ThemeKey == selected());
                if (wanted >= 0) center = wanted;
                if (wanted != centered)
                {
                    centered = wanted;
                    var tint = wanted < 0 ? Colors.Black : cards[wanted].Tint;
                    glow.Fill = new RadialGradientBrush(Frozen(tint, 0x3C).Color, Frozen(tint, 0).Color);
                    floor.Fill = new RadialGradientBrush(Frozen(tint, 0x50).Color, Frozen(tint, 0).Color);
                }
                glow.Width = Math.Min(ActualWidth, 430);
                floor.Width = Math.Min(ActualWidth, 380);
                SetLeft(glow, (ActualWidth - glow.Width) / 2);
                SetLeft(floor, (ActualWidth - floor.Width) / 2);
                double spacing = Math.Max(93, Math.Min(206, ActualWidth * 0.29));
                for (int index = 0; index < cards.Count; index++)
                {
                    var card = cards[index];
                    int slot = index - center;
                    if (slot > cards.Count / 2) slot -= cards.Count;
                    if (slot < -cards.Count / 2) slot += cards.Count;
                    double target = (ActualWidth - card.Width) / 2 + slot * spacing;
                    card.SlideTo(target, animate && IsVisible, ActualWidth, spacing);
                    bool near = Math.Abs(slot) <= 1;
                    card.Opacity = near ? 1 : 0.28;
                    card.IsHitTestVisible = near || Math.Abs(slot) == 2;
                    card.Focusable = near;
                    SetZIndex(card, index == center ? 2 : 1);
                    card.Paint(animate);
                }
            }
        }

        static Border ThemeShelf(List<Press> cards, Func<string> selected, List<Action> refresh)
        {
            var carousel = new ThemeCarousel(cards, selected);
            refresh.Add(() => carousel.Refresh(true));
            return new Border
            {
                Background = carousel.Background, CornerRadius = new CornerRadius(14),
                ClipToBounds = true, Margin = new Thickness(0, 8, 0, 0), Child = carousel
            };
        }
    }
}
