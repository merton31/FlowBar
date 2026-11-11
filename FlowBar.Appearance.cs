using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace FlowBar
{
    partial class Bar
    {
        static readonly string[] MaterialKeys = { "theme", "frost", "metal", "prism" };
        static readonly string[] MaterialNames = { "По теме", "Матовое стекло", "Металл", "Призма" };
        static string Material { get { return C("material", "theme"); } }
        static bool BoardTheme { get { return C("style", "Black") == "Табло"; } }
        sealed class DotMeter : StackPanel
        {
            readonly Brush dim = Frozen(Cur.Fg, 0x28);
            public DotMeter(int count, double width)
            {
                Orientation = Orientation.Horizontal;
                double spacing = width / count;
                for (int i = 0; i < count; i++)
                    Children.Add(new Ellipse { Width = spacing - 2, Height = spacing - 2, Fill = dim, Margin = new Thickness(0, 1, 2, 1) });
            }
            public void Update(double value, Brush active)
            {
                int count = double.IsNaN(value) ? 0 : (int)Math.Round(value * Children.Count);
                for (int i = 0; i < Children.Count; i++) ((Ellipse)Children[i]).Fill = i < count ? active : dim;
            }
        }
        static byte EdgeAlpha { get { return (byte)Clamp(C("material.edge", ""), 100, 0, 100); } }
        static byte Edge(byte alpha) { return (byte)Math.Round(alpha * EdgeAlpha / 100.0); }
        static bool WantsGlass(Sty style)
        {
            return Material == "frost" || Material == "prism" || Material == "theme" && style.Blur != 0;
        }

        static double GlassRadius { get { return Clamp(C("material.blur", ""), -1, -1, 32); } }
        static float GlassBlurPixels(double scale) { return GlassRadius < 0 ? -1 : (float)(GlassRadius * scale); }

        static Brush MaterialFill(Sty style, bool glass)
        {
            if (Material == "metal")
            {
                var metal = new LinearGradientBrush();
                metal.StartPoint = new Point(0, 0); metal.EndPoint = new Point(0, 1);
                bool dark = style.LightText;
                metal.GradientStops.Add(new GradientStop(dark ? Color.FromRgb(63, 67, 72) : Color.FromRgb(235, 237, 239), 0));
                metal.GradientStops.Add(new GradientStop(dark ? Color.FromRgb(38, 40, 44) : Color.FromRgb(213, 216, 220), 0.54));
                metal.GradientStops.Add(new GradientStop(dark ? Color.FromRgb(48, 51, 56) : Color.FromRgb(227, 229, 232), 1));
                var grain = new DrawingBrush(new GeometryDrawing(Frozen(style.Fg, 0x0B), null, new RectangleGeometry(new Rect(0, 0, 2, 1))));
                grain.TileMode = TileMode.Tile; grain.Viewport = new Rect(0, 0, 2, 4); grain.ViewportUnits = BrushMappingMode.Absolute;
                var layers = new DrawingGroup();
                layers.Children.Add(new GeometryDrawing(metal, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
                layers.Children.Add(new GeometryDrawing(grain, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
                return new DrawingBrush(layers);
            }
            Color c = style.Bg;
            double strength = Clamp(C("material.diffusion", ""), 65, 0, 100) / 100;
            int alpha = Material == "frost" ? (int)(120 + strength * 100) : Material == "prism" ? (int)(40 + strength * 80) : c.A;
            if (WantsGlass(style) && !glass) alpha = 242;
            c = MaterialTint(c);
            byte a = (byte)Math.Round(alpha * Opac);
            if (WantsGlass(style) && !glass) a = (byte)Math.Max(224, (int)a);
            var bg = Color.FromArgb(a, c.R, c.G, c.B);
            if (!glass) return new SolidColorBrush(bg);
            var highlight = Color.FromArgb((byte)Math.Min(255, a + 20), (byte)Math.Min(255, c.R + 24), (byte)Math.Min(255, c.G + 24), (byte)Math.Min(255, c.B + 24));
            return new LinearGradientBrush(new GradientStopCollection { new GradientStop(highlight, 0), new GradientStop(bg, 0.55) }, 90);
        }

        static Color MaterialTint(Color color)
        {
            Color tint;
            if (!TryReadColor(C("material.tint", ""), out tint)) return color;
            return Color.FromRgb((byte)((color.R * 3 + tint.R) / 4), (byte)((color.G * 3 + tint.G) / 4), (byte)((color.B * 3 + tint.B) / 4));
        }

        static Brush MaterialEdge(Sty style, bool glass, bool animate)
        {
            if (Material == "prism" && glass)
            {
                var brush = new LinearGradientBrush();
                brush.GradientStops.Add(new GradientStop(Frozen(Color.FromRgb(153, 222, 255), Edge(145)).Color, 0));
                var beam = new GradientStop(Frozen(Color.FromRgb(250, 217, 255), Edge(202)).Color, 0.58);
                brush.GradientStops.Add(beam);
                brush.GradientStops.Add(new GradientStop(Frozen(Color.FromRgb(255, 220, 168), Edge(135)).Color, 1));
                if (animate && Motion)
                    beam.BeginAnimation(GradientStop.OffsetProperty, new DoubleAnimation(0.1, 0.58, TimeSpan.FromMilliseconds(440)) { EasingFunction = new CubicEase() });
                return brush;
            }
            if (glass)
            {
                var bottom = style == Styles["Liquid Glass"] ? Color.FromRgb(170, 231, 249) : Colors.White;
                return new LinearGradientBrush(Color.FromArgb(Edge(0x86), 255, 255, 255), Frozen(bottom, Edge(0x26)).Color, 90);
            }
            return Frozen(style.Fg, Edge(style.LightText ? (byte)0x24 : (byte)0x1C));
        }

        static bool TryReadColor(string value, out Color color)
        {
            color = Colors.Transparent;
            if (value.Length != 7 || value[0] != '#') return false;
            int rgb;
            if (!int.TryParse(value.Substring(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out rgb)) return false;
            color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            return true;
        }

        static bool Sections { get { return C("bar.sections", "0") == "1"; } }
        bool VerticalBar { get { return PositionKey == "left" || PositionKey == "right"; } }
        bool LeftBar { get { return PositionKey == "left"; } }
        static bool ExpressiveMotion { get { return Motion && C("motion", "soft") == "full"; } }
        bool UseSections { get { return Sections && !collapsed && !Flush && !VerticalBar; } }
        Canvas sectionSurface;
        bool sectionPrismPulse;
        readonly List<Backdrop> sectionBackdrops = new List<Backdrop>();
        string sectionKey;

        static int SectionFor(string token)
        {
            if (token.StartsWith("w:"))
                return new[] { "vol", "conn", "radios", "quick", "notif", "lang", "tray" }.Contains(token.Substring(2)) ? 2 : 0;
            return token == "clock" ? 2 : 1;
        }

        void ClearSectionBackdrops()
        {
            foreach (var backdrop in sectionBackdrops) backdrop.Dispose();
            sectionBackdrops.Clear();
        }

        void PaintSections(bool force = false)
        {
            if (sectionSurface == null || root == null || !root.IsLoaded) return;
            if (!UseSections)
            {
                if (sectionSurface.Children.Count != 0) sectionSurface.Children.Clear();
                if (sectionBackdrops.Count != 0) ClearSectionBackdrops();
                sectionKey = null;
                return;
            }
            var view = new Rect(stripScroll.TranslatePoint(new Point(0, 0), root), stripScroll.RenderSize);
            var bounds = new List<Rect>();
            foreach (var group in tokenOf.Where(kv => kv.Key.IsVisible && kv.Key.IsMeasureValid).GroupBy(kv => SectionFor(kv.Value)).OrderBy(g => g.Key))
            {
                var span = Rect.Empty;
                foreach (var item in group)
                {
                    var cell = new Rect(item.Key.TranslatePoint(new Point(0, 0), root), item.Key.RenderSize);
                    cell.Intersect(view);
                    if (!cell.IsEmpty) span.Union(cell);
                }
                if (!span.IsEmpty) bounds.Add(span);
            }
            var buttons = Rect.Empty;
            foreach (var item in rowPanel.Children.OfType<FrameworkElement>().Where(el => el.IsVisible && el != stripScroll))
                buttons.Union(new Rect(item.TranslatePoint(new Point(0, 0), root), item.RenderSize));
            if (overflowTile != null && overflowTile.IsVisible)
            {
                var overflow = new Rect(overflowTile.TranslatePoint(new Point(0, 0), root), overflowTile.RenderSize);
                overflow.Intersect(view);
                if (!overflow.IsEmpty) buttons.Union(overflow);
            }
            if (!buttons.IsEmpty)
            {
                if (bounds.Count > 0 && tokenOf.Any(kv => SectionFor(kv.Value) == 2)) bounds[bounds.Count - 1] = Rect.Union(bounds.Last(), buttons);
                else bounds.Add(buttons);
            }
            var rects = bounds.Where(r => r.Width > 4).Select(r =>
            {
                double left = Math.Max(0, Math.Round(r.Left - 3));
                double right = Math.Min(root.ActualWidth, Math.Round(r.Right + 3));
                return new Rect(left, 0, Math.Max(1, right - left), root.ActualHeight);
            }).ToList();
            var key = string.Join("|", rects.Select(r => r.ToString()));
            if (!force && !sectionPrismPulse && key == sectionKey) return;
            sectionKey = key;
            bool pulse = sectionPrismPulse; sectionPrismPulse = false;
            sectionSurface.Children.Clear();
            N.RECT windowRect = new N.RECT();
            if (hwnd != IntPtr.Zero) N.GetWindowRect(hwnd, out windowRect);
            bool glass = GlassOn;
            while (sectionBackdrops.Count > rects.Count)
            {
                sectionBackdrops.Last().Dispose();
                sectionBackdrops.RemoveAt(sectionBackdrops.Count - 1);
            }
            var origin = root.TranslatePoint(new Point(0, 0), this);
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                double radius = r.Height / 2;
                var shape = new Border
                {
                    Width = r.Width, Height = r.Height, CornerRadius = new CornerRadius(radius),
                    BorderThickness = new Thickness(1), Background = MaterialFill(Cur, glass), BorderBrush = MaterialEdge(Cur, glass, pulse)
                };
                Canvas.SetLeft(shape, r.X); Canvas.SetTop(shape, r.Y); sectionSurface.Children.Add(shape);
                if (!glass || hwnd == IntPtr.Zero) continue;
                if (i >= sectionBackdrops.Count)
                {
                    var bd = Backdrop.Create();
                    if (bd == null) { shape.Background = MaterialFill(Cur, false); continue; }
                    sectionBackdrops.Add(bd);
                }
                double sc = Scale;
                if (i >= sectionBackdrops.Count) continue;
                sectionBackdrops[i].Set(hwnd, windowRect.L + (int)Math.Round((origin.X + r.X) * sc), windowRect.T + (int)Math.Round((origin.Y + r.Y) * sc),
                    (int)Math.Round(r.Width * sc), (int)Math.Round(r.Height * sc), radius * sc, IsVisible, GlassBlurPixels(sc));
            }
            if (!glass && sectionBackdrops.Count > 0) ClearSectionBackdrops();
        }

        void PlaceSideCard(Window window, FrameworkElement anchor, double gap)
        {
            double sc = Scale;
            var bounds = scr.Bounds;
            double center = Top + (anchor == null ? root.ActualHeight / 2 : anchor.TranslatePoint(new Point(0, anchor.ActualHeight / 2), this).Y);
            double x = LeftBar ? Left + root.ActualWidth + SM + gap : Left + SM - window.ActualWidth - gap;
            double y = Math.Max(bounds.Top / sc, Math.Min(bounds.Bottom / sc - window.ActualHeight, center - window.ActualHeight / 2));
            PlaceWin(window, Math.Max(bounds.Left / sc, Math.Min(bounds.Right / sc - window.ActualWidth, x)), y);
        }
        static readonly string[] SceneNames = { "Светлый", "Тёмный", "Пёстрый" };
        static int SceneIndex { get { return (int)Clamp(C("preview.scene", ""), 2, 0, 2); } }
        static bool CompareThemes { get { return C("preview.compare", "0") == "1"; } }
        static readonly Dictionary<int, Brush> previewScenes = new Dictionary<int, Brush>();
        string candidateTheme;
        Bar settingsPreviewMonitor;

        void ChangeTheme(string theme)
        {
            if (!Styles.ContainsKey(theme)) return;
            candidateTheme = null;
            cfg["style"] = theme;
            SaveCfg(); ClosePanels(); RebuildAll(); RestyleWindows();
        }

        void PickTheme(string theme, Action<string> preview)
        {
            if (!CompareThemes) { ChangeTheme(theme); return; }
            candidateTheme = theme;
            preview(null);
        }

        static Brush PreviewScene(bool soft = false)
        {
            int index = SceneIndex;
            Brush brush;
            int radius = !soft ? 0 : GlassRadius < 0 ? 32 : (int)Math.Round(GlassRadius);
            int cacheKey = index * 33 + radius;
            if (previewScenes.TryGetValue(cacheKey, out brush)) return brush;
            var name = new[] { "light", "dark", "color" }[index];
            using (var stream = typeof(Bar).Assembly.GetManifestResourceStream("FlowBar.Preview." + name + ".jpg"))
            {
                if (stream == null) return Brushes.SlateGray;
                BitmapSource image = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                if (radius > 0)
                {
                    double downsample = 1.0 / (1 + radius * 0.86);
                    image = new TransformedBitmap(image, new ScaleTransform(downsample, downsample));
                }
                brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            }
            brush.Freeze();
            previewScenes[cacheKey] = brush;
            return brush;
        }

        void MaterialSettings(StackPanel c, Action again, Action<string> preview)
        {
            Action refresh = () => { SaveCfg(); All(b => b.ApplyStyle()); preview(null); };
            c.Children.Add(SSub("Материал панели"));
            c.Children.Add(Seg(MaterialNames, Math.Max(0, Array.IndexOf(MaterialKeys, Material)), i =>
            {
                cfg["material"] = MaterialKeys[i]; SaveCfg(); ClosePanels(); RebuildAll(); again();
            }));
            if (Material == "frost" || Material == "prism")
                c.Children.Add(SetRow("Плотность стекла", "Меняет непрозрачность материала.",
                    SliderRow(0, 100, Clamp(C("material.diffusion", ""), 65, 0, 100), 5, v => Math.Round(v) + "%",
                    v => { cfg["material.diffusion"] = Math.Round(v).ToString(); refresh(); }, "Плотность стекла")));
            if (WantsGlass(Cur))
            {
                var why = Backdrop.Why;
                if (why != null)
                {
                    c.Children.Add(SHint("Стекло сейчас не работает: " + why + ". Панель рисуется сплошной поверхностью и станет стеклянной сама, когда Windows разрешит прозрачность."));
                    var open = Btn("Параметры прозрачности Windows", () => Launch("ms-settings:colors")); open.HorizontalAlignment = HorizontalAlignment.Left; open.Margin = new Thickness(0, 6, 0, 4);
                    c.Children.Add(open);
                }
                c.Children.Add(SetRow("Размытие фона", "Свой радиус меняет резкость реального фона под панелью и карточками.",
                    Seg(new[] { "Системное", "Свой радиус" }, GlassRadius < 0 ? 0 : 1, i =>
                    {
                        if (i == 0) cfg.Remove("material.blur"); else cfg["material.blur"] = "12";
                        refresh(); again();
                    })));
                if (GlassRadius >= 0)
                    c.Children.Add(SetRow("Радиус размытия", "0 px оставляет фон резким.",
                        SliderRow(0, 32, GlassRadius, 1, v => Math.Round(v) + " px",
                            v => { cfg["material.blur"] = Math.Round(v).ToString(); refresh(); }, "Радиус размытия")));
            }
            c.Children.Add(SetRow("Кромка", null, SliderRow(0, 100, EdgeAlpha, 5, v => Math.Round(v) + "%",
                v => { cfg["material.edge"] = Math.Round(v).ToString(); refresh(); }, "Кромка")));
            if (Material != "metal")
            {
                var tints = new[] { "", "#8CC9F4", "#E2A570", "#A897D7" };
                c.Children.Add(SetRow("Оттенок", null, Seg(new[] { "По теме", "Холодный", "Тёплый", "Лиловый" },
                    Math.Max(0, Array.IndexOf(tints, C("material.tint", ""))),
                    i => { cfg["material.tint"] = tints[i]; refresh(); })));
            }
            c.Children.Add(CardActions(Btn("Сбросить материал", () =>
            {
                foreach (var k in new[] { "material", "material.diffusion", "material.blur", "material.edge", "material.tint", "opacity" }) cfg.Remove(k);
                SaveCfg(); ClosePanels(); RebuildAll(); again();
            })));

            c.Children.Add(SSub("Палитры"));
            var paletteRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var key in new[] { "Бумага", "Табло", "White", "Midnight" })
            {
                var st = Styles[key];
                var colors = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var color in new[] { st.Bg, st.Fg, st.Ac, st.LightText ? Color.FromRgb(255, 167, 121) : Color.FromRgb(169, 66, 30) })
                    colors.Children.Add(new Border { Width = 25, Height = 13, Background = Frozen(color, 255), CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 3, 0) });
                var chip = new Border { Background = Frozen(st.Bg, 255), Child = new TextBlock { Text = "◉  12:30", FontSize = 10, Foreground = Frozen(st.Fg, 255) },
                    Padding = new Thickness(4, 2, 4, 2), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 4, 0, 0) };
                var button = Act(VS(colors, chip, Mg(Txt(ThemeName(key), 11, false), 0, 4)), "Палитра " + ThemeName(key), () => PickTheme(key, preview));
                button.Margin = new Thickness(0, 0, 6, 6);
                paletteRow.Children.Add(button);
            }
            c.Children.Add(SHint("Слева направо: фон, текст, акцент, предупреждение."));
            c.Children.Add(paletteRow);

            c.Children.Add(SSub("Предпросмотр и движение"));
            c.Children.Add(SetRow("Обои предпросмотра", null, Seg(SceneNames, SceneIndex, i => { cfg["preview.scene"] = i.ToString(); SaveCfg(); preview(null); })));
            c.Children.Add(CheckRow("Сравнивать темы перед применением", "Нажмите на тему, затем подтвердите выбор в нижнем предпросмотре.", CompareThemes,
                v => { cfg["preview.compare"] = v ? "1" : "0"; candidateTheme = null; SaveCfg(); again(); }));
            var motions = new[] { "off", "soft", "full" };
            c.Children.Add(SetRow("Движение", null, Seg(new[] { "Без движения", "Мягкое", "Выразительное" },
                Math.Max(0, Array.IndexOf(motions, C("motion", "soft"))), i => { cfg["motion"] = motions[i]; SaveCfg(); })));
            int example = 0;
            var sampleLabel = Txt("Уведомление", 12, true);
            var sample = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Background = Frozen(AccentColor, 0x30), Child = sampleLabel, HorizontalAlignment = HorizontalAlignment.Left };
            var movement = new TranslateTransform(); sample.RenderTransform = movement;
            var examples = new[] { "Уведомление", "Карточка виджета", "Переключение темы" };
            c.Children.Add(SetRow("Эффект для проверки", null, Seg(examples, example, i => { example = i; sampleLabel.Text = examples[i]; }, true)));
            c.Children.Add(CardActions(Btn("Проиграть движение", () =>
            {
                if (!Motion) return;
                int ms = C("motion", "soft") == "full" ? 330 : 180;
                if (example == 2)
                {
                    var brush = new SolidColorBrush(Color.FromArgb(0x80, AccentColor.R, AccentColor.G, AccentColor.B));
                    sample.Background = brush;
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(brush.Color, Color.FromArgb(0x30, AccentColor.R, AccentColor.G, AccentColor.B), TimeSpan.FromMilliseconds(ms)));
                }
                else movement.BeginAnimation(TranslateTransform.YProperty, SettingsMotion(example == 1 ? 18 : 10, 0, ms));
                sample.BeginAnimation(OpacityProperty, SettingsMotion(0.35, 1, ms));
            }), sample));
            ScheduleSettings(c, again);
            AppearanceHistorySettings(c);
        }

        static string ScheduledTheme(DateTime when)
        {
            var mode = C("appearance.schedule", "off");
            if (mode == "system")
            {
                var value = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                return Equals(value, 0) ? C("appearance.night", "Midnight") : C("appearance.day", "Бумага");
            }
            if (mode != "hours") return null;
            int morning = (int)Clamp(C("appearance.morning", ""), 7, 0, 23);
            int evening = (int)Clamp(C("appearance.evening", ""), 19, 0, 23);
            bool day = morning <= evening ? when.Hour >= morning && when.Hour < evening : when.Hour >= morning || when.Hour < evening;
            return day ? C("appearance.day", "Бумага") : C("appearance.night", "Midnight");
        }

        static string scheduleLast;
        void SyncAppearanceSchedule()
        {
            if (DemoMode) return;
            var theme = ScheduledTheme(DateTime.Now);
            if (theme == scheduleLast) return;
            scheduleLast = theme;
            if (theme != null && theme != C("style", "Black") && Styles.ContainsKey(theme)) ChangeTheme(theme);
        }

        void ScheduleSettings(StackPanel c, Action again)
        {
            var modes = new[] { "off", "hours", "system" };
            var current = C("appearance.schedule", "off");
            c.Children.Add(SSub("Расписание оформления"));
            c.Children.Add(Seg(new[] { "Вручную", "По времени", "Как в Windows" },
                Math.Max(0, Array.IndexOf(modes, current)), i =>
                {
                    cfg["appearance.schedule"] = modes[i]; scheduleLast = null;
                    SaveCfg(); SyncAppearanceSchedule(); again();
                }));
            if (current == "off") return;
            var keys = Styles.Keys.ToArray();
            foreach (var mode in new[] { "day", "night" })
            {
                string selected = C("appearance." + mode, mode == "day" ? "Бумага" : "Midnight");
                var picker = SettingsChoice(keys.Select(ThemeName).ToArray(), Math.Max(0, Array.IndexOf(keys, selected)), index =>
                {
                    cfg["appearance." + mode] = keys[index]; scheduleLast = null; SaveCfg(); SyncAppearanceSchedule();
                });
                c.Children.Add(SetRow(mode == "day" ? "Дневная тема" : "Вечерняя тема", null, picker));
            }
            if (current != "hours") return;
            var hourTest = new TextBlock { Text = "", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            foreach (var time in new[] { "morning", "evening" })
            {
                int def = time == "morning" ? 7 : 19;
                c.Children.Add(SetRow(time == "morning" ? "Начало дня" : "Начало вечера", null,
                    SliderRow(0, 23, Clamp(C("appearance." + time, ""), def, 0, 23), 1, v => Math.Round(v).ToString("00") + ":00",
                    v => { cfg["appearance." + time] = Math.Round(v).ToString(); SaveCfg(); }, time == "morning" ? "Начало дня" : "Начало вечера")));
            }
            c.Children.Add(SetRow("Примерить выбранный час", null, SliderRow(0, 23, DateTime.Now.Hour, 1,
                v => Math.Round(v).ToString("00") + ":00", v =>
                {
                    var key = ScheduledTheme(DateTime.Today.AddHours(Math.Round(v)));
                    if (key == null || !Styles.ContainsKey(key)) return;
                    candidateTheme = key;
                    hourTest.Text = "В это время · " + ThemeName(key);
                    if (refreshPreview != null) refreshPreview(key);
                }, "Час для примерки")));
            c.Children.Add(hourTest);
        }

        Action<string> refreshPreview;

        static readonly HashSet<string> LookKeys = new HashSet<string> { "style", "material", "material.edge", "material.tint", "material.diffusion", "material.blur", "accent", "opacity", "size", "pos", "width", "gap", "align", "widgets", "seq", "bar.sections", "appMax", "search", "edgeBtns" };
        static bool IsAppearanceKey(string key)
        {
            var name = key.Split('@')[0];
            return LookKeys.Contains(name) || name.StartsWith("view.") || name.StartsWith("keep.") || name.StartsWith("mon.");
        }
        static string rememberedAppearance;
        Dictionary<string, string> candidateAppearance;
        string candidateAppearanceDate;
        static string SnapshotAppearance()
        {
            var pairs = cfg.Where(kv => IsAppearanceKey(kv.Key)).OrderBy(kv => kv.Key);
            return string.Join("\n", pairs.Select(kv => kv.Key + "=" + kv.Value));
        }

        static IEnumerable<string> AppearanceHistory()
        {
            return SafeFile.ReadLines(F("appearance-history.txt")).Take(8);
        }

        static DateTime lastAppearanceRecord;
        static void RecordAppearance()
        {
            var next = SnapshotAppearance();
            if (next == rememberedAppearance) return;
            string old = rememberedAppearance;
            rememberedAppearance = next;
            if (old == null) return;
            // ползунок сохраняет на каждом шаге: одно перетаскивание — одна запись (вид до него), а не 8 промежуточных
            var now = DateTime.Now; bool burst = (now - lastAppearanceRecord).TotalSeconds < 3; lastAppearanceRecord = now;
            if (burst) return;
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(old));
            var entry = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + encoded;
            var history = AppearanceHistory().Where(line => !line.EndsWith("\t" + encoded, StringComparison.Ordinal));
            SafeFile.WriteLines(F("appearance-history.txt"), new[] { entry }.Concat(history).Take(8));
        }

        void AppearanceHistorySettings(StackPanel c)
        {
            c.Children.Add(SSub("История оформления"));
            foreach (var line in AppearanceHistory())
            {
                var fields = line.Split('\t');
                if (fields.Length != 2) continue;
                string data;
                try { data = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(fields[1])); }
                catch (FormatException) { continue; }
                var values = data.Split('\n').Where(l => l.Contains("=")).Select(l => l.Split(new[] { '=' }, 2))
                    .Where(kv => IsAppearanceKey(kv[0])).GroupBy(kv => kv[0]).ToDictionary(group => group.Key, group => group.Last()[1]);
                string theme;
                if (!values.TryGetValue("style", out theme) || !Styles.ContainsKey(theme)) theme = "Black";
                c.Children.Add(ThemeSwatch(theme, fields[0] + " · " + ThemeName(theme), false, () =>
                {
                    candidateAppearance = values;
                    candidateAppearanceDate = fields[0];
                    if (refreshPreview != null) refreshPreview(null);
                }));
            }
        }

        void RestorePreviewAppearance()
        {
            if (candidateAppearance == null) return;
            foreach (var key in cfg.Keys.Where(IsAppearanceKey).ToList()) cfg.Remove(key);
            foreach (var pair in candidateAppearance) cfg[pair.Key] = pair.Value;
            candidateAppearance = null;
            cfg["appearance.schedule"] = "off";
            SaveCfg(); ClosePanels(); All(b => { b.LoadSeq(); b.reserveKey = null; });
            RebuildAll(); All(b => b.Place()); RestyleWindows();
        }

        void SetupPreviewWidget(Border tile, Bar target, string id)
        {
            Action<int> move = delta =>
            {
                var layout = target.Layout();
                var shown = layout.Where(kv => kv.Value).ToList();
                int from = shown.FindIndex(kv => kv.Key == id), to = from + delta;
                if (from < 0 || to < 0 || to >= shown.Count) return;
                var order = shown.Select(kv => kv.Key).ToList();
                order.RemoveAt(from); order.Insert(to, id);
                CommitPreviewWidgets(target, layout, order);
            };
            var menu = new ContextMenu();
            menu.Items.Add(Item(VerticalBar ? "Выше" : "Левее", () => move(-1)));
            menu.Items.Add(Item(VerticalBar ? "Ниже" : "Правее", () => move(1)));
            var variants = new[] { "", "min", "compact", "full" };
            for (int i = 0; i < variants.Length; i++)
            {
                var key = variants[i];
                if (Variants.Contains(id) && !VerticalBar)
                    menu.Items.Add(Item(new[] { "Авто", "Размер S", "Размер M", "Размер L" }[i], () =>
                    {
                        cfg[target.MK("view." + id)] = key;
                        SaveCfg(); RebuildAll(); if (settingsGo != null) settingsGo(settingsCur());
                    }, target.CM("view." + id, "") == key));
            }
            if (Variants.Contains(id) && !VerticalBar)
            {
                var handle = new System.Windows.Controls.Primitives.Thumb
                {
                    Width = 6, Cursor = Cursors.SizeWE, Background = Frozen(CardFg, 0x39), HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 1, -5, 1), ToolTip = "Потяните за край для выбора S, M или L"
                };
                var content = tile.Child;
                tile.Child = null;
                var face = new Grid(); face.Children.Add(content); face.Children.Add(handle);
                tile.Child = face;
                double dx = 0;
                handle.DragStarted += (s, e) => dx = 0;
                handle.DragDelta += (s, e) => dx += e.HorizontalChange;
                handle.DragCompleted += (s, e) =>
                {
                    if (e.Canceled || Math.Abs(dx) < 12) return;
                    int view = Math.Max(0, Math.Min(2, target.View(id) + (dx > 0 ? 1 : -1)));
                    cfg[target.MK("view." + id)] = variants[view + 1];
                    SaveCfg(); RebuildAll(); if (settingsGo != null) settingsGo(settingsCur());
                };
            }
            tile.ContextMenu = menu; tile.Focusable = true;
            tile.ToolTip = "Перетащите для перестановки. Размер — в меню правой кнопки.";
            tile.AllowDrop = true;
            Point? down = null;
            tile.MouseLeftButtonDown += (s, e) => down = e.GetPosition(tile);
            tile.MouseLeftButtonUp += (s, e) => down = null;
            tile.MouseMove += (s, e) =>
            {
                if (!down.HasValue || e.LeftButton != MouseButtonState.Pressed || Mouse.Captured != null) return;
                var delta = e.GetPosition(tile) - down.Value;
                if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                down = null;
                DragDrop.DoDragDrop(tile, new DataObject("Flowbar.SettingsWidget", id), DragDropEffects.Move);
            };
            tile.DragEnter += (s, e) => { if (e.Data.GetDataPresent("Flowbar.SettingsWidget")) tile.BorderThickness = new Thickness(2, 0, 0, 0); };
            tile.DragLeave += (s, e) => tile.BorderThickness = new Thickness(0);
            tile.BorderBrush = Accent;
            tile.Drop += (s, e) =>
            {
                tile.BorderThickness = new Thickness(0);
                var source = e.Data.GetData("Flowbar.SettingsWidget") as string;
                if (source == null || source == id) return;
                var layout = target.Layout();
                var order = layout.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
                if (!order.Remove(source)) return;
                var at = e.GetPosition(tile);
                bool after = VerticalBar ? at.Y >= tile.ActualHeight / 2 : at.X >= tile.ActualWidth / 2;
                order.Insert(order.IndexOf(id) + (after ? 1 : 0), source);
                CommitPreviewWidgets(target, layout, order);
            };
        }

        static void CommitPreviewWidgets(Bar target, List<KeyValuePair<string, bool>> layout, List<string> order)
        {
            target.MarkCustom();
            var tokens = new Queue<string>(order.Select(id => "w:" + id));
            target.LoadSeq();
            target.seq = target.seq.Where(t => !t.StartsWith("w:") || tokens.Count > 0)
                .Select(t => t.StartsWith("w:") ? tokens.Dequeue() : t).Concat(tokens).ToList();
            target.SaveSeq();
            target.SaveLayout(MergeOrder(layout, order));
            if (settingsGo != null) settingsGo(settingsCur());
        }

        bool CompactMonitor { get { return !primary && C("mon." + scr.DeviceName, "none") == "compact"; } }
        bool LayoutShows(string id)
        {
            if (!CompactMonitor) return true;
            return C("mon.compact." + scr.DeviceName, "conn,vol,bat").Split(',').Contains(id);
        }

        UIElement MonitorMap(Action again)
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            var full = screens.Select(sc => sc.Bounds).Aggregate(System.Drawing.Rectangle.Union);
            double scale = Math.Min(520.0 / full.Width, 145.0 / full.Height);
            var map = new Canvas { Width = full.Width * scale, Height = full.Height * scale, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 8) };
            foreach (var sc in screens)
            {
                var b = bars.FirstOrDefault(item => item.scr.DeviceName == sc.DeviceName);
                bool selected = settingsPreviewMonitor == b || settingsPreviewMonitor == null && sc.Primary;
                var display = new Grid();
                display.Children.Add(new Border { Background = Frozen(CardFg, 0x14), CornerRadius = new CornerRadius(5) });
                var label = Txt(sc.Primary ? "Основной" : sc.Bounds.Width + " × " + sc.Bounds.Height, 10, true);
                label.HorizontalAlignment = HorizontalAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center;
                display.Children.Add(label);
                if (b != null)
                    display.Children.Add(new Border { Width = b.VerticalBar ? 3 : double.NaN, Height = b.VerticalBar ? double.NaN : 3, Background = Accent,
                        Margin = b.VerticalBar ? new Thickness(3, 8, 3, 8) : new Thickness(8, 3, 8, 3), CornerRadius = new CornerRadius(1.5),
                        HorizontalAlignment = b.VerticalBar ? (b.LeftBar ? HorizontalAlignment.Left : HorizontalAlignment.Right) : HorizontalAlignment.Stretch,
                        VerticalAlignment = b.VerticalBar ? VerticalAlignment.Stretch : b.IsTop ? VerticalAlignment.Top : VerticalAlignment.Bottom });
                var cell = Act(display, sc.DeviceName + " · " + sc.Bounds.Width + " × " + sc.Bounds.Height, () => { settingsPreviewMonitor = b; again(); });
                cell.Width = Math.Max(12, sc.Bounds.Width * scale - 4); cell.Height = Math.Max(12, sc.Bounds.Height * scale - 4);
                cell.BorderBrush = selected ? Accent : Frozen(CardFg, 0x32);
                cell.BorderThickness = new Thickness(1); cell.CornerRadius = new CornerRadius(6);
                Canvas.SetLeft(cell, (sc.Bounds.Left - full.Left) * scale); Canvas.SetTop(cell, (sc.Bounds.Top - full.Top) * scale);
                map.Children.Add(cell);
            }
            return map;
        }

        void MonitorGeometrySettings(StackPanel content, Bar target, Action again)
        {
            Action<string, string> set = (key, value) =>
            {
                string local = key + "@" + target.scr.DeviceName;
                if (value == "") cfg.Remove(local); else cfg[local] = value;
                SaveCfg();
                target.ClosePreview();
                if (target.panel != null) target.panel.Close();
                target.ResetFit(true); target.reserveKey = null;
                target.Rebuild(); target.Place();
                settingsPreviewMonitor = target;
                again();
            };
            var edges = new[] { "", "bottom", "top", "left", "right" };
            var sizes = new[] { "", "S", "M", "L", "XL" };
            var suffix = "@" + target.scr.DeviceName;
            content.Children.Add(SetRow("Расположение на мониторе", null, Seg(new[] { "Общее", "Снизу", "Сверху", "Слева", "Справа" },
                Math.Max(0, Array.IndexOf(edges, C("pos" + suffix, ""))), i => set("pos", edges[i]), true)));
            content.Children.Add(SetRow("Размер на мониторе", null, Seg(new[] { "Общий", "S", "M", "L", "XL" },
                Math.Max(0, Array.IndexOf(sizes, C("size" + suffix, ""))), i => set("size", sizes[i]), true)));
        }

        void CompactMonitorSettings(StackPanel c, string device, Action again)
        {
            var key = "mon.compact." + device;
            var values = new HashSet<string>(C(key, "conn,vol,bat").Split(','));
            var list = new WrapPanel();
            foreach (var id in new[] { "conn", "vol", "bat", "net", "weather", "notif" })
            {
                var def = WidgetDefs.FirstOrDefault(d => d[0] == id);
                var check = new CheckBox { Content = def == null ? id : def[1], IsChecked = values.Contains(id), Margin = new Thickness(0, 0, 12, 7) };
                RoutedEventHandler change = (s, e) =>
                {
                    if (check.IsChecked == true) values.Add(id); else values.Remove(id);
                    cfg[key] = string.Join(",", values);
                    SaveCfg(); RebuildAll(); again();
                };
                check.Checked += change; check.Unchecked += change;
                list.Children.Add(check);
            }
            c.Children.Add(list);
        }

        static Border SettingsChoice(string[] labels, int selected, Action<int> changed)
        {
            Border button = null;
            Action open = () =>
            {
                var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                for (int i = 0; i < labels.Length; i++)
                {
                    int index = i;
                    menu.Items.Add(Item((index == selected ? "✓  " : "    ") + labels[index], () =>
                    {
                        selected = index;
                        ((TextBlock)button.Child).Text = labels[index] + "  ▾";
                        AutomationProperties.SetName(button, labels[index]);
                        changed(index);
                    }));
                }
                menu.IsOpen = true;
            };
            button = Btn(labels[selected] + "  ▾", open);
            button.MinWidth = 180;
            button.HorizontalAlignment = HorizontalAlignment.Left;
            AutomationProperties.SetName(button, labels[selected]);
            return button;
        }

        void AddWidgetStateGallery(StackPanel content, Action again)
        {
            content.Children.Add(SSub("Предпросмотр виджетов"));
            var defs = WidgetDefs.Where(d => d[0] != "clock").ToArray();
            var states = new StackPanel();
            var selected = (int)Clamp(C("preview.widget", ""), 0, 0, defs.Length - 1);
            Action<int> render = index =>
            {
                var id = defs[index][0];
                var target = Primary ?? this;
                states.Children.Clear();
                Func<Snap, UIElement> sample = data =>
                {
                    var host = new StackPanel { IsHitTestVisible = false };
                    var updates = new List<Action>();
                    try
                    {
                        var tile = target.MakeWidget(id, data, false, updates);
                        if (tile != null) host.Children.Add(tile);
                        foreach (var update in updates) update();
                    }
                    catch (Exception ex) { Log(ex); }
                    if (host.Children.Count == 0) host.Children.Add(CardStatus("Для этого состояния нет отдельного вида."));
                    return host;
                };
                var layout = target.Layout();
                bool visible = layout.Any(kv => kv.Key == id && kv.Value);
                var unavailable = DemoState(1);
                unavailable.WxDesc = "Нет данных"; unavailable.MediaError = "Источник недоступен";
                states.Children.Add(SetRow("Пример с данными", null, sample(Demo())));
                states.Children.Add(SetRow("Загрузка", null, sample(DemoState(1))));
                states.Children.Add(SetRow("Нет данных", null, sample(unavailable)));
                states.Children.Add(CardActions(Btn(visible ? "✓ На панели · убрать" : "Показать на панели", () =>
                {
                    cfg["preview.widget"] = index.ToString();
                    target.SetVisible(id, !visible);
                    again();
                }, !visible)));
            };
            var picker = SettingsChoice(defs.Select(d => d[1]).ToArray(), selected, index =>
            {
                cfg["preview.widget"] = index.ToString();
                render(index);
            });
            content.Children.Add(picker); content.Children.Add(states); render(selected);
        }

        UIElement ScreenMonth()
        {
            var now = DateTime.Today;
            var days = Enumerable.Range(0, 31).Select(i =>
            {
                var date = now.AddDays(-i);
                return Tuple.Create(date, i == 0 ? screen : ScreenTime.Load(date.ToString("yyyy-MM-dd")));
            }).Reverse().ToList();
            int max = Math.Max(1, days.Max(item => item.Item2.Total));
            var wrap = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7, Margin = new Thickness(0, 10, 0, 8) };
            var details = new StackPanel();
            Action<ScreenTime, DateTime> select = (day, date) =>
            {
                details.Children.Clear();
                details.Children.Add(CardHead(date.ToString("d MMMM") + " · " + ScreenTime.Fmt(day.Total)));
                foreach (var item in day.Apps.OrderByDescending(kv => kv.Value).Take(8))
                    details.Children.Add(Txt(NiceApp(item.Key) + " · " + ScreenTime.Fmt(item.Value), 12, false));
                if (day.Total == 0) details.Children.Add(CardStatus("Нет записанной активности."));
            };
            foreach (var pair in days)
            {
                var item = pair;
                var label = Txt(item.Item1.Day.ToString(), 11, item.Item1 == now);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                var cell = Act(label, item.Item1.ToString("d MMMM") + " · " + ScreenTime.Fmt(item.Item2.Total), () => select(item.Item2, item.Item1));
                cell.Margin = new Thickness(2);
                cell.Background = Frozen(AccentColor, item.Item2.Total == 0 ? (byte)0x10 : (byte)(25 + item.Item2.Total * 110.0 / max));
                wrap.Children.Add(cell);
            }
            select(screen, now);
            return VS(wrap, details);
        }

        UIElement ScreenTimeline(ScreenTime data)
        {
            var body = new StackPanel();
            if (data.Intervals.Count == 0) { body.Children.Add(CardStatus("Интервалы появятся после активности в новой версии Flowbar.")); return body; }
            var palette = new[] { AccentColor, Color.FromRgb(98, 163, 234), Color.FromRgb(183, 124, 230), Color.FromRgb(224, 147, 81), Color.FromRgb(104, 187, 147) };
            var apps = data.Intervals.GroupBy(slot => slot.App).OrderByDescending(g => g.Sum(s => s.End - s.Start)).Take(palette.Length).Select(g => g.Key).ToList();
            var graph = new Canvas { Width = 332, Height = 25, Background = Frozen(CardFg, 0x12), Margin = new Thickness(0, 12, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var slot in data.Intervals.Where(s => s.End - s.Start >= 3))
            {
                int index = apps.IndexOf(slot.App);
                var block = new Border
                {
                    Width = Math.Max(1, slot.End / 86400.0 * graph.Width - slot.Start / 86400.0 * graph.Width), Height = 25,
                    Background = Frozen(index < 0 ? CardFg : palette[index], index < 0 ? (byte)0x60 : (byte)0xDF),
                    ToolTip = NiceApp(slot.App) + " · " + TimeSpan.FromSeconds(slot.Start).ToString(@"hh\:mm\:ss") + " · " + ScreenTime.Fmt(slot.End - slot.Start)
                };
                Canvas.SetLeft(block, slot.Start / 86400.0 * graph.Width); graph.Children.Add(block);
            }
            body.Children.Add(graph);
            body.Children.Add(Txt("00:00                  06:00                  12:00                  18:00", 10, false));
            foreach (var app in apps)
            {
                var dot = new Ellipse { Width = 7, Height = 7, Fill = Frozen(palette[apps.IndexOf(app)], 255), VerticalAlignment = VerticalAlignment.Center };
                body.Children.Add(Mg(HS(dot, Mg(Txt(NiceApp(app), 11, false), 7)), 0, 4));
            }
            body.Children.Add(CardStatus("Данные за сегодня. Свободные участки показывают время без записанной активности."));
            return body;
        }

        static int NextRain(Weather weather)
        {
            if (weather == null) return -1;
            int end = Math.Min(weather.HStart + 12, Math.Min(weather.HProb.Length, weather.HTime.Length));
            for (int i = weather.HStart; i < end; i++)
                if (weather.HProb[i] >= 60) return i;
            return -1;
        }

        static string RainSummary(Weather weather)
        {
            if (weather == null) return "";
            int next = NextRain(weather);
            return next < 0 ? "В ближайшие 12 часов вероятность осадков ниже 60%"
                : weather.HTime[next].ToString("HH:mm") + " · осадки " + weather.HProb[next] + "%";
        }

        static Brush MusicTint(ImageSource source)
        {
            var bitmap = source as BitmapSource;
            if (bitmap == null) return Brushes.Transparent;
            try
            {
                var small = new TransformedBitmap(bitmap, new ScaleTransform(1.0 / bitmap.PixelWidth, 1.0 / bitmap.PixelHeight));
                var converted = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
                var pixel = new byte[4]; converted.CopyPixels(pixel, 4, 0);
                return new LinearGradientBrush(Color.FromArgb(0x64, pixel[2], pixel[1], pixel[0]), Color.FromArgb(0x18, pixel[2], pixel[1], pixel[0]), 35);
            }
            catch (Exception ex) { Log(ex); return Brushes.Transparent; }
        }

        void ShowBattery()
        {
            var body = new StackPanel();
            var value = Txt("", 36, true);
            var status = CardStatus("");
            var progress = new FillBar(270, 5);
            body.Children.Add(value); body.Children.Add(progress); body.Children.Add(status);
            body.Children.Add(CardActions(Btn("Параметры питания", () => Launch("ms-settings:batterysaver"))));
            Action tick = () =>
            {
                var s = live;
                value.Text = s.HasBat ? Math.Round(s.Bat * 100) + "%" : "—";
                progress.Set(s.HasBat ? s.Bat : double.NaN);
                status.Text = !s.HasBat ? "Батарея не обнаружена." : s.Charging ? "Подключено питание · идёт зарядка"
                    : s.BatSec > 0 ? "От батареи · осталось около " + Until(TimeSpan.FromSeconds(s.BatSec)) : "От батареи · время работы неизвестно";
            };
            tick();
            ShowPanel("Батарея", body, 310, null, tick);
        }

        UIElement NetworkDots(List<double> values, string label, Brush color)
        {
            var points = values.Skip(Math.Max(0, values.Count - 28)).ToArray();
            var max = Math.Max(1, points.DefaultIfEmpty().Max());
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Height = 31, Margin = new Thickness(0, 3, 0, 5) };
            for (int i = 0; i < points.Length; i++)
            {
                var cell = new Grid { Background = Brushes.Transparent, ToolTip = label + " · " + RateRu(points[i]) + " · " + (points.Length - i - 1) + " с назад" };
                var dot = new Ellipse { Width = 5, Height = 5, Fill = color, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 24 * points[i] / max) };
                cell.Children.Add(dot); grid.Children.Add(cell);
            }
            return grid;
        }

        void ShowNetworkDots()
        {
            var body = new StackPanel();
            var downloads = new Border(); var uploads = new Border();
            var receive = Txt("", 12, true); var send = Txt("", 12, true); var vpn = CardStatus("");
            foreach (var element in new UIElement[] { receive, downloads, send, uploads, vpn }) body.Children.Add(element);
            body.Children.Add(CardActions(Btn("История системы", ShowSystem), Btn("Сеть Windows", () => Launch("ms-settings:network"))));
            Action refresh = () =>
            {
                var s = live;
                receive.Text = "↓ Приём · " + RateRu(s.Rx); send.Text = "↑ Отдача · " + RateRu(s.Tx);
                downloads.Child = NetworkDots(s.RxHist, "Приём", Accent);
                uploads.Child = NetworkDots(s.TxHist, "Отдача", Frozen(CardFg, 0x90));
                vpn.Text = ConnKind(s.NetKind) + (s.NetVpn ? " · VPN" : "");
            };
            refresh();
            ShowPanel("Сеть", body, 355, null, refresh);
        }

        UIElement DaylightDetails()
        {
            var w = wx;
            if (w == null) return CardStatus("Время рассвета и заката появится после загрузки погоды.");
            var status = CardStatus("Рассвет " + w.Sunrise.ToString("HH:mm") + "   ·   Закат " + w.Sunset.ToString("HH:mm") + "   ·   " + C("wxCity", "Город прогноза"));
            return VS(Mg(DaylightArc(), 0, 6), status);
        }

        FrameworkElement DaylightArc()
        {
            var arc = new Canvas { Width = 51, Height = 14, HorizontalAlignment = HorizontalAlignment.Center, ClipToBounds = true };
            var line = new PathGeometry(new[] { new PathFigure(new Point(1, 13), new[] { new QuadraticBezierSegment(new Point(25, -9), new Point(50, 13), true) }, false) });
            arc.Children.Add(new System.Windows.Shapes.Path { Data = line, Stroke = Accent, StrokeThickness = 1.2 });
            var marker = new Ellipse { Width = 5, Height = 5, Fill = Accent };
            arc.Children.Add(marker);
            arc.Tag = marker;
            UpdateDaylight(arc);
            return arc;
        }

        void UpdateDaylight(FrameworkElement element)
        {
            var w = wx;
            element.Visibility = w == null ? Visibility.Collapsed : Visibility.Visible;
            if (w == null) return;
            var marker = (Ellipse)element.Tag;
            double duration = Math.Max(1, (w.Sunset - w.Sunrise).TotalMinutes);
            double minute = (w.Now - w.Updated).TotalMinutes + (DateTime.Now - w.Sunrise).TotalMinutes;
            double p = Math.Max(0, Math.Min(1, minute / duration));
            Canvas.SetLeft(marker, 49 * p - 1);
            Canvas.SetTop(marker, 10 - 44 * p * (1 - p));
            element.ToolTip = w.Sunrise.ToString("HH:mm") + " — " + w.Sunset.ToString("HH:mm");
        }

        sealed class AppProgress
        {
            public uint Pid;
            public double Value;
            public bool Paused;
            public bool Error, Indeterminate;
        }
        static readonly Dictionary<IntPtr, AppProgress> appProgress = new Dictionary<IntPtr, AppProgress>();
        static bool pollingProgress;
        int backgroundProgressCursor;

        async void PollAppProgress()
        {
            if (DemoMode || C("apps.progress", "0") != "1" || ticks % 3 != 0) return;
            if (ticks % 30 == 0) { Tray.EnableTaskbarProgress(); Tray.PruneTaskProgress(); }
            if (pollingProgress) return;
            var foreground = N.GetForegroundWindow();
            var windows = allWins.GroupBy(w => w.H).Select(group => group.First()).ToList();
            var open = new HashSet<IntPtr>(windows.Select(w => w.H));
            bool changed = false;
            foreach (var h in appProgress.Keys.Where(h => !open.Contains(h)).ToList()) changed |= appProgress.Remove(h);
            var batch = windows.Where(w => w.H == foreground).ToList();
            var background = windows.Where(w => w.H != foreground).ToList();
            for (int i = 0; i < Math.Min(2, background.Count); i++)
            {
                backgroundProgressCursor %= background.Count;
                batch.Add(background[backgroundProgressCursor++]);
            }
            pollingProgress = true;
            try
            {
                var samples = await System.Threading.Tasks.Task.Run(() => batch.Select(w =>
                {
                    AppProgress value;
                    try { value = ReadAppProgress(w.H, w.Pid); }
                    catch (ElementNotAvailableException) { value = null; }
                    catch (System.Runtime.InteropServices.COMException) { value = null; }
                    return Tuple.Create(w.H, value);
                }).ToArray());
                if (C("apps.progress", "0") != "1") return;
                foreach (var sample in samples)
                {
                    if (sample.Item2 == null) { changed |= appProgress.Remove(sample.Item1); continue; }
                    AppProgress previous;
                    if (!appProgress.TryGetValue(sample.Item1, out previous) || previous.Pid != sample.Item2.Pid ||
                        previous.Paused != sample.Item2.Paused || previous.Value != sample.Item2.Value)
                    {
                        appProgress[sample.Item1] = sample.Item2;
                        changed = true;
                    }
                }
            }
            catch (Exception ex) { Log(ex); }
            finally { pollingProgress = false; }
            if (changed) All(b => { b.lastKey = null; b.RefreshApps(); });
        }

        static AppProgress ReadAppProgress(IntPtr window, uint pid)
        {
            var app = AutomationElement.FromHandle(window);
            if (app == null || app.Current.ProcessId != pid) return null;
            var elements = app.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ProgressBar));
            if (elements.Count != 1) return null;
            object pattern;
            if (!elements[0].TryGetCurrentPattern(RangeValuePattern.Pattern, out pattern)) return null;
            var r = ((RangeValuePattern)pattern).Current;
            if (r.Maximum <= r.Minimum || double.IsNaN(r.Value)) return null;
            double fraction = (r.Value - r.Minimum) / (r.Maximum - r.Minimum);
            if (fraction < 0 || fraction > 1) return null;
            var status = elements[0].Current.ItemStatus ?? "";
            bool paused = new[] { "paused", "pause", "на паузе", "приостановлен" }.Any(label => status.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0);
            return new AppProgress { Pid = pid, Value = Math.Round(fraction, 2), Paused = paused };
        }

        static AppProgress ProgressFor(IEnumerable<Win> windows, IntPtr foreground)
        {
            if (C("apps.progress", "0") != "1") return null;
            var native = windows.Select(w => Tray.ProgressFor(w.H, w.Pid)).Where(p => p != null)
                .OrderBy(p => p.State == Tray.ProgressState.Error ? 0 : p.State == Tray.ProgressState.Paused ? 1 : p.State == Tray.ProgressState.Normal ? 2 : 3)
                .ThenBy(p => p.Value).FirstOrDefault();
            if (native != null)
                return new AppProgress { Pid = native.Pid, Value = native.Value, Paused = native.State == Tray.ProgressState.Paused,
                    Error = native.State == Tray.ProgressState.Error, Indeterminate = native.State == Tray.ProgressState.Indeterminate };
            foreach (var w in windows.OrderByDescending(w => w.H == foreground))
            {
                AppProgress value;
                if (appProgress.TryGetValue(w.H, out value) && value.Pid == w.Pid) return value;
            }
            return null;
        }

        void ShowAppMixer()
        {
            var body = new StackPanel();
            var device = CardHead("");
            var rows = new StackPanel();
            body.Children.Add(device); body.Children.Add(rows);
            Action refresh = null;
            refresh = () =>
            {
                double master; bool muted; string output;
                Audio.Read(out master, out muted, out output);
                device.Text = output ?? "Устройство вывода недоступно";
                rows.Children.Clear();
                var sessions = Audio.MixerSessions();
                foreach (var session in sessions)
                {
                    var s = session;
                    string path = ExePath(s.Pid);
                    var label = Txt(path == null ? "Приложение " + s.Pid : AppName(path), 12, true);
                    var icon = path == null ? null : IconFor(path);
                    var toggle = Btn(s.Muted ? "Включить" : "Выключить", () =>
                    {
                        if (!s.SetMute(!s.Muted)) { Notify("Громкость", "Сеанс приложения уже закрыт."); }
                        refresh();
                    });
                    var top = new DockPanel();
                    DockPanel.SetDock(toggle, Dock.Right); top.Children.Add(toggle);
                    if (icon != null) top.Children.Add(Mg(new Image { Source = icon, Width = 18, Height = 18 }, 0, 0, 6, 0));
                    top.Children.Add(label);
                    var slider = new Slider { Minimum = 0, Maximum = 100, Value = s.Volume * 100, Margin = new Thickness(0, 4, 0, 8) };
                    System.Windows.Automation.AutomationProperties.SetName(slider, "Громкость " + label.Text);
                    slider.ValueChanged += (o, e) => { if (!s.SetVolume((float)(e.NewValue / 100))) slider.IsEnabled = false; };
                    rows.Children.Add(VS(top, slider));
                }
                if (sessions.Count == 0) rows.Children.Add(CardStatus("Активных аудиосеансов нет."));
                StyleInputs(rows, CardFg);
            };
            refresh();
            body.Children.Add(CardActions(Btn("Обновить", refresh), Btn("Быстрые настройки", ShowQuick)));
            ShowPanel("Микшер громкости", body, 350, null);
        }

        static readonly HashSet<string> DesktopExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".mp4", ".m4v", ".wmv", ".avi", ".mov"
        };
        static DesktopMedia desktopMedia;
        static DispatcherTimer desktopWatch;
        static DesktopWindows desktopWindows;

        void ToggleDesktop()
        {
            if (desktopWindows != null)
            {
                CloseDesktopMedia(true);
                return;
            }

            string path = C("desktop.media", "");
            ClosePanels();
            if (path.Length == 0 || !File.Exists(path))
            {
                if (!Desk.TryToggleDesktop()) Keys(0x5B, 0x44);
                if (path.Length != 0) Notify("Рабочий стол", "Файл фона недоступен.", true, "\uE7BA");
                return;
            }

            try
            {
                desktopMedia = new DesktopMedia(path, error =>
                {
                    CloseDesktopMedia(true);
                    Notify("Рабочий стол", error, true, "\uE7BA");
                });
                GetWindows(); // allWins covers every monitor, regardless of the panel's app filter.
                var ownWindows = new[] { settings, catalog }.Where(w => w != null && w.IsVisible)
                    .Select(w => new WindowInteropHelper(w).Handle);
                desktopWindows = new DesktopWindows(allWins.Select(w => w.H).Concat(ownWindows));
                desktopWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                desktopWatch.Tick += (o, e) =>
                {
                    if (!desktopMedia.Available) { CloseDesktopMedia(true); return; }
                    if (desktopWindows.ReturnedToWindow(DesktopHasFocus())) CloseDesktopMedia();
                };
                desktopWatch.Start();
            }
            catch (Exception ex)
            {
                CloseDesktopMedia(true);
                Log(ex);
                Notify("Рабочий стол", "Не удалось показать фон. Проверьте формат файла и попробуйте снова.", true, "\uE7BA");
            }
        }

        static bool DesktopHasFocus()
        {
            var h = N.GetForegroundWindow();
            var name = Cls(h);
            return h == IntPtr.Zero || name == "Progman" || name == "WorkerW" || name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd";
        }

        static void CloseDesktopMedia(bool restoreWindows = false)
        {
            if (desktopWatch != null) { desktopWatch.Stop(); desktopWatch = null; }
            var windows = desktopWindows;
            desktopWindows = null;
            var media = desktopMedia;
            desktopMedia = null;
            try { if (media != null) media.Dispose(); }
            finally { if (restoreWindows && windows != null) windows.Restore(); }
        }

        void DesktopMediaSettings(StackPanel content, Action again)
        {
            var path = C("desktop.media", "");
            var actions = CardActions(Btn("Выбрать файл…", () =>
            {
                var picker = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Фон при показе рабочего стола",
                    Filter = "Фото, GIF и видео|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.mp4;*.m4v;*.wmv;*.avi;*.mov",
                    CheckFileExists = true
                };
                if (picker.ShowDialog(settings) != true || !DesktopExtensions.Contains(Path.GetExtension(picker.FileName))) return;
                CloseDesktopMedia(true);
                cfg["desktop.media"] = picker.FileName; SaveCfg(); again();
            }), Btn("Убрать фон", () =>
            {
                CloseDesktopMedia(true);
                cfg.Remove("desktop.media"); SaveCfg(); again();
            }));
            content.Children.Add(SetRow("Фон при показе рабочего стола",
                (path.Length == 0 ? "Файл не выбран. " : Path.GetFileName(path) + ". ") + "Кнопка у края панели показывает рабочий стол и временный фон. GIF и видео повторяются, видео без звука.",
                actions));
        }
    }

    sealed class DesktopWindows
    {
        [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr window, int command);

        sealed class Entry
        {
            public IntPtr Handle;
            public uint Process, Thread;
            public bool Maximized, SawMinimized;
            public bool IsSameWindow()
            {
                uint process;
                return N.GetWindowThreadProcessId(Handle, out process) == Thread && process == Process;
            }
        }

        readonly List<Entry> windows = new List<Entry>();
        readonly IntPtr previousForeground = N.GetForegroundWindow();
        readonly DateTime settlingUntil = DateTime.UtcNow.AddMilliseconds(900);
        IntPtr desktopForeground;

        public DesktopWindows(IEnumerable<IntPtr> handles)
        {
            foreach (var handle in handles.Distinct().OrderBy(h => h == previousForeground))
            {
                if (handle == IntPtr.Zero || N.IsIconic(handle) || !N.IsWindowVisible(handle)) continue;
                const long MinimizeBox = 0x20000;
                if ((N.GetWindowLongPtr(handle, -16).ToInt64() & MinimizeBox) == 0) continue;
                uint pid;
                var thread = N.GetWindowThreadProcessId(handle, out pid);
                if (thread == 0) continue;
                bool maximized = N.IsZoomed(handle);
                if (ShowWindowAsync(handle, handle == previousForeground ? 6 : 7))
                    windows.Add(new Entry { Handle = handle, Process = pid, Thread = thread, Maximized = maximized });
            }
        }

        public bool ReturnedToWindow(bool shellHasFocus)
        {
            var foreground = N.GetForegroundWindow();
            var reopened = false;
            foreach (var entry in windows)
            {
                if (!entry.IsSameWindow()) continue;
                if (N.IsIconic(entry.Handle)) entry.SawMinimized = true;
                else if (entry.Handle == foreground && entry.SawMinimized) reopened = true;
            }
            // Explorer can leave focus on a floating tool while the application windows minimize.
            if (shellHasFocus || DateTime.UtcNow < settlingUntil) desktopForeground = foreground;
            return reopened || !shellHasFocus && DateTime.UtcNow >= settlingUntil && foreground != IntPtr.Zero && foreground != desktopForeground
                && N.IsWindowVisible(foreground) && !N.IsIconic(foreground);
        }

        public void Restore()
        {
            foreach (var entry in windows.AsEnumerable().Reverse().OrderBy(w => w.Handle == previousForeground))
            {
                // Leave reopened windows where the user placed them. For an operation still
                // queued on another thread, queue its matching restore after the minimize.
                if (entry.IsSameWindow() && (N.IsIconic(entry.Handle) || !entry.SawMinimized && DateTime.UtcNow < settlingUntil))
                    ShowWindowAsync(entry.Handle, entry.Maximized ? 3 : 9);
            }
        }
    }

    static partial class Audio
    {
        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ISimpleAudioVolume
        {
            [PreserveSig] int SetMasterVolume(float value, IntPtr context);
            [PreserveSig] int GetMasterVolume(out float value);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        internal sealed class MixerSession
        {
            readonly object control;
            public readonly uint Pid;
            public readonly float Volume;
            public readonly bool Muted;
            internal MixerSession(object value, uint pid, float volume, bool muted)
            {
                control = value; Pid = pid; Volume = volume; Muted = muted;
            }
            public bool SetMute(bool value) { return ((ISimpleAudioVolume)control).SetMute(value, IntPtr.Zero) == 0; }
            public bool SetVolume(float value) { return ((ISimpleAudioVolume)control).SetMasterVolume(value, IntPtr.Zero) == 0; }
        }

        internal static List<MixerSession> MixerSessions()
        {
            var result = new List<MixerSession>();
            try
            {
                var en = (IMMDeviceEnumerator)new Enumerator();
                IMMDevice device;
                if (en.GetDefaultAudioEndpoint(0, 1, out device) != 0 || device == null) return result;
                var iid = typeof(IAudioSessionManager2).GUID;
                object obj;
                if (device.Activate(ref iid, 23, IntPtr.Zero, out obj) != 0) return result;
                IAudioSessionEnumerator sessions;
                if (((IAudioSessionManager2)obj).GetSessionEnumerator(out sessions) != 0) return result;
                int count;
                if (sessions.GetCount(out count) != 0) return result;
                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl2 control;
                    if (sessions.GetSession(i, out control) != 0 || control == null) continue;
                    int state;
                    uint pid;
                    if (control.GetState(out state) != 0 || state == 2 || control.GetProcessId(out pid) < 0 || pid == 0) continue;
                    var volume = control as ISimpleAudioVolume;
                    float value; bool muted;
                    if (volume != null && volume.GetMasterVolume(out value) == 0 && volume.GetMute(out muted) == 0)
                        result.Add(new MixerSession(volume, pid, value, muted));
                }
            }
            catch (Exception ex) { Bar.Log(ex); }
            return result;
        }
    }

    sealed class DesktopMedia : IDisposable
    {
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd, ref N.POINT point);
        [DllImport("user32.dll")] static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll", SetLastError = true)] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

        sealed class Surface : System.Windows.Forms.NativeWindow, IDisposable
        {
            public Surface(IntPtr parent, int x, int y, int width, int height)
            {
                CreateHandle(new System.Windows.Forms.CreateParams
                {
                    Caption = "Flowbar wallpaper surface", Parent = parent,
                    X = x, Y = y, Width = width, Height = height,
                    Style = 0x40000000 | 0x08000000 | 0x02000000, // CHILD, DISABLED, CLIPCHILDREN
                    ExStyle = 0x80000 | 0x08000000 // LAYERED, NOACTIVATE
                });
                if (!SetLayeredWindowAttributes(Handle, 0, 255, 2))
                {
                    int error = Marshal.GetLastWin32Error();
                    DestroyHandle();
                    throw new System.ComponentModel.Win32Exception(error, "Не удалось включить слой поверх обоев");
                }
            }
            public void Dispose() { if (Handle != IntPtr.Zero) DestroyHandle(); }
        }

        readonly List<HwndSource> hosts = new List<HwndSource>();
        readonly List<Surface> surfaces = new List<Surface>();
        MediaPlayer video;
        DispatcherTimer gifTimer;
        readonly IntPtr parent;
        bool disposed;
        public bool Available { get { return !disposed && IsWindow(parent) && hosts.All(host => IsWindow(host.Handle)); } }

        static IntPtr DesktopLayer(out IntPtr insertAfter)
        {
            insertAfter = new IntPtr(1); // HWND_BOTTOM: dedicated wallpaper container on older desktops
            var progman = N.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null);
            if (progman == IntPtr.Zero) return IntPtr.Zero;
            var icons = N.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (icons != IntPtr.Zero && N.FindWindowEx(progman, icons, "WorkerW", null) != IntPtr.Zero)
            {
                // Raised desktop: Explorer composites its wallpaper WorkerW separately. Insert our
                // layered child below DefView and above WorkerW, so the icons keep their input.
                insertAfter = icons;
                return progman;
            }
            var iconHost = icons == IntPtr.Zero ? IntPtr.Zero : progman;
            for (var worker = IntPtr.Zero; iconHost == IntPtr.Zero && (worker = N.FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero;)
            {
                icons = N.FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (icons != IntPtr.Zero) iconHost = worker;
            }
            if (iconHost == IntPtr.Zero) return IntPtr.Zero;
            var layer = N.FindWindowEx(IntPtr.Zero, iconHost, "WorkerW", null);
            if (layer != IntPtr.Zero && IsWindowVisible(layer) && N.FindWindowEx(layer, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
                return layer;
            return IntPtr.Zero;
        }

        public DesktopMedia(string path, Action<string> failure)
        {
            IntPtr insertAfter;
            parent = DesktopLayer(out insertAfter);
            if (parent == IntPtr.Zero) throw new InvalidOperationException("Слой рабочего стола недоступен");
            bool layered = insertAfter != new IntPtr(1);
            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                BitmapDecoder decoder = null;
                if (ext == ".gif" || ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp")
                    using (var stream = File.OpenRead(path))
                        decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                ImageSource source;
                if (decoder == null)
                {
                    video = new MediaPlayer { IsMuted = true, Volume = 0 };
                    var drawing = new VideoDrawing { Player = video };
                    video.MediaOpened += (s, e) =>
                    {
                        if (video.NaturalVideoWidth <= 0 || video.NaturalVideoHeight <= 0)
                        {
                            failure("Файл не содержит видеодорожки.");
                            return;
                        }
                        drawing.Rect = new Rect(0, 0, video.NaturalVideoWidth, video.NaturalVideoHeight);
                    };
                    video.MediaEnded += (s, e) => { video.Position = TimeSpan.Zero; video.Play(); };
                    video.MediaFailed += (s, e) => { if (!disposed) failure("Windows не смогла воспроизвести видео. Попробуйте MP4 с кодеком H.264."); };
                    source = new DrawingImage(drawing);
                }
                else source = decoder.Frames[0];
                var images = new List<Image>();
                foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                {
                    var bounds = screen.Bounds;
                    var at = new N.POINT { X = bounds.Left, Y = bounds.Top };
                    ScreenToClient(parent, ref at);
                    var context = SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(parent));
                    HwndSource host;
                    Surface surface = null;
                    try
                    {
                        if (layered)
                        {
                            surface = new Surface(parent, at.X, at.Y, bounds.Width, bounds.Height);
                            surfaces.Add(surface);
                        }
                        host = new HwndSource(new HwndSourceParameters("Flowbar Desktop Media", bounds.Width, bounds.Height)
                        {
                            ParentWindow = surface == null ? parent : surface.Handle,
                            PositionX = surface == null ? at.X : 0, PositionY = surface == null ? at.Y : 0,
                            WindowStyle = 0x40000000 | 0x04000000 | 0x08000000
                        });
                    }
                    finally { if (context != IntPtr.Zero) SetThreadDpiAwarenessContext(context); }
                    hosts.Add(host);
                    var stage = new Grid { Background = Brushes.Black, ClipToBounds = true, IsHitTestVisible = false };
                    var image = new Image { Source = source, Stretch = Stretch.UniformToFill };
                    stage.Children.Add(image);
                    images.Add(image);
                    host.RootVisual = stage;
                    if (surface != null && !N.SetWindowPos(host.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x17 | 0x40))
                        throw new InvalidOperationException("Не удалось показать содержимое рабочего стола");
                    if (!N.SetWindowPos(surface == null ? host.Handle : surface.Handle, insertAfter, at.X, at.Y, bounds.Width, bounds.Height, 0x10 | 0x40))
                        throw new InvalidOperationException("Не удалось разместить слой рабочего стола");
                }
                if (video != null) { video.Open(new Uri(path, UriKind.Absolute)); video.Play(); }
                else if (decoder.Frames.Count > 1) AnimateGif(images, decoder.Frames);
            }
            catch { Dispose(); throw; }
        }

        void AnimateGif(IList<Image> images, IList<BitmapFrame> frames)
        {
            var delays = frames.Select(frame =>
            {
                var meta = frame.Metadata as BitmapMetadata;
                object delay = meta == null ? null : meta.GetQuery("/grctlext/Delay");
                return TimeSpan.FromMilliseconds(delay is ushort && (ushort)delay > 1 ? (ushort)delay * 10 : 100);
            }).ToList();
            int i = 0;
            gifTimer = new DispatcherTimer { Interval = delays[0] };
            gifTimer.Tick += (s, e) =>
            {
                i = (i + 1) % frames.Count;
                foreach (var image in images) image.Source = frames[i];
                gifTimer.Interval = delays[i];
            };
            gifTimer.Start();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (gifTimer != null) gifTimer.Stop();
            if (video != null) video.Close();
            foreach (var host in hosts) host.Dispose();
            foreach (var surface in surfaces) surface.Dispose();
        }
    }
}
