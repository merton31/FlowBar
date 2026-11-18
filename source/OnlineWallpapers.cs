using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace LumaDock;

internal static class OnlineWallpapers
{
    internal record Photo(int Id, string Title, string Artist, string License, Uri Thumbnail, Uri Source);
    private const string Api = "https://commons.wikimedia.org/w/api.php?format=json&formatversion=2&action=query&prop=imageinfo";
    private static readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 2 * 1024 * 1024 };
    private static readonly Regex markup = new("<[^>]+>", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    public static async Task<Photo[]> Search(string query, CancellationToken cancellation)
    {
        string term = query.Trim();
        if (term.Length > 120) term = term[..120];
        if (term.Length == 0) term = "mountain lake landscape";
        string url = Api + "&generator=search&gsrnamespace=6&gsrlimit=28&gsrsearch=" + Uri.EscapeDataString(term + " filetype:bitmap")
            + "&iiprop=url|size|mime|extmetadata&iiurlwidth=360&iiextmetadatafilter=Artist|LicenseShortName";
        using var document = await Json(url, cancellation);
        return Parse(document.RootElement);
    }

    internal static Photo[] Parse(JsonElement root)
    {
        if (root.TryGetProperty("error", out _)) throw new InvalidDataException("Wikimedia Commons returned an error.");
        if (!root.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) return [];
        return pages.EnumerateArray()
            .OrderBy(page => page.TryGetProperty("index", out var index) && index.TryGetInt32(out int value) ? value : int.MaxValue)
            .Select(ParsePhoto).OfType<Photo>().Take(12).ToArray();
    }

    private static Photo? ParsePhoto(JsonElement page)
    {
        if (!page.TryGetProperty("imageinfo", out var infos) || infos.GetArrayLength() == 0) return null;
        var info = infos[0];
        if (String(info, "mime") is not ("image/jpeg" or "image/png") ||
            !info.TryGetProperty("width", out var width) || !info.TryGetProperty("height", out var height) ||
            width.GetInt32() < 1280 || height.GetInt32() < 600 || width.GetInt32() < 1.4 * height.GetInt32() ||
            ImageUri(String(info, "thumburl")) is not { } preview || !page.TryGetProperty("pageid", out var id))
            return null;
        string artist = "", license = "";
        if (info.TryGetProperty("extmetadata", out var metadata))
        {
            artist = Metadata(metadata, "Artist");
            license = Metadata(metadata, "LicenseShortName");
        }
        if (license.Length == 0) return null;
        string label = String(page, "title");
        if (label.StartsWith("File:", StringComparison.OrdinalIgnoreCase)) label = label[5..];
        return new Photo(id.GetInt32(), Path.GetFileNameWithoutExtension(label).Replace('_', ' '), artist, license, preview,
            new Uri($"https://commons.wikimedia.org/?curid={id.GetInt32()}"));
    }

    public static Task<BitmapImage> Thumbnail(Photo photo, CancellationToken cancellation)
        => Download(photo.Thumbnail, 420, 3 * 1024 * 1024, cancellation);

    public static async Task<string> Save(Photo photo, string folder, CancellationToken cancellation)
    {
        using var document = await Json($"{Api}&pageids={photo.Id}&iiprop=url&iiurlwidth=1920", cancellation);
        var info = document.RootElement.GetProperty("query").GetProperty("pages")[0].GetProperty("imageinfo")[0];
        var url = ImageUri(String(info, "thumburl")) ?? ImageUri(String(info, "url"))
            ?? throw new InvalidDataException("Image address is unavailable.");
        var bitmap = await Download(url, 2560, 28 * 1024 * 1024, cancellation);
        cancellation.ThrowIfCancellationRequested();
        Directory.CreateDirectory(folder);
        string filename = Path.Combine(folder, "commons-wallpaper.jpg");
        string pending = filename + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var jpeg = new JpegBitmapEncoder { QualityLevel = 93 };
            jpeg.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(pending)) jpeg.Save(output);
            cancellation.ThrowIfCancellationRequested();
            File.Move(pending, filename, true);
            return filename;
        }
        finally
        {
            if (File.Exists(pending)) File.Delete(pending);
        }
    }

    private static async Task<BitmapImage> Download(Uri address, int width, int limit, CancellationToken cancellation)
    {
        using var request = Request(address);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit ||
            response.Content.Headers.ContentType?.MediaType is not ("image/jpeg" or "image/png"))
            throw new InvalidDataException("Unexpected image response.");
        await using var body = await response.Content.ReadAsStreamAsync(cancellation);
        using var memory = new MemoryStream();
        var buffer = new byte[64 * 1024];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        while (true)
        {
            int read = await body.ReadAsync(buffer, deadline.Token);
            if (read == 0) break;
            if (memory.Length + read > limit) throw new InvalidDataException("Image is too large.");
            memory.Write(buffer, 0, read);
        }
        memory.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = width;
        bitmap.StreamSource = memory;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static async Task<JsonDocument> Json(string url, CancellationToken cancellation)
    {
        using var request = Request(new Uri(url));
        using var response = await client.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellation);
        return await JsonDocument.ParseAsync(body, cancellationToken: cancellation);
    }

    private static HttpRequestMessage Request(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("FlowBar/2.0 (desktop wallpaper gallery)");
        return request;
    }

    private static Uri? ImageUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.Host == "upload.wikimedia.org" && uri.IsDefaultPort && uri.UserInfo.Length == 0 ? uri : null;

    private static string String(JsonElement parent, string key) =>
        parent.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string Metadata(JsonElement container, string key)
    {
        if (!container.TryGetProperty(key, out var item)) return "";
        string value = String(item, "value");
        if (value.Length > 2000) value = value[..2000];
        var plain = WebUtility.HtmlDecode(markup.Replace(value, "")).Trim();
        return plain.Length > 100 ? plain[..100] : plain;
    }
}
