using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LumaDock;

// LumaDock.exe --preview-glass <папка>: снимки дока с живым стеклом для разных стилей, материалов и прозрачности.
// Под доком ставится окно с искусственным фоном (полосы, круги, градиент), поэтому на снимках нет ничего с рабочего стола пользователя.
internal static class GlassPreview
{
    public static readonly (string Style, string Material, int Transparency)[] Cases =
    [
        ("Liquid", "theme", 0), ("Liquid", "frost", 0), ("Liquid", "prism", 0), ("Liquid", "theme", 60),
        ("Black", "theme", 0), ("Black", "theme", 70), ("Black", "metal", 0), ("Midnight", "frost", 40),
        ("White", "frost", 30), ("White", "metal", 0), ("Clear", "theme", 100), ("Clear", "theme", 0), ("Clear", "frost", 50)
    ];

    public static async System.Threading.Tasks.Task Run(DockWindow dock, DockModel model, string folder)
    {
        await System.Threading.Tasks.Task.Delay(800);
        var backdrop = Backdrop(dock);
        backdrop.Show();
        dock.Topmost = true;
        dock.Activate();
        var saved = (model.Settings.Style, model.Settings.Material, model.Settings.Transparency);
        try
        {
            foreach (var (style, material, transparency) in Cases)
            {
                model.Settings.Style = style;
                model.Settings.Material = material;
                model.Settings.Transparency = transparency;
                model.Save();
                // стекло обновляется по таймеру дока: ждём несколько кадров, чтобы снимок попал под нынешний материал
                await System.Threading.Tasks.Task.Delay(2200);
                Program.Capture(dock, System.IO.Path.Combine(folder, $"glass-{style}-{material}-{transparency}.png"));
                if (!dock.HasLiveBackdrop) throw new InvalidOperationException("The Windows session did not supply the backdrop pixels.");
                if (transparency == 0 && style == "Liquid" && material == "theme") await Probe(dock, folder);
            }
        }
        finally
        {
            (model.Settings.Style, model.Settings.Material, model.Settings.Transparency) = saved;
            model.Save();
            backdrop.Close();
        }
    }

    // Контроль фона: средний цвет экрана под доком (окно-фон должно быть цветным, а не чёрным) и снимок этого прямоугольника
    private static async System.Threading.Tasks.Task Probe(DockWindow dock, string folder)
    {
        var dpi = VisualTreeHelper.GetDpi(dock);
        int x = (int)(dock.Left * dpi.DpiScaleX), y = (int)(dock.Top * dpi.DpiScaleY), w = (int)(dock.ActualWidth * dpi.DpiScaleX), h = (int)(dock.ActualHeight * dpi.DpiScaleY);
        using var shot = new System.Drawing.Bitmap(w, h);
        using (var graphics = System.Drawing.Graphics.FromImage(shot)) graphics.CopyFromScreen(x, y, 0, 0, shot.Size);
        long r = 0, g = 0, b = 0;
        for (int row = 0; row < h; row += 4)
            for (int column = 0; column < w; column += 4) { var pixel = shot.GetPixel(column, row); r += pixel.R; g += pixel.G; b += pixel.B; }
        long count = (long)((h + 3) / 4) * ((w + 3) / 4);
        File.WriteAllText(System.IO.Path.Combine(folder, "backdrop-mean.txt"), $"{r / count},{g / count},{b / count} под доком {w}x{h}");
        shot.Save(System.IO.Path.Combine(folder, "backdrop-under-dock.png"));
        // тот же прямоугольник при невидимом доке: если цвета отличаются, снимок экрана захватывает сам док
        dock.Opacity = 0;
        await System.Threading.Tasks.Task.Delay(400);
        using var bare = new System.Drawing.Bitmap(w, h);
        using (var graphics = System.Drawing.Graphics.FromImage(bare)) graphics.CopyFromScreen(x, y, 0, 0, bare.Size);
        long br = 0, bg = 0, bb = 0;
        for (int row = 0; row < h; row += 4)
            for (int column = 0; column < w; column += 4) { var pixel = bare.GetPixel(column, row); br += pixel.R; bg += pixel.G; bb += pixel.B; }
        File.AppendAllText(System.IO.Path.Combine(folder, "backdrop-mean.txt"), $"{Environment.NewLine}без дока: {br / count},{bg / count},{bb / count}");
        bare.Save(System.IO.Path.Combine(folder, "backdrop-without-dock.png"));
        dock.Opacity = 1;
        await System.Threading.Tasks.Task.Delay(200);
    }

    // Окно-фон чуть больше дока: градиент, вертикальные полосы и круги — на них видно размытие, расщепление цвета и просвет
    private static Window Backdrop(DockWindow dock)
    {
        var canvas = new Canvas { Background = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(20, 150, 190), 0), new(Color.FromRgb(210, 60, 150), .5), new(Color.FromRgb(240, 170, 60), 1)
        }, 0) };
        for (int stripe = 0; stripe < 160; stripe++)
        {
            var bar = new Rectangle { Width = 5, Height = 400, Fill = stripe % 2 == 0 ? Brushes.White : Brushes.Black };
            Canvas.SetLeft(bar, stripe * 18);
            Canvas.SetTop(bar, 0);
            canvas.Children.Add(bar);
        }
        for (int circle = 0; circle < 14; circle++)
        {
            var dot = new Ellipse { Width = 70, Height = 70, Fill = new SolidColorBrush(Color.FromArgb(220, (byte)(40 + circle * 15), (byte)(240 - circle * 12), 90)) };
            Canvas.SetLeft(dot, 30 + circle * 130);
            Canvas.SetTop(dot, 60 + circle % 3 * 35);
            canvas.Children.Add(dot);
        }
        return new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true,
            Left = dock.Left - 60, Top = dock.Top - 100, Width = dock.ActualWidth + 120, Height = dock.ActualHeight + 200, Content = canvas
        };
    }
}
