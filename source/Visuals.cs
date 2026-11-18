using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LumaDock;

internal static partial class Visuals
{
    public static readonly Brush Paper = Brush("#F4F4F6"), Muted = Brush("#A1A1A7");
    public static readonly Brush SurfaceInk = Brush("#191919"), Row = Brush("#262626"), Line = Brush("#383838");
    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");
    public static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    public static Brush Accent(Preferences settings)
    {
        try { return Brush(settings.Accent); }
        catch (FormatException) { return Brush("#0A88FF"); }
    }

    public static TextBlock Text(string value, double size = 14, bool bold = false, Brush? ink = null) =>
        RawText(L10n.T(value), size, bold, ink);

    public static TextBlock RawText(string value, double size = 14, bool bold = false, Brush? ink = null) =>
        new() { Text = value, FontSize = size, FontFamily = Font, Foreground = ink ?? Paper,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };

    public static TextBlock Symbol(string code, double size = 17, Brush? ink = null) =>
        new() { Text = code, FontFamily = new("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size,
            Foreground = ink ?? Paper, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };

    public static Border Card(UIElement? content, double radius = 14, Brush? background = null, Brush? stroke = null) =>
        new() { Child = content, CornerRadius = new(radius), Background = background ?? Row,
            BorderBrush = stroke, BorderThickness = new(stroke is null ? 0 : 1), SnapsToDevicePixels = true };

    public static Button Button(object content, Action click, Brush? background = null, double radius = 10)
    {
        var border = new FrameworkElementFactory(typeof(Border), "ButtonFrame");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetBinding(FrameworkElement.MarginProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var keyboard = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        keyboard.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#B7D5F4"), "ButtonFrame"));
        keyboard.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1), "ButtonFrame"));
        template.Triggers.Add(keyboard);
        var button = new Button
        {
            Content = content is string label ? Text(label, 14, true) : content,
            Background = background ?? Row, Foreground = Paper, Padding = new(10, 5, 10, 5),
            Template = template
        };
        if (content is string title && !string.IsNullOrWhiteSpace(title))
            System.Windows.Automation.AutomationProperties.SetName(button, L10n.T(title));
        button.MouseEnter += (_, _) => button.Opacity = .82;
        button.MouseLeave += (_, _) => button.Opacity = 1;
        button.Click += (_, _) => click();
        return button;
    }

    public static StackPanel RowStack(params UIElement[] elements)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var element in elements) row.Children.Add(element);
        return row;
    }

    public static StackPanel Stack(params UIElement[] elements)
    {
        var stack = new StackPanel();
        foreach (var element in elements) stack.Children.Add(element);
        return stack;
    }

    public static DockPanel Right(UIElement left, UIElement right)
    {
        var row = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        row.Children.Add(left);
        return row;
    }

    public static Brush FrameGradient() => new LinearGradientBrush(
        new GradientStopCollection { new(Color.FromRgb(28, 39, 74), 0), new(Color.FromRgb(68, 34, 73), .58),
            new(Color.FromRgb(108, 54, 39), 1) }, 60);

    public static Brush DockFinish(string style) => style switch
    {
        "Black" => Brush("#FB151515"),
        "White" => Brush("#F3F1F2F4"),
        "Clear" => Brush("#40303033"),
        "iOS" => new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#EE436091"), Color.FromRgb(26, 38, 61), 22),
        "Midnight" => new LinearGradientBrush(Color.FromRgb(41, 47, 64), Color.FromRgb(8, 11, 18), 75),
        _ => new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#DE3B3D43"),
            (Color)ColorConverter.ConvertFromString("#F00C0C0E"), 90)
    };

    public static Brush GlassTint() => new LinearGradientBrush(
        new GradientStopCollection { new(Color.FromArgb(84, 168, 182, 209), 0),
            new(Color.FromArgb(78, 27, 30, 38), .19), new(Color.FromArgb(126, 11, 11, 15), .89),
            new(Color.FromArgb(84, 78, 79, 89), 1) }, 90);

    public static Brush GlassRim() => new LinearGradientBrush(
        new GradientStopCollection { new(Color.FromArgb(170, 232, 238, 252), 0),
            new(Color.FromArgb(78, 201, 212, 234), .31), new(Color.FromArgb(38, 17, 21, 29), .74),
            new(Color.FromArgb(99, 214, 220, 240), 1) }, 90);

    public static bool LightTiles(Preferences preferences) => preferences.Style == "White" ||
        preferences.Style == "Clear" && preferences.ClearWidgets == "White";

    public static Brush ClearSample()
    {
        var pattern = new DrawingGroup();
        pattern.Children.Add(new GeometryDrawing(Brush("#C3C3C8"), null, new RectangleGeometry(new Rect(0, 0, 12, 12))));
        pattern.Children.Add(new GeometryDrawing(Brush("#94949A"), null, new RectangleGeometry(new Rect(0, 0, 6, 6))));
        pattern.Children.Add(new GeometryDrawing(Brush("#94949A"), null, new RectangleGeometry(new Rect(6, 6, 6, 6))));
        return new DrawingBrush(pattern) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 12, 12), TileMode = TileMode.Tile };
    }

    // Пункт меню; icon — значок слева (глиф или картинка программы). Без значка текст всё равно стоит в одной колонке с остальными
    public static MenuItem Menu(string title, Action click, object? icon = null)
    {
        var item = new MenuItem { Header = L10n.T(title), Icon = icon };
        item.Click += (_, _) => click();
        return item;
    }

    // Пункт с готовым текстом (названия окон и программ не переводятся); длинный текст обрезается многоточием
    public static MenuItem MenuText(string title, Action click, object? icon = null)
    {
        var item = Menu(title, click, icon);
        item.Header = new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 300 };
        return item;
    }

    public static TextBlock MenuGlyph(string glyph) => Symbol(glyph, 17);

    // Значок виджетов: четыре скруглённых квадрата с тонкой линией
    public static FrameworkElement MenuWidgetsIcon()
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 2, Columns = 2, Width = 16, Height = 16 };
        for (int i = 0; i < 4; i++)
            grid.Children.Add(new Border { BorderBrush = Paper, BorderThickness = new Thickness(1.4), CornerRadius = new CornerRadius(2.5), Margin = new Thickness(.8) });
        return grid;
    }

    public static Image MenuIcon(ImageSource source)
    {
        var icon = new Image { Source = source, Width = 20, Height = 20 };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        return icon;
    }

    // Заголовок раздела: бледный мелкий текст, не пункт
    public static Separator MenuHeader(string text)
    {
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetValue(TextBlock.TextProperty, text);
        label.SetValue(TextBlock.ForegroundProperty, Muted);
        label.SetValue(TextBlock.FontSizeProperty, 12.0);
        label.SetValue(TextBlock.FontWeightProperty, FontWeights.Normal);
        label.SetValue(TextBlock.MarginProperty, new Thickness(10, 9, 10, 3));
        return new Separator { Template = new ControlTemplate(typeof(Separator)) { VisualTree = label }, IsHitTestVisible = false, Focusable = false };
    }

    public static ContextMenu DockMenu()
    {
        var menu = new ContextMenu { Foreground = Paper, Background = SurfaceInk, FontFamily = Font, FontSize = 14, FontWeight = FontWeights.SemiBold, MinWidth = 240 };
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetValue(Border.BackgroundProperty, SurfaceInk);
        frame.SetValue(Border.BorderBrushProperty, Line);
        frame.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
        frame.SetValue(Border.PaddingProperty, new Thickness(6));
        frame.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        menu.Template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = frame };

        // строка: колонка значка шириной 32 (есть и у пунктов без значка, чтобы текст не прыгал) и текст
        var style = new Style(typeof(MenuItem));
        var row = new FrameworkElementFactory(typeof(Border), "Row");
        row.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        row.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        row.SetValue(Border.PaddingProperty, new Thickness(8, 6, 18, 6));
        var layout = new FrameworkElementFactory(typeof(DockPanel));
        var iconColumn = new FrameworkElementFactory(typeof(Border));
        iconColumn.SetValue(FrameworkElement.WidthProperty, 32.0);
        iconColumn.SetValue(DockPanel.DockProperty, Dock.Left);
        iconColumn.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        var icon = new FrameworkElementFactory(typeof(ContentPresenter));
        icon.SetValue(ContentPresenter.ContentSourceProperty, "Icon");
        icon.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        iconColumn.AppendChild(icon);
        var title = new FrameworkElementFactory(typeof(ContentPresenter));
        title.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        title.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        layout.AppendChild(iconColumn);
        layout.AppendChild(title);
        row.AppendChild(layout);
        var template = new ControlTemplate(typeof(MenuItem)) { VisualTree = row };
        var highlight = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        highlight.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#39393A"), "Row"));
        template.Triggers.Add(highlight);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Paper));
        menu.Resources.Add(typeof(MenuItem), style);

        // разделитель — тонкая линия
        var line = new FrameworkElementFactory(typeof(Border));
        line.SetValue(FrameworkElement.HeightProperty, 1.0);
        line.SetValue(Border.BackgroundProperty, Brush("#3A3A3C"));
        line.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 5, 6, 5));
        var separator = new Style(typeof(Separator));
        separator.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Separator)) { VisualTree = line }));
        menu.Resources.Add(typeof(Separator), separator);
        menu.Resources[MenuItem.SeparatorStyleKey] = separator; // меню берёт стиль разделителя по этому ключу, а не по типу
        MenuWatch.Attach(menu);
        return menu;
    }
}
