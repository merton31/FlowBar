using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace LumaDock;

internal sealed class LiveData
{
    private static readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(9),
        MaxResponseContentBufferSize = 2 * 1024 * 1024 };

    internal record Place(string Name, string Region, double Latitude, double Longitude);
    internal record ForecastDay(DateTime Date, double Low, double High, int Code, DateTime? Sunrise, DateTime? Sunset);
    internal record ForecastHour(DateTime At, double Temp, int? Chance, int Code, bool IsDay);
    internal record Weather(decimal Temp, decimal Feels, int Humidity, int Wind, int Code, bool IsDay, DateTime Updated,
        ForecastDay[] Days, ForecastHour[] Hours);
    internal record Quote(string Title, string Value, string Detail, string Url);

    internal static readonly string[] PublicIds = ["Currency", "Crypto", "GitHub", "GitHub Overview", "GitHub Activity",
        "Weather", "Weather Details", "Today & Forecast", "Weather Status", "Hourly", "Sunrise & Sunset", "Clock & Weather", "LeetCode Stats"];
    public Weather? Forecast { get; internal set; }
    public string? WeatherError { get; private set; }
    public Dictionary<string, Quote> Quotes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Errors { get; } = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Query, DateTimeOffset At)> requests = new(StringComparer.Ordinal);
    private readonly HashSet<string> pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> currentQueries = new(StringComparer.Ordinal);

    // Язык поиска городов: русский при русском интерфейсе или русских буквах в запросе (при языке en запрос «Москва» ничего не находит)
    internal static string SearchLanguage(string name, bool russianInterface) =>
        russianInterface || name.Any(letter => letter is >= 'А' and <= 'я' or 'Ё' or 'ё') ? "ru" : "en";

    public static async Task<Place[]> FindPlaces(string name)
    {
        name = name.Trim();
        if (name.Length < 2) return [];
        using var data = await Json("https://geocoding-api.open-meteo.com/v1/search?count=7&language=" + SearchLanguage(name, L10n.Russian) + "&name=" + Uri.EscapeDataString(name));
        if (!data.RootElement.TryGetProperty("results", out var results)) return [];
        return results.EnumerateArray().Select(city =>
        {
            string title = city.GetProperty("name").GetString()!;
            // область помогает отличить одноимённые города (Москва в России и в штате Айдахо)
            string area = city.TryGetProperty("admin1", out var admin) ? admin.GetString() ?? "" : "";
            string country = city.TryGetProperty("country", out var region) ? region.GetString() ?? "" : "";
            string where = string.Join(", ", new[] { area.Length > 0 && !string.Equals(area, title, StringComparison.OrdinalIgnoreCase) ? area : "", country }.Where(part => part.Length > 0));
            return new Place(title, where, city.GetProperty("latitude").GetDouble(), city.GetProperty("longitude").GetDouble());
        }).ToArray();
    }

    public async Task RefreshWeather(Place place, bool force = false)
    {
        string query = FormattableString.Invariant($"&latitude={place.Latitude}&longitude={place.Longitude}");
        if (currentQueries.GetValueOrDefault("weather") != query)
        {
            Forecast = null;
            WeatherError = null;
            currentQueries["weather"] = query;
            force = true;
        }
        string name = "weather:" + query;
        if (!Begin(name, query, force, 10)) return;
        try
        {
            using var data = await Json("https://api.open-meteo.com/v1/forecast?timezone=auto&forecast_days=7" + query +
                "&current=temperature_2m,apparent_temperature,relative_humidity_2m,wind_speed_10m,weather_code,is_day" +
                "&daily=temperature_2m_min,temperature_2m_max,weather_code,sunrise,sunset" +
                "&hourly=temperature_2m,precipitation_probability,weather_code,is_day");
            var result = ParseWeather(data.RootElement);
            if (currentQueries["weather"] == query)
            {
                Forecast = result;
                WeatherError = null;
            }
        }
        catch (Exception ex) when (IsDataError(ex))
        {
            if (currentQueries["weather"] == query) WeatherError = "Weather unavailable. Try refreshing.";
        }
        finally { pending.Remove(name); }
    }

    internal static Weather ParseWeather(JsonElement root)
    {
        var current = root.GetProperty("current");
        var daily = root.GetProperty("daily");
        var hourly = root.GetProperty("hourly");
        var days = Enumerable.Range(0, daily.GetProperty("time").GetArrayLength()).Select(i => new ForecastDay(
            ParseTime(daily.GetProperty("time")[i]), daily.GetProperty("temperature_2m_min")[i].GetDouble(),
            daily.GetProperty("temperature_2m_max")[i].GetDouble(), WeatherCode(daily.GetProperty("weather_code")[i]),
            OptionalTime(daily.GetProperty("sunrise")[i]), OptionalTime(daily.GetProperty("sunset")[i]))).ToArray();
        var hours = Enumerable.Range(0, hourly.GetProperty("time").GetArrayLength())
            .Where(i => hourly.GetProperty("temperature_2m")[i].ValueKind == JsonValueKind.Number)
            .Select(i => new ForecastHour(ParseTime(hourly.GetProperty("time")[i]), hourly.GetProperty("temperature_2m")[i].GetDouble(),
                hourly.GetProperty("precipitation_probability")[i].ValueKind == JsonValueKind.Number
                    ? hourly.GetProperty("precipitation_probability")[i].GetInt32() : null,
                WeatherCode(hourly.GetProperty("weather_code")[i]), hourly.GetProperty("is_day")[i].GetInt32() == 1)).ToArray();
        return new(current.GetProperty("temperature_2m").GetDecimal(), current.GetProperty("apparent_temperature").GetDecimal(),
            current.GetProperty("relative_humidity_2m").GetInt32(), (int)current.GetProperty("wind_speed_10m").GetDouble(),
            WeatherCode(current.GetProperty("weather_code")), current.GetProperty("is_day").GetInt32() == 1,
            ParseTime(current.GetProperty("time")), days, hours);
    }

    private static int WeatherCode(JsonElement value) => value.ValueKind == JsonValueKind.Number ? value.GetInt32() : -1;

    private static DateTime? OptionalTime(JsonElement value) => value.ValueKind == JsonValueKind.String ? ParseTime(value) : null;

    internal static IEnumerable<ForecastHour> UpcomingHours(Weather forecast) => forecast.Hours.Where(hour => hour.At > forecast.Updated);

    public async Task RefreshQuote(string id, string terms, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(terms)) return;
        if (currentQueries.GetValueOrDefault(id) != terms)
        {
            currentQueries[id] = terms;
            Quotes.Remove(id);
            Errors.Remove(id);
            force = true;
        }
        var request = id + ":" + terms;
        if (!Begin(request, terms, force, id == "Currency" ? 30 : 5)) return;
        try
        {
            var result = id switch
            {
                "Currency" => await Currency(terms),
                "Crypto" => await Crypto(terms),
                "LeetCode Stats" => await LeetCode(terms),
                _ => await GitHub(id, terms)
            };
            if (currentQueries[id] == terms)
            {
                Quotes[id] = result;
                Errors.Remove(id);
            }
        }
        catch (Exception ex) when (IsDataError(ex))
        {
            if (currentQueries[id] == terms)
                Errors[id] = ex is FormatException ? ex.Message : "Data unavailable. Check the input or try again.";
        }
        finally { pending.Remove(request); }
    }

    private bool Begin(string name, string query, bool force, int minutes)
    {
        if (pending.Contains(name) || !force && requests.TryGetValue(name, out var prior)
            && prior.Query == query && DateTimeOffset.Now - prior.At < TimeSpan.FromMinutes(minutes)) return false;
        requests[name] = (query, DateTimeOffset.Now);
        if (requests.Count > 100)
            foreach (string old in requests.Where(item => DateTimeOffset.Now - item.Value.At > TimeSpan.FromHours(1)).Select(item => item.Key).ToArray())
                requests.Remove(old);
        return pending.Add(name);
    }

    private static async Task<Quote> Currency(string input)
    {
        string[] pair = input.ToUpperInvariant().Split(new[] { '/', ' ', '-', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (pair.Length != 2 || pair.Any(code => code.Length != 3 || code.Any(c => !char.IsAsciiLetter(c))))
            throw new FormatException("Use two currency codes, such as EUR/USD.");
        using var response = await Json($"https://api.frankfurter.dev/v2/rate/{pair[0]}/{pair[1]}");
        var item = response.RootElement;
        return new($"{pair[0]} / {pair[1]}", item.GetProperty("rate").GetDecimal().ToString("0.####"),
            item.GetProperty("date").GetString() ?? "", "https://frankfurter.dev/");
    }

    private static async Task<Quote> Crypto(string input)
    {
        string symbol = input.Trim().ToUpperInvariant();
        if (symbol.Length is < 4 or > 16 || symbol.Any(c => !char.IsAsciiLetterOrDigit(c)))
            throw new FormatException("Enter a trading pair, such as BTCUSDT.");
        using var response = await Json($"https://data-api.binance.vision/api/v3/ticker/24hr?symbol={symbol}");
        var item = response.RootElement;
        decimal price = decimal.Parse(item.GetProperty("lastPrice").GetString()!, CultureInfo.InvariantCulture);
        decimal change = decimal.Parse(item.GetProperty("priceChangePercent").GetString()!, CultureInfo.InvariantCulture);
        return new(symbol, $"{price:N2}", $"{change:+0.00;-0.00;0.00}% · 24h", "https://www.binance.com/en/markets");
    }

    private static async Task<Quote> GitHub(string id, string input)
    {
        string name = input.Trim().TrimStart('@');
        if (name.Length is < 1 or > 39 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new FormatException("Enter a GitHub username.");
        string handle = Uri.EscapeDataString(name);
        string url = $"https://github.com/{handle}";
        if (id == "GitHub Activity")
        {
            using var response = await Json($"https://api.github.com/users/{handle}/events/public?per_page=30");
            var list = response.RootElement;
            var last = list.GetArrayLength() > 0 ? list[0].GetProperty("type").GetString() : "No public events";
            return new($"@{name}", $"{list.GetArrayLength()} events", last ?? "", url);
        }
        using var profile = await Json($"https://api.github.com/users/{handle}");
        var details = profile.RootElement;
        return new($"@{name}", $"{details.GetProperty("public_repos").GetInt32()} repos",
            $"{details.GetProperty("followers").GetInt32()} followers", url);
    }

    private static async Task<Quote> LeetCode(string input)
    {
        string name = input.Trim();
        if (name.Length is < 2 or > 64 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new FormatException("Enter a LeetCode username.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://leetcode.com/graphql/");
        request.Headers.UserAgent.ParseAdd("FlowBar/2.0");
        const string query = "query Progress($username: String!) { matchedUser(username: $username) { submitStatsGlobal { acSubmissionNum { difficulty count } } } }";
        request.Content = new StringContent(JsonSerializer.Serialize(new { query, variables = new { username = name } }),
            System.Text.Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var matched = data.RootElement.GetProperty("data").GetProperty("matchedUser");
        if (matched.ValueKind == JsonValueKind.Null) throw new FormatException("No matching LeetCode profile.");
        var counts = matched.GetProperty("submitStatsGlobal").GetProperty("acSubmissionNum").EnumerateArray()
            .ToDictionary(row => row.GetProperty("difficulty").GetString() ?? "", row => row.GetProperty("count").GetInt32());
        return new(name, $"{counts.GetValueOrDefault("All")} solved",
            $"{counts.GetValueOrDefault("Easy")} easy · {counts.GetValueOrDefault("Medium")} medium · {counts.GetValueOrDefault("Hard")} hard",
            $"https://leetcode.com/u/{Uri.EscapeDataString(name)}/");
    }

    internal static async Task<JsonDocument> Json(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("FlowBar/2.0");
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await client.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
    }

    private static DateTime ParseTime(JsonElement value) => DateTime.Parse(value.GetString()!, CultureInfo.InvariantCulture);
    internal static bool IsDataError(Exception ex) => ex is HttpRequestException or System.Threading.Tasks.TaskCanceledException
        or JsonException or FormatException or InvalidOperationException or KeyNotFoundException or OverflowException or ArgumentException;

    public static string Condition(int code) => code switch
    {
        0 => "Clear", 1 or 2 => "Partly cloudy", 3 => "Overcast", 45 or 48 => "Fog",
        >= 51 and <= 57 => "Drizzle", >= 61 and <= 67 or >= 80 and <= 82 => "Rain",
        >= 71 and <= 77 or 85 or 86 => "Snow", >= 95 => "Thunderstorms", _ => "Conditions unavailable"
    };
}
