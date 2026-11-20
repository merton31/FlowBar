using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LumaDock;

// Кнопка диктовки из FlowBar: шарик из частиц. 36 кадров лежат в одной картинке 6×6 (voice-orb.png), кадры меняются 24 раза в секунду,
// пока шарик виден; во время записи свечение и яркость выше. Перенесено из FlowBar.VoiceOrb.cs.
internal sealed class VoiceOrb : FrameworkElement
{
    private const int Cells = 6;
    private const int FramesPerSecond = 24;
    private const int RenderRate = 60;
    // Частота перерисовки в покое (без записи): каждый кадр обновляет окно дока целиком, а маленькому шарику в доке хватает 30 (60 кадров стоили около 4% ядра); во время записи всегда 60
    public int IdleRate { get; set; } = RenderRate;
    private static readonly Lazy<BitmapSource[]> Frames = new(ReadFrames);
    private static readonly Brush IdleGlow = ParticleGlow(94);
    private static readonly Brush ActiveGlow = ParticleGlow(185);
    private readonly Stopwatch clock = new();
    private bool active, animate, running, onFrames;
    private System.Windows.Threading.DispatcherTimer? idleTimer;
    private double lastDrawn;
    private int frame;
    private double blend; // доля следующего кадра в текущей картинке, 0…1

    public VoiceOrb()
    {
        Width = Height = 74;
        IsHitTestVisible = false;
        IsVisibleChanged += (_, _) => SyncTimer();
        Loaded += (_, _) => SyncTimer();
        Unloaded += (_, _) => Stop();
    }

    // Шаг по кадрам экрана (DispatcherTimer не держит 16 мс и давал около 40 кадров/с); на быстрых мониторах ограничиваем до 60
    private void OnFrame(object? sender, EventArgs e)
    {
        double now = clock.Elapsed.TotalSeconds;
        if (sender is not System.Windows.Threading.DispatcherTimer && now - lastDrawn < 1.0 / ((active ? RenderRate : IdleRate) / (WindowsShell.Economy ? 2 : 1)) - .002) return;
        lastDrawn = now;
        // 36 кадров крутятся со скоростью 24 в секунду, а рисуем 60 раз: соседние кадры плавно перетекают друг в друга
        double position = now * FramesPerSecond % (Cells * Cells);
        frame = (int)position;
        blend = position - frame;
        InvalidateVisual();
    }

    private void Stop()
    {
        if (!running) return;
        if (onFrames) CompositionTarget.Rendering -= OnFrame;
        idleTimer?.Stop();
        running = false;
    }

    // Запись идёт по кадрам экрана (60 в секунду). В покое достаточно обычного таймера: подписка на кадры экрана заставляет WPF обновлять окно дока на каждом кадре монитора, и это стоило большую часть расхода
    private void Start()
    {
        onFrames = active || IdleRate >= RenderRate;
        if (onFrames) CompositionTarget.Rendering += OnFrame;
        else
        {
            idleTimer ??= new System.Windows.Threading.DispatcherTimer();
            idleTimer.Interval = TimeSpan.FromSeconds(1.0 / IdleRate);
            idleTimer.Tick -= OnFrame;
            idleTimer.Tick += OnFrame;
            idleTimer.Start();
        }
        running = true;
    }

    // Число кадров для проверки: сколько разных кадров показано с момента запуска таймера
    public int CurrentFrame => frame;
    public bool Running => running;

    public void SetState(bool recording, bool motion)
    {
        bool modeChanged = active != recording;
        if (modeChanged) { active = recording; InvalidateVisual(); }
        animate = motion;
        if (modeChanged && running) { Stop(); Start(); } // запись и покой крутятся разными способами
        SyncTimer();
    }

    private void SyncTimer()
    {
        bool play = animate && IsLoaded && IsVisible;
        if (play && !running) { frame = 0; blend = 0; lastDrawn = 0; clock.Restart(); Start(); InvalidateVisual(); }
        else if (!play && running) { Stop(); clock.Stop(); frame = 0; blend = 0; InvalidateVisual(); }
    }

    private static BitmapSource[] ReadFrames()
    {
        var resource = Application.GetResourceStream(new Uri("/FlowBar;component/voice-orb.png", UriKind.Relative));
        if (resource is null) return [];
        using var stream = resource.Stream;
        var sheet = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        int cell = sheet.PixelWidth / Cells;
        if (cell <= 0 || sheet.PixelHeight != sheet.PixelWidth || cell * Cells != sheet.PixelWidth) return [];
        var frames = new BitmapSource[Cells * Cells];
        for (int index = 0; index < frames.Length; index++)
        {
            var image = new CroppedBitmap(sheet, new Int32Rect(index % Cells * cell, index / Cells * cell, cell, cell));
            image.Freeze();
            frames[index] = image;
        }
        return frames;
    }

    private static Brush ParticleGlow(byte intensity)
    {
        var glow = new RadialGradientBrush();
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(intensity, 219, 245, 255), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(intensity / 3), 112, 185, 255), .21));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 60, 135, 245), 1));
        glow.Freeze();
        return glow;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var images = Frames.Value;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        if (images.Length == 0)
        {
            dc.DrawEllipse(Brushes.LightSkyBlue, null, center, ActualWidth * .2, ActualHeight * .2);
            return;
        }
        dc.DrawEllipse(ActiveGlow, null, center, ActualWidth * (active ? .50 : .46), ActualHeight * (active ? .50 : .46));
        var bounds = new Rect(RenderSize);
        var after = images[(frame + 1) % images.Length];
        // три слоя разной яркости, каждый — смесь текущего и следующего кадров
        foreach (double level in new[] { 1, .72, active ? .5 : .38 })
        {
            dc.PushOpacity(level * (1 - blend));
            dc.DrawImage(images[frame], bounds);
            dc.Pop();
            dc.PushOpacity(level * blend);
            dc.DrawImage(after, bounds);
            dc.Pop();
        }
    }
}
