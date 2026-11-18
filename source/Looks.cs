using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using static LumaDock.Visuals;

namespace LumaDock;

// Оформление дока: стиль (тема) + материал + прозрачность. Одни и те же правила рисуют и сам док, и плитку-превью в миксере стилей.
// Материалы из FlowBar: «По теме» (как у стиля), «Матовое стекло» (сильное размытие и молочный слой), «Металл» (матовые полосы, не прозрачный),
// «Призма» (цветное расщепление и радужный слой). Прозрачность убирает непрозрачность основы и открывает живое стекло под доком.
internal static class Looks
{
    public static readonly string[] Styles = ["Black", "White", "Liquid", "iOS", "Midnight", "Clear"];
    public static readonly string[] Materials = ["theme", "frost", "metal", "prism"];

    public static string MaterialName(string material) => material switch
    {
        "frost" => "Frosted glass", "metal" => "Metal", "prism" => "Prism", _ => "By theme"
    };

    // Код оформления: «LD1:» и JSON со стилем, материалом, прозрачностью и цветом акцента в base64. Им можно поделиться или перенести на другой компьютер
    public static string Export(Preferences p)
    {
        string accent = p.UseCustomAccent && p.CustomAccent is { } custom ? custom : p.Accent;
        var json = System.Text.Json.JsonSerializer.Serialize(new { style = p.Style, material = Material(p), transparency = Math.Clamp(p.Transparency, 0, 100), accent });
        return "LD1:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }

    // Применяет код, только если он целиком верен: неверный код не меняет ничего
    public static bool TryImport(Preferences p, string? code)
    {
        if (code is null || code.Length is < 8 or > 512 || !code.StartsWith("LD1:", StringComparison.Ordinal)) return false;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(code[4..]));
            var root = document.RootElement;
            string? style = root.GetProperty("style").GetString(), material = root.GetProperty("material").GetString(), accent = root.GetProperty("accent").GetString();
            int transparency = root.GetProperty("transparency").GetInt32();
            if (Array.IndexOf(Styles, style) < 0 || Array.IndexOf(Materials, material) < 0 || transparency is < 0 or > 100
                || accent is null || !System.Text.RegularExpressions.Regex.IsMatch(accent, @"\A#[0-9A-Fa-f]{6}\z")) return false;
            // рисование берёт p.Accent, поэтому цвет записывается и туда, и в свой цвет
            (p.Style, p.Material, p.Transparency, p.Accent, p.CustomAccent, p.UseCustomAccent) = (style!, material!, transparency, accent, accent, true);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException) { return false; }
    }

    public static string Material(Preferences p) =>Array.IndexOf(Materials, p.Material) >= 0 ? p.Material : "theme";
    public static double Clear(Preferences p) => Math.Clamp(p.Transparency, 0, 100) / 100.0;

    // Док показывает живое стекло, если стиль «Liquid», материал стеклянный или включена прозрачность; металл всегда непрозрачный
    public static bool Glassy(Preferences p) =>
        Material(p) != "metal" && (p.Style == "Liquid" || Material(p) is "frost" or "prism" || p.Transparency > 0);

    // Сила размытия стекла: матовое — в четыре раза крупнее (меньше точек выборки)
    public static int Detail(Preferences p) => Material(p) == "frost" ? 4 : 1;

    // Расщепление цвета у призмы: красный и синий каналы берутся со сдвигом в пикселях выборки
    public static int Dispersion(Preferences p) => Material(p) == "prism" ? 3 : 0;

    public static bool LightStyle(string style) => style == "White";

    private static Brush Base(Preferences p, bool glass)
    {
        var brush = (glass && p.Style == "Liquid" ? GlassTint() : DockFinish(p.Style)).Clone();
        if (glass && Material(p) != "metal") brush.Opacity = 1 - .85 * Clear(p);
        return brush;
    }

    private static Brush MetalBase(string style) => LightStyle(style)
        ? Gradient(0, "#FFB4B7BD", "#FFEFF0F2", "#FFC7CACF", "#FFF7F8F9", "#FFA9ACB2")
        : Gradient(0, "#FF2E3035", "#FF8E9198", "#FF45474D", "#FF9FA2A9", "#FF34363B");

    private static LinearGradientBrush Gradient(byte alpha, params string[] colors)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, .15), EndPoint = new Point(1, .85) };
        for (int index = 0; index < colors.Length; index++)
        {
            var color = (Color)ColorConverter.ConvertFromString(colors[index]);
            if (alpha != 0) color.A = alpha;
            brush.GradientStops.Add(new GradientStop(color, index / (double)(colors.Length - 1)));
        }
        return brush;
    }

    // Заливка внутренней части дока: основа стиля и слой материала в одной кисти
    public static Brush Surface(Preferences p, bool glass)
    {
        string material = Material(p);
        var group = new DrawingGroup();
        Brush Fill(Brush brush) { brush.Freeze(); return brush; }
        void Layer(Brush brush) => group.Children.Add(new GeometryDrawing(brush, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
        if (material == "metal") Layer(Fill(MetalBase(p.Style)));
        else Layer(Fill(Base(p, glass)));
        if (material == "frost")
            Layer(Fill(new SolidColorBrush(Color.FromArgb((byte)(LightStyle(p.Style) ? 0x34 : 0x2A), 255, 255, 255))));
        else if (material == "prism")
            Layer(Fill(Gradient(0x3A, "#FF78A0", "#FFC85A", "#6EE6AA", "#6EAAFF", "#BE82FF")));
        else if (material == "metal")
        {
            var bands = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            bands.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, 255, 255, 255), 0));
            bands.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), .2));
            bands.GradientStops.Add(new GradientStop(Color.FromArgb(0x4A, 255, 255, 255), .5));
            bands.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), .8));
            bands.GradientStops.Add(new GradientStop(Color.FromArgb(0x26, 255, 255, 255), 1));
            Layer(Fill(bands));
        }
        group.Freeze();
        return new DrawingBrush(group) { Stretch = Stretch.Fill };
    }

    public static Brush Rim(Preferences p) =>
        Material(p) == "metal" ? Brush("#D2FFFFFF") : Glassy(p) ? GlassRim() : Brush(p.Style == "White" ? "#B8FFFFFF" : "#9EAE9B9C");

    // Плитка-превью миксера: цветной фон «обои» под прозрачной заливкой, пятна света акцента, блик и кромка
    public static FrameworkElement Tile(string style, string material, int transparency, Color accent, double size, bool animate)
    {
        var look = new Preferences { Style = style, Material = material, Transparency = transparency };
        bool glass = Glassy(look);
        var layer = new Grid { Width = size, Height = size };
        var wall = new Border
        {
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromRgb(28, 39, 74), 0), new(Color.FromRgb(120, 60, 150), .45), new(Color.FromRgb(214, 120, 72), 1)
            }, 40)
        };
        if (material == "frost") wall.Effect = new BlurEffect { Radius = 16 };
        layer.Children.Add(new Border { Child = wall, ClipToBounds = true });
        layer.Children.Add(new Border { Background = Surface(look, glass) });
        Color white = Colors.White;
        Color Alpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
        void Blob(double blobSize, double x, double y, Color color, byte alpha)
        {
            var ellipse = new Ellipse
            {
                Width = blobSize, Height = blobSize, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(x, y, 0, 0), IsHitTestVisible = false,
                Fill = new RadialGradientBrush(Alpha(color, alpha), Alpha(color, 0))
            };
            var move = new TranslateTransform();
            ellipse.RenderTransform = move;
            if (animate)
            {
                move.BeginAnimation(TranslateTransform.XProperty, new System.Windows.Media.Animation.DoubleAnimation(-14 + (x > 40 ? 28 : 0), 0, TimeSpan.FromMilliseconds(380)));
                move.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(10 - (y > 40 ? 20 : 0), 0, TimeSpan.FromMilliseconds(380)));
            }
            layer.Children.Add(ellipse);
        }
        if (material == "prism") { Blob(100, -22, -26, Hue(accent, -70), 0xA0); Blob(100, 40, -10, accent, 0xA0); Blob(96, 14, 44, Hue(accent, 70), 0x98); }
        else if (material == "frost") Blob(130, -30, -40, accent, 0x70);
        else if (material == "metal") Blob(70, 8, 52, accent, 0x50);
        else Blob(118, -24, -32, accent, 0x90);
        var sheen = new LinearGradientBrush(Alpha(white, material == "frost" ? (byte)0x18 : (byte)0x34), Alpha(white, 0), 90);
        layer.Children.Add(new Border { Height = size * .55, VerticalAlignment = VerticalAlignment.Top, Background = sheen });
        layer.Children.Add(new Border { CornerRadius = new CornerRadius(26), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Alpha(white, 0x30)) });
        return layer;
    }

    // Сдвиг цвета по кругу оттенков (для разноцветных пятен призмы)
    public static Color Hue(Color color, double degrees)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min, hue = 0;
        if (delta > 1e-9)
        {
            hue = max == r ? (g - b) / delta % 6 : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
            hue *= 60;
        }
        hue = ((hue + degrees) % 360 + 360) % 360;
        double saturation = max <= 0 ? 0 : delta / max, chroma = max * saturation, x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1)), m = max - chroma;
        (double rr, double gg, double bb) = hue switch
        {
            < 60 => (chroma, x, 0d), < 120 => (x, chroma, 0d), < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma), < 300 => (x, 0d, chroma), _ => (chroma, 0d, x)
        };
        return Color.FromArgb(color.A, (byte)Math.Round((rr + m) * 255), (byte)Math.Round((gg + m) * 255), (byte)Math.Round((bb + m) * 255));
    }
}
