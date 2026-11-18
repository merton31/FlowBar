using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Screen = System.Windows.Forms.Screen;
using static LumaDock.Visuals;

namespace LumaDock;

internal sealed partial class DockWindow
{
    private void CaptureSurface(DockPanel content, Window window)
    {
        var preview = new System.Windows.Controls.Image { Height = 101, Stretch = Stretch.Uniform };
        var status = Text("", 12, false, Muted);
        var panel = Stack(preview, status);
        var actions = RowStack(Button("Region", () => { window.Close(); WindowsShell.Key(0x5B, 0x10, 0x53); }),
            Button("Screen", () => _ = Capture(null)),
            Button("Window", () =>
            {
                var menu = DockMenu();
                menu.PlacementTarget = preview;
                foreach (var item in windows.Take(12)) menu.Items.Add(Menu(item.Title, () => _ = Capture(item.Handle)));
                if (menu.Items.Count > 0) menu.IsOpen = true;
                else status.Text = L10n.T("No windows to capture");
            }));
        panel.Children.Add(actions);
        var text = RowStack(Button("Copy image", () => { if (preview.Source is BitmapSource image) Clipboard.SetImage(image); }),
            Button("Copy text (OCR)", () => _ = ReadText()));
        panel.Children.Add(text);
        content.Children.Add(panel);

        async Task Capture(nint? hwnd)
        {
            var screen = Screen.PrimaryScreen?.Bounds;
            var bounds = hwnd.HasValue ? WindowsShell.WindowBounds(hwnd.Value) : screen;
            if (!bounds.HasValue) { status.Text = L10n.T("Capture area unavailable"); return; }
            var area = System.Drawing.Rectangle.Intersect(bounds.Value, System.Windows.Forms.SystemInformation.VirtualScreen);
            if (area.Width <= 0 || area.Height <= 0) return;
            window.Opacity = 0;
            try
            {
                await Task.Delay(145);
                using var image = new Bitmap(area.Width, area.Height);
                using var graphics = Graphics.FromImage(image);
                graphics.CopyFromScreen(area.Location, System.Drawing.Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
                using var stream = new MemoryStream();
                image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;
                var decoded = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                preview.Source = decoded;
                Clipboard.SetImage(decoded);
                status.Text = L10n.T("Image copied to clipboard");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ExternalException or InvalidOperationException)
            { status.Text = L10n.T("Capture failed"); }
            finally { if (window.IsVisible) window.Opacity = 1; }
        }

        async Task ReadText()
        {
            var source = preview.Source as BitmapSource ?? Clipboard.GetImage();
            if (source is null) { status.Text = L10n.T("Capture an image first"); return; }
            status.Text = L10n.T("Recognizing text…");
            try
            {
                string recognized = await WindowsOcr.Read(source);
                if (string.IsNullOrWhiteSpace(recognized)) status.Text = L10n.T("No text recognized");
                else { Clipboard.SetText(recognized); status.Text = L10n.T("Text copied to clipboard"); }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or
                System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            { status.Text = L10n.T("Windows OCR is unavailable for this image."); }
        }
    }

    private void WallpaperSurface(DockPanel content, Window window)
    {
        var list = new WrapPanel();
        var panel = Stack();
        var info = Text("Choose a Windows wallpaper or open your own image.", 12, false, Muted);
        panel.Children.Add(info);
        var browse = Button("Open image…", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = L10n.T("Images") + "|*.jpg;*.jpeg;*.png;*.bmp" };
            if (dialog.ShowDialog(window) == true) Apply(dialog.FileName);
        });
        panel.Children.Add(RowStack(browse, Button("Browse online", () => BrowseWallpapers(Apply)),
            Button("Windows settings", () => WindowsShell.Launch("ms-settings:personalization-background"))));
        panel.Children.Add(list);
        content.Children.Add(Scroll(panel));
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string root = Path.Combine(windows, "Web", "Wallpaper");
        var wallpapers = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.jpg", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToArray()
            : [];
        foreach (string path in wallpapers.Take(20))
        {
            try
            {
                var thumbnail = new BitmapImage();
                thumbnail.BeginInit();
                thumbnail.UriSource = new Uri(path);
                thumbnail.DecodePixelWidth = 180;
                thumbnail.CacheOption = BitmapCacheOption.OnLoad;
                thumbnail.EndInit();
                var tile = Button(new System.Windows.Controls.Image { Source = thumbnail, Width = 119, Height = 75, Stretch = Stretch.UniformToFill },
                    () => Apply(path));
                tile.ToolTip = Path.GetFileNameWithoutExtension(path);
                tile.Margin = new(3);
                tile.Padding = new(0);
                list.Children.Add(tile);
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException) { }
        }
        bool Apply(string path)
        {
            bool applied = WindowsShell.ApplyWallpaper(path);
            info.Text = applied ? L10n.F($"Applied · {Path.GetFileName(path)}") : L10n.T("Wallpaper could not be applied");
            currentWallpaper = null;
            Refresh();
            return applied;
        }
    }

    private void BrowseWallpapers(Func<string, bool> apply) => Surface("Online Wallpapers", 750, 560, false, (window, shell) =>
    {
        var lifetime = new System.Threading.CancellationTokenSource();
        window.Closed += (_, _) => lifetime.Cancel();
        var query = Input("Search Wikimedia Commons", "mountain lake landscape");
        var status = Text("Search for landscape photos.", 13, false, Muted);
        var photos = new WrapPanel();
        bool loading = false;
        bool applying = false;
        var panel = Stack(Right(Text("Online Wallpapers", 20, true), CloseButton(window)),
            Right(query, Button("Search", () => _ = Search())), status, photos);
        panel.Margin = new(12);
        shell.Child = Scroll(panel);
        query.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            e.Handled = true;
            _ = Search();
        };
        if (!smoke) _ = Search();

        async Task Search()
        {
            if (loading || applying) return;
            loading = true;
            status.Text = L10n.T("Searching Wikimedia Commons…");
            photos.Children.Clear();
            try
            {
                var found = await OnlineWallpapers.Search(query.Text, lifetime.Token);
                status.Text = L10n.T(found.Length == 0 ? "No matching landscape images" : "Wikimedia Commons · select an image to set it as wallpaper");
                foreach (var photo in found)
                {
                    var preview = new System.Windows.Controls.Image { Width = 192, Height = 100, Stretch = Stretch.UniformToFill };
                    var title = RawText(photo.Title, 13, true);
                    var credit = Text($"{(photo.Artist.Length == 0 ? "Wikimedia Commons" : photo.Artist)} · {photo.License}", 11, false, Muted);
                    var select = Button(Stack(preview, title, credit), () => _ = Set(photo));
                    select.Padding = new(4);
                    var card = Stack(select, Button("Source and licence", () => WindowsShell.Launch(photo.Source.AbsoluteUri), System.Windows.Media.Brushes.Transparent));
                    card.Width = 202;
                    card.Margin = new(0, 6, 8, 6);
                    photos.Children.Add(card);
                    using var thumbnailTime = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    thumbnailTime.CancelAfter(TimeSpan.FromSeconds(5));
                    try { preview.Source = await OnlineWallpapers.Thumbnail(photo, thumbnailTime.Token); }
                    catch (Exception ex) when (IsWallpaperError(ex)) { }
                    if (lifetime.IsCancellationRequested) return;
                }
            }
            catch (Exception ex) when (IsWallpaperError(ex))
            {
                if (!lifetime.IsCancellationRequested) status.Text = L10n.T("Online gallery unavailable. Try again or use a local image.");
            }
            finally { loading = false; }
        }

        async Task Set(OnlineWallpapers.Photo photo)
        {
            if (applying) return;
            applying = true;
            status.Text = L10n.T("Downloading wallpaper…");
            try
            {
                string folder = Path.Combine(Path.GetDirectoryName(model.Profile)!, "Wallpapers");
                string path = await OnlineWallpapers.Save(photo, folder, lifetime.Token);
                if (lifetime.IsCancellationRequested) return;
                status.Text = apply(path) ? L10n.F($"Wallpaper applied · {photo.Title}") : L10n.T("Image downloaded. Windows could not set the wallpaper.");
            }
            catch (Exception ex) when (IsWallpaperError(ex))
            {
                if (!lifetime.IsCancellationRequested) status.Text = L10n.T("Could not download this image.");
            }
            finally { applying = false; }
        }
    });

    private static bool IsWallpaperError(Exception ex) => LiveData.IsDataError(ex) ||
        ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Runtime.InteropServices.COMException or TimeoutException;
}

internal static partial class WindowsShell
{
    public static System.Drawing.Rectangle? WindowBounds(nint window)
    {
        if (!GetWindowRect(window, out var outer)) return null;
        if (DwmGetWindowAttribute(window, 9, out NativeRect frame, Marshal.SizeOf<NativeRect>()) == 0 && frame.Right > frame.Left) outer = frame;
        return System.Drawing.Rectangle.FromLTRB(outer.Left, outer.Top, outer.Right, outer.Bottom);
    }

    public static bool ApplyWallpaper(string path) => SystemParametersInfoSet(0x14, 0, path, 3);
    [DllImport("user32", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfoSet(uint action, uint parameter, string value, uint flags);
}

internal static class WindowsOcr
{
    public static async Task<string> Read(BitmapSource source)
    {
        string path = Path.Combine(Path.GetTempPath(), "lumadock-ocr-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            double scale = Math.Min(1, 2200.0 / Math.Max(source.PixelWidth, source.PixelHeight));
            var reduced = scale < 1 ? new TransformedBitmap(source, new ScaleTransform(scale, scale)) : source;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(reduced));
            using (var file = File.Create(path)) encoder.Save(file);
            var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            var start = new ProcessStartInfo(exe)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true,
                RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8
            };
            start.Environment.Add("LUMADOCK_OCR_FILE", path);
            foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(OcrScript)) })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException();
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(14)); }
            catch (TimeoutException) { if (!process.HasExited) process.Kill(); throw; }
            if (process.ExitCode != 0) throw new InvalidOperationException("Windows OCR is unavailable");
            return (await output).Trim();
        }
        finally
        {
            try { File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private const string OcrScript = """
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $bridge = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
            $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetGenericArguments().Count -eq 1 -and $_.GetParameters().Count -eq 1 -and
            $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
        } | Select-Object -First 1
        function Await($operation, [Type]$type) {
            $task = $bridge.MakeGenericMethod($type).Invoke($null, @($operation))
            $task.GetAwaiter().GetResult()
        }
        $storage = [Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]
        $streamType = [Windows.Storage.Streams.IRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime]
        $decoder = [Windows.Graphics.Imaging.BitmapDecoder,Windows.Graphics.Imaging,ContentType=WindowsRuntime]
        $bitmapType = [Windows.Graphics.Imaging.SoftwareBitmap,Windows.Graphics.Imaging,ContentType=WindowsRuntime]
        $access = [Windows.Storage.FileAccessMode,Windows.Storage,ContentType=WindowsRuntime]
        $engineType = [Windows.Media.Ocr.OcrEngine,Windows.Media.Ocr,ContentType=WindowsRuntime]
        $resultType = [Windows.Media.Ocr.OcrResult,Windows.Media.Ocr,ContentType=WindowsRuntime]
        $file = Await ($storage::GetFileFromPathAsync($env:LUMADOCK_OCR_FILE)) $storage
        $stream = Await ($file.OpenAsync($access::Read)) $streamType
        try {
            $intermediate = Await ($decoder::CreateAsync($stream)) $decoder
            $bitmap = Await ($intermediate.GetSoftwareBitmapAsync()) $bitmapType
            try {
                $engine = $engineType::TryCreateFromUserProfileLanguages()
                if ($null -eq $engine) { throw 'OCR language unavailable' }
                $result = Await ($engine.RecognizeAsync($bitmap)) $resultType
                [Console]::WriteLine($result.Text)
            } finally { $bitmap.Dispose() }
        } finally { $stream.Dispose() }
        """;
}
