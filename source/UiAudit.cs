using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace LumaDock;

internal static partial class Program
{
    private static void AuditInterface(DockWindow dock, DockModel model, string folder)
    {
        using var catalog = Csv("widgets.csv", "language", "category", "id", "caption", "dock face", "gallery face", "click route", "visual evidence");
        using var controls = Csv("controls.csv", "language", "surface", "type", "name", "enabled", "width", "height");
        using var scenarios = Csv("scenarios.csv", "language", "scenario", "result");
        var categories = Widgets.ReferenceCategories.SelectMany(category => category.Catalog.Split('|').Select(id => (category.Name, Id: id))).ToArray();
        if (categories.Length != 107 || categories.Select(item => item.Id).Distinct().Count() != categories.Length)
            throw new InvalidOperationException("The reference catalog is incomplete.");

        foreach (string language in new[] { "en", "ru" })
        {
            L10n.SetLanguage(language);
            foreach (string tab in new[] { "Appearance", "Dock", "Automations", "Sounds", "General" })
            {
                var settings = dock.OpenSettings(tab);
                if (tab == "Automations") InspectChild(settings, "Open", "Renewal dates", language);
                Inspect(settings, "Settings / " + tab, language);
            }
            Inspect(dock.OpenGallery(), "Widget gallery", language);
            Inspect(dock.OpenFolderPin(), "Windows folders", language);
            var variants = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (category, id) in categories.Concat(Widgets.WindowsFolders.Select(id => (Name: "Windows folders", Id: id))))
            {
                var widget = Widgets.Available[id];
                foreach (bool gallery in new[] { false, true })
                {
                    var face = Widgets.Face(widget, model, gallery, out var refresh);
                    refresh();
                    face.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    if (!double.IsFinite(face.DesiredSize.Width) || !double.IsFinite(face.DesiredSize.Height))
                        throw new InvalidOperationException($"Widget face has invalid dimensions: {id}");
                }
                string route;
                if (DockWindow.NativeWidgetAction(id) is not null) route = "Native action assigned; not executed";
                else
                {
                    route = "Window opened";
                    var window = dock.OpenWidget(id) ?? throw new InvalidOperationException($"Widget window missing: {id}");
                    if (id == "Calendar")
                    {
                        InspectChild(window, "Event details…", "New event details", language);
                        InspectChild(window, "Calendars", "Calendar feeds", language);
                    }
                    if (id == "Wallpaper Gallery") InspectChild(window, "Browse online", "Online wallpapers", language);
                    if (id == "AI Limits") InspectChild(window, "Add readings", "AI usage editor", language);
                    if (ConnectedData.Providers.ContainsKey(id))
                        InspectChild(window, "Connect", "Connect / " + id, language);
                    Inspect(window, id, language, id is "Todos" or "Calendar");
                }
                Row(catalog, language, category, id, widget.GalleryCaption, "Constructed and measured", "Constructed and measured",
                    route, id is "Todos" or "Calendar" ? "Captured; reviewed separately" : "No pixel parity assertion");
                string[] styles = Widgets.Styles(id);
                if (styles.Length > 1 && variants.Add(string.Join("|", styles.OrderBy(value => value, StringComparer.Ordinal))))
                    Inspect(dock.OpenWidgetStyles(id), "Style picker / " + id, language);
            }
            VerifyFormInteractions(dock, model, language, scenarios);
        }

        void InspectChild(Window parent, string button, string name, string language)
        {
            var before = Application.Current.Windows.OfType<Window>().ToHashSet();
            parent.UpdateLayout();
            Click(parent, L10n.T(button));
            var child = Application.Current.Windows.OfType<Window>().SingleOrDefault(window => !before.Contains(window))
                ?? throw new InvalidOperationException($"Form did not open: {name}");
            Inspect(child, name, language);
        }

        void Inspect(Window window, string name, string language, bool capture = false)
        {
            try
            {
                window.UpdateLayout();
                if (!window.IsLoaded || window.ActualWidth <= 0 || window.ActualHeight <= 0)
                    throw new InvalidOperationException($"Window failed to lay out: {name}");
                if (capture) Capture(window, Path.Combine(folder, $"{name}-{language}.png"));
                Record(window, name, language);
            }
            finally { window.Close(); }
        }

        void Record(DependencyObject element, string surface, string language)
        {
            if (element is Control control && control.IsVisible &&
                control is ButtonBase or TextBox or PasswordBox or DatePicker or ComboBox or Slider or ProgressBar &&
                (control is not TextBox || control.TemplatedParent is not DatePicker))
            {
                string label = AutomationProperties.GetName(control);
                if (string.IsNullOrWhiteSpace(label))
                    label = control is ContentControl { Content: TextBlock text } ? text.Text :
                        control.ToolTip as string ?? "(unnamed)";
                Row(controls, language, surface, control.GetType().Name, label,
                    control.IsEnabled ? "yes" : "no", control.ActualWidth.ToString("0.#", CultureInfo.InvariantCulture),
                    control.ActualHeight.ToString("0.#", CultureInfo.InvariantCulture));
            }
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
                Record(VisualTreeHelper.GetChild(element, index), surface, language);
        }

        StreamWriter Csv(string filename, params string[] header)
        {
            var writer = new StreamWriter(Path.Combine(folder, filename), false, new UTF8Encoding(true));
            Row(writer, header);
            return writer;
        }
    }

    private static void VerifyFormInteractions(DockWindow dock, DockModel model, string language, StreamWriter results)
    {
        var todos = dock.OpenWidget("Todos")!;
        Edit(todos, "New todo", " ");
        Click(todos, L10n.T("Add task"));
        if (model.Settings.Todos.Count != 0) throw new InvalidOperationException("Blank task was saved.");
        Click(todos, L10n.T("Schedule reminder"));
        todos.UpdateLayout();
        Edit(todos, "New todo", "Audit task");
        Edit(todos, "Reminder time", "24:99");
        Click(todos, L10n.T("Add task"));
        if (model.Settings.Todos.Count != 0 || Field(todos, "New todo").Text != "Audit task")
            throw new InvalidOperationException("Invalid reminder lost the draft or saved a task.");
        Edit(todos, "Reminder time", "9:30");
        Click(todos, L10n.T("Add task"));
        var task = new DockModel(model.Profile).Settings.Todos.Single();
        if (task.Text != "Audit task" || task.Due?.TimeOfDay != TimeSpan.FromHours(9.5))
            throw new InvalidOperationException("Reminder was not saved.");
        todos.Close();
        model.Settings.Todos.Clear();
        model.Save(false);
        Row(results, language, "Todos: blank, invalid input, preserved draft, saved reminder", "PASS");

        var calendar = dock.OpenWidget("Calendar")!;
        Click(calendar, L10n.T("Next month"));
        calendar.UpdateLayout();
        var nextMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1);
        if (FindControl(calendar, nextMonth.ToString("D", L10n.Culture)) is not Button)
            throw new InvalidOperationException("The next month was not displayed.");
        Click(calendar, L10n.T("Today"));
        calendar.UpdateLayout();
        Edit(calendar, "Event name", "Audit event");
        Edit(calendar, "Event time", "not-a-time");
        Click(calendar, L10n.T("Add event"));
        if (model.Settings.Events.Count != 0 || Field(calendar, "Event name").Text != "Audit event")
            throw new InvalidOperationException("Invalid calendar time lost the draft or saved an event.");
        Edit(calendar, "Event time", "11:45");
        Click(calendar, L10n.T("Add event"));
        var item = new DockModel(model.Profile).Settings.Events.Single();
        if (item.Title != "Audit event" || item.Start.TimeOfDay != new TimeSpan(11,45,0) || item.End - item.Start != TimeSpan.FromHours(1))
            throw new InvalidOperationException("Calendar event was not saved.");
        calendar.UpdateLayout();
        Click(calendar, L10n.T("Remove event"));
        if (new DockModel(model.Profile).Settings.Events.Count != 0) throw new InvalidOperationException("Calendar event was not removed.");
        calendar.Close();
        Row(results, language, "Calendar: month navigation, invalid input, preserved draft, add and remove", "PASS");

        var notes = dock.OpenWidget("Notes")!;
        Edit(notes, "Notes", "Line 1\nСтрока 2");
        if (new DockModel(model.Profile).Settings.Notes != Field(notes, "Notes").Text)
            throw new InvalidOperationException("Multiline notes were not saved.");
        notes.Close();
        model.Settings.Notes = "";
        model.Save(false);
        Row(results, language, "Notes: multiline Cyrillic text persists", "PASS");

        var timer = dock.OpenWidget("Timer")!;
        Edit(timer, "Minutes", "17");
        timer.Close();
        timer = dock.OpenWidget("Timer")!;
        if (Field(timer, "Minutes").Text != "17" || model.Timer.Duration != TimeSpan.FromMinutes(17))
            throw new InvalidOperationException("Timer duration did not survive reopening.");
        Edit(timer, "Minutes", "0");
        Click(timer, L10n.T("Start / pause"));
        if (model.Timer.Running || model.Timer.Duration != TimeSpan.FromMinutes(17))
            throw new InvalidOperationException("Invalid timer duration was accepted.");
        timer.Close();
        Row(results, language, "Timer: edited duration survives reopening; invalid duration rejected", "PASS");

        var world = dock.OpenWidget("World Clocks")!;
        var city = FindControl(world, L10n.F($"Clock {1}")) as ComboBox
            ?? throw new InvalidOperationException("First city selector is missing.");
        int original = city.SelectedIndex;
        int alternative = (original + 1) % Widgets.ClockCities.Length;
        city.SelectedIndex = alternative;
        if (new DockModel(model.Profile).Settings.WorldClockCities[0] != Widgets.ClockCities[alternative].Key)
            throw new InvalidOperationException("Selected city was not saved.");
        city.SelectedIndex = original;
        world.Close();
        Row(results, language, "World clocks: city selection saves the stable city identifier", "PASS");

        static TextBox Field(Window window, string key) => (TextBox?)FindControl(window, L10n.T(key))
            ?? throw new InvalidOperationException($"Input missing: {key}");
        static void Edit(Window window, string key, string value) => Field(window, key).Text = value;
    }

    private static void Row(TextWriter writer, params string[] values) =>
        writer.WriteLine(string.Join(",", values.Select(value => "\"" + value.Replace("\"", "\"\"") + "\"")));
}
