using System;
using System.Windows;
using System.Windows.Media;

namespace LumaDock;

// Индикатор открытых окон под значком программы, как в FlowBar 1.x: короткие чёрточки по числу окон на этом мониторе (не больше двух),
// у активного окна чёрточка длиннее и цвета акцента; окна на других мониторах — полые прямоугольные кольца справа.
internal sealed class WindowIndicator : FrameworkElement
{
    private readonly int local, remote;
    private readonly double localWidth;
    private readonly Brush mark, rim;
    private readonly Pen ring, ringRim;

    public WindowIndicator(int local, int remote, bool active, double tileHeight, Color accent, bool lightTiles)
    {
        this.local = Math.Min(2, local);
        this.remote = Math.Min(2, remote);
        localWidth = active ? Math.Max(7, Math.Round(tileHeight * (this.local == 1 ? .31 : .16))) : 4;
        if (remote > 0) localWidth = Math.Min(localWidth, 8);
        Width = 2 + this.local * localWidth + this.remote * 7 + Math.Max(0, this.local + this.remote - 1) * 2;
        Height = 6;
        Margin = new(0, 0, 0, 1);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        // чёрточка видна на любой подложке: акцент у активного окна, иначе белая на тёмной плитке и чёрная на светлой, с тонкой каймой противоположного цвета
        var body = active ? accent : lightTiles ? Colors.Black : Colors.White;
        var edge = Luminance(body) >= .18 ? Colors.Black : Colors.White;
        mark = Frozen(body);
        rim = Frozen(edge);
        ring = new Pen(mark, 1.25);
        ringRim = new Pen(rim, 2.65);
        ring.Freeze();
        ringRim.Freeze();
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte value)
        {
            double s = value / 255.0;
            return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double x = 1;
        for (int i = 0; i < local; i++)
        {
            dc.DrawRoundedRectangle(rim, null, new Rect(x - .75, 1, localWidth + 1.5, 4), 2, 2);
            dc.DrawRoundedRectangle(mark, null, new Rect(x, 1.75, localWidth, 2.5), 1.25, 1.25);
            x += localWidth + 2;
        }
        for (int i = 0; i < remote; i++)
        {
            var frame = new Rect(x + .5, 1.4, 6, 3.2);
            dc.DrawRoundedRectangle(null, ringRim, frame, 1.6, 1.6);
            dc.DrawRoundedRectangle(null, ring, frame, 1.6, 1.6);
            x += 9;
        }
    }
}
