using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static LumaDock.Visuals;

namespace LumaDock;

internal sealed class FlipDigit : FrameworkElement
{
    private static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        "Progress", typeof(double), typeof(FlipDigit), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));
    private readonly Brush ink, top, bottom;
    private readonly double fontSize, radius;
    private char current, previous;

    public FlipDigit(bool gallery, Brush ink, bool light)
    {
        Width = gallery ? 30 : 22;
        Height = gallery ? 50 : 34;
        radius = gallery ? 7 : 5;
        fontSize = gallery ? 32 : 23;
        this.ink = ink;
        top = Brush(light ? "#E4E5E7" : "#3A3A3F");
        bottom = Brush(light ? "#D9DADD" : "#2B2B2F");
        VerticalAlignment = VerticalAlignment.Center;
    }

    public void SetDigit(char digit)
    {
        if (current == digit) return;
        previous = current;
        current = digit;
        AutomationProperties.SetName(this, digit.ToString());
        if (previous != '\0' && IsVisible && SystemParameters.ClientAreaAnimation)
            BeginAnimation(ProgressProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            });
        else InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (current == '\0') return;
        double progress = (double)GetValue(ProgressProperty);
        double half = ActualHeight / 2;
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        var upper = new Rect(0, 0, ActualWidth, half);
        var lower = new Rect(0, half, ActualWidth, half);
        dc.PushClip(new RectangleGeometry(bounds, radius, radius));
        DrawHalf(upper, top, current);
        DrawHalf(lower, bottom, progress < 1 ? previous : current);
        if (progress < 1)
        {
            double scale = Math.Abs(Math.Cos(Math.PI * progress));
            dc.PushTransform(new ScaleTransform(1, scale, 0, half));
            if (progress < .5) DrawHalf(upper, top, previous);
            else DrawHalf(lower, bottom, current);
            dc.Pop();
        }
        dc.DrawRectangle(Brush("#76000000"), null, new Rect(0, half - .5, ActualWidth, 1));
        dc.Pop();

        void DrawHalf(Rect part, Brush face, char digit)
        {
            dc.PushClip(new RectangleGeometry(part));
            dc.DrawRectangle(face, null, part);
            var label = new FormattedText(digit.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Font, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), fontSize, ink,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(label, new Point((ActualWidth - label.Width) / 2, (ActualHeight - label.Height) / 2));
            dc.Pop();
        }
    }
}
