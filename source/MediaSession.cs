using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace LumaDock;

// Системный медиасеанс Windows (Spotify, браузер и другие) напрямую через WinRT: без фонового PowerShell (он держал около 100 МБ) и с событиями,
// поэтому пауза, смена трека и перемотка видны сразу, а не по опросу.
internal sealed class MediaSession : IDisposable
{
    // Position и Duration — секунды с момента чтения; у потоковых передач длительности нет (0). Cover осталось ради проверок: обложка теперь лежит в Artwork
    internal sealed record Track(string Title, string Artist, string State, string Source, string? Cover, double Position = 0, double Duration = 0);
    private readonly SemaphoreSlim gate = new(1);
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? hooked;
    private string? coverKey;
    private DateTimeOffset retry;
    private string? pending;
    private DateTimeOffset holdUntil;
    // Пока идёт чтение, пришло ещё одно событие: чтение повторяется, чтобы не потерять свежее состояние
    private volatile bool dirty;
    private int poking;
    private bool disposed;
    // Состояние изменилось (чтение или нажатие): окна перерисовывают плитки сразу, не дожидаясь своего такта
    public event Action? Changed;
    public Track? Current { get; private set; }
    public BitmapSource? Artwork { get; private set; }
    // Когда прочитано текущее состояние: между чтениями время дорисовывается по часам
    public DateTimeOffset ReadAt { get; private set; }

    public async Task Refresh()
    {
        if (disposed || DateTimeOffset.Now < retry) return;
        if (!gate.Wait(0)) { dirty = true; return; }
        try
        {
            if (manager is null)
            {
                manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
                manager.CurrentSessionChanged += (_, _) => Poke();
            }
            var session = manager.GetCurrentSession();
            Hook(session);
            Track? read = null;
            if (session is not null)
            {
                var props = await session.TryGetMediaPropertiesAsync().AsTask();
                var timeline = session.GetTimelineProperties();
                string source = session.SourceAppUserModelId ?? "";
                double span = timeline is null ? 0 : Math.Round((timeline.EndTime - timeline.StartTime).TotalSeconds, 1);
                double at = timeline is null ? 0 : Math.Round((timeline.Position - timeline.StartTime).TotalSeconds, 1);
                read = new Track(props.Title ?? "", props.Artist ?? "", session.GetPlaybackInfo().PlaybackStatus.ToString(), source, null, at, span);
                string key = source + "|" + read.Title + "|" + read.Artist;
                // обложку читаем при смене трека, а также пока её нет (часть приложений присылает её чуть позже названия)
                if (key != coverKey || (Artwork is null && props.Thumbnail is not null))
                {
                    coverKey = key;
                    Artwork = await ReadCover(props.Thumbnail);
                }
            }
            else { Artwork = null; coverKey = null; }
            // пока ждём реакции приложения на клавишу, запоздалое чтение со старым состоянием не должно откатывать значок назад
            if (read is not null && pending is not null && DateTimeOffset.Now < holdUntil && read.State != pending) read = read with { State = pending };
            else if (read is not null && read.State == pending) pending = null;
            Current = read;
            ReadAt = DateTimeOffset.Now;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            ErrorLog.Write(ex, "Media session");
            retry = DateTimeOffset.Now.AddSeconds(30);
            Unhook();
            manager = null;
            Current = null;
            Artwork = null;
            coverKey = null;
        }
        finally { gate.Release(); }
        Changed?.Invoke();
        if (dirty) { dirty = false; await Refresh(); }
    }

    private static async Task<BitmapSource?> ReadCover(Windows.Storage.Streams.IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return null;
        try
        {
            using var source = await thumbnail.OpenReadAsync().AsTask();
            if (source.Size is 0 or > 4194304) return null;
            using var copy = new MemoryStream();
            await source.AsStreamForRead().CopyToAsync(copy);
            copy.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 640; // обложка в окне плеера крупная
            bitmap.StreamSource = copy;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is COMException or IOException or NotSupportedException or FormatException or ArgumentException) { return null; }
    }

    // События системного сеанса приходят из чужих потоков: перечитываем в потоке окна, слив частые события в одно (около 60 мс)
    private void Hook(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(session, hooked)) return;
        Unhook();
        hooked = session;
        if (session is null) return;
        session.MediaPropertiesChanged += OnSessionChanged;
        session.PlaybackInfoChanged += OnSessionChanged;
        session.TimelinePropertiesChanged += OnSessionChanged;
    }

    private void Unhook()
    {
        if (hooked is null) return;
        hooked.MediaPropertiesChanged -= OnSessionChanged;
        hooked.PlaybackInfoChanged -= OnSessionChanged;
        hooked.TimelinePropertiesChanged -= OnSessionChanged;
        hooked = null;
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) => Poke();

    private void Poke()
    {
        if (disposed || Interlocked.Exchange(ref poking, 1) == 1) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) { poking = 0; return; }
        dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(60);
            poking = 0;
            await Refresh();
        });
    }

    // Клавиша мультимедиа из панели: значок воспроизведения меняется сразу, не дожидаясь чтения, а настоящее состояние приходит событием (и ещё перечитывается дважды на случай, если событие не придёт)
    public void Press(byte key)
    {
        WindowsShell.Key(key);
        if (key == 0xB3 && Current is { } track)
        {
            bool playing = track.State == "Playing";
            double at = track.Position + (playing ? (DateTimeOffset.Now - ReadAt).TotalSeconds : 0);
            pending = playing ? "Paused" : "Playing";
            holdUntil = DateTimeOffset.Now.AddSeconds(1.2);
            Current = track with { State = pending, Position = Math.Min(at, track.Duration > 0 ? track.Duration : at) };
            ReadAt = DateTimeOffset.Now;
            Changed?.Invoke();
        }
        _ = ReadSoon();
    }

    private async Task ReadSoon()
    {
        foreach (int wait in new[] { 150, 400 })
        {
            await Task.Delay(wait);
            await Refresh();
        }
    }

    // Перемотка в системном сеансе (Spotify и другие, которые это позволяют); результат приходит событием
    public async Task SeekAsync(double seconds)
    {
        try
        {
            var session = manager?.GetCurrentSession();
            if (session is not null) await session.TryChangePlaybackPositionAsync((long)(seconds * 10_000_000)).AsTask();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException) { }
    }

    // Для самопроверки: подставляет трек и обложку вместо настоящего сеанса
    internal void SetForTest(Track track, BitmapSource? artwork)
    {
        Current = track;
        Artwork = artwork;
        ReadAt = DateTimeOffset.Now;
    }

    public void Dispose()
    {
        disposed = true;
        Unhook();
        manager = null;
    }
}
