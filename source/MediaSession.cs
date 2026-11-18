using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace LumaDock;

internal sealed class MediaSession : IDisposable
{
    internal sealed record Track(string Title, string Artist, string State, string Source, string? Cover);
    private Process? bridge;
    private readonly SemaphoreSlim gate = new(1);
    private string? coverData;
    private DateTimeOffset retry;
    public Track? Current { get; private set; }
    public BitmapImage? Artwork { get; private set; }

    public async Task Refresh()
    {
        if (DateTimeOffset.Now < retry || !gate.Wait(0)) return;
        try
        {
            if (bridge is null || bridge.HasExited) Start();
            if (bridge is null) return;
            await bridge.StandardInput.WriteLineAsync("read");
            var line = await bridge.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4));
            if (line is null) throw new IOException("Media session ended");
            Current = JsonSerializer.Deserialize<Track>(line);
            var cover = Current?.Cover;
            if (cover != coverData)
            {
                coverData = cover;
                Artwork = Decode(cover);
            }
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or
            System.ComponentModel.Win32Exception or JsonException)
        {
            retry = DateTimeOffset.Now.AddSeconds(30);
            CloseBridge();
            Current = null;
            Artwork = null;
            coverData = null;
        }
        finally { gate.Release(); }
    }

    private static BitmapImage? Decode(string? data)
    {
        if (string.IsNullOrEmpty(data)) return null;
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(data));
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 260;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or IOException) { return null; }
    }

    private void Start()
    {
        CloseBridge();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)) }) start.ArgumentList.Add(arg);
        bridge = Process.Start(start);
    }

    private const string Script = """
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $async = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
            $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetGenericArguments().Count -eq 1 -and $_.GetParameters().Count -eq 1 -and
            $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
        } | Select-Object -First 1
        function Await($operation, [Type]$type) {
            $task = $async.MakeGenericMethod($type).Invoke($null, @($operation))
            $task.GetAwaiter().GetResult()
        }
        $managerType = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager,Windows.Media,ContentType=WindowsRuntime]
        $propertiesType = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties,Windows.Media,ContentType=WindowsRuntime]
        $streamType = [Windows.Storage.Streams.IRandomAccessStreamWithContentType,Windows.Storage.Streams,ContentType=WindowsRuntime]
        $inputType = [Windows.Storage.Streams.IInputStream,Windows.Storage.Streams,ContentType=WindowsRuntime]
        $sizeProperty = [Windows.Storage.Streams.IRandomAccessStream].GetProperty('Size')
        $asStream = [System.IO.WindowsRuntimeStreamExtensions].GetMethod('AsStreamForRead', [Type[]]@($inputType))
        $manager = Await ($managerType::RequestAsync()) $managerType
        $artKey = ''
        $cover = $null
        while (($line = [Console]::ReadLine()) -ne $null) {
            if ($line -ne 'read') { continue }
            try {
                $session = $manager.GetCurrentSession()
                if ($null -eq $session) { [Console]::WriteLine('null'); continue }
                $props = Await ($session.TryGetMediaPropertiesAsync()) $propertiesType
                $key = $session.SourceAppUserModelId + '|' + $props.Title + '|' + $props.Artist
                if ($key -ne $artKey) {
                    $cover = $null
                    $artKey = $key
                    if ($null -ne $props.Thumbnail) {
                        try {
                            $image = Await ($props.Thumbnail.OpenReadAsync()) $streamType
                            try {
                                # Поток приходит как «голый» COM-объект: $image.Size пуст, а AsStreamForRead не находит перегрузку, поэтому обложка пропадала.
                                # Читаем размер и открываем поток через рефлексию по интерфейсам WinRT.
                                $size = $sizeProperty.GetValue($image)
                                if ($size -gt 0 -and $size -le 4194304) {
                                    $native = $asStream.Invoke($null, @($image))
                                    $bytes = New-Object System.IO.MemoryStream
                                    try { $native.CopyTo($bytes); $cover = [Convert]::ToBase64String($bytes.ToArray()) }
                                    finally { $bytes.Dispose(); $native.Dispose() }
                                }
                            } finally { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($image) }
                        } catch {}
                    }
                }
                $data = @{ Title = $props.Title; Artist = $props.Artist; State = $session.GetPlaybackInfo().PlaybackStatus.ToString();
                    Source = $session.SourceAppUserModelId; Cover = $cover }
                [Console]::WriteLine(($data | ConvertTo-Json -Compress))
            } catch { [Console]::WriteLine('null') }
        }
        """;

    private void CloseBridge()
    {
        if (bridge is null) return;
        try
        {
            if (!bridge.HasExited) bridge.Kill();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        bridge.Dispose();
        bridge = null;
    }

    public void Dispose() => CloseBridge();
}
