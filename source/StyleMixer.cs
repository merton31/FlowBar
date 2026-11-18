using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using static LumaDock.Visuals;

namespace LumaDock;

// Миксер стилей из FlowBar: слева стили (темы), справа материалы, в центре плитка с превью выбранного сочетания.
// От выбранных слов к плитке тянутся пунктирные линии: изогнутые, а у слова напротив центра плитки прямые.
// Выбор и прозрачность сохраняются сразу, док перерисовывается.
internal sealed class StyleMixer : Border
{
    private const double ColW = 128, Gap = 44, Tile = 108, RowH = 40, Top = 30, Inset = 31;
    private static int previousLeft = -1, previousRight = -1; // выбор до перестройки страницы: линия стартует с прежнего слова
    private readonly string[] leftKeys, rightKeys, leftNames, rightNames;
    private readonly Func<string> leftSelected, rightSelected;
    private readonly Func<int> transparency;
    private readonly Func<Color> accent;
    private readonly List<Button> leftWords = [], rightWords = [];
    private readonly TextBlock leftCaption, rightCaption;
    private readonly MixLine leftLine, rightLine;
    private readonly Grid tile = new() { Width = Tile, Height = Tile, Clip = new RectangleGeometry(new Rect(0, 0, Tile, Tile), 26, 26) };
    private readonly double center;
    private string? tileKey;

    public StyleMixer(string[] leftKeys, string[] leftNames, Func<string> leftSelected, Action<string> leftPick,
        string[] rightKeys, string[] rightNames, Func<string> rightSelected, Action<string> rightPick,
        Func<int> transparency, Func<Color> accent)
    {
        this.leftKeys = leftKeys; this.leftNames = leftNames; this.leftSelected = leftSelected;
        this.rightKeys = rightKeys; this.rightNames = rightNames; this.rightSelected = rightSelected;
        this.transparency = transparency; this.accent = accent;
        int rows = Math.Max(leftKeys.Length, rightKeys.Length);
        double body = Math.Max(rows * RowH, Tile), width = ColW * 2 + Gap * 2 + Tile;
        center = Top + body / 2;
        var canvas = new Canvas { Width = width, Height = Top + body + 14, HorizontalAlignment = HorizontalAlignment.Center };
        double tileX = ColW + Gap;
        leftCaption = Caption("Style", ColW, TextAlignment.Right, 0);
        rightCaption = Caption("Material", ColW, TextAlignment.Left, tileX + Tile + Gap);
        canvas.Children.Add(leftCaption);
        canvas.Children.Add(rightCaption);
        leftLine = new MixLine { Width = width, Height = canvas.Height, LabelX = ColW + 6, TileX = tileX, Center = center, IsHitTestVisible = false };
        rightLine = new MixLine { Width = width, Height = canvas.Height, LabelX = tileX + Tile + Gap - 6, TileX = tileX + Tile, Center = center, IsHitTestVisible = false };
        canvas.Children.Add(leftLine);
        canvas.Children.Add(rightLine);
        var shadow = new Border
        {
            Width = Tile, Height = Tile, CornerRadius = new CornerRadius(26), Child = tile,
            Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 7, Direction = 270, Opacity = .34, Color = Colors.Black }
        };
        Canvas.SetLeft(shadow, tileX);
        Canvas.SetTop(shadow, center - Tile / 2);
        canvas.Children.Add(shadow);
        for (int index = 0; index < leftKeys.Length; index++)
        {
            int slot = index;
            leftWords.Add(Word(canvas, leftNames[index], 0, RowY(index, leftKeys.Length), true, () => leftPick(leftKeys[slot])));
        }
        for (int index = 0; index < rightKeys.Length; index++)
        {
            int slot = index;
            rightWords.Add(Word(canvas, rightNames[index], tileX + Tile + Gap, RowY(index, rightKeys.Length), false, () => rightPick(rightKeys[slot])));
        }
        CornerRadius = new CornerRadius(14);
        Background = Row;
        BorderBrush = Line;
        BorderThickness = new Thickness(1);
        Margin = new Thickness(0, 0, 0, 16);
        Padding = new Thickness(0, 4, 0, 0);
        Child = canvas;
        AutomationProperties.SetName(this, L10n.T("Style mixer"));
        if (previousLeft >= 0 && previousLeft < leftKeys.Length) leftLine.LabelY = RowY(previousLeft, leftKeys.Length);
        if (previousRight >= 0 && previousRight < rightKeys.Length) rightLine.LabelY = RowY(previousRight, rightKeys.Length);
        Repaint(true);
    }

    private double RowY(int index, int count) => center + (index - (count - 1) / 2.0) * RowH;

    private static TextBlock Caption(string text, double width, TextAlignment align, double x)
    {
        var caption = Text(text, 11, false, Muted);
        caption.Width = width;
        caption.TextAlignment = align;
        Canvas.SetLeft(caption, x);
        Canvas.SetTop(caption, 8);
        return caption;
    }

    private static Button Word(Canvas canvas, string text, double x, double y, bool leftSide, Action pick)
    {
        var label = Text(text, 13, false, Paper);
        label.HorizontalAlignment = leftSide ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        var word = Button(label, pick, Brushes.Transparent, 8);
        word.Width = ColW;
        word.Height = 30;
        word.Padding = new Thickness(8, 0, 8, 0);
        word.Cursor = Cursors.Hand;
        Canvas.SetLeft(word, x);
        Canvas.SetTop(word, y - 15);
        canvas.Children.Add(word);
        return word;
    }

    // Перерисовка после выбора: выбранные слова ярче, линии переезжают к ним, плитка плавно меняется
    public void Repaint(bool animate)
    {
        int left = Array.IndexOf(leftKeys, leftSelected()), right = Array.IndexOf(rightKeys, rightSelected());
        Paint(leftWords, leftNames, left, "Style: ");
        Paint(rightWords, rightNames, right, "Material: ");
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x78, 0xF4, 0xF4, 0xF6)), 1) { DashStyle = new DashStyle([3, 3], 0), DashCap = PenLineCap.Flat };
        pen.Freeze();
        Move(leftLine, pen, left, leftKeys.Length, animate);
        Move(rightLine, pen, right, rightKeys.Length, animate);
        previousLeft = left;
        previousRight = right;
        string style = left >= 0 ? leftKeys[left] : "Black", material = right >= 0 ? rightKeys[right] : "theme";
        string key = style + "|" + material + "|" + transparency() + "|" + accent();
        if (key == tileKey) return;
        bool first = tileKey is null;
        tileKey = key;
        var layer = Looks.Tile(style, material, transparency(), accent(), Tile, animate && !first);
        if (first || !animate) { tile.Children.Clear(); tile.Children.Add(layer); return; }
        tile.Children.Add(layer);
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320));
        fade.Completed += (_, _) => { while (tile.Children.Count > 1) tile.Children.RemoveAt(0); };
        layer.BeginAnimation(OpacityProperty, fade);
    }

    private static void Paint(List<Button> words, string[] names, int selected, string prefix)
    {
        for (int index = 0; index < words.Count; index++)
        {
            var label = (TextBlock)words[index].Content;
            label.FontWeight = index == selected ? FontWeights.SemiBold : FontWeights.Normal;
            words[index].Opacity = index == selected ? 1 : .5;
            words[index].Tag = index == selected;
            AutomationProperties.SetName(words[index], L10n.T(prefix.TrimEnd(' ', ':')) + ": " + names[index] + (index == selected ? ", " + L10n.T("selected") : ""));
        }
    }

    private void Move(MixLine line, Pen pen, int selected, int count, bool animate)
    {
        line.Stroke = pen;
        line.Shown = selected >= 0;
        if (selected >= 0)
        {
            double from = line.LabelY, to = RowY(selected, count);
            line.BeginAnimation(MixLine.LabelYProperty, null);
            line.LabelY = to;
            if (animate && Math.Abs(from - to) > .5)
                line.BeginAnimation(MixLine.LabelYProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
        }
        line.InvalidateVisual();
    }

    // Пунктирная линия от слова к плитке; LabelY анимируется — линия «переезжает» к новому слову
    private sealed class MixLine : FrameworkElement
    {
        public static readonly DependencyProperty LabelYProperty = DependencyProperty.Register(nameof(LabelY), typeof(double), typeof(MixLine),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
        public double LabelY { get => (double)GetValue(LabelYProperty); set => SetValue(LabelYProperty, value); }
        public double LabelX, TileX, Center;
        public Pen? Stroke;
        public bool Shown;
        protected override void OnRender(DrawingContext dc)
        {
            if (!Shown || Stroke is null) return;
            double attach = Math.Max(Center - Tile / 2 + Inset, Math.Min(Center + Tile / 2 - Inset, LabelY)), middle = (LabelX + TileX) / 2;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(LabelX, LabelY), false, false);
                context.BezierTo(new Point(middle, LabelY), new Point(middle, attach), new Point(TileX, attach), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, Stroke, geometry);
        }
    }
}
