using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace FlowBar
{
    partial class Bar
    {
        // Выбор стиля «миксер»: слева темы, справа материал, в центре плитка с превью выбранного сочетания.
        // От выбранных слов к плитке тянутся пунктирные линии: изогнутые, а у слова напротив центра плитки прямые.
        // Цвета берутся из текущей темы и акцента: подложка, текст, линии и сама плитка меняются вместе с ними.
        internal static class MixGeometry
        {
            public const double Row = 44, TileSize = 108, Inset = 31;
            // центр строки списка: список из n слов выровнен по центру плитки
            public static double RowY(int index, int count, double center) { return center + (index - (count - 1) / 2.0) * Row; }
            // куда линия входит в плитку: у слов за краем плитки — не выше и не ниже закруглений
            public static double AttachY(double labelY, double center) { return Math.Max(center - TileSize / 2 + Inset, Math.Min(center + TileSize / 2 - Inset, labelY)); }
        }

        internal static Color MixColor(Color a, Color b, double t)
        {
            return Color.FromArgb(255, (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        internal static Color HueShift(Color c, double deg)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min, h = 0;
            if (d > 1e-9) { if (max == r) h = ((g - b) / d) % 6; else if (max == g) h = (b - r) / d + 2; else h = (r - g) / d + 4; h *= 60; }
            h = ((h + deg) % 360 + 360) % 360;
            double s = max <= 0 ? 0 : d / max, cc = max * s, x = cc * (1 - Math.Abs((h / 60) % 2 - 1)), m = max - cc, r1, g1, b1;
            if (h < 60) { r1 = cc; g1 = x; b1 = 0; } else if (h < 120) { r1 = x; g1 = cc; b1 = 0; } else if (h < 180) { r1 = 0; g1 = cc; b1 = x; }
            else if (h < 240) { r1 = 0; g1 = x; b1 = cc; } else if (h < 300) { r1 = x; g1 = 0; b1 = cc; } else { r1 = cc; g1 = 0; b1 = x; }
            return Color.FromArgb(c.A, (byte)Math.Round((r1 + m) * 255), (byte)Math.Round((g1 + m) * 255), (byte)Math.Round((b1 + m) * 255));
        }

        // пунктирная линия от слова к плитке; LabelY анимируется — линия «переезжает» к новому слову
        sealed class MixLine : FrameworkElement
        {
            public static readonly DependencyProperty LabelYProperty = DependencyProperty.Register("LabelY", typeof(double), typeof(MixLine),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
            public double LabelY { get { return (double)GetValue(LabelYProperty); } set { SetValue(LabelYProperty, value); } }
            public double LabelX, TileX, Center; public Pen Stroke; public bool Shown;
            protected override void OnRender(DrawingContext dc)
            {
                if (!Shown || Stroke == null) return;
                double ay = MixGeometry.AttachY(LabelY, Center), mid = (LabelX + TileX) / 2;
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(LabelX, LabelY), false, false);
                    c.BezierTo(new Point(mid, LabelY), new Point(mid, ay), new Point(TileX, ay), true, false);
                }
                g.Freeze(); dc.DrawGeometry(null, Stroke, g);
            }
        }

        static int mixPrevLeft = -1, mixPrevRight = -1; // выбор до перестройки страницы: линия стартует с прежнего слова

        sealed class StyleMixer : Border
        {
            const double ColW = 150, Gap = 64, Tile = MixGeometry.TileSize, Top = 36;
            readonly string[] lk, rk, ln, rn; readonly Func<string> lsel, rsel;
            readonly List<Press> lb = new List<Press>(), rb = new List<Press>();
            readonly TextBlock leftCap, rightCap;
            readonly MixLine lineL, lineR; readonly Grid tile; readonly double center;
            string tileKey;

            public StyleMixer(string[] leftKeys, string[] leftNames, Func<string> leftSel, Action<string> leftPick, Action<string, bool> leftOver,
                string[] rightKeys, string[] rightNames, Func<string> rightSel, Action<string> rightPick)
            {
                lk = leftKeys; ln = leftNames; lsel = leftSel; rk = rightKeys; rn = rightNames; rsel = rightSel;
                int rows = Math.Max(lk.Length, rk.Length);
                double body = Math.Max(rows * MixGeometry.Row, Tile), width = ColW * 2 + Gap * 2 + Tile; center = Top + body / 2;
                var canvas = new Canvas { Width = width, Height = Top + body + 16, HorizontalAlignment = HorizontalAlignment.Center };
                double tileX = ColW + Gap;
                leftCap = new TextBlock { Text = "Тема", FontSize = 11, Width = ColW, TextAlignment = TextAlignment.Right }; Canvas.SetLeft(leftCap, 0); Canvas.SetTop(leftCap, 10);
                rightCap = new TextBlock { Text = "Материал", FontSize = 11, Width = ColW }; Canvas.SetLeft(rightCap, tileX + Tile + Gap); Canvas.SetTop(rightCap, 10);
                canvas.Children.Add(leftCap); canvas.Children.Add(rightCap);
                lineL = new MixLine { Width = width, Height = canvas.Height, LabelX = ColW + 8, TileX = tileX, Center = center, IsHitTestVisible = false };
                lineR = new MixLine { Width = width, Height = canvas.Height, LabelX = tileX + Tile + Gap - 8, TileX = tileX + Tile, Center = center, IsHitTestVisible = false };
                canvas.Children.Add(lineL); canvas.Children.Add(lineR);
                tile = new Grid { Width = Tile, Height = Tile, Clip = new RectangleGeometry(new Rect(0, 0, Tile, Tile), 26, 26) };
                var shadow = new Border { Width = Tile, Height = Tile, CornerRadius = new CornerRadius(26), Child = tile, Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 8, Direction = 270, Opacity = 0.28, Color = Colors.Black } };
                Canvas.SetLeft(shadow, tileX); Canvas.SetTop(shadow, center - Tile / 2); canvas.Children.Add(shadow);
                for (int i = 0; i < lk.Length; i++) { int k = i; lb.Add(Word(canvas, ln[i], 0, MixGeometry.RowY(i, lk.Length, center), true, () => leftPick(lk[k]), over => { if (leftOver != null) leftOver(lk[k], over); })); }
                for (int i = 0; i < rk.Length; i++) { int k = i; rb.Add(Word(canvas, rn[i], tileX + Tile + Gap, MixGeometry.RowY(i, rk.Length, center), false, () => rightPick(rk[k]), null)); }
                CornerRadius = new CornerRadius(14); BorderThickness = new Thickness(1); Margin = new Thickness(0, 10, 0, 0); Child = canvas;
                // после перестройки страницы линии стартуют с прежнего слова и плавно переезжают к выбранному
                if (Motion && mixPrevLeft >= 0 && mixPrevLeft < lk.Length) lineL.LabelY = MixGeometry.RowY(mixPrevLeft, lk.Length, center);
                if (Motion && mixPrevRight >= 0 && mixPrevRight < rk.Length) lineR.LabelY = MixGeometry.RowY(mixPrevRight, rk.Length, center);
                Repaint(true);
            }

            static Press Word(Canvas canvas, string text, double x, double y, bool leftSide, Action pick, Action<bool> over)
            {
                var t = new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = leftSide ? HorizontalAlignment.Right : HorizontalAlignment.Left, TextTrimming = TextTrimming.CharacterEllipsis };
                var p = new Press { Child = t, Width = ColW, Height = 30, CornerRadius = new CornerRadius(8), Cursor = System.Windows.Input.Cursors.Hand, Invoke = pick };
                p.MouseLeftButtonUp += (s, e) => { e.Handled = true; pick(); };
                p.MouseEnter += (s, e) => { if (over != null) over(true); };
                p.MouseLeave += (s, e) => { if (over != null) over(false); };
                Canvas.SetLeft(p, x); Canvas.SetTop(p, y - 15); canvas.Children.Add(p);
                return p;
            }

            public void Repaint(bool animate)
            {
                var fg = CardFg;
                Background = Frozen(fg, 0x09); BorderBrush = Frozen(fg, 0x19);
                leftCap.Foreground = Frozen(fg, 0x80); rightCap.Foreground = Frozen(fg, 0x80);
                int li = Array.IndexOf(lk, lsel()), ri = Array.IndexOf(rk, rsel());
                Paint(lb, ln, li, "Тема: "); Paint(rb, rn, ri, "Материал: ");
                var pen = new Pen(Frozen(fg, 0x78), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0), DashCap = PenLineCap.Flat }; pen.Freeze();
                MoveLine(lineL, pen, li, lk.Length, animate); MoveLine(lineR, pen, ri, rk.Length, animate);
                mixPrevLeft = li; mixPrevRight = ri;
                string themeKey = li >= 0 ? lk[li] : C("style", "Black"), material = rsel();
                string key = themeKey + "|" + material + "|" + AccentColor + "|" + fg;
                if (key == tileKey) return;
                bool first = tileKey == null; tileKey = key;
                var layer = MixTile(themeKey, material, AccentColor, animate && !first);
                if (first || !animate || !Motion) { tile.Children.Clear(); tile.Children.Add(layer); return; }
                tile.Children.Add(layer);
                var fade = SettingsMotion(0, 1, 380);
                fade.Completed += (s, e) => { while (tile.Children.Count > 1) tile.Children.RemoveAt(0); };
                layer.BeginAnimation(OpacityProperty, fade);
            }

            void Paint(List<Press> buttons, string[] names, int selected, string prefix)
            {
                for (int i = 0; i < buttons.Count; i++)
                {
                    var t = (TextBlock)buttons[i].Child;
                    t.Foreground = Frozen(CardFg, 255); t.FontWeight = i == selected ? FontWeights.SemiBold : FontWeights.Normal;
                    buttons[i].Opacity = i == selected ? 1 : 0.5;
                    System.Windows.Automation.AutomationProperties.SetName(buttons[i], prefix + names[i] + (i == selected ? ", выбрано" : ""));
                }
            }

            void MoveLine(MixLine line, Pen pen, int selected, int count, bool animate)
            {
                line.Stroke = pen; line.Shown = selected >= 0;
                if (selected >= 0)
                {
                    double from = line.LabelY, to = MixGeometry.RowY(selected, count, center);
                    line.BeginAnimation(MixLine.LabelYProperty, null); line.LabelY = to;
                    if (animate && Motion && Math.Abs(from - to) > 0.5) line.BeginAnimation(MixLine.LabelYProperty, GlideMotion(from, to));
                }
                line.InvalidateVisual();
            }
        }

        // Превью сочетания: основа цвета темы, пятна света акцента, блик и кромка. Материал меняет характер: матовое — мягче и крупнее, металл — полосы, призма — три оттенка.
        static FrameworkElement MixTile(string themeKey, string material, Color accent, bool animate)
        {
            Sty s; if (!Styles.TryGetValue(themeKey, out s)) s = Cur;
            Color white = Colors.White, black = Colors.Black, fg = Color.FromRgb(s.Fg.R, s.Fg.G, s.Fg.B);
            var bg = Color.FromRgb(s.Bg.R, s.Bg.G, s.Bg.B);
            if (s.Bg.A < 0xC0) bg = MixColor(MixColor(bg, accent, 0.5), Sty.Lum(fg) > 150 ? Color.FromRgb(0x30, 0x34, 0x3C) : Color.FromRgb(0xD8, 0xDC, 0xE4), 0.35); // стеклянные темы: прозрачная основа не нарисуется — берём цвет акцента
            if (material == "metal") bg = MixColor(bg, Color.FromRgb(0x80, 0x84, 0x8A), 0.45);
            var layer = new Grid { Width = MixGeometry.TileSize, Height = MixGeometry.TileSize };
            var baseBrush = new LinearGradientBrush(MixColor(bg, white, 0.14), MixColor(bg, black, 0.22), 45); baseBrush.GradientStops.Insert(1, new GradientStop(bg, 0.55));
            layer.Children.Add(new Border { Background = baseBrush });
            Func<Color, byte, Color> a = (c, al) => Color.FromArgb(al, c.R, c.G, c.B);
            Action<double, double, double, Color, byte> blob = (size, x, y, c, al) =>
            {
                var e = new Ellipse { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(x, y, 0, 0), IsHitTestVisible = false };
                e.Fill = new RadialGradientBrush(a(c, al), a(c, 0));
                var move = new TranslateTransform(); e.RenderTransform = move;
                if (animate && Motion) { move.BeginAnimation(TranslateTransform.XProperty, GlideMotion(-16 + (x > 40 ? 32 : 0), 0)); move.BeginAnimation(TranslateTransform.YProperty, GlideMotion(12 - (y > 40 ? 24 : 0), 0)); }
                layer.Children.Add(e);
            };
            if (material == "metal")
            {
                var bands = new LinearGradientBrush { StartPoint = new Point(0, 0.2), EndPoint = new Point(1, 0.8) };
                bands.GradientStops.Add(new GradientStop(a(white, 0x40), 0)); bands.GradientStops.Add(new GradientStop(a(white, 0), 0.28));
                bands.GradientStops.Add(new GradientStop(a(white, 0x78), 0.5)); bands.GradientStops.Add(new GradientStop(a(white, 0), 0.72)); bands.GradientStops.Add(new GradientStop(a(white, 0x30), 1));
                layer.Children.Add(new Border { Background = bands });
                blob(70, 8, 52, accent, 0x60);
            }
            else if (material == "prism")
            {
                blob(100, -22, -26, HueShift(accent, -70), 0xB0); blob(100, 40, -10, accent, 0xB0); blob(96, 14, 44, HueShift(accent, 70), 0xA8);
            }
            else if (material == "frost")
            {
                blob(140, -34, -42, accent, 0x88); blob(110, 50, 44, fg, 0x38);
                layer.Children.Add(new Border { Background = Frozen(white, 0x1C) });
            }
            else
            {
                blob(124, -26, -34, accent, 0xC0); blob(92, 58, 52, fg, 0x46);
            }
            var sheen = new LinearGradientBrush(a(white, material == "frost" ? (byte)0x18 : (byte)0x34), a(white, 0), 90);
            layer.Children.Add(new Border { Height = MixGeometry.TileSize * 0.55, VerticalAlignment = VerticalAlignment.Top, Background = sheen });
            layer.Children.Add(new Border { CornerRadius = new CornerRadius(26), BorderThickness = new Thickness(1), BorderBrush = Frozen(white, 0x30) });
            return layer;
        }
    }
}
