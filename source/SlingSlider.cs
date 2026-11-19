using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LumaDock;

// Ползунок-рогатка из прошлой панели (FlowBar 1.x): ручку оттягивают мышью и отпускают, цель летит туда же, куда ушла мышь.
// Пунктирная дуга заранее показывает точку приземления, значение применяется при отпускании, короткий клик выбирает точку на дорожке.
// Штатный Slider сохраняется: клавиатура, UI Automation и значения работают как у обычного; рисунок отделён от положения значения.
internal sealed class SlingSlider : Slider
{
    private const double Inset = 10;
    private const double LandingSeconds = 0.13;
    private static readonly Color Ink = Color.FromRgb(0xE8, 0xE8, 0xEA);
    private static readonly Typeface ValueFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static readonly Lazy<ControlTemplate> SlingTemplate = new(() => (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
        @"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                           xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Slider'>
            <Grid Background='Transparent'>
                <Track x:Name='PART_Track' IsHitTestVisible='False' Opacity='0'>
                    <Track.Thumb><Thumb Width='20' Height='12'/></Track.Thumb>
                </Track>
            </Grid>
          </ControlTemplate>"));

    private Color accent = Colors.Transparent;
    private Pen rail = null!, filled = null!, disabled = null!, spring = null!, springShade = null!, trail = null!, focus = null!;
    private Brush handle = null!, active = null!, disabledHandle = null!;
    private bool dragging, settling;
    private Point pullStart;
    private double anchor, aimedValue, tension;
    private Vector deflection, releaseFrom, releaseBend;
    private double flightSeconds, releaseSeconds;
    private long releasedAt;
    private FormattedText? valueLabel;
    private string labelText = "";
    private double labelDpi;

    public Func<double, string>? FormatValue { get; set; }

    private static bool Motion => SystemParameters.ClientAreaAnimation;

    public SlingSlider(Brush accentBrush, double height)
    {
        Template = SlingTemplate.Value;
        Height = height;
        Cursor = Cursors.Hand;
        Foreground = accentBrush;
        SmallChange = 1;
        LargeChange = 10;
        FocusVisualStyle = null;
        Unloaded += (_, _) => { dragging = false; StopSettling(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) StopSettling(); };
        IsEnabledChanged += (_, _) => { if (!IsEnabled) StopSettling(); InvalidateVisual(); };
        IsKeyboardFocusedChanged += (_, _) => InvalidateVisual();
        MouseEnter += (_, _) => InvalidateVisual();
        MouseLeave += (_, _) => InvalidateVisual();
    }

    private double RailLength => Math.Max(0, ActualWidth - 2 * Inset);
    private double CenterY => ActualHeight * (ActualHeight < 24 ? 0.5 : 0.44);

    private double PositionForValue(double value)
    {
        double fraction = Maximum > Minimum ? (value - Minimum) / (Maximum - Minimum) : 0;
        return Inset + RailLength * (IsDirectionReversed ? 1 - fraction : fraction);
    }

    // значение в точке x дорожки, крайние значения всегда достижимы
    internal static double ValueAt(double x, double rail, double min, double max, bool reversed, double current)
    {
        if (rail <= 0 || max <= min) return current;
        double fraction = Math.Clamp((x - Inset) / rail, 0, 1);
        if (reversed) fraction = 1 - fraction;
        return min + fraction * (max - min);
    }

    private double ValueForPosition(double x) => ValueAt(x, RailLength, Minimum, Maximum, IsDirectionReversed, Value);

    // смещение цели от якоря равно смещению мыши (без ускорения): вправо — вправо, влево — влево; натяжение нужно только рисунку
    internal static double Shot(Vector pull, double rail, out double tension)
    {
        tension = Math.Min(1, pull.Length / Math.Max(1, Math.Min(100, rail * 0.48)));
        return pull.X;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!IsEnabled || !CaptureMouse()) return;
        Focus();
        StartPull(e.GetPosition(this));
        e.Handled = true;
    }

    private void StartPull(Point point)
    {
        StopSettling();
        dragging = true;
        pullStart = point;
        aimedValue = Math.Abs(point.X - PositionForValue(Value)) <= Inset ? Value : ValueForPosition(point.X);
        anchor = PositionForValue(aimedValue);
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging || !IsMouseCaptured) return;
        PullTo(e.GetPosition(this));
        e.Handled = true;
    }

    private void PullTo(Point point)
    {
        var pull = point - pullStart;
        // мёртвая зона отделяет прицеливание от обычного клика по дорожке
        if (pull.Length <= 3 || RailLength <= 0) pull = new Vector();
        double shot = Shot(pull, RailLength, out tension);
        aimedValue = ValueForPosition(anchor + shot);
        double clearance = ActualHeight < 24 ? 4 : 5;
        // ручка резинки оттягивается в сторону, противоположную мыши (как в настоящей рогатке), цель летит туда же, куда мышь
        double headX = Math.Max(clearance, Math.Min(ActualWidth - clearance, anchor - pull.X));
        double pullY = Math.Abs(pull.Y) < 3 ? Math.Abs(pull.X) * 0.19 : pull.Y;
        deflection = Motion
            ? new Vector(headX - PositionForValue(aimedValue), Math.Max(clearance - CenterY, Math.Min(ActualHeight - CenterY - clearance, pullY)))
            : new Vector();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!dragging) { base.OnMouseLeftButtonUp(e); return; }
        PullTo(e.GetPosition(this));
        dragging = false;
        SetCurrentValue(ValueProperty, aimedValue);
        ReleaseMouseCapture();
        BeginSettling();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!dragging) return;
        dragging = false;
        StopSettling();
        InvalidateVisual();
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        if (settling && Math.Abs(PositionForValue(newValue) - PositionForValue(oldValue)) > 1) StopSettling();
        InvalidateVisual();
    }

    private void BeginSettling()
    {
        if (Motion && IsEnabled && IsVisible && tension > 0 && deflection.Length > 0.7)
        {
            releaseFrom = deflection;
            releaseBend = TrajectoryBend(releaseFrom);
            flightSeconds = 0.14 + tension * 0.13;
            releaseSeconds = 0;
            releasedAt = Stopwatch.GetTimestamp();
            settling = true;
            CompositionTarget.Rendering += SettleFrame;
        }
        else StopSettling();
        InvalidateVisual();
    }

    private void SettleFrame(object? sender, EventArgs e) => AdvanceLaunch((Stopwatch.GetTimestamp() - releasedAt) / (double)Stopwatch.Frequency);

    private Vector TrajectoryBend(Vector pull)
    {
        bool upward = pull.Y >= 0;
        double clearance = ActualHeight < 24 ? 4 : 5;
        double room = upward ? CenterY - clearance : ActualHeight - CenterY - clearance;
        double height = Math.Max(0, room) * (0.30 + 0.70 * tension);
        // контрольная точка квадратичной кривой с заданной высотой вершины
        double lift = height + Math.Sqrt(height * (height + Math.Abs(pull.Y)));
        return new Vector(pull.X * 0.38, upward ? -lift : lift);
    }

    private static Vector FlightOffset(Vector start, Vector bend, double progress)
    {
        double remaining = 1 - progress;
        return start * (remaining * remaining) + bend * (2 * progress * remaining);
    }

    private double FlightProgress(double seconds)
    {
        double time = Math.Max(0, Math.Min(1, seconds / flightSeconds));
        return time * (0.50 + 0.50 * time);
    }

    private void AdvanceLaunch(double seconds)
    {
        if (!Motion || seconds >= flightSeconds + LandingSeconds) StopSettling();
        else
        {
            releaseSeconds = seconds;
            if (seconds < flightSeconds) deflection = FlightOffset(releaseFrom, releaseBend, FlightProgress(seconds));
            else
            {
                double t = (seconds - flightSeconds) / LandingSeconds;
                double bounce = 4 * t * (1 - t) * (1 - t);
                double targetX = PositionForValue(Value);
                double x = Math.Max(5 - targetX, Math.Min(ActualWidth - 5 - targetX, -releaseFrom.X * 0.10 * bounce));
                deflection = new Vector(x, releaseBend.Y * 0.31 * bounce);
            }
        }
        InvalidateVisual();
    }

    private void StopSettling()
    {
        if (settling) CompositionTarget.Rendering -= SettleFrame;
        settling = false;
        deflection = new Vector();
        tension = 0;
        releaseSeconds = 0;
    }

    private static Pen Line(Color color, byte opacity, double thickness, bool dotted = false)
    {
        var brush = new SolidColorBrush(Color.FromArgb(opacity, color.R, color.G, color.B));
        brush.Freeze();
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, DashCap = PenLineCap.Round };
        if (dotted) pen.DashStyle = new DashStyle([0, 3.5], 0);
        pen.Freeze();
        return pen;
    }

    // Цвет акцента берётся из Foreground (сплошной или первый цвет градиента); перо пересоздаётся только при смене цвета
    private void UpdatePalette()
    {
        var next = Foreground switch
        {
            SolidColorBrush solid => solid.Color,
            GradientBrush { GradientStops.Count: > 0 } gradient => gradient.GradientStops[0].Color,
            _ => Colors.White
        };
        if (rail is not null && accent == next) return;
        accent = next;
        handle = Frozen(Ink, 255);
        active = Frozen(next, 255);
        disabledHandle = Frozen(Ink, 100);
        rail = Line(Ink, 58, 1.2);
        filled = Line(next, 235, 1.8);
        disabled = Line(Ink, 102, 1.4);
        spring = Line(next, 215, 1.25);
        springShade = Line(next, 103, 1.0);
        trail = Line(next, 176, 1.0, true);
        focus = Line(next, 214, 1);
        valueLabel = null;
    }

    private static Brush Frozen(Color color, byte opacity)
    {
        var brush = new SolidColorBrush(Color.FromArgb(opacity, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static void Curve(DrawingContext dc, Pen pen, Point from, Point first, Point second, Point end)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(from, false, false);
            path.BezierTo(first, second, end, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private void DrawTrajectory(DrawingContext dc, Point target, Vector pull, Vector bend, bool aiming)
    {
        Curve(dc, trail, target + pull, target + (pull + bend * 2) / 3, target + bend * (2.0 / 3), target);
        if (!aiming) return;
        var arrow = FlightOffset(pull, bend, 0.73);
        var direction = FlightOffset(pull, bend, 0.85) - arrow;
        if (direction.Length < 2) return;
        direction.Normalize();
        var basePoint = target + arrow - direction * 5;
        var normal = new Vector(-direction.Y, direction.X) * 2.5;
        dc.DrawLine(spring, basePoint + normal, target + arrow);
        dc.DrawLine(spring, basePoint - normal, target + arrow);
    }

    private void DrawBands(DrawingContext dc, Point head, Vector pull)
    {
        double baseX = Math.Max(Inset, Math.Min(Inset + RailLength, anchor));
        double spread = Math.Min(6, RailLength * 0.18);
        double left = Math.Max(Inset, baseX - spread), right = Math.Min(Inset + RailLength, baseX + spread);
        Curve(dc, spring, new Point(left, CenterY), new Point(left, CenterY + pull.Y * 0.65), new Point(head.X - spread, head.Y), head);
        Curve(dc, springShade, new Point(right, CenterY), new Point(right, CenterY + pull.Y * 0.65), new Point(head.X + spread, head.Y), head);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (RailLength <= 0) return;
        UpdatePalette();
        bool aiming = dragging && tension > 0;
        var target = new Point(PositionForValue(dragging ? aimedValue : Value), CenterY);
        var left = new Point(Inset, CenterY);
        var right = new Point(Inset + RailLength, CenterY);
        bool pulled = Motion && aiming;
        bool launched = Motion && settling;
        bool flying = launched && releaseSeconds < flightSeconds;
        dc.DrawLine(rail, left, right);
        if (!pulled && !flying) dc.DrawLine(IsEnabled ? filled : disabled, IsDirectionReversed ? right : left, target);
        var head = pulled || launched ? target + deflection : target;
        if (pulled || launched)
        {
            double fade = pulled || flying ? 1 : Math.Max(0, 1 - (releaseSeconds - flightSeconds) / LandingSeconds);
            dc.PushOpacity(fade);
            DrawTrajectory(dc, target, pulled ? deflection : releaseFrom, pulled ? TrajectoryBend(deflection) : releaseBend, pulled);
            dc.DrawLine(spring, new Point(target.X, CenterY - 3.5), new Point(target.X, CenterY + 3.5));
            dc.Pop();
            if (pulled) DrawBands(dc, head, deflection);
            if (flying && releaseSeconds > 0.018)
            {
                var previous = FlightOffset(releaseFrom, releaseBend, FlightProgress(releaseSeconds - 0.032));
                dc.DrawLine(springShade, target + previous, head);
            }
        }
        if (aiming && ActualHeight >= 28) DrawValue(dc, target.X, aimedValue);
        double radius = (ActualHeight < 22 ? 3.5 : 4.5) + (IsMouseOver && IsEnabled ? 0.4 : 0);
        if (IsKeyboardFocused && !dragging && !launched) dc.DrawEllipse(null, focus, head, radius + 2.5, radius + 2.5);
        double impact = launched && !flying ? Math.Max(0, 1 - (releaseSeconds - flightSeconds) / 0.055) : 0;
        if (impact > 0)
        {
            double ring = radius + 2 + (1 - impact) * 4;
            dc.PushOpacity(impact);
            dc.DrawEllipse(null, spring, target, ring, ring * 0.60);
            dc.Pop();
        }
        double bead = radius + (pulled || flying ? tension * 0.9 : 0);
        dc.DrawEllipse(IsEnabled ? (launched || dragging ? active : handle) : disabledHandle, null, head, bead + impact, bead - impact * 0.85);
    }

    private void DrawValue(DrawingContext dc, double x, double value)
    {
        string text = FormatValue is null ? Math.Round(value).ToString() : FormatValue(value);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (valueLabel is null || labelText != text || labelDpi != dpi)
        {
            labelText = text;
            labelDpi = dpi;
            valueLabel = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ValueFace, 10, active, dpi);
        }
        dc.DrawText(valueLabel, new Point(Math.Max(0, Math.Min(ActualWidth - valueLabel.Width, x - valueLabel.Width / 2)), 0));
    }
}
