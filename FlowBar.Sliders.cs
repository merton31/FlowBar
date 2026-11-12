using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FlowBar
{
    partial class Bar
    {
        // Штатный Slider сохраняет клавиатуру, UI Automation и команды PART_Track.
        // Рисунок ручки отделён от положения значения и никогда не меняет раскладку.
        internal sealed class ElasticSlider : Slider
        {
            const double Inset = 10;
            static readonly Style ElasticStyle = (Style)System.Windows.Markup.XamlReader.Parse(
                @"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                         xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Slider'>
                    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
                    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'>
                        <Grid Background='Transparent'>
                            <Track x:Name='PART_Track' IsHitTestVisible='False' Opacity='0'>
                                <Track.Thumb><Thumb Width='20' Height='12'/></Track.Thumb>
                            </Track>
                        </Grid>
                    </ControlTemplate></Setter.Value></Setter>
                </Style>");

            static readonly Color Violet = Color.FromRgb(0xAD, 0x95, 0xFF);
            static readonly Color DeepViolet = Color.FromRgb(0x61, 0x39, 0xC5);
            static readonly Typeface ValueFace = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            Color palette = Colors.Transparent;
            Pen rail, filled, disabled, spring, springShade, trail, focus;
            Brush handle, active, disabledHandle;
            bool dragging, settling;
            Point pullStart;
            double anchor, aimedValue, tension;
            Vector deflection, releaseFrom, releaseBend;
            double flightSeconds, releaseSeconds;
            long releasedAt;
            const double LandingSeconds = 0.13;
            FormattedText valueLabel;
            string labelText;
            double labelDpi;

            public Action DragStarted, DragEnded;
            public Func<double, string> FormatValue;

            public ElasticSlider(bool compact = false)
            {
                Style = ElasticStyle;
                Height = compact ? 16 : 36;
                Cursor = Cursors.Hand;
                Foreground = Frozen(compact ? Cur.Fg : CardFg, 255);
                SmallChange = 1;
                LargeChange = 10;
                Unloaded += (s, e) => { dragging = false; StopSettling(); };
                IsVisibleChanged += (s, e) => { if (!IsVisible) StopSettling(); };
                IsEnabledChanged += (s, e) => { if (!IsEnabled) StopSettling(); InvalidateVisual(); };
                IsKeyboardFocusedChanged += (s, e) => InvalidateVisual();
                MouseEnter += (s, e) => InvalidateVisual();
                MouseLeave += (s, e) => InvalidateVisual();
            }

            double RailLength { get { return Math.Max(0, ActualWidth - 2 * Inset); } }
            double CenterY { get { return ActualHeight * (ActualHeight < 24 ? 0.5 : 0.44); } }
            double PositionForValue(double value)
            {
                double fraction = Maximum > Minimum ? (value - Minimum) / (Maximum - Minimum) : 0;
                return Inset + RailLength * (IsDirectionReversed ? 1 - fraction : fraction);
            }

            double ValueForPosition(double x) { return ValueAt(x, RailLength, Minimum, Maximum, IsDirectionReversed, IsSnapToTickEnabled ? TickFrequency : 0, Value); }

            // значение в точке x дорожки; tick > 0 — округление до шага, крайнее значение всегда достижимо
            internal static double ValueAt(double x, double rail, double min, double max, bool reversed, double tick, double current)
            {
                if (rail <= 0 || max <= min) return current;
                double fraction = Math.Max(0, Math.Min(1, (x - Inset) / rail));
                if (reversed) fraction = 1 - fraction;
                double value = min + fraction * (max - min);
                if (tick > 0)
                {
                    double step = Math.Round((value - min) / tick, MidpointRounding.AwayFromZero);
                    double nearest = Math.Min(max, min + step * tick);
                    value = max - value <= Math.Abs(value - nearest) ? max : nearest;
                }
                return value;
            }

            // смещение цели от якоря: тянем влево — цель уходит вправо, чем сильнее натяжение, тем дальше
            internal static double Shot(Vector pull, double rail, out double tension)
            {
                tension = Math.Min(1, pull.Length / Math.Max(1, Math.Min(100, rail * 0.48)));
                return -pull.X * (0.65 + 1.15 * tension);
            }

            protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
            {
                if (!IsEnabled || !CaptureMouse()) return;
                Focus();
                StartPull(e.GetPosition(this));
                e.Handled = true;
            }

            void StartPull(Point point)
            {
                StopSettling();
                dragging = true;
                pullStart = point;
                aimedValue = Math.Abs(point.X - PositionForValue(Value)) <= Inset ? Value : ValueForPosition(point.X);
                anchor = PositionForValue(aimedValue);
                if (DragStarted != null) DragStarted();
                InvalidateVisual();
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (!dragging || !IsMouseCaptured) return;
                PullTo(e.GetPosition(this));
                e.Handled = true;
            }

            void PullTo(Point point)
            {
                Vector pull = point - pullStart;
                double distance = pull.Length;
                // Мёртвая зона отделяет прицеливание от обычного клика по дорожке.
                if (distance <= 3 || RailLength <= 0) pull = new Vector();
                double shot = Shot(pull, RailLength, out tension);
                aimedValue = ValueForPosition(anchor + shot);
                double clearance = ActualHeight < 24 ? 4 : 5;
                double headX = Math.Max(clearance, Math.Min(ActualWidth - clearance, anchor + pull.X));
                double pullY = Math.Abs(pull.Y) < 3 ? Math.Abs(pull.X) * 0.19 : pull.Y;
                deflection = Motion ? new Vector(headX - PositionForValue(aimedValue),
                    Math.Max(clearance - CenterY, Math.Min(ActualHeight - CenterY - clearance, pullY))) : new Vector();
                InvalidateVisual();
            }

            protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
            {
                if (!dragging) { base.OnMouseLeftButtonUp(e); return; }
                PullTo(e.GetPosition(this));
                FinishPull();
                e.Handled = true;
            }

            void FinishPull()
            {
                dragging = false;
                SetCurrentValue(ValueProperty, aimedValue);
                ReleaseMouseCapture();
                BeginSettling();
                if (DragEnded != null) DragEnded();
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

            void BeginSettling()
            {
                if (Motion && IsEnabled && IsVisible && tension > 0 && deflection.Length > 0.7)
                {
                    releaseFrom = deflection;
                    releaseBend = TrajectoryBend(releaseFrom);
                    flightSeconds = (ExpressiveMotion ? 0.16 : 0.14) + tension * 0.13;
                    releaseSeconds = 0;
                    releasedAt = Stopwatch.GetTimestamp();
                    settling = true;
                    CompositionTarget.Rendering += SettleFrame;
                }
                else StopSettling();
                InvalidateVisual();
            }

            void SettleFrame(object sender, EventArgs e)
            {
                AdvanceLaunch((Stopwatch.GetTimestamp() - releasedAt) / (double)Stopwatch.Frequency);
            }

            Vector TrajectoryBend(Vector pull)
            {
                bool upward = pull.Y >= 0;
                double clearance = ActualHeight < 24 ? 4 : 5;
                double room = upward ? CenterY - clearance : ActualHeight - CenterY - clearance;
                double height = Math.Max(0, room) * (0.30 + 0.70 * tension);
                // Контрольная точка квадратичной кривой с заданной высотой вершины.
                double lift = height + Math.Sqrt(height * (height + Math.Abs(pull.Y)));
                return new Vector(pull.X * 0.38, upward ? -lift : lift);
            }

            static Vector FlightOffset(Vector start, Vector bend, double progress)
            {
                double remaining = 1 - progress;
                return start * (remaining * remaining) + bend * (2 * progress * remaining);
            }

            double FlightProgress(double seconds)
            {
                double time = Math.Max(0, Math.Min(1, seconds / flightSeconds));
                return time * (0.50 + 0.50 * time);
            }

            void AdvanceLaunch(double seconds)
            {
                if (!Motion || seconds >= flightSeconds + LandingSeconds) StopSettling();
                else
                {
                    releaseSeconds = seconds;
                    if (seconds < flightSeconds)
                        deflection = FlightOffset(releaseFrom, releaseBend, FlightProgress(seconds));
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

            void StopSettling()
            {
                if (settling) CompositionTarget.Rendering -= SettleFrame;
                settling = false;
                deflection = new Vector();
                tension = 0;
                releaseSeconds = 0;
            }

            static Pen Line(Color color, byte opacity, double thickness, bool dotted = false)
            {
                var pen = new Pen(Frozen(color, opacity), thickness);
                pen.StartLineCap = pen.EndLineCap = pen.DashCap = PenLineCap.Round;
                if (dotted) pen.DashStyle = new DashStyle(new double[] { 0, 3.5 }, 0);
                pen.Freeze();
                return pen;
            }

            void UpdatePalette()
            {
                var color = Foreground as SolidColorBrush;
                var next = color == null ? CardFg : color.Color;
                if (rail != null && palette == next) return;
                palette = next;
                var purple = next.R + next.G + next.B > 380 ? Violet : DeepViolet;
                handle = Frozen(next, 255);
                active = Frozen(purple, 255);
                disabledHandle = Frozen(next, 100);
                rail = Line(next, 58, 1);
                filled = Line(next, 214, 1.35);
                disabled = Line(next, 102, 1.1);
                spring = Line(purple, 215, 1.25);
                springShade = Line(purple, 103, 1.0);
                trail = Line(purple, 176, 1.0, true);
                focus = Line(purple, 214, 1);
                valueLabel = null;
            }

            static void Curve(DrawingContext dc, Pen pen, Point from, Point first, Point second, Point end)
            {
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure(from, false, false);
                    path.BezierTo(first, second, end, true, false);
                }
                dc.DrawGeometry(null, pen, geometry);
            }

            void DrawTrajectory(DrawingContext dc, Point target, Vector pull, Vector bend, bool aiming)
            {
                Curve(dc, trail, target + pull, target + (pull + bend * 2) / 3,
                    target + bend * (2.0 / 3), target);
                if (!aiming) return;
                Vector arrow = FlightOffset(pull, bend, 0.73);
                Vector direction = FlightOffset(pull, bend, 0.85) - arrow;
                if (direction.Length < 2) return;
                direction.Normalize();
                var basePoint = target + arrow - direction * 5;
                var normal = new Vector(-direction.Y, direction.X) * 2.5;
                dc.DrawLine(spring, basePoint + normal, target + arrow);
                dc.DrawLine(spring, basePoint - normal, target + arrow);
            }

            void DrawBands(DrawingContext dc, Point head, Vector pull)
            {
                double baseX = Math.Max(Inset, Math.Min(Inset + RailLength, anchor));
                double spread = Math.Min(6, RailLength * 0.18);
                double left = Math.Max(Inset, baseX - spread), right = Math.Min(Inset + RailLength, baseX + spread);
                Curve(dc, spring, new Point(left, CenterY), new Point(left, CenterY + pull.Y * 0.65),
                    new Point(head.X - spread, head.Y), head);
                Curve(dc, springShade, new Point(right, CenterY), new Point(right, CenterY + pull.Y * 0.65),
                    new Point(head.X + spread, head.Y), head);
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
                    DrawTrajectory(dc, target, pulled ? deflection : releaseFrom,
                        pulled ? TrajectoryBend(deflection) : releaseBend, pulled);
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
                double radius = (ActualHeight < 22 ? 3 : 4) + (IsMouseOver && IsEnabled ? 0.4 : 0);
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
                dc.DrawEllipse(IsEnabled ? (launched || dragging ? active : handle) : disabledHandle, null,
                    head, bead + impact, bead - impact * 0.85);
            }

            void DrawValue(DrawingContext dc, double x, double value)
            {
                var text = FormatValue == null ? Math.Round(value) + "%" : FormatValue(value);
                var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                if (valueLabel == null || labelText != text || labelDpi != dpi)
                {
                    labelText = text;
                    labelDpi = dpi;
                    valueLabel = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, ValueFace, 10, active, dpi);
                }
                dc.DrawText(valueLabel, new Point(Math.Max(0, Math.Min(ActualWidth - valueLabel.Width, x - valueLabel.Width / 2)), 0));
            }
        }
    }
}
