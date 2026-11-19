using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static LumaDock.Visuals;

namespace LumaDock;

// Виджет «Вывод звука» в доке: вид зависит от выбранного размера плитки, а звук можно менять, не открывая виджет —
// щелчок по кружку с динамиком включает и выключает звук, ползунок тянется мышью.
internal static partial class Widgets
{
    // узкий — ползунок с кружком, обычный — полоска, широкий — деления, очень широкий — «лесенка» с крупным числом
    internal enum VolumeLook { Knob, Bar, Segments, Ramp }

    internal static VolumeLook VolumeLookFor(double width) =>
        width < 200 ? VolumeLook.Knob : width < 290 ? VolumeLook.Bar : width < 385 ? VolumeLook.Segments : VolumeLook.Ramp;

    // Индикатор громкости; Set задан — его можно тянуть мышью (значение 0–1)
    internal sealed class VolumeStrip : FrameworkElement
    {
        private double fraction;
        private bool silent;
        public VolumeLook Look { get; init; }
        public Brush Fill { get; init; } = Brushes.White;
        public Brush Track { get; init; } = Brushes.Gray;
        public Action<double>? Set { get; init; }

        public void Show(double value, bool muted)
        {
            if (fraction == value && silent == muted) return;
            fraction = value;
            silent = muted;
            InvalidateVisual();
        }

        // у ползунка с кружком края отступают на радиус кружка
        private double Pad => Look == VolumeLook.Knob ? 7 : 0;
        private double Level(MouseEventArgs e) => Math.Clamp((e.GetPosition(this).X - Pad) / Math.Max(1, ActualWidth - 2 * Pad), 0, 1);

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (Set is null) return;
            CaptureMouse();
            e.Handled = true; // плитка не должна считать это нажатием и открывать виджет
            Set(Level(e));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (IsMouseCaptured) Set?.Invoke(Level(e));
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!IsMouseCaptured) return;
            Set?.Invoke(Level(e));
            ReleaseMouseCapture();
            e.Handled = true;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight, f = Math.Clamp(fraction, 0, 1);
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // чтобы мышь ловилась и между делениями
            var fill = silent ? Brush("#6B6B70") : Fill;
            switch (Look)
            {
                case VolumeLook.Knob:
                    double span = w - 2 * Pad, x = Pad + span * f;
                    dc.DrawRoundedRectangle(Track, null, new Rect(Pad, h / 2 - 2, span, 4), 2, 2);
                    dc.DrawRoundedRectangle(fill, null, new Rect(Pad, h / 2 - 2, span * f, 4), 2, 2);
                    dc.DrawEllipse(Brushes.White, new Pen(Brush("#55000000"), 1), new Point(x, h / 2), 6.5, 6.5);
                    break;
                case VolumeLook.Bar:
                    var area = new RectangleGeometry(new Rect(0, 0, w, h), h / 2, h / 2);
                    dc.DrawGeometry(Track, null, area);
                    dc.PushClip(area);
                    dc.DrawRectangle(fill, null, new Rect(0, 0, w * f, h));
                    dc.Pop();
                    break;
                case VolumeLook.Segments:
                    const int count = 20;
                    const double gap = 2;
                    double width = (w - gap * (count - 1)) / count;
                    int lit = (int)Math.Round(f * count);
                    for (int i = 0; i < count; i++)
                        dc.DrawRoundedRectangle(i < lit ? fill : Track, null, new Rect(i * (width + gap), 0, width, h), 2.5, 2.5);
                    break;
                default:
                    int bars = Math.Max(8, (int)(w / 9));
                    double step = w / bars, thick = step * .56;
                    int on = (int)Math.Round(f * bars);
                    for (int i = 0; i < bars; i++)
                    {
                        double tall = h * (.3 + .7 * i / (bars - 1));
                        dc.DrawRoundedRectangle(i < on ? fill : Track, null, new Rect(i * step + (step - thick) / 2, h - tall, thick, tall), thick / 2, thick / 2);
                    }
                    break;
            }
        }
    }

    private static FrameworkElement AudioFace(Widget widget, DockModel model, Brush ink, Brush muted, Brush accent, Brush chip, Brush track, out Action update)
    {
        double width = WidthOf(model.Settings, widget);
        var look = VolumeLookFor(width);
        double inner = width - 22; // от ширины плитки отнимаются поля кнопки и рамка
        var percent = Text("", look == VolumeLook.Ramp ? 22 : 11, true, ink);
        var caption = Text("", 9, true, muted);
        VolumeStrip? strip = null;
        Border? button = null;
        TextBlock? glyph = null;
        void Sync()
        {
            var audio = model.Audio;
            double level = audio?.Volume ?? 0;
            bool off = audio?.Muted == true;
            strip!.Show(level, off);
            percent.Text = audio is null ? "—" : look == VolumeLook.Ramp || !off ? $"{level * 100:F0}%" : L10n.T(look == VolumeLook.Knob ? "Off" : "Muted");
            percent.Foreground = off ? muted : ink;
            caption.Text = off ? L10n.T("Muted") : L10n.T(widget.Id);
            glyph!.Text = off ? "" : "";
            button!.Background = off ? Brush("#7A2E2E") : chip;
        }
        Border Chip(double size, double icon)
        {
            glyph = Symbol("", icon, ink);
            var card = Card(glyph, size / 2, chip);
            card.Width = card.Height = size;
            card.VerticalAlignment = VerticalAlignment.Center;
            card.Cursor = Cursors.Hand;
            card.ToolTip = L10n.T("Mute or unmute");
            card.MouseLeftButtonDown += (_, e) => { card.CaptureMouse(); e.Handled = true; };
            card.MouseLeftButtonUp += (_, e) =>
            {
                if (!card.IsMouseCaptured) return;
                bool hit = card.IsMouseOver;
                card.ReleaseMouseCapture();
                e.Handled = true;
                if (!hit) return;
                model.Audio = WindowsShell.Audio(null, true);
                Sync();
            };
            return button = card;
        }
        VolumeStrip Strip(double height) => strip = new VolumeStrip
        {
            Look = look, Fill = accent, Track = track, Height = height, Cursor = Cursors.Hand,
            Set = level => { model.Audio = WindowsShell.Audio(level); Sync(); }
        };
        update = Sync;
        FrameworkElement face;
        switch (look)
        {
            case VolumeLook.Knob:
                // кружок, ползунок и число в один ряд
                percent.Width = 34;
                percent.TextAlignment = TextAlignment.Right;
                var knobStrip = Strip(20);
                knobStrip.Width = inner - 80;
                knobStrip.Margin = new(8, 0, 6, 0);
                face = RowStack(Chip(28, 14), knobStrip, percent);
                break;
            case VolumeLook.Bar:
                // название с маленьким кружком и числом, ниже полоска
                var name = Text(widget.Id, 11, true, ink);
                name.Margin = new(7, 0, 0, 0);
                var title = RowStack(Chip(22, 12), name);
                var bar = Strip(14);
                bar.Margin = new(0, 5, 0, 0);
                face = Stack(Right(title, percent), bar);
                break;
            case VolumeLook.Segments:
                // большой кружок слева, справа название с числом и деления
                var grid = new Grid { Width = inner };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                var segmentChip = Chip(34, 16);
                var rightSide = Stack(Right(Text(widget.Id, 11, true, ink), percent), Strip(16));
                rightSide.Margin = new(10, 0, 0, 0);
                ((FrameworkElement)rightSide.Children[1]).Margin = new(0, 6, 0, 0);
                Grid.SetColumn(rightSide, 1);
                rightSide.VerticalAlignment = VerticalAlignment.Center;
                grid.Children.Add(segmentChip);
                grid.Children.Add(rightSide);
                face = grid;
                break;
            default:
                // кружок, крупное число с подписью и «лесенка» во всю остальную ширину
                var wide = new Grid { Width = inner };
                wide.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                wide.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                wide.ColumnDefinitions.Add(new ColumnDefinition());
                var rampChip = Chip(36, 17);
                var number = Stack(percent, caption);
                number.Width = 64;
                number.Margin = new(10, 0, 8, 0);
                number.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(number, 1);
                var ramp = Strip(34);
                ramp.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(ramp, 2);
                wide.Children.Add(rampChip);
                wide.Children.Add(number);
                wide.Children.Add(ramp);
                face = wide;
                break;
        }
        face.VerticalAlignment = VerticalAlignment.Center;
        Sync();
        return face;
    }
}
