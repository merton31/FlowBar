using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using static LumaDock.Visuals;

namespace LumaDock;

internal sealed partial class DockWindow
{
    private static bool TryReadClock(string value, out TimeSpan time)
    {
        bool parsed = DateTime.TryParseExact(value.Trim(), new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var clock);
        time = clock.TimeOfDay;
        return parsed;
    }

    private void CalendarSurface(Window window, Border shell)
    {
        DateTime selected = DateTime.Today;
        DateTime month = new(selected.Year, selected.Month, 1);
        var heading = Text("", 22, true);
        var agendaTitle = Text("", 15, true);
        var count = Text("", 11, false, Muted);
        var grid = new UniformGrid { Columns = 7, Rows = 7 };
        var entries = Stack();
        var empty = Stack(Symbol("\uE787", 18, Muted), Text("Nothing planned", 13, false, Muted));
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.VerticalAlignment = VerticalAlignment.Center;
        var time = Input("HH:mm", DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture), "Event time");
        time.Width = 60;
        var name = Input("New event", label: "Event name");
        var feedback = Text("", 12, false, Brush("#FF9088"));
        feedback.TextWrapping = TextWrapping.Wrap;
        var panel = new System.Windows.Controls.DockPanel { Margin = new(22) };
        var top = (FrameworkElement)Right(heading, RowStack(
            Button("Calendars", () => CalendarFeeds(Fill)),
            Button("Today", () => Select(DateTime.Today), Brushes.Transparent),
            Named("‹", "Previous month", () => Move(-1)),
            Named("›", "Next month", () => Move(1)), CloseButton(window)));
        top.Margin = new(0, 0, 0, 13);
        System.Windows.Controls.DockPanel.SetDock(top, System.Windows.Controls.Dock.Top);
        panel.Children.Add(top);

        var agenda = new System.Windows.Controls.DockPanel { Margin = new(13) };
        var status = (FrameworkElement)Right(agendaTitle, count);
        status.Margin = new(0, 0, 0, 9);
        System.Windows.Controls.DockPanel.SetDock(status, System.Windows.Controls.Dock.Top);
        agenda.Children.Add(status);
        var add = Named("+", "Add event", Add);
        add.ToolTip = L10n.T("New event");
        var input = new System.Windows.Controls.DockPanel();
        System.Windows.Controls.DockPanel.SetDock(time, System.Windows.Controls.Dock.Left);
        System.Windows.Controls.DockPanel.SetDock(add, System.Windows.Controls.Dock.Right);
        input.Children.Add(time);
        input.Children.Add(add);
        input.Children.Add(name);
        var editors = Stack(feedback, input, Button("Event details…", () => EditCalendar(selected, Fill, time.Text)));
        System.Windows.Controls.DockPanel.SetDock(editors, System.Windows.Controls.Dock.Bottom);
        agenda.Children.Add(editors);
        var eventBody = new Grid();
        var scroll = Scroll(entries);
        eventBody.Children.Add(scroll);
        eventBody.Children.Add(empty);
        agenda.Children.Add(eventBody);
        var agendaCard = Card(agenda, 18, Brush("#222222"));
        agendaCard.Margin = new(16, 0, 0, 0);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(56, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(44, GridUnitType.Star) });
        grid.Margin = new(0, 19, 0, 35);
        columns.Children.Add(grid);
        Grid.SetColumn(agendaCard, 1);
        columns.Children.Add(agendaCard);
        panel.Children.Add(columns);
        shell.Child = panel;
        name.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Add(); } };
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.None && Keyboard.FocusedElement is not TextBox)
            { name.Focus(); e.Handled = true; }
        };
        Fill();

        void Move(int offset) => Select(month.AddMonths(offset));

        void Select(DateTime date)
        {
            selected = date.Date;
            month = new(date.Year, date.Month, 1);
            feedback.Text = "";
            Fill();
        }

        void Add()
        {
            if (string.IsNullOrWhiteSpace(name.Text))
            {
                feedback.Text = L10n.T("Enter an event name.");
                name.Focus();
                return;
            }
            if (!TryReadClock(time.Text, out var clock))
            {
                feedback.Text = L10n.T("Enter a valid time in HH:mm format.");
                time.Focus();
                return;
            }
            var start = selected + clock;
            P.Events.Add(new CalendarEvent { Title = name.Text.Trim(), Start = start, End = start.AddHours(1) });
            model.Save();
            name.Clear();
            feedback.Text = "";
            Fill();
        }

        void Fill()
        {
            heading.Text = month.ToString(L10n.Pair("MMMM, yyyy", "MMMM yyyy"), L10n.Culture);
            grid.Children.Clear();
            foreach (string weekday in new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" })
            {
                var label = Text(L10n.T(weekday).ToUpper(L10n.Culture), 11, true, Muted);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                var pill = Card(label, 13, Row);
                pill.Height = 26;
                pill.Margin = new(3);
                grid.Children.Add(pill);
            }
            int leading = ((int)month.DayOfWeek + 6) % 7;
            int length = DateTime.DaysInMonth(month.Year, month.Month);
            for (int slot = 0; slot < 42; slot++)
            {
                int dayNumber = slot - leading + 1;
                if (dayNumber < 1 || dayNumber > length) { grid.Children.Add(new Border()); continue; }
                var date = month.AddDays(dayNumber - 1);
                bool isSelected = date == selected;
                bool today = date == DateTime.Today;
                bool hasEvent = P.Events.Any(item => OnDate(item, date));
                var label = Text(dayNumber.ToString(L10n.Culture) + (hasEvent ? " ·" : ""), 15, true, date < DateTime.Today ? Muted : Paper);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                var marker = Card(label, 15, today ? Accent(P) : Brushes.Transparent);
                marker.Width = hasEvent ? 37 : 28;
                marker.Height = 28;
                marker.HorizontalAlignment = HorizontalAlignment.Center;
                var tile = Button(marker, () => Select(date), isSelected ? Brush("#38383B") : Row, 12);
                tile.Padding = new(0, 1, 0, 0);
                AutomationProperties.SetName(tile, date.ToString("D", L10n.Culture));
                var frame = Card(tile, 13, Brushes.Transparent, isSelected ? Accent(P) : null);
                frame.Margin = new(3);
                grid.Children.Add(frame);
            }
            var found = P.Events.Where(item => OnDate(item, selected)).OrderBy(item => item.Start).ToArray();
            agendaTitle.Text = selected == DateTime.Today ? L10n.T("Today") : selected.ToString(L10n.Pair("ddd, MMM d", "ddd, d MMM"), L10n.Culture);
            count.Text = found.Length == 0 ? L10n.T("Free") : L10n.F($"{found.Length} events");
            entries.Children.Clear();
            empty.Visibility = found.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var item in found)
            {
                var label = RawText(item.Title, 13, true);
                label.TextWrapping = TextWrapping.Wrap;
                var meta = Text(item.AllDay ? "All day" : item.Start.ToString("HH:mm"), 11, false, Muted);
                var detail = Stack(label, meta);
                if (!string.IsNullOrWhiteSpace(item.Location)) detail.Children.Add(RawText(item.Location, 11, false, Muted));
                var row = Card(item.Source.Length == 0 ? Right(detail, Named("×", "Remove event", () =>
                { P.Events.Remove(item); model.Save(); Fill(); })) : detail, 10, Row);
                row.Padding = new(8);
                row.Margin = new(0, 0, 0, 5);
                entries.Children.Add(row);
            }
        }

        static bool OnDate(CalendarEvent item, DateTime date) => item.Start >= date && item.Start < date.AddDays(1)
            || item.Start < date && item.End > date;
    }

    private static Button Named(string glyph, string name, Action click)
    {
        var button = Button(glyph, click, Brushes.Transparent);
        AutomationProperties.SetName(button, L10n.T(name));
        button.ToolTip = L10n.T(name);
        return button;
    }
}
