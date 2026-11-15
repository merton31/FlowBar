using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FlowBar
{
    partial class Bar
    {
        sealed class TaskSchedule : StackPanel
        {
            public DateTime? Date { get; private set; }
            public TimeSpan? Time { get; private set; }
            DateTime cursor;
            TimeSpan clock;
            readonly Wheel[] wheels = new Wheel[5];
            readonly TextBlock state = CardStatus("");
            readonly Border clearDate, clearTime;

            public TaskSchedule()
            {
                Margin = new Thickness(0, 4, 0, 6);
                var title = Txt("Срок", 12, true);
                title.VerticalAlignment = VerticalAlignment.Center;
                title.Foreground = Frozen(TaskAccent, 255);
                var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                state.Margin = new Thickness(0);
                DockPanel.SetDock(state, Dock.Right); heading.Children.Add(state); heading.Children.Add(title);
                Children.Add(heading);

                var columns = new Grid();
                for (int i = 0; i < 5; i++) columns.ColumnDefinitions.Add(new ColumnDefinition());
                string[] names = { "День", "Месяц", "Год", "Часы", "Минуты" };
                string[] formats = { "dd", "MM", "yyyy" };
                for (int i = 0; i < wheels.Length; i++)
                {
                    int unit = i;
                    var wheel = new Wheel(names[i], offset =>
                    {
                        if (unit < 3) return ShiftDate(cursor, unit, offset).ToString(formats[unit], CultureInfo.InvariantCulture);
                        int part = unit == 3 ? clock.Hours : clock.Minutes;
                        return Wrap(part + offset, unit == 3 ? 24 : 60).ToString("00", CultureInfo.InvariantCulture);
                    }, offset => Change(unit, offset), i < 3 ? 7 : 5);
                    wheels[i] = wheel;
                    Grid.SetColumn(wheel, i); columns.Children.Add(wheel);
                }
                Children.Add(columns);
                clearDate = TaskButton("Снять срок", () => Set(null, null));
                clearTime = TaskButton("Снять время", () => Set(Date, null));
                var clear = CardActions(clearDate, clearTime);
                clear.Margin = new Thickness(0, 5, 0, 0);
                Children.Add(clear);
                Set(null, null);
            }

            public void Set(DateTime? date, TimeSpan? time)
            {
                Date = date.HasValue ? date.Value.Date : (DateTime?)null;
                Time = Date.HasValue ? time : null;
                cursor = Date ?? DateTime.Today;
                clock = Time ?? new TimeSpan(DateTime.Now.Hour, DateTime.Now.Minute, 0);
                Refresh(-1, 0);
            }

            void Change(int unit, int offset)
            {
                if (unit < 3) cursor = ShiftDate(cursor, unit, offset);
                else
                {
                    int hours = unit == 3 ? Wrap(clock.Hours + offset, 24) : clock.Hours;
                    int minutes = unit == 4 ? Wrap(clock.Minutes + offset, 60) : clock.Minutes;
                    clock = new TimeSpan(hours, minutes, 0);
                    Time = clock;
                }
                Date = cursor;
                Refresh(unit, offset);
            }

            void Refresh(int changed, int direction)
            {
                state.Text = !Date.HasValue ? "Без срока" : Time.HasValue ? "Дата и время выбраны" : "Без времени";
                clearDate.Visibility = Date.HasValue ? Visibility.Visible : Visibility.Collapsed;
                clearTime.Visibility = Time.HasValue ? Visibility.Visible : Visibility.Collapsed;
                for (int i = 0; i < wheels.Length; i++)
                    wheels[i].Paint(i < 3 ? Date.HasValue : Time.HasValue, i == changed ? Math.Sign(direction) : 0);
            }

            static int Wrap(int value, int count) { return (value % count + count) % count; }

            static DateTime ShiftDate(DateTime day, int unit, int steps)
            {
                try { return unit == 0 ? day.AddDays(steps) : unit == 1 ? day.AddMonths(steps) : day.AddYears(steps); }
                catch (ArgumentOutOfRangeException) { return day; }
            }

            sealed class Wheel : StackPanel
            {
                readonly Border surface;
                readonly Grid values = new Grid();
                readonly TextBlock[] items = new TextBlock[3];
                readonly Func<int, string> format;
                readonly string name;

                public Wheel(string name, Func<int, string> format, Action<int> move, int page)
                {
                    this.name = name;
                    this.format = format;
                    Margin = new Thickness(0, 0, 4, 0);
                    var label = Txt(name, 10, false);
                    label.HorizontalAlignment = HorizontalAlignment.Center;
                    label.Opacity = 0.76;
                    label.Margin = new Thickness(0, 0, 0, 3);
                    Children.Add(label);

                    var height = 23;
                    for (int i = 0; i < 3; i++)
                    {
                        values.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
                        items[i] = Txt("", i == 1 ? 15 : 11, i == 1);
                        items[i].HorizontalAlignment = HorizontalAlignment.Center;
                        items[i].VerticalAlignment = VerticalAlignment.Center;
                        Grid.SetRow(items[i], i); values.Children.Add(items[i]);
                    }
                    System.Windows.Documents.Typography.SetNumeralAlignment(values, FontNumeralAlignment.Tabular);
                    var baseTint = TaskAccent;
                    var focus = new Border { Background = Frozen(baseTint, 0x18), BorderBrush = Frozen(baseTint, 0x39),
                        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(2, 0, 2, 0),
                        IsHitTestVisible = false };
                    Grid.SetRow(focus, 1);
                    values.Children.Insert(0, focus);
                    values.RenderTransform = new TranslateTransform();
                    surface = new Border { Height = 3 * height, Background = Frozen(baseTint, 0x05), Child = values,
                        CornerRadius = new CornerRadius(8), ClipToBounds = true, Focusable = true, Cursor = Cursors.Hand };
                    surface.ToolTip = "Колесо мыши или стрелки меняют значение";
                    surface.MouseLeftButtonUp += (s, e) =>
                    {
                        surface.Focus(); e.Handled = true;
                        move(Math.Max(-1, Math.Min(1, (int)(e.GetPosition(surface).Y / height) - 1)));
                    };
                    surface.PreviewMouseWheel += (s, e) =>
                    {
                        e.Handled = true;
                        move(-Math.Sign(e.Delta) * Math.Max(1, Math.Abs(e.Delta) / 120));
                    };
                    surface.PreviewKeyDown += (s, e) =>
                    {
                        switch (e.Key)
                        {
                            case System.Windows.Input.Key.Up: move(-1); break;
                            case System.Windows.Input.Key.Down: move(1); break;
                            case System.Windows.Input.Key.PageUp: move(-page); break;
                            case System.Windows.Input.Key.PageDown: move(page); break;
                            case System.Windows.Input.Key.Space:
                            case System.Windows.Input.Key.Enter: move(0); break;
                            default: return;
                        }
                        e.Handled = true;
                    };
                    surface.GotKeyboardFocus += (s, e) => focus.BorderBrush = Frozen(baseTint, 255);
                    surface.LostKeyboardFocus += (s, e) => focus.BorderBrush = Frozen(baseTint, 0x39);
                    Children.Add(surface);
                }

                public void Paint(bool active, int direction)
                {
                    for (int i = 0; i < 3; i++) items[i].Text = format(i - 1);
                    items[0].Opacity = items[2].Opacity = 0.48;
                    items[1].Opacity = active ? 1 : 0.76;
                    items[1].Foreground = Frozen(TaskAccent, 255);
                    System.Windows.Automation.AutomationProperties.SetName(surface, name + ": " + items[1].Text);
                    if (direction != 0 && Motion)
                    {
                        var shift = (TranslateTransform)values.RenderTransform;
                        shift.BeginAnimation(TranslateTransform.YProperty,
                            new DoubleAnimation(direction * 9, 0, TimeSpan.FromMilliseconds(110)) { FillBehavior = FillBehavior.Stop });
                    }
                }
            }
        }
    }
}
