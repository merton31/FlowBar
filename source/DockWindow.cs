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
    internal static readonly Regex UnreadPrefix = new(@"^\((?<n>[0-9]{1,4}\+?)\)\s", RegexOptions.Compiled);
    private bool glassSkipped;
    private (Point, double, double, int, int, int, System.Drawing.Bitmap?) glassInputs;
    private long glassSecond;
    internal int GlassFps { get; private set; }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (smoke || !barVisible || !Looks.Glassy(P)) return;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        // Пока фон под доком неподвижен (30 кадров подряд без изменений), снимаем 60 раз в секунду: быстрее незачем, а процессор не греется
        int rate = Math.Min(WindowsShell.Economy ? 30 : int.MaxValue, glassIdle > 30 ? Math.Min(60, glassHz) : glassHz);
        if (now - glassStamp < System.Diagnostics.Stopwatch.Frequency * .85 / rate) return;
        glassStamp = now;
        SampleGlass();
        glassIdle = !glassSkipped && glass.Changed ? 0 : glassIdle + 1;
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
    // Окно виджета закрывается, как только док забирает фокус (нажатие мыши), и до самого щелчка; без этой отметки щелчок открыл бы окно заново вместо закрытия
    private (string? Key, long At) closedByFocus;
    // плитки программ по пути (для прыжка значка при мигании окна) и время, до которого док показывается из-за мигания
    private readonly Dictionary<string, Border> appTiles = new(StringComparer.OrdinalIgnoreCase);
    private DateTime flashUntil, dragOverUntil;
    private List<WindowsShell.WindowInfo> windows = [];
    private string windowSignature = "";
    // Монитор каждого окна и активное окно: из них складываются значки открытых окон под иконками
    private Dictionary<nint, string> windowScreens = [];
    private nint activeWindow;
    private Rect display;
    // Виджеты этого дока: у дополнительного монитора со своей раскладкой — его список, иначе общий (основной)
    internal List<string> Items => monitor is not null && P.Monitors.TryGetValue(monitor.DeviceName, out var setup) && setup.Widgets == "own" ? setup.Items : P.Items;
    // «Только приложения» на дополнительном мониторе: без виджетов, остаются Пуск и значки программ
    private bool AppsOnly => monitor is not null && P.Monitors.TryGetValue(monitor.DeviceName, out var setup) && setup.Widgets == "none";
    private bool PinnedHere(string id) => ReferenceEquals(Items, P.Items) && model.IsPinned(id);
    // Монитор этого дока (основной — у главного)
    private Screen ThisScreen => monitor ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
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
    private int edgeTicks; // сколько опросов подряд указатель держится у нижнего края при полноэкранном окне
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
        WireDrag();
        // Пока тянут файл из другой программы, IsMouseOver у дока не меняется: держим док открытым по самому перетаскиванию
        DragOver += (_, _) => dragOverUntil = DateTime.UtcNow.AddMilliseconds(600);
        Drop += (_, e) =>
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
            // как на панели задач Windows: перетаскиванием закрепляются программы, ярлыки и папки, а документ — нет (для него есть «Закрепить файл или программу…»)
            if (!P.Locked) PinPaths(paths.Where(path => Directory.Exists(path) || new[] { ".exe", ".lnk", ".url", ".appref-ms", ".bat", ".cmd" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)));
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
                    ActivityLog.Record("горячая клавиша", (slot + 1).ToString());
                    Dispatcher.BeginInvoke(() => RunHotkey(slot));
                    handled = true;
                }
                if (msg == 0x8066 && w == 1 && reserved && !placing)
                    Dispatcher.BeginInvoke(() => PlaceLayout((FrameworkElement)background.Child, renegotiate: true));
                if (msg == taskbarRestart) ActivityLog.Record("система: панель задач Windows перезапущена");
                else if (msg == 0x007E) ActivityLog.Record("система: изменились параметры экрана");
                else if (msg == 0x02E0) ActivityLog.Record("система: изменился масштаб экрана (DPI)");
                if (msg == 0x8066 && w == 1 && reserved && !placing) ActivityLog.Record("система: Windows изменила рабочую область");
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
        model.Media.Changed += RedrawFaces;
        Closed += (_, _) =>
        {
            model.Media.Changed -= RedrawFaces;
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
        WindowsShell.ReadEconomy();
        model.Audio = WindowsShell.Audio();
        if (smoke) return;
        if (ticks % 8 == 0)
        {
            model.Microphone = WindowsShell.Audio(capture: true);
        }
        if (ticks % 12 == 0) model.Network.Update();
        if (handle != 0 && ticks % 60 == 0 && (P.Items.Contains("Brightness") || surfaces.ContainsKey("Widgets")))
            _ = ReadBrightness();
        if (ticks % (temperatureMisses >= 3 ? 2400 : 60) == 0 && (P.Items.Contains("Temperature") || P.Automations.Health))
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
        // Активным считается последнее окно из списка, бывшее на переднем плане (сам док фокус не берёт)
        var front = WindowsShell.Foreground();
        if (current.Any(item => item.Handle == front)) activeWindow = front;
        else if (!current.Any(item => item.Handle == activeWindow)) activeWindow = 0;
        var screens = current.ToDictionary(item => item.Handle, item => WindowsShell.ScreenOf(item.Handle));
        // Из заголовка панель использует только число непрочитанного в начале «(3) …»; остальное (индикатор в терминале, загрузка вкладки, таймер игры) меняется часто и не должно пересобирать весь док
        var signature = string.Join('|', current.OrderBy(item => item.Handle).Select(item => $"{item.Handle}:{UnreadPrefix.Match(item.Title).Value}:{screens[item.Handle]}")) + "#" + activeWindow;
        // свежие заголовки (для подсказок и миниатюр) сохраняем всегда, а док пересобираем только при изменении подписи
        windows = current;
        windowScreens = screens;
        if (signature == windowSignature) return;
        windowSignature = signature;
        if (!IsMouseOver && ContextMenu?.IsOpen != true && thumbnail is null) Refresh();
        else windowsDirty = true;
    }

    // Без прав администратора Windows не отдаёт температуру: после трёх пустых ответов подряд спрашиваем раз в десять минут, а не каждые 15 секунд
    private int temperatureMisses;
    private async System.Threading.Tasks.Task ReadTemperature()
    {
        model.Temperature = await System.Threading.Tasks.Task.Run(WindowsShell.Temperature);
        temperatureMisses = model.Temperature is null ? temperatureMisses + 1 : 0;
    }
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

    // Раз в десять секунд (и при запуске) сверяем закреплённые значки панели Windows; читаем их заново, только если они поменялись
    private string? pinSignature;
    private void SyncWindowsPins()
    {
        string signature = WindowsPins.Signature();
        if (signature.Length == 0 || signature == pinSignature) return;
        pinSignature = signature;
        model.ImportWindowsPins(WindowsPins.Read());
    }

    // Системный трей Windows над плиткой, по которой нажали (на экране этой панели)
    private void OpenWindowsTray(FrameworkElement? tile)
    {
        CloseThumbnail();
        var screen = (monitor ?? Screen.PrimaryScreen)?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        var anchor = new System.Drawing.Rectangle(screen.Left + screen.Width / 2, screen.Bottom, 0, 0);
        if (tile is not null && tile.IsLoaded)
        {
            var topLeft = tile.PointToScreen(new Point(0, 0));
            var bottomRight = tile.PointToScreen(new Point(tile.ActualWidth, tile.ActualHeight)); // PointToScreen уже в физических пикселях
            anchor = new System.Drawing.Rectangle((int)topLeft.X, (int)topLeft.Y, (int)(bottomRight.X - topLeft.X), (int)(bottomRight.Y - topLeft.Y));
        }
        _ = WindowsShell.OpenTrayFlyout(anchor, screen);
    }

    private bool windowsDirty;
    private DateTime hoverUntil;
    private bool lastFullscreen;
    private void RedrawFaces() { if (barVisible) foreach (var update in faces.ToArray()) update(); }

    private void Tick()
    {
        if (!secondary) model.Tick();
        // состояние плеера приходит событиями системного сеанса; раз в две секунды перечитываем на случай пропущенного события
        if (!smoke && !secondary && ticks % 8 == 0) _ = model.Media.Refresh();
        if (++ticks % 4 == 0)
        {
            ReadLive();
            if (!smoke && !secondary) model.Automation.Tick(DateTime.Now);
            if (!smoke && !secondary && ticks % 120 == 4) RefreshOnline();
            if (!smoke && !secondary && ticks % 40 == 8) SyncWindowsPins();
            if (!smoke && !secondary && P.ReplaceTaskbar && WindowsShell.TaskbarChanged) WindowsShell.HideTaskbar(P.AutoHideWindows);
        }
        if (windowsDirty && !IsMouseOver && ContextMenu?.IsOpen != true && thumbnail is null) { windowsDirty = false; Refresh(); }
        // скрытый док (автоскрытие, игра на весь экран) не виден, поэтому плитки не обновляем; данные собираются как прежде, а при показе плитки обновляются сразу
        if (barVisible && (!WindowsShell.Economy || ticks % 2 == 0)) foreach (var update in faces) update();
        if (!smoke)
        {
            bool fullscreen = WindowsShell.IsFullscreen(display);
            var pointer = System.Windows.Forms.Cursor.Position;
            var dpi = VisualTreeHelper.GetDpi(this);
            // над игрой на весь экран край не выдвигает док (как панель Windows); над браузером и видео — выдвигает
            bool game = fullscreen && WindowsShell.IsGameForeground();
            bool atEdge = !game && pointer.Y >= (Top + Height) * dpi.DpiScaleY - 3 &&
                pointer.X >= Left * dpi.DpiScaleX && pointer.X < (Left + Width) * dpi.DpiScaleX;
            // поверх полноэкранного окна (браузер, видео) дока не видно, но он выезжает, если подержать указатель у нижнего края (около полсекунды: в играх от случайного касания края он не мешает)
            edgeTicks = fullscreen && atEdge ? edgeTicks + 1 : 0;
            // док не прячется, пока открыто что-то от него: меню, виджет или настройки (даже если окно потеряло фокус), миниатюра; после ухода указателя держится ещё секунду с небольшим
            bool held = surfaces.Values.Any(surface => surface.IsVisible) || ContextMenu?.IsOpen == true || thumbnail is not null || DateTime.UtcNow < flashUntil || dragTile is not null
                || DateTime.UtcNow < dragOverUntil;
            // Выезд от края тоже продлевает показ: при перетаскивании файла из другой программы указатель уходит с полоски у края на док раньше, чем придёт первое событие DragOver
            bool revealedByEdge = atEdge && (!fullscreen || edgeTicks >= 2);
            if (held || IsMouseOver || revealedByEdge) hoverUntil = DateTime.UtcNow.AddMilliseconds(1200);
            var visible = BarShouldShow(fullscreen, P.AutoHide, P.ShowOnDesktop && WindowsShell.IsDesktopActive() && windows.All(item => WindowsShell.Minimized(item.Handle)), DateTime.UtcNow < hoverUntil, atEdge, edgeTicks, held);
            if (fullscreen != lastFullscreen)
            {
                lastFullscreen = fullscreen;
                ActivityLog.Record(fullscreen ? "система: полноэкранное окно" : "система: полноэкранное окно закрыто", $"монитор {display.Width:0}×{display.Height:0}");
            }
            if (visible != barVisible)
            {
                ActivityLog.Record(visible ? "док показан" : "док скрыт",
                    fullscreen ? "(полноэкранное окно" + (atEdge ? ", указатель у края)" : ")") : visible ? (held ? "(открыто окно или меню)" : atEdge ? "(указатель у края)" : "(указатель на доке)") : "(автоскрытие)");
                barVisible = visible;
                if (visible) RedrawFaces();
                IsHitTestVisible = visible;
                BeginAnimation(OpacityProperty, new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromMilliseconds(125)));
            }
        }
        else if (previewLiveGlass) SampleGlass();
    }

    // Виден ли док. При полноэкранном окне его нет, пока указатель не продержался у нижнего края два опроса (около полсекунды); без полноэкранного — по настройке автоскрытия
    internal static bool BarShouldShow(bool fullscreen, bool autoHide, bool desktopShown, bool hover, bool atEdge, int edgeTicks, bool pinnedOpen)
    {
        bool reveal = hover || pinnedOpen || atEdge && (!fullscreen || edgeTicks >= 2);
        return fullscreen ? reveal : !autoHide || desktopShown || reveal;
    }

    // Чистая прозрачность (без материала и без стиля Liquid): док остаётся настоящим прозрачным окном, и сквозь него видно всё, что под ним — окна, видео, обои.
    // Картинка-стекло из обоев тут не нужна: она закрывала окна под доком (рабочая область равна экрану) и делала док непрозрачным.
    private bool SeeThrough => !previewLiveGlass && Looks.Glassy(P) && Looks.Material(P) == "theme" && P.Style != "Liquid";

    private void SampleGlass()
    {
        if (!IsLoaded || !Looks.Glassy(P) || SeeThrough || background.ActualWidth <= 0 || background.ActualHeight <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var position = background.PointToScreen(new Point());
        // Обычный режим: стекло из обоев (док не исключается из захвата, на части систем это даёт чёрную панель). Проверки с живым снимком экрана — как раньше
        var shot = previewLiveGlass ? null : WallpaperShot();
        // Стекло собирается из неподвижных обоев: пока положение, размер, настройки и сами обои те же, пересчёт даёт ту же картинку (около 0,85 мс на кадр, до 144 раз в секунду)
        var inputs = (position, background.ActualWidth, background.ActualHeight, Looks.Detail(P), Looks.Dispersion(P), P.Refraction, shot);
        glassSkipped = !previewLiveGlass && liveBackdrop.ImageSource is not null && inputs.Equals(glassInputs);
        if (glassSkipped) return;
        glassInputs = inputs;
        var snapshot = glass.Update((int)position.X, (int)position.Y,
            (int)(background.ActualWidth * dpi.DpiScaleX), (int)(background.ActualHeight * dpi.DpiScaleY), Looks.Detail(P), Looks.Dispersion(P), P.Refraction,
            shot, (int)position.X - screenBounds.X, (int)position.Y - screenBounds.Y);
        if (snapshot is null) return;
        liveBackdrop.ImageSource = snapshot;
        background.Background = liveBackdrop;
    }

    private void RefreshOnline()
    {
        if (P.WeatherPlace is { } place) _ = model.Data.RefreshWeather(place);
        foreach (var id in P.Items.Concat(P.Monitors.Values.SelectMany(setup => setup.Items)).Distinct())
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
    // Список приложений, не поместившихся в док, прокручивается колесом мыши; закреплённые приложения стоят на месте слева от него
    private ScrollViewer? appScroller;
    private double appScrollOffset, appScrollTarget;
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
        StepScroll(dt);
        if (dragTile is not null) DragStep(dt);
        else MagnifyStep(dt);
    }

    // Левый край плитки в координатах ряда дока без учёта её собственного увеличения и сдвига, но с учётом прокрутки списка приложений
    private double RowOffset(Border tile)
    {
        // прокрутку уже учитывает положение родителя на экране (ScrollViewer сдвигает содержимое при компоновке), вручную её вычитать нельзя
        if (VisualTreeHelper.GetParent(tile) is not UIElement parent || magnetRow is null) return 0;
        return parent.TransformToAncestor(magnetRow).Transform(default).X + VisualTreeHelper.GetOffset(tile).X;
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
        var extras = new double[count];
        var visible = new bool[count];
        // Плитки, уехавшие за край списка, не видны (список обрезает их): они не растут и не раздвигают соседей
        double viewLeft = 0, viewRight = 0;
        if (appScroller is not null && magnetRow is not null)
        {
            viewLeft = appScroller.TransformToAncestor(magnetRow).Transform(default).X;
            viewRight = viewLeft + appScroller.ActualWidth;
        }
        for (int index = 0; index < count; index++)
        {
            var item = magnets[index];
            centers[index] = RowOffset(item.Tile) + item.Tile.ActualWidth / 2;
            // плитка списка, у которой середина за краем, обрезана больше чем наполовину и лежит под соседними значками: она не участвует в раскладке
            visible[index] = appScroller is null || !appScroller.IsAncestorOf(item.Tile) || (centers[index] >= viewLeft && centers[index] <= viewRight);
            double target = 1;
            if (visible[index] && magnetX is { } pointer && boost > 0 && item.App)
            {
                double distance = (centers[index] - pointer) / unit;
                target = 1 + boost * Math.Exp(-distance * distance / 2.6);
            }
            double scale = item.Scale.ScaleX;
            scale += (target - scale) * follow;
            if (Math.Abs(target - scale) < .002) scale = target;
            item.Scale.ScaleX = item.Scale.ScaleY = scale;
            extras[index] = visible[index] ? item.Width * (scale - 1) : 0;
            System.Windows.Controls.Panel.SetZIndex(item.Tile, (int)Math.Round((scale - 1) * 100));
        }
        // плитки раскладываются слева направо по настоящему положению в доке (приложения могут идти не в порядке создания плиток)
        var order = Enumerable.Range(0, count).Where(index => visible[index]).OrderBy(index => centers[index]).ToArray();
        if (order.Length == 0) return;
        var displacement = new double[count];
        double before = 0;
        foreach (int index in order)
        {
            displacement[index] = before + extras[index] / 2;
            before += extras[index];
        }
        // сдвиг считается от точки под указателем: она остаётся на месте, левые плитки уходят влево, правые вправо
        double pivot = displacement[order[0]];
        if (magnetLast >= centers[order[^1]]) pivot = displacement[order[^1]];
        else
            for (int step = 0; step + 1 < order.Length; step++)
                if (magnetLast < centers[order[step + 1]])
                {
                    int left = order[step], right = order[step + 1];
                    double span = Math.Max(1, centers[right] - centers[left]);
                    double part = Math.Clamp((magnetLast - centers[left]) / span, 0, 1);
                    pivot = displacement[left] + (displacement[right] - displacement[left]) * part;
                    break;
                }
        for (int index = 0; index < count; index++) magnets[index].Shift.X = visible[index] ? displacement[index] - pivot : 0;
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
            // Над рядом нет слоя обрезки: иначе увеличенный значок срезается по верхней границе ряда (кроме окна просмотра списка, оно с запасом)
            for (DependencyObject d = first.Tile; d is not null && !ReferenceEquals(d, background); d = VisualTreeHelper.GetParent(d))
                if (d is Visual visual && d is not System.Windows.Controls.ScrollContentPresenter && VisualTreeHelper.GetClip(visual) is not null)
                    return $"{d.GetType().Name} обрезает содержимое: увеличенные значки будут срезаны";
            magnetLast = RowOffset(first.Tile) + first.Tile.ActualWidth / 2;
            magnetX = magnetLast;
            for (int step = 0; step < 90; step++) MagnifyStep(.03);
            double want = Math.Clamp(P.MagnificationScale, 1.2, 3);
            if (Math.Abs(first.Scale.ScaleX - want) > .02) return $"под указателем масштаб {first.Scale.ScaleX:0.00}, ожидался {want:0.00}";
            if (Math.Abs(first.Shift.X) > 1) return $"плитка под указателем сдвинулась на {first.Shift.X:0.0}";
            var farthest = magnets.OrderByDescending(item => Math.Abs(RowOffset(item.Tile) - RowOffset(first.Tile))).First();
            if (farthest.App && farthest.Scale.ScaleX > first.Scale.ScaleX - .01) return "далёкая плитка выросла так же, как плитка под указателем";
            magnetX = null;
            for (int step = 0; step < 200; step++) MagnifyStep(.03);
            if (magnets.Any(item => item.Scale.ScaleX != 1 || Math.Abs(item.Shift.X) > .01)) return "плитки не вернулись в покой после ухода указателя";
            return "";
        }
        finally { P.Magnification = was; magnetX = null; }
    }

    private record WindowFacts(string? AppId, List<string> Hosts);
    private readonly Dictionary<(nint Handle, string App), WindowFacts> windowFacts = [];

    // Под какой плиткой показывать окно. Обычно по программе окна, но Windows сводит окна по AppUserModelID, и у многих программ окно принадлежит
    // другому процессу, чем закреплённый файл (Steam: окно у steamwebhelper.exe, закреплён steam.exe). Поэтому окно без своей закреплённой программы
    // ищет закреплённую по тому же AppUserModelID (из ярлыка или пакета магазина), а при его наличии — среди главных программ в цепочке запуска.
    private string GroupKey(WindowsShell.WindowInfo window, Dictionary<string, string?> pinned)
    {
        string own = WindowsShell.ResolveApp(window.App);
        if (window.Handle == 0 || pinned.ContainsKey(own)) return own;
        if (!windowFacts.TryGetValue((window.Handle, own), out var facts))
        {
            string? id = JumpList.AppId(window.Handle);
            windowFacts[(window.Handle, own)] = facts = new(id, string.IsNullOrEmpty(id) ? [] : WindowsShell.HostApps(window.Handle, window.App));
        }
        if (string.IsNullOrEmpty(facts.AppId)) return own;
        foreach (var (key, id) in pinned)
            if (string.Equals(id, facts.AppId, StringComparison.OrdinalIgnoreCase)) return key;
        return facts.Hosts.FirstOrDefault(pinned.ContainsKey) ?? own;
    }

    private void Refresh()
    {
        EndDrag(false); // плитки пересобираются, перенос в этот момент прерывается
        faces.Clear();
        magnets.Clear();
        dragTiles.Clear();
        appScroller = null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        magnetRow = row;
        // Какие окна показывает этот док: all — все, main — только основной, own — окна своего монитора; остальные видны кольцами у значка
        var mode = P.WindowsOnDocks;
        var shown = mode == "all" || mode == "main" && !secondary ? windows : mode == "main" ? [] : windows.Where(OnThisScreen).ToList();
        var elsewhere = windows.Except(shown).ToList();
        // закреплённые программы: путь для сопоставления -> AppUserModelID (у приложений магазина из id, у остальных из ярлыка)
        var pinnedApps = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in Items.Where(id => id.StartsWith("app:", StringComparison.Ordinal)))
            pinnedApps[WindowsShell.MatchApp(id[4..])] = WindowsShell.IsStoreApp(id[4..]) ? id[(4 + WindowsShell.StorePrefix.Length)..] : JumpList.ShortcutAppId(id[4..]);
        windowFacts.Keys.Where(key => !windows.Any(window => window.Handle == key.Handle)).ToList().ForEach(key => windowFacts.Remove(key));
        var groups = shown.GroupBy(item => GroupKey(item, pinnedApps), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var elsewhereGroups = elsewhere.GroupBy(item => GroupKey(item, pinnedApps), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var scale = P.Size / 75;
        var iconSize = P.IconSize * .76;
        var tileHeight = Math.Max(47, iconSize + 10);
        var tiles = Items.Distinct(StringComparer.OrdinalIgnoreCase).Where(id => !AppsOnly || id is "Start" or "Search" || id.StartsWith("app:", StringComparison.Ordinal)).ToList();
        string AppKey(string path) => WindowsShell.MatchApp(path);
        var pinned = tiles.Where(id => id.StartsWith("app:", StringComparison.Ordinal))
            .Select(id => AppKey(id[4..])).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var afterApps = tiles.FindLastIndex(id => id.StartsWith("app:", StringComparison.Ordinal) || id == "Start") + 1;
        // открытые программы идут в порядке, который задал перенос мышью; новые встают в конец (по алфавиту, если появились сразу несколько)
        var unpinned = groups.Keys.Where(path => !pinned.Contains(path)).OrderBy(path => path).ToList();
        foreach (var path in unpinned)
            if (!openOrder.Contains(path, StringComparer.OrdinalIgnoreCase)) openOrder.Add(path);
        tiles.InsertRange(afterApps, openOrder.Where(path => unpinned.Contains(path, StringComparer.OrdinalIgnoreCase)).Select(path => "app:" + path));
        // Плитки приложений и папок собираются в один блок на месте первого из них: закреплённые стоят на месте, открытые лежат в списке с прокруткой колесом мыши
        StackPanel? pinnedStack = null, openStack = null;
        int appSlot = -1;
        void AddApp(Border tile, bool isPinned)
        {
            if (appSlot < 0)
            {
                appSlot = row.Children.Count;
                row.Children.Add(new Border());
            }
            if (isPinned && P.StickyPinnedApps) (pinnedStack ??= new StackPanel { Orientation = Orientation.Horizontal }).Children.Add(tile);
            else (openStack ??= new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom }).Children.Add(tile);
        }
        foreach (var id in tiles)
        {
            if (id == "Start") row.Children.Add(StartTile(tileHeight));
            else if (id.StartsWith("files:", StringComparison.Ordinal) && P.FileGroups.TryGetValue(id, out var group))
            {
                Border? tile = null;
                tile = Tile(id, Symbol("\uE8B7", 22, Accent(P)), tileHeight, tileHeight, () => OpenFiles(id, group, tile));
                var menu = CommonMenu();
                menu.Items.Insert(0, Menu("Remove from dock", () => { P.FileGroups.Remove(id); model.ToggleWidget(id, Items); }));
                tile.ContextMenu = menu;
                AddApp(tile, true);
            }
            else if (id.StartsWith("app:", StringComparison.Ordinal))
            {
                var appKey = AppKey(id[4..]);
                groups.TryGetValue(appKey, out var running);
                elsewhereGroups.TryGetValue(appKey, out var other);
                AddApp(AppTile(id, running ?? [], other ?? [], tileHeight, iconSize), Items.Contains(id, StringComparer.OrdinalIgnoreCase));
            }
            else if (Widgets.Available.TryGetValue(id, out var widget))
            {
                Border? tile = null;
                var face = Widgets.Face(widget, model, false, out Action update,
                    id == "Quick Toggles" ? name =>
                    {
                        if (name is "Wi-Fi" or "Bluetooth") OpenWidget(name, tile);
                        else OpenWidget("Quick Toggles", tile); // Ethernet, хот-спот, «В самолёте»: панель быстрых настроек, а не окно параметров
                    } : null);
                faces.Add(update);
                tile = Tile(id, face, Widgets.WidthOf(P, widget), tileHeight, () => OpenWidget(id, tile));
                if (id == "Pomodoro") tile.BorderBrush = Brush("#FF4A43");
                var menu = CommonMenu();
                if (!PinnedHere(id)) menu.Items.Insert(0, Menu("Remove from dock", () => model.ToggleWidget(id, Items)));
                AddShiftItems(menu, id, 0);
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

        if (appSlot >= 0)
        {
            var host = new StackPanel { Orientation = Orientation.Horizontal };
            if (pinnedStack is not null) host.Children.Add(pinnedStack);
            if (openStack is not null)
            {
                // окно просмотра списка выше плиток на запас под увеличение значков (иначе увеличенные значки обрезались бы), а сам ряд остаётся прежней высоты
                double headroom = P.Magnification ? tileHeight * (Math.Clamp(P.MagnificationScale, 1.2, 3) - 1) : 0;
                var holder = new Grid { Height = tileHeight + headroom };
                holder.Children.Add(openStack);
                var scroller = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = holder, Margin = new(0, -headroom, 0, 0)
                };
                scroller.PreviewMouseWheel += (_, e) =>
                {
                    if (scroller.ScrollableWidth <= 0) return;
                    ScrollApps(e.Delta);
                    e.Handled = true;
                };
                // пока список плавно едет к цели, положение ведёт StepScroll; после остановки подхватываем то, что оставил ScrollViewer (он мог обрезать смещение)
                scroller.ScrollChanged += (_, e) =>
                {
                    if (e.HorizontalChange == 0 || Math.Abs(appScrollTarget - appScrollOffset) >= .5) return;
                    appScrollOffset = appScrollTarget = scroller.HorizontalOffset;
                };
                host.Children.Add(scroller);
                appScroller = scroller;
                Dispatcher.BeginInvoke(() => scroller.ScrollToHorizontalOffset(appScrollOffset), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            row.Children.RemoveAt(appSlot);
            row.Children.Insert(appSlot, host);
        }
        if (row.Children.Count == 0)
            row.Children.Add(Tile("Widgets", Symbol("\uE71D"), 47, tileHeight, () => OpenGallery()));
        row.LayoutTransform = new ScaleTransform(scale, scale);
        bool useLiveGlass = Looks.Glassy(P) && (!smoke || previewLiveGlass);
        var inner = Card(row, 15, Looks.Material(P) == "theme" && !Looks.Glassy(P) ? DockFinish(P.Style) : Looks.Surface(P, useLiveGlass));
        // Отступ — целое число пикселей: при дробном (3 DIP = 3,75 px) Windows округляет высоту карточки вниз, ряд оказывается меньше нужного
        // и получает слой обрезки по своим границам, а он срезает увеличенные значки, выступающие над рядом
        double pixel = 1 / VisualTreeHelper.GetDpi(this).DpiScaleX;
        inner.Padding = new(Math.Round(Math.Max(0, P.Padding) * scale / pixel) * pixel);
        background.Child = inner;
        background.BorderThickness = new(Math.Clamp(P.DockBorder, 0, 4)); // толщина границы по краям панели, 0 — без границы
        background.BorderBrush = Looks.Rim(P);
        background.Background = useLiveGlass && !SeeThrough
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
    private uint lastAffinity = uint.MaxValue;

    private void ExcludeFromCapture()
    {
        if (handle == 0) return;
        // Только при смене значения: повторный вызов на слоистом окне при каждой перерисовке (а она идёт при каждой смене активного окна) делал панель чёрной
        uint affinity = Looks.Glassy(P) && previewLiveGlass ? 0x11u : 0u;
        if (affinity == lastAffinity) return;
        lastAffinity = affinity;
        SetWindowDisplayAffinity(handle, affinity);
    }

    // Обои, растянутые по монитору дока (режим «Заполнить»): из них собирается стекло, пока не нужен живой снимок экрана
    private System.Drawing.Bitmap? wallpaperShot;
    private string? wallpaperShotKey;
    private System.Drawing.Rectangle screenBounds;
    private long wallpaperChecked;

    private System.Drawing.Bitmap? WallpaperShot()
    {
        // обои и монитор проверяются раз в две секунды: на каждом кадре это лишняя работа
        if (wallpaperShot is not null && Environment.TickCount64 - wallpaperChecked < 2000) return wallpaperShot;
        wallpaperChecked = Environment.TickCount64;
        var bounds = System.Windows.Forms.Screen.FromHandle(handle).Bounds;
        screenBounds = bounds;
        string key = WindowsShell.Wallpaper() + "|" + bounds;
        if (key == wallpaperShotKey) return wallpaperShot;
        wallpaperShotKey = key;
        wallpaperShot?.Dispose();
        wallpaperShot = new System.Drawing.Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using var canvas = System.Drawing.Graphics.FromImage(wallpaperShot);
        canvas.Clear(System.Drawing.Color.FromArgb(24, 24, 28));
        var path = WindowsShell.Wallpaper();
        if (path is null) return wallpaperShot;
        try
        {
            using var image = System.Drawing.Image.FromFile(path);
            double fit = Math.Max((double)bounds.Width / image.Width, (double)bounds.Height / image.Height);
            int width = (int)(image.Width * fit), height = (int)(image.Height * fit);
            canvas.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            canvas.DrawImage(image, (bounds.Width - width) / 2, (bounds.Height - height) / 2, width, height);
        }
        catch (Exception ex) when (ex is IOException or OutOfMemoryException or ArgumentException or System.Runtime.InteropServices.ExternalException) { ErrorLog.Write(ex, "Wallpaper"); }
        return wallpaperShot;
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

    // Повторный Measure «вручную» пропускает ветку, которую не пометили устаревшей: помечаем весь путь от элемента до верхнего
    private static void InvalidateUp(UIElement leaf, UIElement top)
    {
        DependencyObject? node = leaf;
        while (node is not null)
        {
            if (node is UIElement element) element.InvalidateMeasure();
            if (ReferenceEquals(node, top)) break;
            node = VisualTreeHelper.GetParent(node);
        }
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
            if (appScroller is { Content: FrameworkElement scrolled } viewer)
            {
                // не хватает места: сначала ужимается список открытых приложений (он прокручивается колесом), и лишь если и этого мало, сжимается весь док
                viewer.MaxWidth = double.PositiveInfinity;
                InvalidateUp(viewer, inner);
                inner.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double over = inner.DesiredSize.Width + 2 - (width - 10);
                if (over > 0)
                {
                    double factor = (((inner as Border)?.Child as FrameworkElement)?.LayoutTransform as ScaleTransform)?.ScaleX ?? 1;
                    double natural = scrolled.DesiredSize.Width;
                    viewer.MaxWidth = Math.Max(Math.Min(natural, 110), natural - (over + 8) / factor); // +8: запас на округление, чтобы док не сжимался на доли процента
                    InvalidateUp(viewer, inner);
                    inner.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                }
            }
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
        var button = Button(face, () => { ActivityLog.Record("нажатие на плитку", ActivityLog.Tile(id)); click(); }, tileColor, 13);
        var tile = Card(button,
            14, Brushes.Transparent, cardVisible && P.TileBorders ? Brush(light ? "#A8CACAD0" : P.Style == "Liquid" ? "#28FFFFFF" : "#AD444447") : null);
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
        var name = app ? WindowsShell.AppLabel(id[4..]) : id;
        AutomationProperties.SetName(button, app ? name : L10n.T(name));
        if (P.Names) tile.ToolTip = app ? name : L10n.T(name);
        // переносятся закреплённые плитки и значки открытых программ (их порядок помнится, пока работает панель)
        if (P.Locked || !app && !Items.Contains(id)) return tile;
        dragTiles[tile] = id;
        Point start = default;
        tile.PreviewMouseLeftButtonDown += (_, eventArgs) => start = eventArgs.GetPosition(tile);
        tile.PreviewMouseMove += (_, eventArgs) =>
        {
            // ползунок звука внутри плитки тянется мышью — плитку при этом не переносим
            if (eventArgs.LeftButton != MouseButtonState.Pressed || !tile.IsMouseCaptureWithin || dragTile is not null || Mouse.Captured is Widgets.VolumeStrip) return;
            var delta = eventArgs.GetPosition(tile) - start;
            if (Math.Abs(delta.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(delta.Y) > SystemParameters.MinimumVerticalDragDistance)
                BeginDrag(tile, id, start.X);
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
        menu.Items.Insert(1, Menu("File Explorer", () => WindowsShell.Launch("explorer.exe")));
        menu.Items.Insert(2, Menu("Windows Settings", () => WindowsShell.Launch("ms-settings:")));
        menu.Items.Insert(3, Menu("Search", () => WindowsShell.Key(0x5B, 0x53)));
        menu.Items.Insert(4, Menu("Show Desktop", () => WindowsShell.Key(0x5B, 0x44)));
        tile.ContextMenu = menu;
        return tile;
    }

    private bool OnThisScreen(WindowsShell.WindowInfo window) => !windowScreens.TryGetValue(window.Handle, out var name) || name == ThisScreen.DeviceName; // монитор окна ещё не известен — считаем его здешним

    private Border AppTile(string id, List<WindowsShell.WindowInfo> running, List<WindowsShell.WindowInfo> elsewhere, double height, double iconSize)
    {
        string path = id[4..];
        if (!icons.TryGetValue(path, out var image) || image is null && running.Count > 0)
            icons[path] = image = WindowsShell.Icon(path, running.FirstOrDefault()?.Handle ?? 0);
        FrameworkElement graphic = image is null ? Symbol("\uE8A7", iconSize * .7) : new Image { Source = image, Width = iconSize, Height = iconSize };
        RenderOptions.SetBitmapScalingMode(graphic, BitmapScalingMode.HighQuality);
        var layered = new Grid();
        layered.Children.Add(graphic);
        if (running.Count + elsewhere.Count > 0)
        {
            // Чёрточки — окна на этом мониторе, кольца — на других; у активного окна чёрточка длиннее и цвета акцента
            int here = running.Count(OnThisScreen);
            bool active = running.Any(window => window.Handle == activeWindow && OnThisScreen(window));
            var accent = (Accent(P) as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
            layered.Children.Add(new WindowIndicator(here, running.Count - here + elsewhere.Count, active, height, accent, LightTiles(P)));
        }
        if (P.AppBadges && running.Count > 0)
        {
            var unread = running.Select(window => UnreadPrefix.Match(window.Title)).FirstOrDefault(match => match.Success);
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
            // Окна, видимые на других мониторах, открываются так же, если на этом их нет
            var targets = running.Count > 0 ? running : elsewhere;
            if (targets.Count == 1) WindowsShell.ActivateOrMinimize(targets);
            else if (targets.Count > 1)
            {
                if (tile is not null) ShowThumbnail(tile, targets, true);
                else WindowsShell.ActivateNext(targets);
            }
            else if (!WindowsShell.Launch(path)) Report(L10n.F($"Could not open {path}"));
        });
        if (running.Count + elsewhere.Count > 0) DwellOpen(tile, () =>
        {
            var targets = running.Count > 0 ? running : elsewhere;
            if (targets.Count == 1) WindowsShell.Activate(targets[0].Handle);
            else ShowThumbnail(tile, targets, true); // несколько окон: показываем карточки, как Windows, и выбор окна — тоже наведением
        });
        // Меню собирается заново при каждом открытии, чтобы список переходов программы был свежим
        var appMenu = DockMenu();
        tile.ContextMenuOpening += (_, _) => FillAppMenu(appMenu, id, path, running, image);
        FillAppMenu(appMenu, id, path, running, image);
        tile.ContextMenu = appMenu;
        if (P.WindowPreviews && running.Count > 0)
        {
            var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            // пока миниатюры уже открыты, соседний значок показывает свои сразу, как в Windows; иначе — после короткой паузы
            tile.MouseEnter += (_, _) =>
            {
                closePreview.Stop();
                if (thumbnail is not null && !appMenu.IsOpen) ShowThumbnail(tile, running); else wait.Start();
            };
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

    // Как в Windows: файл, который тянут и держат над значком или карточкой, открывает это окно (около 0,7 с), чтобы можно было бросить файл в него
    private void DwellOpen(UIElement target, Action open)
    {
        var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        var lastOver = DateTime.MinValue;
        bool done = false; // один раз за наведение: иначе миниатюры мигали бы заново каждые 0,7 с
        target.AllowDrop = true;
        target.DragOver += (_, _) =>
        {
            var now = DateTime.UtcNow;
            if (now - lastOver > TimeSpan.FromMilliseconds(400)) done = false; // указатель уходил и вернулся
            lastOver = now;
            closePreview.Stop();
            if (!done && !wait.IsEnabled) wait.Start();
        };
        target.DragLeave += (_, _) => { if (thumbnail is not null) { closePreview.Stop(); closePreview.Start(); } };
        wait.Tick += (_, _) =>
        {
            wait.Stop();
            if (DateTime.UtcNow - lastOver >= TimeSpan.FromMilliseconds(300)) return; // указатель уже ушёл
            done = true;
            open();
        };
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
        preview.AllowDrop = true;
        preview.DragOver += (_, _) => closePreview.Stop(); // и пока над карточками тянут файл
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
            card.MouseLeftButtonUp += (_, _) => { CloseThumbnail(); WindowsShell.Activate(window.Handle, true); };
            DwellOpen(card, () => { CloseThumbnail(); WindowsShell.Activate(window.Handle); });
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

    // Поворот колеса на delta: сдвигает цель прокрутки списка приложений, к которой список плавно едет по кадрам (StepScroll)
    internal void ScrollApps(double delta)
    {
        if (appScroller is null) return;
        appScrollTarget = Math.Clamp(appScrollTarget - delta * .75, 0, appScroller.ScrollableWidth);
    }

    private void StepScroll(double dt)
    {
        if (appScroller is null) return;
        appScrollTarget = Math.Clamp(appScrollTarget, 0, appScroller.ScrollableWidth);
        double gap = appScrollTarget - appScrollOffset;
        if (gap == 0) return;
        appScrollOffset = Math.Abs(gap) < .5 ? appScrollTarget : appScrollOffset + gap * (1 - Math.Exp(-dt * 16));
        appScroller.ScrollToHorizontalOffset(appScrollOffset);
    }

    // Для самопроверки: при множестве открытых программ док не сжимается, а список прокручивается; закреплённые приложения при этом не двигаются
    internal string? ScrollCheck(string? shots = null)
    {
        var saved = windows;
        bool sticky = P.StickyPinnedApps;
        try
        {
            P.StickyPinnedApps = true;
            windows = Enumerable.Range(0, 70).Select(n => new WindowsShell.WindowInfo(0, "Test " + n, @"C:\Fake\app" + n + ".exe", "app" + n)).ToList();
            Refresh();
            UpdateLayout();
            if (appScroller is null) return "список приложений не появился";
            if (appScroller.ScrollableWidth <= 0) return "прокрутка не нужна при 70 открытых программах";
            if ((background.Child as FrameworkElement)?.LayoutTransform is ScaleTransform { ScaleX: < .98 } squeezed)
                return $"док сжался вместо прокрутки: масштаб {squeezed.ScaleX:0.00}, ширина дока {ActualWidth:0}";
            var apps = magnets.Where(item => item.App).ToList();
            var pinnedTile = apps.First().Tile;
            var openTile = apps.Last().Tile;
            double pinnedBefore = RowOffset(pinnedTile), openBefore = RowOffset(openTile);
            ScrollApps(-400); // колесо «вниз»: список уезжает влево
            StepScroll(1 / 60.0);
            UpdateLayout();
            double firstFrame = appScroller.HorizontalOffset;
            if (firstFrame <= 1 || firstFrame >= appScrollTarget - 1) return $"прокрутка не плавная: после первого кадра {firstFrame:0} из {appScrollTarget:0}";
            for (int frame = 0; frame < 150; frame++) StepScroll(1 / 60.0);
            UpdateLayout();
            if (Math.Abs(appScroller.HorizontalOffset - appScrollTarget) > 1) return $"список не доехал до цели: {appScroller.HorizontalOffset:0} из {appScrollTarget:0}";
            if (appScroller.HorizontalOffset < 100) return "колесо не прокрутило список";
            if (Math.Abs(RowOffset(pinnedTile) - pinnedBefore) > 1) return "закреплённое приложение сдвинулось при прокрутке";
            if (RowOffset(openTile) > openBefore - 100) return "открытое приложение не сдвинулось при прокрутке";
            double screenWidth = (display.Right - display.Left) / VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (ActualWidth > screenWidth + 1) return "док шире экрана";
            if (shots is not null) Program.Capture(this, Path.Combine(shots, "scroll-rest.png"));
            // прокрученный список с увеличением значков: зазоры между соседними плитками такие же, как в покое (ни наложений, ни лишних щелей), где бы ни стоял указатель
            bool magnified = P.Magnification;
            P.Magnification = true;
            try
            {
                var viewport = appScroller.TransformToAncestor(magnetRow!).TransformBounds(new Rect(0, 0, appScroller.ActualWidth, 1));
                // плитки слева направо и зазоры между соседними; из списка берутся те, у кого середина видна (так же, как в раскладке увеличения)
                Dictionary<(int, int), double> Gaps()
                {
                    var rects = magnets.Select((item, index) => (Index: index, Rect: item.Tile.TransformToAncestor(magnetRow!)
                            .TransformBounds(new Rect(0, 0, item.Tile.ActualWidth, item.Tile.ActualHeight)), Listed: appScroller.IsAncestorOf(item.Tile)))
                        .Where(entry => !entry.Listed || (RowOffset(magnets[entry.Index].Tile) + magnets[entry.Index].Tile.ActualWidth / 2 is var middle
                            && middle >= viewport.Left && middle <= viewport.Right))
                        .OrderBy(entry => entry.Rect.Left).ToList();
                    var gaps = new Dictionary<(int, int), double>();
                    for (int n = 1; n < rects.Count; n++) gaps[(rects[n - 1].Index, rects[n].Index)] = rects[n].Rect.Left - rects[n - 1].Rect.Right;
                    return gaps;
                }
                var rest = Gaps();
                double pinnedMiddle = RowOffset(pinnedTile) + pinnedTile.ActualWidth / 2;
                foreach (var (name, point) in new[] { ("pinned", pinnedMiddle), ("list-0.15", viewport.Left + viewport.Width * .15),
                    ("list-0.50", viewport.Left + viewport.Width * .5), ("list-0.85", viewport.Left + viewport.Width * .85) })
                {
                    magnetLast = point;
                    magnetX = point;
                    for (int step = 0; step < 90; step++) MagnifyStep(.03);
                    if (shots is not null) Program.Capture(this, Path.Combine(shots, $"scroll-hover-{name}.png"));
                    foreach (var (pair, gap) in Gaps())
                        if (rest.TryGetValue(pair, out double was) && Math.Abs(gap - was) > 1.5)
                            return $"зазор между плитками #{pair.Item1} и #{pair.Item2} при указателе «{name}»: {gap:0.0} px вместо {was:0.0}";
                }
            }
            finally
            {
                P.Magnification = magnified;
                magnetX = null;
                for (int step = 0; step < 200; step++) MagnifyStep(.03);
            }
            // если закреплённые приложения не стоят на месте, колесо прокручивает весь блок приложений, включая самый левый значок
            P.StickyPinnedApps = false;
            appScrollOffset = appScrollTarget = 0;
            Refresh();
            UpdateLayout();
            var firstApp = magnets.First(item => item.App).Tile;
            double firstBefore = RowOffset(firstApp);
            ScrollApps(-400);
            for (int frame = 0; frame < 150; frame++) StepScroll(1 / 60.0);
            UpdateLayout();
            if (RowOffset(firstApp) > firstBefore - 100) return "при выключенной настройке «Закреплённые приложения на месте» крайний значок не прокрутился";
            return "";
        }
        finally
        {
            windows = saved;
            P.StickyPinnedApps = sticky;
            appScrollOffset = appScrollTarget = 0;
            Refresh();
        }
    }

    // Для самопроверки: снимок дока с настоящими открытыми окнами этого компьютера (как выглядят их значки)
    internal void CaptureRealApps(string path)
    {
        var saved = windows;
        try
        {
            windows = WindowsShell.Windows();
            Refresh();
            UpdateLayout();
            Program.Capture(this, path);
        }
        finally
        {
            windows = saved;
            Refresh();
        }
    }

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

    // «Сдвинуть влево/вправо» в меню ярлыка: меняет его местами с соседним (то же, что перетаскивание, но без мыши)
    internal void AddShiftItems(ContextMenu menu, string id, int at)
    {
        int index = Items.IndexOf(id);
        if (index < 0 || P.Locked) return;
        if (index > 0) menu.Items.Insert(at++, Menu("Move left", () => model.Shift(id, -1, Items)));
        if (index < Items.Count - 1) menu.Items.Insert(at++, Menu("Move right", () => model.Shift(id, 1, Items)));
    }

    private ContextMenu CommonMenu()
    {
        var menu = DockMenu();
        menu.Items.Add(Menu("Widgets…", () => OpenGallery(), MenuWidgetsIcon()));
        menu.Items.Add(Menu("Settings", () => OpenSettings(), MenuGlyph("")));
        menu.Items.Add(Menu("Task Manager", () => WindowsShell.Launch("taskmgr.exe")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Menu("Pin Windows folder", () => { if (!P.Locked) OpenFolderPin(); }));
        menu.Items.Add(Menu("Pin file or app…", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
            if (dialog.ShowDialog() != true) return;
            PinPaths(dialog.FileNames);
        }));
        menu.Items.Add(Menu("Report a problem…", () => OpenBugReport()));
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
                if (!P.Locked && !Items.Contains(id)) model.ToggleWidget(id, Items);
                window.Close();
            });
            entry.IsEnabled = !Items.Contains(id);
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
            if (!Items.Contains(app, StringComparer.OrdinalIgnoreCase)) Items.Add(app);
        }
        else
        {
            var id = "files:" + Guid.NewGuid().ToString("N");
            P.FileGroups[id] = new FileGroup { Name = Path.GetFileName(Path.GetDirectoryName(valid[0])) ?? "Files", Paths = [.. valid] };
            Items.Add(id);
        }
        model.Save();
    }

    private void OpenFiles(string id, FileGroup group, FrameworkElement? anchor) => Surface(id, 365, 340, true, (window, shell) =>
    {
        var entries = Stack(Right(RawText(group.Name, 16, true), CloseButton(window)));
        entries.Margin = new(12);
        // Пункты раскрываются веером: каждый выезжает из стопки сверху с небольшой задержкой
        int fan = 0;
        foreach (string path in group.Paths)
        {
            var entry = Button(RawText(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), 14, true), () => WindowsShell.Launch(path));
            var slide = new TranslateTransform(0, -28 - fan * 4);
            entry.RenderTransform = slide;
            entry.Opacity = 0;
            var delay = TimeSpan.FromMilliseconds(Math.Min(fan, 10) * 35);
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { BeginTime = delay, EasingFunction = new BackEase { Amplitude = .35, EasingMode = EasingMode.EaseOut } });
            entry.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { BeginTime = delay });
            entries.Children.Add(entry);
            fan++;
        }
        shell.Child = Scroll(entries);
    }, anchor);

    private static class SystemHotkey
    {
        [System.Runtime.InteropServices.DllImport("user32")] public static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
        [System.Runtime.InteropServices.DllImport("user32")] public static extern bool UnregisterHotKey(nint window, int id);
    }
}
