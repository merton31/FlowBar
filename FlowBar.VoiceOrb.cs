using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FlowBar
{
    sealed class VoiceOrb : FrameworkElement
    {
        const int Cells = 6;
        const int FramesPerSecond = 24;
        static readonly Lazy<BitmapSource[]> Frames = new Lazy<BitmapSource[]>(ReadFrames);
        readonly DispatcherTimer timer;
        readonly Stopwatch clock = new Stopwatch();
        static readonly Brush ContrastHalo = MakeContrastHalo();
        static readonly Brush IdleGlow = MakeParticleGlow(94);
        static readonly Brush ActiveGlow = MakeParticleGlow(185);
        public bool LightSurface { get; set; }
        public bool Bright { get; set; } // ярче обычного: для виджета на панели, где шарик без плитки и должен быть виден сразу
        bool active;
        bool animate;
        int frame;

        public VoiceOrb()
        {
            Width = Height = 74;
            IsHitTestVisible = false;
            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(1000.0 / FramesPerSecond) };
            timer.Tick += (s, e) =>
            {
                int next = (int)(clock.Elapsed.TotalSeconds * FramesPerSecond) % (Cells * Cells);
                if (frame == next) return;
                frame = next;
                InvalidateVisual();
            };
            IsVisibleChanged += (s, e) => SyncTimer();
            Loaded += (s, e) => SyncTimer();
            Unloaded += (s, e) => timer.Stop();
        }

        static BitmapSource[] ReadFrames()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FlowBar.VoiceOrb.png"))
            {
                if (stream == null) return new BitmapSource[0];
                var sheet = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                int cell = sheet.PixelWidth / Cells;
                if (cell <= 0 || sheet.PixelHeight != sheet.PixelWidth || cell * Cells != sheet.PixelWidth) return new BitmapSource[0];
                var frames = new BitmapSource[Cells * Cells];
                for (int i = 0; i < frames.Length; i++)
                {
                    var image = new CroppedBitmap(sheet, new Int32Rect(i % Cells * cell, i / Cells * cell, cell, cell));
                    image.Freeze();
                    frames[i] = image;
                }
                return frames;
            }
        }

        public void SetState(bool active, bool motion)
        {
            if (this.active != active) { this.active = active; InvalidateVisual(); }
            animate = motion;
            SyncTimer();
        }

        void SyncTimer()
        {
            bool play = animate && IsLoaded && IsVisible;
            if (play && !timer.IsEnabled) { frame = 0; clock.Restart(); timer.Start(); InvalidateVisual(); }
            else if (!play && timer.IsEnabled) { timer.Stop(); clock.Stop(); frame = 0; InvalidateVisual(); }
        }

        static Brush MakeContrastHalo()
        {
            var halo = new RadialGradientBrush();
            halo.GradientStops.Add(new GradientStop(Color.FromArgb(0xE0, 22, 41, 62), 0));
            halo.GradientStops.Add(new GradientStop(Color.FromArgb(0xBA, 23, 49, 76), 0.49));
            halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, 39, 99, 158), 0.98));
            halo.Freeze();
            return halo;
        }

        static Brush MakeParticleGlow(byte intensity)
        {
            var glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(intensity, 219, 245, 255), 0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(intensity / 3), 112, 185, 255), 0.21));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 60, 135, 245), 1));
            glow.Freeze();
            return glow;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (LightSurface)
                dc.DrawEllipse(ContrastHalo, null, new Point(ActualWidth / 2, ActualHeight / 2), ActualWidth / 2, ActualHeight / 2);
            var images = Frames.Value;
            if (images.Length > 0)
            {
                var center = new Point(ActualWidth / 2, ActualHeight / 2);
                dc.DrawEllipse(active || Bright ? ActiveGlow : IdleGlow, null, center, ActualWidth * (Bright ? 0.46 : 0.40), ActualHeight * (Bright ? 0.46 : 0.40));
                var bounds = new Rect(RenderSize);
                dc.DrawImage(images[frame], bounds);
                dc.PushOpacity(active ? 0.72 : Bright ? 0.72 : 0.46);
                dc.DrawImage(images[frame], bounds);
                dc.Pop();
                if (Bright) { dc.PushOpacity(active ? 0.5 : 0.38); dc.DrawImage(images[frame], bounds); dc.Pop(); } // ещё один слой частиц — заметнее без подложки
            }
            else dc.DrawEllipse(Brushes.LightSkyBlue, null, new Point(ActualWidth / 2, ActualHeight / 2), 15, 15);
        }
    }

    partial class Bar
    {
        static bool LightTaskSurface { get { return Sty.Lum(CardBg) >= 128; } }
        static Color TaskAccent
        {
            get { return LightTaskSurface ? Color.FromRgb(35, 92, 141) : Color.FromRgb(170, 213, 255); }
        }

        static Border TaskSurface(UIElement child, bool composer = false)
        {
            var surface = SettingsRow(child);
            var tint = TaskAccent;
            surface.CornerRadius = new CornerRadius(composer ? 14 : 10);
            surface.BorderBrush = Frozen(tint, composer ? (byte)0x39 : (byte)0x25);
            if (composer)
            {
                var top = LightTaskSurface ? Frozen(tint, 0x20).Color : Color.FromArgb(0x88, 8, 15, 26);
                var bottom = LightTaskSurface ? Frozen(tint, 0x0B).Color : Color.FromArgb(0x58, 10, 16, 27);
                var fill = new LinearGradientBrush(top, bottom, 115);
                fill.Freeze();
                surface.Background = fill;
            }
            else surface.Background = Frozen(tint, LightTaskSurface ? (byte)0x05 : (byte)0x0B);
            return surface;
        }

        static Border TaskButton(string text, Action click, bool primary = false)
        {
            var button = Btn(text, click);
            button.CornerRadius = new CornerRadius(9);
            button.BorderThickness = new Thickness(1);
            button.BorderBrush = Frozen(TaskAccent, primary ? (byte)0x6B : (byte)0x34);
            button.Background = Frozen(TaskAccent, primary ? (byte)0x34 : (byte)0x10);
            button.Padding = new Thickness(11, 6, 11, 6);
            if (primary) ((TextBlock)button.Child).Foreground = Frozen(TaskAccent, 255);
            button.IsEnabledChanged += (s, e) => button.Opacity = button.IsEnabled ? 1 : 0.48;
            button.MouseLeave += (s, e) => button.Opacity = button.IsEnabled ? 1 : 0.48;
            return button;
        }

        static Press VoiceRecordButton(VoiceOrb orb, TextBlock label, Action click, bool inline)
        {
            if (inline)
            {
                orb.Width = orb.Height = 104;
                orb.LightSurface = LightTaskSurface;
                var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                label.FontSize = 18;
                label.MinWidth = 128;
                row.Children.Add(orb); row.Children.Add(label);
                var inlineButton = Act(row, "Старт", click);
                inlineButton.VerticalAlignment = VerticalAlignment.Center;
                inlineButton.MinWidth = 232;
                inlineButton.ToolTip = "Записать голос для новой задачи";
                inlineButton.MouseEnter += (s, e) => label.Foreground = Frozen(TaskAccent, 255);
                inlineButton.MouseLeave += (s, e) => label.Foreground = Frozen(CardFg, 255);
                return inlineButton;
            }
            var icon = G("\uE720", 13);
            icon.Foreground = Frozen(Color.FromRgb(175, 212, 255), 255);
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new Thickness(10, 0, 0, 0);
            var content = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(orb, Dock.Left);
            DockPanel.SetDock(icon, Dock.Right);
            content.Children.Add(orb); content.Children.Add(icon); content.Children.Add(label);
            var button = Act(content, "Записать", click);
            button.Base = Color.FromRgb(15, 20, 28);
            button.Background = Frozen(button.Base, 255);
            button.BorderThickness = new Thickness(1);
            button.BorderBrush = Frozen(Color.FromRgb(147, 189, 235), 55);
            button.CornerRadius = new CornerRadius(15);
            button.Padding = new Thickness(4, 0, 13, 0);
            button.MinWidth = 216;
            button.MouseEnter += (s, e) => Fade(button, Color.FromRgb(24, 36, 52), 130);
            button.MouseLeave += (s, e) => Fade(button, button.Base, 150);
            return button;
        }
    }
}
