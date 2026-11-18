using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LumaDock;

// Кнопка диктовки из FlowBar: шарик из частиц. 36 кадров лежат в одной картинке 6×6 (voice-orb.png), кадры меняются 24 раза в секунду,
// пока шарик виден; во время записи свечение и яркость выше. Перенесено из FlowBar.VoiceOrb.cs.
internal sealed class VoiceOrb : FrameworkElement
{
    private const int Cells = 6;
    private const int FramesPerSecond = 24;
    private static readonly Lazy<BitmapSource[]> Frames = new(ReadFrames);
    private static readonly Brush IdleGlow = ParticleGlow(94);
    private static readonly Brush ActiveGlow = ParticleGlow(185);
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = new();
    private bool active, animate;
    private int frame;

    public VoiceOrb()
    {
        Width = Height = 74;
        IsHitTestVisible = false;
        timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(1000.0 / FramesPerSecond) };
        timer.Tick += (_, _) =>
        {
            int next = (int)(clock.Elapsed.TotalSeconds * FramesPerSecond) % (Cells * Cells);
            if (frame == next) return;
            frame = next;
            InvalidateVisual();
        };
        IsVisibleChanged += (_, _) => SyncTimer();
        Loaded += (_, _) => SyncTimer();
        Unloaded += (_, _) => timer.Stop();
    }

    // Число кадров для проверки: сколько разных кадров показано с момента запуска таймера
    public int CurrentFrame => frame;
    public bool Running => timer.IsEnabled;

    public void SetState(bool recording, bool motion)
    {
        if (active != recording) { active = recording; InvalidateVisual(); }
        animate = motion;
        SyncTimer();
    }

    private void SyncTimer()
    {
        bool play = animate && IsLoaded && IsVisible;
        if (play && !timer.IsEnabled) { frame = 0; clock.Restart(); timer.Start(); InvalidateVisual(); }
        else if (!play && timer.IsEnabled) { timer.Stop(); clock.Stop(); frame = 0; InvalidateVisual(); }
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
        dc.DrawImage(images[frame], bounds);
        dc.PushOpacity(.72);
        dc.DrawImage(images[frame], bounds);
        dc.Pop();
        dc.PushOpacity(active ? .5 : .38);
        dc.DrawImage(images[frame], bounds);
        dc.Pop();
    }
}
