using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using static LumaDock.Visuals;

namespace LumaDock;

internal sealed class StockTrace : FrameworkElement
{
    private ConnectedData.StockPoint[] points = [];
    private readonly bool compact;
    private int selected = -1;

    public StockTrace(bool compact)
    {
        this.compact = compact;
        if (compact) return;
        MouseMove += (_, e) =>
        {
            if (points.Length < 2) return;
            double fraction = Math.Clamp((e.GetPosition(this).X - 3) / Math.Max(1, ActualWidth - 6), 0, 1);
            int index = (int)Math.Round((points.Length - 1) * fraction);
            if (selected == index) return;
            selected = index;
            var point = points[index];
            ToolTip = L10n.F($"{point.At:dd MMM yyyy} · {point.Close:N2}");
            InvalidateVisual();
        };
        MouseLeave += (_, _) => { selected = -1; InvalidateVisual(); };
    }

    public void Set(ConnectedData.StockPoint[] values)
    {
        points = values;
        selected = -1;
        ToolTip = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (points.Length < 2 || ActualHeight < 5 || ActualWidth < 7) return;
        double left = 3, right = ActualWidth - 3, top = 3, bottom = ActualHeight - (compact ? 2 : 18);
        if (bottom <= top) return;
        decimal lo = points.Min(item => item.Close), hi = points.Max(item => item.Close);
        decimal range = hi - lo;
        var rising = points[^1].Close >= points[0].Close;
        var ink = Brush(rising ? "#49D484" : "#FF626B");
        Point At(int index) => new(left + index * (right - left) / (points.Length - 1),
            bottom - (range == 0 ? .5 : (double)((points[index].Close - lo) / range)) * (bottom - top));
        var line = new StreamGeometry();
        using (var path = line.Open())
        {
            path.BeginFigure(At(0), false, false);
            for (int i = 1; i < points.Length; i++) path.LineTo(At(i), true, false);
        }
        if (!compact)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            var guides = new Pen(Line, 1);
            for (int i = 0; i < 3; i++)
            {
                double y = top + i * (bottom - top) / 2;
                dc.DrawLine(guides, new(left, y), new(right, y));
            }
            DateLabel(points[0], left, false);
            DateLabel(points[^1], right, true);
            if (selected >= 0)
            {
                var point = At(selected);
                dc.DrawLine(new Pen(Muted, 1), new(point.X, top), new(point.X, bottom));
                dc.DrawEllipse(ink, null, point, 3, 3);
            }
        }
        dc.DrawGeometry(null, new Pen(ink, compact ? 1.4 : 2) { LineJoin = PenLineJoin.Round }, line);

        void DateLabel(ConnectedData.StockPoint point, double x, bool end)
        {
            var label = new FormattedText(point.At.ToString("dd MMM", L10n.Culture),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 11, Muted,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(label, new Point(end ? x - label.Width : x, bottom + 4));
        }
    }
}
