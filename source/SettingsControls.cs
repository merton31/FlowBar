using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using static LumaDock.Visuals;

namespace LumaDock;

internal sealed partial class DockWindow
{
    private static StackPanel SettingsGroup(StackPanel parent, string? title = null)
    {
        if (title is not null)
        {
            var heading = Text(title, 13, false, Muted);
            heading.Margin = new(4, 16, 0, 8);
            parent.Children.Add(heading);
        }
        var rows = Stack();
        parent.Children.Add(Card(rows, 12));
        return rows;
    }

    private static void SettingsRow(StackPanel group, UIElement left, FrameworkElement right, string? help = null)
    {
        if (group.Children.Count > 0)
            group.Children.Add(new Border { Height = 1, Background = Brush("#35FFFFFF"), Margin = new(14, 0, 0, 0) });
        var control = RowStack();
        if (help is not null)
        {
            var hint = Text("ⓘ", 15, false, Muted);
            hint.ToolTip = L10n.T(help);
            hint.Margin = new(0, 0, 14, 0);
            control.Children.Add(hint);
        }
        right.VerticalAlignment = VerticalAlignment.Center;
        control.Children.Add(right);
        var row = Right(left, control);
        row.Height = 44;
        row.Margin = new(14, 0, 14, 0);
        group.Children.Add(row);
    }

    private ToggleButton ToggleSwitch(string name, bool active, Action<bool> set)
    {
        var frame = new FrameworkElementFactory(typeof(Border), "Frame");
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));
        frame.SetValue(Border.BackgroundProperty, Brush("#484849"));
        frame.SetValue(Border.PaddingProperty, new Thickness(2));
        var circle = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse), "Knob");
        circle.SetValue(FrameworkElement.WidthProperty, 22d);
        circle.SetValue(FrameworkElement.HeightProperty, 22d);
        circle.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        circle.SetValue(System.Windows.Shapes.Shape.FillProperty, Brushes.White);
        frame.AppendChild(circle);
        var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = frame };
        var check = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        check.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#2BCD52"), "Frame"));
        check.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right, "Knob"));
        template.Triggers.Add(check);
        var button = new ToggleButton { Width = 44, Height = 26, IsChecked = active, Template = template };
        AutomationProperties.SetName(button, L10n.T(name));
        button.Checked += (_, _) => { set(true); model.Save(); };
        button.Unchecked += (_, _) => { set(false); model.Save(); };
        return button;
    }

    private void DockToggle(StackPanel group, string name, bool value, Action<bool> save, string help) =>
        SettingsRow(group, Text(name, 14, true), ToggleSwitch(name, value, save), help);

    private static FrameworkElement Segments<T>(T selected, Action<T> changed, params (string Name, T Value)[] options)
    {
        var bar = RowStack();
        foreach (var (name, value) in options)
        {
            var label = Text(name, 13, Equals(value, selected));
            var entry = Button(label, () =>
            {
                foreach (Button other in bar.Children)
                {
                    other.Background = Brushes.Transparent;
                    if (other.Content is TextBlock text) { text.Foreground = Muted; text.FontWeight = FontWeights.Normal; }
                }
                label.Foreground = Paper;
                label.FontWeight = FontWeights.SemiBold;
                changed(value);
            }, Equals(value, selected) ? Brush("#414143") : Brushes.Transparent, 7);
            label.Foreground = Equals(value, selected) ? Paper : Muted;
            AutomationProperties.SetName(entry, L10n.T(name));
            entry.Padding = new(10, 0, 10, 0);
            entry.Height = 28;
            bar.Children.Add(entry);
            entry.Click += (_, _) => entry.Background = Brush("#414143");
        }
        var shell = Card(bar, 10, Brush("#191919"));
        shell.Padding = new(3);
        return shell;
    }

    private Slider DockSlider(StackPanel group, string name, double value, double min, double max, double step,
        string suffix, Action<double> save, string help, Slider? scale = null)
    {
        var amount = Text("", 13, false, Muted);
        amount.Margin = new(8, 0, 0, 0);
        var slider = FilledSlider(Accent(P), value, 22, name, min, max);
        slider.TickFrequency = slider.SmallChange = step;
        slider.IsSnapToTickEnabled = true;
        slider.Width = 160;
        void Display()
        {
            double displayed = scale is null ? slider.Value : Math.Round(slider.Value * scale.Value / 75, MidpointRounding.AwayFromZero);
            amount.Text = displayed.ToString("0.##", L10n.Culture) + (L10n.Russian && suffix == " px" ? " пикс." : suffix);
        }
        Display();
        if (scale is not null) scale.ValueChanged += (_, _) => Display();
        slider.ValueChanged += (_, _) => { save(slider.Value); Display(); model.Save(); };
        SettingsRow(group, RowStack(Text(name, 14, true), amount), slider, help);
        return slider;
    }

    private void SoundSettings(StackPanel parent)
    {
        var alerts = SettingsGroup(parent);
        SettingsRow(alerts, Text("Alert sound", 14, true), ToggleSwitch("Alert sound", P.AlertSound, value => P.AlertSound = value));
        var description = Text("Plays when a timer, a focus session or a reminder ends.", 12, false, Muted);
        description.Margin = new(4, 8, 0, 0);
        parent.Children.Add(description);
        var tones = SettingsGroup(parent, "Tone");
        var checks = new System.Collections.Generic.Dictionary<string, TextBlock>(StringComparer.Ordinal);
        foreach (string sound in new[] { "Crystal", "Chime", "Ripple", "Bell", "Sonar", "Pulse" })
        {
            var mark = Symbol("\uE73E", 15, Accent(P));
            mark.Opacity = P.Sound == sound ? 1 : 0;
            checks.Add(sound, mark);
            var button = Button(Right(Text(sound, 14, true), mark), () =>
            {
                P.Sound = sound;
                model.Save(false);
                foreach (var entry in checks) entry.Value.Opacity = entry.Key == P.Sound ? 1 : 0;
                DockSounds.Play(sound);
            }, Brushes.Transparent, 4);
            AutomationProperties.SetName(button, L10n.T(sound));
            if (tones.Children.Count > 0)
                tones.Children.Add(new Border { Height = 1, Background = Brush("#35FFFFFF"), Margin = new(14, 0, 0, 0) });
            button.Height = 44;
            button.Padding = new(14, 0, 14, 0);
            tones.Children.Add(button);
        }
    }
}
