using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LumaDock;

internal sealed partial class ConnectedData
{
    internal record Connection(string Name, string SecretLabel, string? AccountLabel, string Dashboard);
    internal record Entry(string Title, string Detail, string? Url = null);
    internal record Line(DateTime At, decimal Amount, string Currency);
    internal record StockPoint(DateTimeOffset At, decimal Close);
    internal record Snapshot(string Value, string Detail, Entry[] Entries, Line[]? Revenue = null, bool Partial = false,
        StockPoint[]? Prices = null, string? PriceStatus = null);

    private static readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 3 * 1024 * 1024
    };

    internal static readonly Dictionary<string, Connection> Providers = new(StringComparer.Ordinal)
    {
        ["Gmail"] = new("Gmail", "Read-only OAuth access token", null, "https://mail.google.com/"),
        ["Search Console"] = new("Search Console", "Read-only OAuth access token", "Property URL or sc-domain:example.com", "https://search.google.com/search-console/"),
        ["Linear"] = new("Linear", "Personal API key with read access", null, "https://linear.app/"),
        ["Supabase"] = new("Supabase", "Management access token", null, "https://supabase.com/dashboard/"),
        ["Resend"] = new("Resend", "API key with email read access", null, "https://resend.com/emails"),
        ["Plausible"] = new("Plausible", "Stats API key", "Site ID", "https://plausible.io/"),
        ["Stripe"] = new("Stripe", "Restricted key with Charges read access", null, "https://dashboard.stripe.com/"),
        ["Lemon Squeezy"] = new("Lemon Squeezy", "Orders API key", "Store ID (optional)", "https://app.lemonsqueezy.com/"),
        ["Stock Chart"] = new("Stock Chart", "Finnhub API token", "Stock symbol", "https://finnhub.io/"),
        ["Stock Watchlist"] = new("Stock Watchlist", "Finnhub API token", "Symbols separated by commas", "https://finnhub.io/"),
        ["DeepSeek Balance"] = new("DeepSeek Balance", "DeepSeek API key", null, "https://platform.deepseek.com/"),
        ["Polar"] = new("Polar", "Organization token with orders read access", null, "https://polar.sh/dashboard"),
        ["Shopify"] = new("Shopify", "Admin API access token with read_orders", "Store name or myshopify.com host", "https://admin.shopify.com/"),
        ["DataFast Chart"] = new("DataFast Chart", "Analytics read key", "Website ID (for account tokens)", "https://datafa.st/"),
        ["TikTok"] = new("TikTok", "OAuth access token with user.info.basic and user.info.stats", null, "https://www.tiktok.com/"),
        ["YouTube"] = new("YouTube", "YouTube Data API key", "Channel ID or @handle", "https://studio.youtube.com/"),
        ["X Profile"] = new("X Profile", "X API read bearer token", "Username", "https://x.com/"),
        ["Instagram"] = new("Instagram", "Instagram professional account access token", null, "https://www.instagram.com/"),
        ["Dub"] = new("Dub", "Workspace API key with analytics read access", null, "https://app.dub.co/"),
        ["Paddle"] = new("Paddle", "API key with transaction.read permission", null, "https://vendors.paddle.com/"),
        ["Dodo Payments"] = new("Dodo Payments", "Live API key with payments read access", null, "https://app.dodopayments.com/"),
        ["Gumroad"] = new("Gumroad", "OAuth access token with view_sales", null, "https://gumroad.com/dashboard"),
    };
    internal static readonly string[] Finance = ["Stripe", "Lemon Squeezy", "Polar", "Shopify", "Paddle", "Dodo Payments", "Gumroad"];
    public Dictionary<string, Snapshot> Dashboards { get; } = new();
    public Dictionary<string, string> Errors { get; } = new();
    private readonly Dictionary<string, DateTime> attempts = new(StringComparer.Ordinal);
    private readonly HashSet<string> inFlight = new(StringComparer.Ordinal);

    public static string BaseId(string id) => id switch
    {
        "Stripe Chart" => "Stripe", "Lemon Squeezy Chart" => "Lemon Squeezy", "Polar Chart" => "Polar", "Shopify Chart" => "Shopify",
        "Paddle Chart" => "Paddle", "Dodo Chart" => "Dodo Payments", "Gumroad Chart" => "Gumroad", _ => id
    };

    internal static bool Supports(string id) => id == "Total Revenue" || Providers.ContainsKey(BaseId(id));

    public async Task Refresh(string id, Preferences preferences, bool force = false)
    {
        if (id == "Total Revenue")
        {
            await Task.WhenAll(Finance.Select(provider => Refresh(provider, preferences, force)));
            var loaded = Finance.Where(Dashboards.ContainsKey).Select(provider => Dashboards[provider]).ToArray();
            if (loaded.Length > 0)
            {
                var lines = loaded.SelectMany(value => value.Revenue ?? []).ToArray();
                var summaries = Totals(lines);
                Dashboards[id] = new Snapshot(summaries.Length == 1 ? summaries[0].Detail : $"{summaries.Length} currencies",
                    $"{loaded.Length}/{Finance.Length} revenue providers · last 30 days", summaries, lines,
                    loaded.Any(item => item.Partial));
                Errors.Remove(id);
            }
            else Errors[id] = "Connect a revenue provider first.";
            return;
        }

        id = BaseId(id);
        if (!Providers.ContainsKey(id) || inFlight.Contains(id)) return;
        if (!force && attempts.TryGetValue(id, out var previous) && DateTime.Now - previous < TimeSpan.FromMinutes(5)) return;
        var secret = CredentialStore.Read(id);
        if (string.IsNullOrWhiteSpace(secret)) { Errors[id] = "Connect this service to load data."; return; }
        string account = preferences.ProviderAccounts.GetValueOrDefault(id) ?? "";
        attempts[id] = DateTime.Now;
        inFlight.Add(id);
        try
        {
            var snapshot = id switch
            {
                "Gmail" => await Gmail(secret),
                "Search Console" => await SearchConsole(secret, account),
                "Linear" => await Linear(secret),
                "Supabase" => await Supabase(secret),
                "Resend" => await Resend(secret),
                "Plausible" => await Plausible(secret, account),
                "Stripe" => await Stripe(secret),
                "Lemon Squeezy" => await Lemon(secret, account),
                "Stock Chart" => await Stocks(secret, account, true),
                "Stock Watchlist" => await Stocks(secret, account, false),
                "DeepSeek Balance" => await DeepSeek(secret),
                "Polar" => await Polar(secret),
                "Shopify" => await Shopify(secret, account),
                "DataFast Chart" => await DataFast(secret, account),
                "TikTok" => await TikTok(secret),
                "YouTube" => await YouTube(secret, account),
                "X Profile" => await XProfile(secret, account),
                "Instagram" => await Instagram(secret),
                "Dub" => await Dub(secret),
                "Paddle" => await Paddle(secret),
                "Dodo Payments" => await Dodo(secret),
                "Gumroad" => await Gumroad(secret),
                _ => throw new InvalidOperationException()
            };
            if (CredentialStore.Read(id) == secret && (preferences.ProviderAccounts.GetValueOrDefault(id) ?? "") == account)
            {
                Dashboards[id] = snapshot;
                Errors.Remove(id);
            }
        }
        catch (HttpRequestException ex)
        {
            Errors[id] = ex.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Access denied. Check the key, account and read permissions.",
                HttpStatusCode.TooManyRequests => "Provider rate limit reached.",
                _ => "The provider is unavailable. Try again."
            };
        }
        catch (FormatException ex) { Errors[id] = ex.Message; }
        catch (Exception ex) when (LiveData.IsDataError(ex)) { Errors[id] = "The provider did not return the expected data."; }
        finally { inFlight.Remove(id); }
    }

    public void Disconnect(string id)
    {
        CredentialStore.Delete(id);
        Dashboards.Remove(id);
        Dashboards.Remove("Total Revenue");
        Errors.Remove(id);
        attempts.Remove(id);
    }

    private static async Task<JsonDocument> Get(string url, string secret, object? body = null, bool rawKey = false, string? keyHeader = null)
    {
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, url);
        request.Headers.UserAgent.ParseAdd("FlowBar/2.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (keyHeader is not null) request.Headers.Add(keyHeader, secret);
        else if (rawKey) request.Headers.TryAddWithoutValidation("Authorization", secret);
        else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static async Task<Snapshot> Gmail(string secret)
    {
        using var data = await Get("https://gmail.googleapis.com/gmail/v1/users/me/labels/INBOX", secret);
        var inbox = data.RootElement;
        int count = inbox.GetProperty("messagesUnread").GetInt32();
        return new($"{count:N0} unread", "Inbox", []);
    }

    private static async Task<Snapshot> SearchConsole(string secret, string site)
    {
        if (string.IsNullOrWhiteSpace(site)) throw new FormatException("Enter a property URL or sc-domain property.");
        var date = DateTime.UtcNow.Date.AddDays(-3);
        var body = new { startDate = $"{date.AddDays(-29):yyyy-MM-dd}", endDate = $"{date:yyyy-MM-dd}", dimensions = new[] { "query" }, rowLimit = 25 };
        using var data = await Get($"https://www.googleapis.com/webmasters/v3/sites/{Uri.EscapeDataString(site)}/searchAnalytics/query", secret, body);
        if (!data.RootElement.TryGetProperty("rows", out var rows)) return new("0 clicks", "No query rows · 30 days", []);
        var values = rows.EnumerateArray().ToArray();
        var entries = values.Select(row => new Entry(row.GetProperty("keys")[0].GetString() ?? "", $"{row.GetProperty("clicks").GetDouble():N0} clicks")).ToArray();
        return new($"{values.Sum(row => row.GetProperty("clicks").GetDouble()):N0} clicks", "Top queries · 30 days", entries);
    }

    private static async Task<Snapshot> Linear(string secret)
    {
        const string query = "{ viewer { name assignedIssues(first: 35) { nodes { identifier title url state { type name } } pageInfo { hasNextPage } } } }";
        using var data = await Get("https://api.linear.app/graphql", secret, new { query }, true);
        if (data.RootElement.TryGetProperty("errors", out _)) throw new InvalidOperationException("Linear GraphQL error");
        var account = data.RootElement.GetProperty("data").GetProperty("viewer");
        var issues = account.GetProperty("assignedIssues");
        var entries = issues.GetProperty("nodes").EnumerateArray().Where(item => String(item.GetProperty("state"), "type") is not ("completed" or "canceled"))
            .Select(row => new Entry($"{String(row, "identifier")}  {String(row, "title")}", String(row.GetProperty("state"), "name"), String(row, "url"))).ToArray();
        return new($"{entries.Length:N0} open", account.GetProperty("name").GetString() ?? "Assigned issues", entries,
            Partial: issues.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean());
    }

    private static async Task<Snapshot> Supabase(string secret)
    {
        using var data = await Get("https://api.supabase.com/v1/projects", secret);
        var root = data.RootElement;
        var rows = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("projects");
        var entries = rows.EnumerateArray().Select(row => new Entry(String(row, "name"), String(row, "status"),
            $"https://supabase.com/dashboard/project/{Uri.EscapeDataString(String(row, "id"))}")).ToArray();
        return new($"{entries.Length} projects", "Project status", entries);
    }

    private static async Task<Snapshot> Resend(string secret)
    {
        using var data = await Get("https://api.resend.com/emails?limit=100", secret);
        var rows = data.RootElement.GetProperty("data").EnumerateArray().ToArray();
        int delivered = rows.Count(row => String(row, "last_event") is "delivered" or "opened" or "clicked");
        var entries = rows.Take(12).Select(row => new Entry(String(row, "subject"), String(row, "last_event"))).ToArray();
        return new(rows.Length == 0 ? "—" : $"{delivered / (double)rows.Length:P0}",
            $"Delivered · latest {rows.Length} messages", entries);
    }

    private static async Task<Snapshot> Plausible(string secret, string site)
    {
        if (string.IsNullOrWhiteSpace(site)) throw new FormatException("Enter the Plausible site ID.");
        var payload = new { site_id = site, metrics = new[] { "visitors", "pageviews", "bounce_rate" }, date_range = "7d" };
        using var data = await Get("https://plausible.io/api/v2/query", secret, payload);
        var metrics = data.RootElement.GetProperty("results")[0].GetProperty("metrics");
        return new($"{metrics[0].GetDouble():N0} visitors", $"{site} · last 7 days",
            [new("Pageviews", $"{metrics[1].GetDouble():N0}"), new("Bounce rate", $"{metrics[2].GetDouble():N1}%")]);
    }

    private static async Task<Snapshot> Stripe(string secret)
    {
        var day = DateTimeOffset.UtcNow.AddDays(-30);
        var lines = new List<Line>();
        string cursor = "";
        bool more = true;
        for (int page = 0; page < 5 && more; page++)
        {
            using var data = await Get($"https://api.stripe.com/v1/charges?limit=100&created%5Bgte%5D={day.ToUnixTimeSeconds()}{cursor}", secret);
            var rows = data.RootElement.GetProperty("data").EnumerateArray().ToArray();
            foreach (var row in rows.Where(row => row.GetProperty("paid").GetBoolean() && row.GetProperty("captured").GetBoolean()))
            {
                string currency = String(row, "currency").ToUpperInvariant();
                decimal amount = (row.GetProperty("amount_captured").GetDecimal() - row.GetProperty("amount_refunded").GetDecimal()) / Unit(currency);
                lines.Add(new(DateTimeOffset.FromUnixTimeSeconds(row.GetProperty("created").GetInt64()).LocalDateTime, amount, currency));
            }
            more = rows.Length > 0 && data.RootElement.GetProperty("has_more").GetBoolean();
            if (more) cursor = "&starting_after=" + Uri.EscapeDataString(String(rows[^1], "id"));
        }
        return Revenue("Stripe", lines.ToArray(), more);
    }

    private static async Task<Snapshot> Lemon(string secret, string store)
    {
        if (store.Length > 0 && !store.All(char.IsAsciiDigit)) throw new FormatException("Store ID must use digits only.");
        var threshold = DateTimeOffset.UtcNow.AddDays(-30);
        var lines = new List<Line>();
        var prefix = "https://api.lemonsqueezy.com/v1/orders?page%5Bsize%5D=100&";
        if (store.Length > 0) prefix += "filter%5Bstore_id%5D=" + store + "&";
        bool more = true;
        for (int page = 1; page <= 5 && more; page++)
        {
            using var data = await Get($"{prefix}page%5Bnumber%5D={page}", secret);
            var rows = data.RootElement.GetProperty("data").EnumerateArray().Select(row => row.GetProperty("attributes")).ToArray();
            more = rows.Length == 100;
            foreach (var row in rows)
            {
                var created = row.GetProperty("created_at").GetDateTimeOffset();
                if (created < threshold) { more = false; break; }
                string status = String(row, "status");
                if (status is not ("paid" or "partial_refund")) continue;
                string currency = String(row, "currency").ToUpperInvariant();
                decimal amount = row.GetProperty("total").GetDecimal();
                if (row.TryGetProperty("refunded_amount", out var refunded) && refunded.ValueKind == JsonValueKind.Number)
                    amount -= refunded.GetDecimal();
                lines.Add(new(created.LocalDateTime, amount / 100, currency));
            }
        }
        return Revenue("Lemon Squeezy", lines.ToArray(), more);
    }

    private static async Task<Snapshot> Stocks(string token, string account, bool history)
    {
        var symbols = account.ToUpperInvariant().Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Distinct().Take(12).ToArray();
        if (symbols.Length == 0 || symbols.Any(symbol => symbol.Length > 12 || symbol.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-'))))
            throw new FormatException("Enter a stock symbol such as MSFT.");
        if (history && symbols.Length != 1) throw new FormatException("Enter one symbol for Stock Chart.");
        var entries = new List<Entry>();
        foreach (var symbol in symbols)
        {
            using var data = await Get($"https://finnhub.io/api/v1/quote?symbol={Uri.EscapeDataString(symbol)}", token, keyHeader: "X-Finnhub-Token");
            var quote = data.RootElement;
            var price = quote.GetProperty("c").GetDecimal();
            var change = quote.GetProperty("dp");
            string delta = change.ValueKind == JsonValueKind.Number ? $"{change.GetDouble():+0.00;-0.00;0.00}%" : "—";
            entries.Add(new(symbol, price > 0 ? $"{price:N2}  ·  {delta}" : "No quote"));
        }
        var snapshot = new Snapshot(entries[0].Detail, "Latest provider quote", entries.ToArray());
        if (!history) return snapshot;

        try
        {
            var end = DateTimeOffset.UtcNow;
            long from = end.AddDays(-94).ToUnixTimeSeconds();
            long to = end.ToUnixTimeSeconds();
            using var candles = await Get($"https://finnhub.io/api/v1/stock/candle?symbol={Uri.EscapeDataString(symbols[0])}&resolution=D&from={from}&to={to}",
                token, keyHeader: "X-Finnhub-Token");
            var prices = ParseStockHistory(candles.RootElement);
            return snapshot with { Prices = prices, PriceStatus = prices.Length < 2 ? "Not enough daily closes from Finnhub." : null };
        }
        catch (HttpRequestException ex)
        {
            return snapshot with { PriceStatus = ex.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Finnhub denied access to historical prices for this key.",
                HttpStatusCode.TooManyRequests => "Finnhub history rate limit reached.",
                _ => "Historical prices are unavailable."
            }};
        }
        catch (Exception ex) when (LiveData.IsDataError(ex))
        {
            return snapshot with { PriceStatus = "Historical prices are unavailable." };
        }
    }

    internal static StockPoint[] ParseStockHistory(JsonElement response)
    {
        string status = String(response, "s");
        if (status == "no_data") return [];
        if (status != "ok") throw new FormatException("Unexpected stock history status.");
        var times = response.GetProperty("t");
        var prices = response.GetProperty("c");
        if (times.GetArrayLength() != prices.GetArrayLength()) throw new FormatException("Stock history arrays differ in length.");
        var byDate = new SortedDictionary<DateTimeOffset, decimal>();
        for (int index = 0; index < times.GetArrayLength(); index++)
        {
            if (times[index].ValueKind != JsonValueKind.Number || !times[index].TryGetInt64(out long timestamp)
                || timestamp is <= 0 or > 253402300799 || prices[index].ValueKind != JsonValueKind.Number
                || !prices[index].TryGetDecimal(out decimal price) || price <= 0) continue;
            byDate[DateTimeOffset.FromUnixTimeSeconds(timestamp)] = price;
        }
        return byDate.Select(entry => new StockPoint(entry.Key, entry.Value)).ToArray();
    }

    private static async Task<Snapshot> DeepSeek(string token)
    {
        using var data = await Get("https://api.deepseek.com/user/balance", token);
        var root = data.RootElement;
        var entries = root.GetProperty("balance_infos").EnumerateArray().Select(row => new Entry(String(row, "currency"), String(row, "total_balance"))).ToArray();
        return new(entries.Length == 0 ? "—" : $"{entries[0].Detail} {entries[0].Title}",
            root.GetProperty("is_available").GetBoolean() ? "Available" : "Unavailable", entries);
    }

    private static Snapshot Revenue(string name, Line[] lines, bool partial)
    {
        var rows = Totals(lines);
        return new(rows.Length switch { 0 => "No sales", 1 => rows[0].Detail, _ => $"{rows.Length} currencies" },
            $"{name} · last 30 days", rows, lines, partial);
    }

    private static Entry[] Totals(Line[] lines) => lines.GroupBy(line => line.Currency).OrderBy(group => group.Key)
        .Select(group => new Entry(group.Key, $"{group.Sum(line => line.Amount):N2} {group.Key}")).ToArray();

    private static decimal Unit(string currency) => currency switch
    {
        "JPY" or "KRW" or "VND" or "CLP" or "XAF" or "XOF" or "XPF" or "BIF" or "DJF" or "GNF" or "KMF" or "MGA" or "PYG" or "RWF" or "UGX" or "VUV" => 1,
        "BHD" or "JOD" or "KWD" or "OMR" or "TND" => 1000,
        _ => 100
    };

    private static string String(JsonElement item, string key) =>
        item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
