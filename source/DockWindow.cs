using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using static LumaDock.Visuals;
using Screen = System.Windows.Forms.Screen;

namespace LumaDock;

internal sealed partial class DockWindow : Window
{
    private const string DragId = "LumaDock.DockItem";
    private readonly DockModel model;
    private readonly bool smoke;
    private readonly bool previewLiveGlass;
    // Дополнительный док на другом мониторе: общие дела (опрос системы, горячие клавиши, сторож панели задач, уведомления) выполняет только главный док
    private readonly Screen? monitor;
    private readonly bool secondary;
    private Preferences P => model.Settings;
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(250) };
    // Прозрачный режим: стекло снимается на каждом кадре WPF, но не чаще частоты монитора, на котором стоит док (таймер Windows даёт только ~64 Гц, поэтому кадры берём из CompositionTarget.Rendering)
    private int glassHz = 60;
    private long glassStamp;
    private int glassFrames;
    private int glassIdle;
    private long glassSecond;
    internal int GlassFps { get; private set; }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (smoke || !barVisible || !Looks.Glassy(P)) return;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        // Пока фон под доком неподвижен (30 кадров подряд без изменений), снимаем 60 раз в секунду: быстрее незачем, а процессор не греется
        int rate = glassIdle > 30 ? Math.Min(60, glassHz) : glassHz;
        if (now - glassStamp < System.Diagnostics.Stopwatch.Frequency * .85 / rate) return;
        glassStamp = now;
        SampleGlass();
        glassIdle = glass.Changed ? 0 : glassIdle + 1;
        glassFrames++;
        if (now - glassSecond >= System.Diagnostics.Stopwatch.Frequency)
        {
            GlassFps = glassFrames;
            glassFrames = 0;
            glassSecond = now;
        }
    }
    private readonly List<Action> faces = [];
    private readonly Dictionary<string, ImageSource?> icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Window> surfaces = new();
    // плитки программ по пути (для прыжка значка при мигании окна) и время, до которого док показывается из-за мигания
    private readonly Dictionary<string, Border> appTiles = new(StringComparer.OrdinalIgnoreCase);
    private DateTime flashUntil;
    private List<WindowsShell.WindowInfo> windows = [];
    private string windowSignature = "";
    private Rect display;
    private nint handle;
    private int ticks;
    private bool reserved, placing;
    private int reservedHeight;
    private Rect? approvedArea;
    private Window? thumbnail;
    private readonly DispatcherTimer closePreview = new() { Interval = TimeSpan.FromMilliseconds(290) };
    private string? currentWallpaper;
    private ImageBrush? wallpaperBrush;
    private readonly Border background;
    private readonly Grid dockRoot = new();
    private readonly LiveGlass glass = new();
    private readonly BrowserSites browserSites = new();
    private readonly ImageBrush liveBackdrop = new() { Stretch = Stretch.Fill };
    private double dockHeadroom;
    private double BarTop => Top + dockHeadroom;
    private bool barVisible = true;
    private DateTimeOffset lastCalendarRefresh;

    public bool HasLiveBackdrop => liveBackdrop.ImageSource is not null;

    public DockWindow(DockModel model, bool smoke, bool previewLiveGlass = false, Screen? monitor = null)
    {
        this.model = model;
        this.smoke = smoke;
        this.monitor = monitor;
        secondary = monitor is not null;
        this.previewLiveGlass = previewLiveGlass;
        Title = "FlowBar";
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(L10n.Culture.IetfLanguageTag);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        UseLayoutRounding = true;
        background = Card(null, 17);
        background.VerticalAlignment = VerticalAlignment.Bottom;
        dockRoot.Children.Add(background);
        Content = dockRoot;
        ContextMenu = CommonMenu();
        AllowDrop = true;
        Drop += (_, e) =>
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
            if (!P.Locked) PinPaths(paths);
        };
        closePreview.Tick += (_, _) => { closePreview.Stop(); CloseThumbnail(); };
        if (!secondary) model.SaveFailed += message => Report(L10n.F($"Could not save the profile.\n{message}"));
        model.Changed += Refresh;
        if (!secondary) model.Automation.Notify += Toast;
        SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            handle = source.Handle;
            ExcludeFromCapture();
            WindowsShell.PreventActivation(handle);
            var taskbarRestart = WindowsShell.TaskbarRestartMessage;
            source.AddHook((nint hwnd, int msg, nint w, nint l, ref bool handled) =>
            {
                if (msg == 0x0312 && w >= 1 && w <= Hotkeys.Items.Length)
                {
                    int slot = (int)w - 1;
                    Dispatcher.BeginInvoke(() => RunHotkey(slot));
                    handled = true;
                }
                if (msg == 0x8066 && w == 1 && reserved && !placing)
                    Dispatcher.BeginInvoke(() => PlaceLayout((FrameworkElement)background.Child, renegotiate: true));
                if (msg == taskbarRestart)
                {
                    reserved = false;
                    approvedArea = null;
                    Dispatcher.BeginInvoke(Refresh);
                }
                else if (msg == 0x007E || msg == 0x02E0)
                    Dispatcher.BeginInvoke(Refresh);
                else if ((uint)msg == WindowsShell.ShellHookMessage && (int)w == WindowsShell.ShellFlash)
                {
                    var flashing = l;
                    Dispatcher.BeginInvoke(() => FlashApp(WindowsShell.WindowApp(flashing)));
                }
                return 0;
            });
            if (!secondary)
            {
                ApplyHotkeys();
                if (!smoke) WindowsShell.RegisterShellHook(handle);
            }
            Refresh();
        };
        clock.Tick += (_, _) => Tick();
        CompositionTarget.Rendering += OnRendering;
        if (!smoke)
        {
            CompositionTarget.Rendering += OnMagnify;
            PreviewMouseMove += (_, e) =>
            {
                if (!P.Magnification || magnetRow is null) return;
                magnetLast = e.GetPosition(magnetRow).X;
                magnetX = magnetLast;
            };
            MouseLeave += (_, _) => magnetX = null;
        }
        Closed += (_, _) =>
        {
            clock.Stop();
            CompositionTarget.Rendering -= OnRendering;
            CompositionTarget.Rendering -= OnMagnify;
            glass.Dispose();
            model.Changed -= Refresh;
            CloseThumbnail();
            if (reserved) WindowsShell.Release(handle);
            if (secondary) return;
            model.Media.Dispose();
            model.Activity.Stop();
            if (!smoke) for (int slot = 0; slot < Hotkeys.Items.Length; slot++) SystemHotkey.UnregisterHotKey(handle, slot + 1);
            if (!smoke) WindowsShell.UnregisterShellHook(handle);
            WindowsShell.RestoreTaskbar();
        };
        ReadLive();
        Refresh();
        clock.Start();
    }

    private void Report(string message)
    {
        if (!smoke) MessageBox.Show(message, "FlowBar", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ReadLive()
    {
        if (!secondary) ReadSystem();
        if (smoke) return;
        ReadWindows();
    }

    // Общие данные о системе читает один главный док, иначе экранное время, звук и сеть считались бы дважды
    private void ReadSystem()
    {
        model.Usage = WindowsShell.ReadUsage();
        model.Audio = WindowsShell.Audio();
        if (smoke) return;
        if (ticks % 8 == 0)
        {
            model.Microphone = WindowsShell.Audio(capture: true);
            _ = model.Media.Refresh();
        }
        if (ticks % 12 == 0) model.Network.Update();
        if (handle != 0 && ticks % 60 == 0 && (P.Items.Contains("Brightness") || surfaces.ContainsKey("Widgets")))
            _ = ReadBrightness();
        if (ticks % 60 == 0 && (P.Items.Contains("Temperature") || P.Automations.Health))
            _ = ReadTemperature();
        if (P.TrackScreenTime)
        {
            var now = DateTimeOffset.Now;
            var app = WindowsShell.ForegroundApp();
            var idle = WindowsShell.IdleTime();
            string? site = P.TrackWebsites && idle < TimeSpan.FromMinutes(2)
                ? browserSites.Observe(app?.Handle ?? 0, app?.Name, now) : null;
            if (site is null && (!P.TrackWebsites || idle >= TimeSpan.FromMinutes(2))) browserSites.Reset();
            model.Activity.Sample(now, app?.Name, idle, site);
        }
    }

    private void ReadWindows()
    {
        var current = WindowsShell.Windows();
        var signature = string.Join('|', current.OrderBy(item => item.Handle).Select(item => $"{item.Handle}:{item.Title}"));
        if (signature == windowSignature) return;
        windowSignature = signature;
        windows = current;
            if (!IsMouseOver && ContextMenu?.IsOpen != true && thumbnail is null) Refresh();
        else windowsDirty = true;
    }

    private async System.Threading.Tasks.Task ReadTemperature() => model.Temperature = await System.Threading.Tasks.Task.Run(WindowsShell.Temperature);
    private bool readingBrightness;
    private async System.Threading.Tasks.Task ReadBrightness()
    {
        if (readingBrightness) return;
        readingBrightness = true;
        try
        {
            var result = await System.Threading.Tasks.Task.Run(() => WindowsShell.Brightness(handle));
            model.BrightnessLevel = result.Supported ? result.Brightness : null;
        }
        finally { readingBrightness = false; }
    }

    private bool windowsDirty;
    private void Tick()
    {
        if (!secondary) model.Tick();
        if (++ticks % 4 == 0)
        {
            ReadLive();
            if (!smoke && !secondary) model.Automation.Tick(DateTime.Now);
            if (!smoke && !secondary && ticks % 120 == 4) RefreshOnline();
            if (!smoke && !secondary && P.ReplaceTaskbar && WindowsShell.TaskbarChanged) WindowsShell.HideTaskbar(P.AutoHideWindows);
        }
        if (windowsDirty && !IsMouseOver && ContextMenu?.IsOpen != true && thumbnail is null) { windowsDirty = false; Refresh(); }
        foreach (var update in faces) update();
        if (!smoke)
        {
            bool fullscreen = WindowsShell.IsFullscreen(display);
            var pointer = System.Windows.Forms.Cursor.Position;
            var dpi = VisualTreeHelper.GetDpi(this);
            bool atEdge = pointer.Y >= (Top + Height) * dpi.DpiScaleY - 3 &&
                pointer.X >= Left * dpi.DpiScaleX && pointer.X < (Left + Width) * dpi.DpiScaleX;
            var visible = !fullscreen && (!P.AutoHide || (P.ShowOnDesktop && WindowsShell.IsDesktopActive())
                || IsMouseOver || atEdge || surfaces.Values.Any(surface => surface.IsActive) || DateTime.UtcNow < flashUntil);
            if (visible != barVisible)
            {
                barVisible = visible;
                IsHitTestVisible = visible;
                BeginAnimation(OpacityProperty, new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromMilliseconds(125)));
            }
        }
        else if (previewLiveGlass) SampleGlass();
    }

    private void SampleGlass()
    {
        if (!IsLoaded || !Looks.Glassy(P) || background.ActualWidth <= 0 || background.ActualHeight <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var position = background.PointToScreen(new Point());
        var snapshot = glass.Update((int)position.X, (int)position.Y,
            (int)(background.ActualWidth * dpi.DpiScaleX), (int)(background.ActualHeight * dpi.DpiScaleY), Looks.Detail(P), Looks.Dispersion(P));
        if (snapshot is null) return;
        liveBackdrop.ImageSource = snapshot;
        background.Background = liveBackdrop;
    }

    private void RefreshOnline()
    {
        if (P.WeatherPlace is { } place) _ = model.Data.RefreshWeather(place);
        foreach (var id in P.Items)
        {
            if (P.DataInputs.TryGetValue(id, out var terms)) _ = model.Data.RefreshQuote(id, terms);
            if (ConnectedData.Supports(id)) _ = model.Connected.Refresh(id, P);
        }
        if (P.Automations.WorkInbox) _ = model.Connected.Refresh("Linear", P);
        if (P.Automations.Milestones) _ = model.Connected.Refresh("Total Revenue", P);
        if (DateTimeOffset.Now - lastCalendarRefresh >= TimeSpan.FromMinutes(20))
        {
            lastCalendarRefresh = DateTimeOffset.Now;
            foreach (string feed in P.CalendarSources.ToArray()) _ = RefreshCalendar(feed);
        }
    }

    private async System.Threading.Tasks.Task RefreshCalendar(string feed)
    {
        try
        {
            var current = await Calendars.Load(feed);
            if (!P.CalendarSources.Contains(feed)) return;
            var previous = P.Events.Where(item => item.Source == feed);
            if (previous.Select(item => (item.Title, item.Start, item.End, item.AllDay, item.Location))
                .SequenceEqual(current.Select(item => (item.Title, item.Start, item.End, item.AllDay, item.Location)))) return;
            P.Events.RemoveAll(item => item.Source == feed);
            P.Events.AddRange(current);
            model.Save(false);
        }
        catch (Exception ex) when (LiveData.IsDataError(ex) || ex is IOException or UnauthorizedAccessException) { }
    }

    // Увеличение как в Dock на Mac: масштаб плитки приложения зависит от расстояния до указателя по колоколу Гаусса,
    // соседние плитки раздвигаются (плитка под указателем стоит на месте), а движение сглаживается на каждом кадре, а не прыгает при входе в плитку.
    private readonly List<(Border Tile, ScaleTransform Scale, TranslateTransform Shift, bool App, double Width)> magnets = [];
    private Panel? magnetRow;
    private double? magnetX;
    private double magnetLast;
    private long magnetStamp;

    private void OnMagnify(object? sender, EventArgs e)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double dt = Math.Min(.05, (now - magnetStamp) / (double)System.Diagnostics.Stopwatch.Frequency);
        magnetStamp = now;
        MagnifyStep(dt);
    }

    internal void MagnifyStep(double dt)
    {
        int count = magnets.Count;
        if (count == 0) return;
        if (magnetX is null)
        {
            bool resting = true;
            for (int index = 0; index < count && resting; index++) resting = magnets[index].Scale.ScaleX == 1;
            if (resting) return;
        }
        double boost = P.Magnification ? Math.Clamp(P.MagnificationScale, 1.2, 3) - 1 : 0;
        double unit = magnets.Where(item => item.App).Select(item => item.Width).DefaultIfEmpty(48).Average() + Math.Max(0, P.Spacing);
        double follow = 1 - Math.Exp(-dt * 20);
        var centers = new double[count];
        var displacement = new double[count];
        double before = 0;
        for (int index = 0; index < count; index++)
        {
            var item = magnets[index];
            centers[index] = VisualTreeHelper.GetOffset(item.Tile).X + item.Tile.ActualWidth / 2;
            double target = 1;
            if (magnetX is { } pointer && boost > 0 && item.App)
            {
                double distance = (centers[index] - pointer) / unit;
                target = 1 + boost * Math.Exp(-distance * distance / 2.6);
            }
            double scale = item.Scale.ScaleX;
            scale += (target - scale) * follow;
            if (Math.Abs(target - scale) < .002) scale = target;
            item.Scale.ScaleX = item.Scale.ScaleY = scale;
            double extra = item.Width * (scale - 1);
            displacement[index] = before + extra / 2;
            before += extra;
            System.Windows.Controls.Panel.SetZIndex(item.Tile, (int)Math.Round((scale - 1) * 100));
        }
        // сдвиг считается от точки под указателем: она остаётся на месте, левые плитки уходят влево, правые вправо
        double pivot = displacement[0];
        if (magnetLast >= centers[count - 1]) pivot = displacement[count - 1];
        else
            for (int index = 0; index + 1 < count; index++)
                if (magnetLast < centers[index + 1])
                {
                    double span = Math.Max(1, centers[index + 1] - centers[index]);
                    double part = Math.Clamp((magnetLast - centers[index]) / span, 0, 1);
                    pivot = displacement[index] + (displacement[index + 1] - displacement[index]) * part;
                    break;
                }
        for (int index = 0; index < count; index++) magnets[index].Shift.X = displacement[index] - pivot;
    }

    // Для самопроверки: ведёт «указатель» по первой плитке приложения и проверяет рост, неподвижность плитки под указателем и возврат в покой; null — приложений в доке нет
    internal string? MagnifyCheck()
    {
        var first = magnets.FirstOrDefault(item => item.App);
        if (first.Tile is null) return null;
        bool was = P.Magnification;
        P.Magnification = true;
        try
        {
            magnetLast = VisualTreeHelper.GetOffset(first.Tile).X + first.Tile.ActualWidth / 2;
            magnetX = magnetLast;
            for (int step = 0; step < 90; step++) MagnifyStep(.03);
            double want = Math.Clamp(P.MagnificationScale, 1.2, 3);
            if (Math.Abs(first.Scale.ScaleX - want) > .02) return $"под указателем масштаб {first.Scale.ScaleX:0.00}, ожидался {want:0.00}";
            if (Math.Abs(first.Shift.X) > 1) return $"плитка под указателем сдвинулась на {first.Shift.X:0.0}";
            var farthest = magnets.OrderByDescending(item => Math.Abs(VisualTreeHelper.GetOffset(item.Tile).X - VisualTreeHelper.GetOffset(first.Tile).X)).First();
            if (farthest.App && farthest.Scale.ScaleX > first.Scale.ScaleX - .01) return "далёкая плитка выросла так же, как плитка под указателем";
            magnetX = null;
            for (int step = 0; step < 200; step++) MagnifyStep(.03);
            if (magnets.Any(item => item.Scale.ScaleX != 1 || Math.Abs(item.Shift.X) > .01)) return "плитки не вернулись в покой после ухода указателя";
            return "";
        }
        finally { P.Magnification = was; magnetX = null; }
    }

    private void Refresh()
    {
        faces.Clear();
        magnets.Clear();
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        magnetRow = row;
        var groups = windows.GroupBy(item => WindowsShell.ResolveApp(item.App), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var scale = P.Size / 75;
        var iconSize = P.IconSize * .76;
        var tileHeight = Math.Max(47, iconSize + 10);
        var tiles = P.Items.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var pinned = tiles.Where(id => id.StartsWith("app:", StringComparison.Ordinal))
            .Select(id => WindowsShell.MatchApp(id[4..])).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var afterApps = tiles.FindLastIndex(id => id.StartsWith("app:", StringComparison.Ordinal) || id == "Start") + 1;
        tiles.InsertRange(afterApps, groups.Keys.Where(path => !pinned.Contains(path)).OrderBy(path => path).Select(path => "app:" + path));
        foreach (var id in tiles)
        {
            if (id == "Start") row.Children.Add(StartTile(tileHeight));
            else if (id.StartsWith("files:", StringComparison.Ordinal) && P.FileGroups.TryGetValue(id, out var group))
            {
                Border? tile = null;
                tile = Tile(id, Symbol("\uE8B7", 22, Accent(P)), tileHeight, tileHeight, () => OpenFiles(id, group, tile));
                var menu = CommonMenu();
                menu.Items.Insert(0, Menu("Remove from dock", () => { P.FileGroups.Remove(id); model.ToggleWidget(id); }));
                tile.ContextMenu = menu;
                row.Children.Add(tile);
            }
            else if (id.StartsWith("app:", StringComparison.Ordinal))
            {
                groups.TryGetValue(WindowsShell.MatchApp(id[4..]), out var running);
                row.Children.Add(AppTile(id, running ?? [], tileHeight, iconSize));
            }
            else if (Widgets.Available.TryGetValue(id, out var widget))
            {
                Border? tile = null;
                var face = Widgets.Face(widget, model, false, out Action update,
                    id == "Quick Toggles" ? name =>
                    {
                        if (name is "Wi-Fi" or "Bluetooth") OpenWidget(name, tile);
                        else WindowsShell.Launch(Widgets.QuickLinks.First(item => item.Name == name).Address);
                    } : null);
                faces.Add(update);
                tile = Tile(id, face, Widgets.WidthOf(P, widget), tileHeight, () => OpenWidget(id, tile));
                if (id == "Pomodoro") tile.BorderBrush = Brush("#FF4A43");
                var menu = CommonMenu();
                if (!model.IsPinned(id)) menu.Items.Insert(0, Menu("Remove from dock", () => model.ToggleWidget(id)));
                var sizes = Widgets.SizeChoices(id);
                if (sizes.Length > 0)
                {
                    // размер виджета: заголовок и варианты, у текущего галочка
                    double now = Widgets.WidthOf(P, widget);
                    int at = 0;
                    menu.Items.Insert(at++, MenuHeader("Widget size"));
                    foreach (var (label, width) in sizes)
                    {
                        double chosen = width;
                        menu.Items.Insert(at++, Menu(label, () => { P.WidgetWidths[id] = chosen; model.Save(); }, Math.Abs(chosen - now) < 1 ? MenuGlyph("\uE73E") : null));
                    }
                    menu.Items.Insert(at, new Separator());
                }
                if (Widgets.Styles(id).Length > 1)
                    menu.Items.Insert(0, Menu("Widget style…", () => OpenWidgetStyles(id, tile)));
                tile.ContextMenu = menu;
                row.Children.Add(tile);
            }
        }

        if (row.Children.Count == 0)
            row.Children.Add(Tile("Widgets", Symbol("\uE71D"), 47, tileHeight, () => OpenGallery()));
        row.LayoutTransform = new ScaleTransform(scale, scale);
        bool useLiveGlass = Looks.Glassy(P) && (!smoke || previewLiveGlass);
        var inner = Card(row, 15, Looks.Material(P) == "theme" && !Looks.Glassy(P) ? DockFinish(P.Style) : Looks.Surface(P, useLiveGlass));
        inner.Padding = new(Math.Max(0, P.Padding) * scale);
        background.Child = inner;
        background.BorderThickness = new(1);
        background.BorderBrush = Looks.Rim(P);
        background.Background = useLiveGlass
            ? liveBackdrop.ImageSource is not null ? liveBackdrop : WallpaperBackdrop() ?? Brushes.Transparent
            : Brushes.Transparent;
        PlaceLayout(inner);
        ExcludeFromCapture();
        foreach (var update in faces) update();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);

    // Живое стекло снимает экран под доком. Окно дока (прозрачное, слоистое) попадало в такой снимок само, и стекло показывало собственное
    // тёмное изображение вместо фона. Пока стекло включено, док исключается из захвата экрана (WDA_EXCLUDEFROMCAPTURE, Windows 10 2004+).
    private void ExcludeFromCapture()
    {
        if (handle == 0) return;
        SetWindowDisplayAffinity(handle, Looks.Glassy(P) && (!smoke || previewLiveGlass) ? 0x11u : 0u);
    }

    private Brush? WallpaperBackdrop()
    {
        var path = WindowsShell.Wallpaper();
        if (StringComparer.OrdinalIgnoreCase.Equals(currentWallpaper, path)) return wallpaperBrush;
        currentWallpaper = path;
        wallpaperBrush = null;
        if (path is null) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.DecodePixelWidth = 960;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            wallpaperBrush = new ImageBrush(bitmap) { Stretch = Stretch.Fill, Viewbox = new Rect(0, .86, 1, .14), Opacity = .7 };
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException) { }
        return wallpaperBrush;
    }

    private void PlaceLayout(FrameworkElement inner, bool renegotiate = false)
    {
        if (placing) return;
        placing = true;
        try
        {
            var screen = monitor ?? Screen.PrimaryScreen;
            var screenPx = screen?.Bounds ?? new(0, 0, 1920, 1080);
            var workPx = screen?.WorkingArea ?? screenPx;
            var dpi = VisualTreeHelper.GetDpi(this);
            if (handle == 0 && screenPx.Width > 0 && !secondary)
            {
                var approximate = screenPx.Width / SystemParameters.PrimaryScreenWidth;
                if (approximate > 0) dpi = new DpiScale(approximate, approximate);
            }
            display = new(screenPx.Left, screenPx.Top, screenPx.Width, screenPx.Height);
            glassHz = Displays.Hz(screen?.DeviceName);
            var width = screenPx.Width / dpi.DpiScaleX;
            inner.LayoutTransform = Transform.Identity;
            inner.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var fit = Math.Min(1, (width - 10) / Math.Max(1, inner.DesiredSize.Width + 2));
            if (fit < 1)
            {
                inner.LayoutTransform = new ScaleTransform(fit, fit);
                inner.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            }
            Width = P.FullWidth ? width - 4 : Math.Min(width, inner.DesiredSize.Width + 2);
            double barHeight = inner.DesiredSize.Height + 2;
            dockHeadroom = P.Magnification ? barHeight * (Math.Clamp(P.MagnificationScale, 1.2, 3) - 1) : 0;
            Height = barHeight + dockHeadroom;
            Left = (screenPx.Left / dpi.DpiScaleX) + (width - Width) / 2;
            bool replace = !smoke && !secondary && handle != 0 && P.ReplaceTaskbar && WindowsShell.HideTaskbar(P.AutoHideWindows);
            if (replace && !P.AutoHide)
            {
                var height = (int)Math.Ceiling((barHeight + P.Edge) * dpi.DpiScaleY);
                if (!reserved || height != reservedHeight || renegotiate)
                {
                    var result = WindowsShell.Reserve(handle, display, height, !reserved);
                    if (result.HasValue)
                    {
                        reserved = true;
                        approvedArea = result;
                    }
                    reservedHeight = height;
                }
            }
            else
            {
                if (reserved) WindowsShell.Release(handle);
                reserved = false;
                approvedArea = null;
                if (!replace && !secondary) WindowsShell.RestoreTaskbar();
            }
            var bottom = replace ? approvedArea?.Bottom ?? screenPx.Bottom : workPx.Bottom;
            Top = bottom / dpi.DpiScaleY - Height - P.Edge - (replace ? 0 : 2);
        }
        finally { placing = false; }
    }

    private Border Tile(string id, FrameworkElement face, double width, double height, Action click)
    {
        bool app = id.StartsWith("app:", StringComparison.Ordinal);
        bool cardVisible = !app || P.AppCards;
        var light = Visuals.LightTiles(P);
        var tileColor = !cardVisible ? Brushes.Transparent : light ? Brush("#A6FFFFFF")
            : P.Style == "Liquid" ? Brush("#2E000000")
            : P.Style == "Clear" && P.ClearWidgets == "Glass" ? GlassTint() : Brush("#E5070708");
        if (cardVisible && Looks.Glassy(P) && P.Transparency > 0)
        {
            // прозрачность делает прозрачными и карточки виджетов, иначе стекло под доком видно только в узкой рамке между ними
            tileColor = tileColor.Clone();
            tileColor.Opacity = 1 - .8 * Looks.Clear(P);
        }
        var button = Button(face, click, tileColor, 13);
        var tile = Card(button,
            14, Brushes.Transparent, cardVisible ? Brush(light ? "#A8CACAD0" : P.Style == "Liquid" ? "#28FFFFFF" : "#AD444447") : null);
        tile.Width = width;
        tile.Height = height;
        double spacing = Math.Max(0, P.Spacing) / 2;
        tile.Margin = new(spacing, 0, spacing, 0);
        button.Padding = new(width > 60 ? 8 : 2, 1, width > 60 ? 8 : 2, 1);
        tile.RenderTransformOrigin = new(.5, 1);
        // Масштаб и сдвиг плитки задаёт MagnifyStep каждый кадр (увеличение как в Dock на Mac), здесь только заготовки преобразований
        var magnify = new ScaleTransform();
        var shift = new TranslateTransform();
        tile.RenderTransform = new TransformGroup { Children = { magnify, shift } };
        magnets.Add((tile, magnify, shift, app, width));
        var name = app ? Path.GetFileNameWithoutExtension(id[4..]) : id;
        AutomationProperties.SetName(button, app ? name : L10n.T(name));
        if (P.Names) tile.ToolTip = app ? name : L10n.T(name);
        if (!P.Items.Contains(id) || P.Locked) return tile;
        Point start = default;
        tile.PreviewMouseLeftButtonDown += (_, eventArgs) => start = eventArgs.GetPosition(tile);
        tile.PreviewMouseMove += (_, eventArgs) =>
        {
            if (eventArgs.LeftButton != MouseButtonState.Pressed || !tile.IsMouseCaptureWithin) return;
            var delta = eventArgs.GetPosition(tile) - start;
            if (Math.Abs(delta.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(delta.Y) > SystemParameters.MinimumVerticalDragDistance)
                DragDrop.DoDragDrop(tile, new DataObject(DragId, id), DragDropEffects.Move);
        };
        tile.AllowDrop = true;
        tile.DragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(DragId)) return;
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        };
        tile.Drop += (_, e) =>
        {
            if (e.Data.GetData(DragId) is not string source) return;
            e.Handled = true;
            model.MoveItem(source, id);
        };
        return tile;
    }

    private Border StartTile(double height)
    {
        var logo = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Rows = 2, Width = 29, Height = 29 };
        for (int i = 0; i < 4; i++)
            logo.Children.Add(new Border { Background = Brush(new[] { "#E9E9EA", "#A2A2A4", "#B4B4B6", "#D9D9DC" }[i]), Margin = new(.6) });
        var tile = Tile("Start", logo, height, height, () => WindowsShell.Key(0x5B));
        var menu = CommonMenu();
        menu.Items.Insert(0, Menu("Power user menu", () => WindowsShell.Key(0x5B, 0x58)));
        menu.Items.Insert(1, Menu("Task Manager", () => WindowsShell.Launch("taskmgr.exe")));
        menu.Items.Insert(2, Menu("File Explorer", () => WindowsShell.Launch("explorer.exe")));
        menu.Items.Insert(3, Menu("Windows Settings", () => WindowsShell.Launch("ms-settings:")));
        menu.Items.Insert(4, Menu("Search", () => WindowsShell.Key(0x5B, 0x53)));
        menu.Items.Insert(5, Menu("Show Desktop", () => WindowsShell.Key(0x5B, 0x44)));
        tile.ContextMenu = menu;
        return tile;
    }

    private Border AppTile(string id, List<WindowsShell.WindowInfo> running, double height, double iconSize)
    {
        string path = id[4..];
        if (!icons.TryGetValue(path, out var image) || image is null && running.Count > 0)
            icons[path] = image = WindowsShell.Icon(path, running.FirstOrDefault()?.Handle ?? 0);
        FrameworkElement graphic = image is null ? Symbol("\uE8A7", iconSize * .7) : new Image { Source = image, Width = iconSize, Height = iconSize };
        RenderOptions.SetBitmapScalingMode(graphic, BitmapScalingMode.HighQuality);
        var layered = new Grid();
        layered.Children.Add(graphic);
        if (running.Count > 0)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 3, Height = 3, Fill = Accent(P),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
            layered.Children.Add(dot);
        }
        if (P.AppBadges && running.Count > 0)
        {
            var unread = running.Select(window => Regex.Match(window.Title, @"^\((?<n>[0-9]{1,4}\+?)\)\s")).FirstOrDefault(match => match.Success);
            if (unread is not null)
            {
                var badge = Card(Text(unread.Groups["n"].Value, 9, true), 8, Accent(P));
                badge.Padding = new(4, 0, 4, 1);
                badge.HorizontalAlignment = HorizontalAlignment.Right;
                badge.VerticalAlignment = VerticalAlignment.Top;
                layered.Children.Add(badge);
            }
        }
        Border? tile = null;
        tile = Tile(id, layered, height, height, () =>
        {
            CloseThumbnail();
            if (running.Count == 1) WindowsShell.Activate(running[0].Handle, true);
            else if (running.Count > 1)
            {
                if (tile is not null) ShowThumbnail(tile, running, true);
                else WindowsShell.ActivateNext(running);
            }
            else if (!WindowsShell.Launch(path)) Report(L10n.F($"Could not open {path}"));
        });
        // Меню собирается заново при каждом открытии, чтобы список переходов программы был свежим
        var appMenu = DockMenu();
        tile.ContextMenuOpening += (_, _) => FillAppMenu(appMenu, id, path, running, image);
        FillAppMenu(appMenu, id, path, running, image);
        tile.ContextMenu = appMenu;
        if (P.WindowPreviews && running.Count > 0)
        {
            var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
            tile.MouseEnter += (_, _) => { closePreview.Stop(); wait.Start(); };
            tile.MouseLeave += (_, _) => { wait.Stop(); closePreview.Stop(); closePreview.Start(); };
            wait.Tick += (_, _) =>
            {
                wait.Stop();
                if (tile.IsMouseOver && !appMenu.IsOpen && running.Count > 0) ShowThumbnail(tile, running);
            };
        }
        appTiles[path] = tile;
        appTiles[WindowsShell.ResolveApp(path)] = tile;
        return tile;
    }

    // Регистрирует глобальные сочетания из настроек (Hotkeys.cs); возвращает номера, которые Windows не приняла, например, если сочетание занято другой программой
    internal List<int> ApplyHotkeys()
    {
        var refused = new List<int>();
        if (smoke || handle == 0 || secondary) return refused;
        for (int slot = 0; slot < Hotkeys.Items.Length; slot++)
        {
            SystemHotkey.UnregisterHotKey(handle, slot + 1);
            if (Hotkeys.TryParse(Hotkeys.Get(P, slot), out uint modifiers, out uint key) && !SystemHotkey.RegisterHotKey(handle, slot + 1, modifiers | 0x4000, key))
                refused.Add(slot);
        }
        return refused;
    }

    // Сочетание открывает или закрывает окно; диктовка при открытии сразу начинает запись
    internal bool StartDictationOnOpen;
    internal void RunHotkey(int slot)
    {
        string widget = Hotkeys.Items[slot];
        if (widget == "Dictation" && !surfaces.ContainsKey(widget) && !Dictation.IsRecording) StartDictationOnOpen = true;
        OpenWidget(widget);
    }

    // Значок прыгает, когда окно программы мигает в панели задач (ей что-то нужно); скрытый док на три секунды показывается
    internal bool FlashApp(string path)
    {
        if (path.Length == 0 || !appTiles.TryGetValue(path, out var tile) || !tile.IsVisible) return false;
        flashUntil = DateTime.UtcNow.AddSeconds(3);
        var move = new TranslateTransform();
        tile.RenderTransform = move;
        var hop = new DoubleAnimationUsingKeyFrames { RepeatBehavior = new RepeatBehavior(2) };
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(-12, KeyTime.FromPercent(.25), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(.5), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(-5, KeyTime.FromPercent(.7), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(.9), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        hop.Duration = TimeSpan.FromMilliseconds(1100);
        move.BeginAnimation(TranslateTransform.YProperty, hop);
        return true;
    }

    private void ShowThumbnail(FrameworkElement target, List<WindowsShell.WindowInfo> running, bool clicked = false)
    {
        CloseThumbnail();
        var previews = running.Take(clicked ? 8 : 4).ToArray();
        if (previews.Length == 0) return;
        // Каждое окно — отдельная карточка: значок и заголовок сверху, живая миниатюра под ними. Не помещаются в ширину — переходят на следующий ряд.
        const double cardWidth = 208, thumbHeight = 116, header = 24, inset = 6, gap = 8, pad = 10;
        double cardHeight = inset * 2 + header + 4 + thumbHeight;
        var dpi = VisualTreeHelper.GetDpi(this);
        double screenWidth = (display.Right - display.Left) / dpi.DpiScaleX;
        int columns = Math.Max(1, Math.Min(previews.Length, (int)((screenWidth * .92 - pad * 2 + gap) / (cardWidth + gap))));
        int rows = (previews.Length + columns - 1) / columns;
        var preview = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowActivated = false, ShowInTaskbar = false, Topmost = true, Background = SurfaceInk,
            Width = columns * (cardWidth + gap) - gap + pad * 2, Height = rows * (cardHeight + gap) - gap + pad * 2, Owner = this };
        preview.MouseEnter += (_, _) => closePreview.Stop();
        preview.MouseLeave += (_, _) => { closePreview.Stop(); closePreview.Start(); };
        preview.PreviewKeyDown += (_, args) => { if (args.Key == Key.Escape) CloseThumbnail(); };
        // по центру значка, но целиком в пределах монитора
        var position = target.PointToScreen(new Point(0, 0));
        double center = (position.X + target.ActualWidth * dpi.DpiScaleX / 2) / dpi.DpiScaleX;
        preview.Left = Math.Clamp(center - preview.Width / 2, display.Left / dpi.DpiScaleX,
            Math.Max(display.Left / dpi.DpiScaleX, display.Right / dpi.DpiScaleX - preview.Width));
        preview.Top = BarTop - preview.Height - 7;

        // Закрыть окно прямо из миниатюры; остальные остаются, а когда закрыто последнее, миниатюры исчезают
        void CloseWindow(WindowsShell.WindowInfo window)
        {
            WindowsShell.CloseWindow(window.Handle);
            var rest = running.Where(item => item.Handle != window.Handle).ToList();
            if (rest.Count == 0) CloseThumbnail(); else ShowThumbnail(target, rest, clicked);
        }

        var board = new Canvas();
        var slots = new List<(double X, double Y)>();
        for (int index = 0; index < previews.Length; index++)
        {
            var window = previews[index];
            double x = pad + index % columns * (cardWidth + gap), y = pad + index / columns * (cardHeight + gap);
            slots.Add((x, y));
            var icon = new Image { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center,
                Source = WindowsShell.Icon(window.App, window.Handle) };
            var title = RawText(window.Title, 12, true, Paper);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.VerticalAlignment = VerticalAlignment.Center;
            title.Margin = new(8, 0, 4, 0);
            var close = Card(Symbol("", 10, Muted), 6, Brushes.Transparent);
            close.Width = close.Height = 20;
            close.ToolTip = L10n.T("Close window");
            close.MouseEnter += (_, _) => close.Background = Brush("#33FFFFFF");
            close.MouseLeave += (_, _) => close.Background = Brushes.Transparent;
            close.MouseLeftButtonUp += (_, args) => { args.Handled = true; CloseWindow(window); };
            var top = new DockPanel { Height = header, VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(icon, Dock.Left);
            DockPanel.SetDock(close, Dock.Right);
            top.Children.Add(icon);
            top.Children.Add(close);
            top.Children.Add(title);
            var card = Card(top, 12, Brushes.Transparent, Brush("#26FFFFFF"));
            card.Width = cardWidth;
            card.Height = cardHeight;
            card.Padding = new(inset + 2, inset, inset + 2, inset);
            card.VerticalAlignment = VerticalAlignment.Top;
            card.Cursor = Cursors.Hand;
            card.MouseEnter += (_, _) => { card.Background = Brush("#22FFFFFF"); card.BorderBrush = Accent(P); };
            card.MouseLeave += (_, _) => { card.Background = Brushes.Transparent; card.BorderBrush = Brush("#26FFFFFF"); };
            card.MouseLeftButtonUp += (_, _) => { CloseThumbnail(); WindowsShell.Activate(window.Handle); };
            card.MouseDown += (_, args) => { if (args.ChangedButton == MouseButton.Middle) { args.Handled = true; CloseWindow(window); } };
            Canvas.SetLeft(card, x);
            Canvas.SetTop(card, y);
            board.Children.Add(card);
        }
        preview.Content = new Border { Child = board, Background = SurfaceInk, BorderBrush = Looks.Rim(P), BorderThickness = new(1) };
        preview.SourceInitialized += (_, _) => WindowsShell.RoundCorners(new WindowInteropHelper(preview).Handle);
        preview.Show();
        var host = new WindowInteropHelper(preview).Handle;
        var thumbs = slots.Select((slot, index) => WindowsShell.Thumbnail(host, previews[index].Handle,
            (int)((slot.X + inset + 2) * dpi.DpiScaleX), (int)((slot.Y + inset + header + 4) * dpi.DpiScaleY),
            (int)((cardWidth - (inset + 2) * 2) * dpi.DpiScaleX), (int)(thumbHeight * dpi.DpiScaleY))).ToArray();
        preview.Closed += (_, _) => { foreach (var thumb in thumbs.Where(value => value != 0)) WindowsShell.DwmUnregisterThumbnail(thumb); };
        LiveThumbnails = thumbs.Count(value => value != 0);
        thumbnail = preview;
    }

    // Для самопроверки: показывает окно миниатюр для первых count окон рабочего стола, привязанное к первой плитке дока
    internal Window? PreviewTest(int count)
    {
        var list = WindowsShell.Windows().Take(count).ToList();
        if (list.Count == 0 || magnets.Count == 0) return null;
        ShowThumbnail(magnets[0].Tile, list, true);
        return thumbnail;
    }

    // сколько живых миниатюр Windows приняла в последнем окне миниатюр (для самопроверки)
    internal int LiveThumbnails { get; private set; }

    internal void ClosePreviewTest() => CloseThumbnail();

    internal DockModel Model => model;

    // Для самопроверки: есть ли в доке плитка нужной ширины
    internal bool HasTileWidth(double width) => magnets.Any(item => Math.Abs(item.Width - width) < .5);

    // Перестроить док: разрешение или положение его монитора могли измениться
    internal void Relayout() => Refresh();

    // Для самопроверки: где док стоит на экране (физические пиксели) и на каком мониторе
    internal Rect ScreenBounds()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new(Left * dpi.DpiScaleX, Top * dpi.DpiScaleY, ActualWidth * dpi.DpiScaleX, ActualHeight * dpi.DpiScaleY);
    }

    private void CloseThumbnail()
    {
        closePreview.Stop();
        thumbnail?.Close();
        thumbnail = null;
    }

    private ContextMenu CommonMenu()
    {
        var menu = DockMenu();
        menu.Items.Add(Menu("Widgets…", () => OpenGallery(), MenuWidgetsIcon()));
        menu.Items.Add(Menu("Settings", () => OpenSettings(), MenuGlyph("")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Menu("Pin Windows folder", () => { if (!P.Locked) OpenFolderPin(); }));
        menu.Items.Add(Menu("Pin file or app…", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
            if (dialog.ShowDialog() != true) return;
            PinPaths(dialog.FileNames);
        }));
        menu.Items.Add(Menu("Quit FlowBar", Close));
        return menu;
    }

    internal Window OpenFolderPin() => Surface("Pin Windows folder", 350, 342, false, (window, shell) =>
    {
        var title = (FrameworkElement)Right(Text("Pin Windows folder", 16, true), CloseButton(window));
        title.Margin = new(0, 0, 0, 8);
        var panel = Stack(title);
        panel.Margin = new(13);
        foreach (var id in Widgets.WindowsFolders)
        {
            var entry = Button(id, () =>
            {
                if (!P.Locked && !P.Items.Contains(id)) model.ToggleWidget(id);
                window.Close();
            });
            entry.IsEnabled = !P.Items.Contains(id);
            System.Windows.Automation.AutomationProperties.SetName(entry, L10n.T(id));
            if (!entry.IsEnabled) entry.Opacity = .5;
            entry.Margin = new(0, 0, 0, 5);
            panel.Children.Add(entry);
        }
        shell.Child = Scroll(panel);
    });

    private void PinPaths(IEnumerable<string> paths)
    {
        string[] valid = paths.Where(path => File.Exists(path) || Directory.Exists(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (valid.Length == 0) return;
        if (valid.Length == 1)
        {
            string app = "app:" + valid[0];
            if (!P.Items.Contains(app, StringComparer.OrdinalIgnoreCase)) P.Items.Add(app);
        }
        else
        {
            var id = "files:" + Guid.NewGuid().ToString("N");
            P.FileGroups[id] = new FileGroup { Name = Path.GetFileName(Path.GetDirectoryName(valid[0])) ?? "Files", Paths = [.. valid] };
            P.Items.Add(id);
        }
        model.Save();
    }

    private void OpenFiles(string id, FileGroup group, FrameworkElement? anchor) => Surface(id, 365, 340, true, (window, shell) =>
    {
        var entries = Stack(Right(RawText(group.Name, 16, true), CloseButton(window)));
        entries.Margin = new(12);
        foreach (string path in group.Paths)
            entries.Children.Add(Button(RawText(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), 14, true), () => WindowsShell.Launch(path)));
        shell.Child = Scroll(entries);
    }, anchor);

    private static class SystemHotkey
    {
        [System.Runtime.InteropServices.DllImport("user32")] public static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
        [System.Runtime.InteropServices.DllImport("user32")] public static extern bool UnregisterHotKey(nint window, int id);
    }
}
