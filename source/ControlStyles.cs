using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace LumaDock;

internal static partial class Visuals
{
    private static readonly Brush TrackInk = Brush("#3D3D3E");
    private static readonly Lazy<ResourceDictionary> DateStyles = new(() => new ResourceDictionary
    {
        Source = new Uri("/FlowBar;component/DockControls.xaml", UriKind.Relative)
    });

    public static DatePicker DateField(string label, double width, Brush accent, DateTime? value = null)
    {
        var field = new DatePicker
        {
            Style = (Style)DateStyles.Value["DockDateField"],
            SelectedDate = value,
            SelectedDateFormat = DatePickerFormat.Short,
            Language = System.Windows.Markup.XmlLanguage.GetLanguage(L10n.Culture.IetfLanguageTag),
            FirstDayOfWeek = DayOfWeek.Monday,
            Width = width,
            Tag = L10n.T("Date"),
            ToolTip = L10n.T(label)
        };
        field.Resources["DockControlAccent"] = accent;
        AutomationProperties.SetName(field, L10n.T(label));
        field.CalendarOpened += (_, _) =>
        {
            if (field.Template.FindName("PART_Popup", field) is Popup { Child: Calendar calendar })
            {
                calendar.Resources["DockControlAccent"] = accent;
                calendar.Language = field.Language;
            }
        };
        return field;
    }

    // Флажок в общем тёмном стиле: акцент берётся из профиля, подпись для чтения с экрана — name
    public static CheckBox CheckField(object content, bool isChecked, Brush accent, string name)
    {
        var box = new CheckBox
        {
            Style = (Style)DateStyles.Value["DockCheckBox"], Content = content, IsChecked = isChecked,
            VerticalContentAlignment = VerticalAlignment.Center, Padding = new(5, 5, 5, 5)
        };
        box.Resources["DockControlAccent"] = accent;
        AutomationProperties.SetName(box, name);
        return box;
    }

    // Выпадающий список в общем тёмном стиле (города мировых часов)
    public static ComboBox ComboField(double width, Brush accent, string name)
    {
        var combo = new ComboBox { Style = (Style)DateStyles.Value["DockComboBox"], Width = width, MaxDropDownHeight = 220 };
        combo.Resources["DockControlAccent"] = accent;
        AutomationProperties.SetName(combo, name);
        return combo;
    }

    // Поле пароля интеграций в общем тёмном стиле
    public static PasswordBox PasswordField(string name)
    {
        var field = new PasswordBox { Style = (Style)DateStyles.Value["DockPasswordBox"] };
        AutomationProperties.SetName(field, name);
        return field;
    }

    public static Slider FilledSlider(Brush accent, double value, double height, string name, double min = 0, double max = 100)
    {
        // все ползунки панели — «рогатка» из прошлой версии (SlingSlider)
        var slider = new SlingSlider(accent, height) { Minimum = min, Maximum = max, Value = value };
        AutomationProperties.SetName(slider, L10n.T(name));
        return slider;
    }

    public static ProgressBar FilledMeter(Brush accent, double height, string name, Brush? track = null)
    {
        var meter = new ProgressBar
        {
            Height = height, Foreground = accent, Background = track ?? TrackInk,
            Template = RangeTemplate(typeof(ProgressBar))
        };
        AutomationProperties.SetName(meter, L10n.T(name));
        return meter;
    }

    private static ControlTemplate RangeTemplate(Type controlType)
    {
        var stack = new FrameworkElementFactory(typeof(Grid));
        var fill = new FrameworkElementFactory(typeof(RangeFill));
        foreach (var (property, name) in new[]
        {
            (RangeFill.ValueProperty, "Value"), (RangeFill.MinimumProperty, "Minimum"),
            (RangeFill.MaximumProperty, "Maximum"), (RangeFill.FillProperty, "Foreground"),
            (RangeFill.TrackBrushProperty, "Background"), (RangeFill.FocusedProperty, "IsKeyboardFocused")
        })
            fill.SetBinding(property, new Binding(name) { RelativeSource = RelativeSource.TemplatedParent });
        stack.AppendChild(fill);
        var template = new ControlTemplate(controlType) { VisualTree = stack };
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .5));
        template.Triggers.Add(disabled);
        return template;
    }

    private sealed class RangeFill : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = Register("Value", typeof(double), 0d);
        public static readonly DependencyProperty MinimumProperty = Register("Minimum", typeof(double), 0d);
        public static readonly DependencyProperty MaximumProperty = Register("Maximum", typeof(double), 100d);
        public static readonly DependencyProperty FillProperty = Register("Fill", typeof(Brush), Brushes.Transparent);
        public static readonly DependencyProperty TrackBrushProperty = Register("TrackBrush", typeof(Brush), TrackInk);
        public static readonly DependencyProperty FocusedProperty = Register("Focused", typeof(bool), false);

        private static DependencyProperty Register(string name, Type type, object value) =>
            DependencyProperty.Register(name, type, typeof(RangeFill),
                new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender));

        protected override void OnRender(DrawingContext dc)
        {
            double max = (double)GetValue(MaximumProperty), min = (double)GetValue(MinimumProperty);
            double fraction = max > min ? Math.Clamp(((double)GetValue(ValueProperty) - min) / (max - min), 0, 1) : 0;
            var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
            var clip = new RectangleGeometry(bounds, ActualHeight / 2, ActualHeight / 2);
            dc.DrawGeometry((Brush)GetValue(TrackBrushProperty), null, clip);
            dc.PushClip(clip);
            dc.DrawRectangle((Brush)GetValue(FillProperty), null, new Rect(0, 0, ActualWidth * fraction, ActualHeight));
            if ((bool)GetValue(FocusedProperty))
                dc.DrawGeometry(null, new Pen(Brush("#C3E1FF"), 2), clip);
            dc.Pop();
        }
    }
}
