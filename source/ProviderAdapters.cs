using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LumaDock;

internal sealed partial class ConnectedData
{
    private static async Task<Snapshot> Polar(string secret)
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var lines = new List<Line>();
        bool more = true;
        for (int page = 1; page <= 5 && more; page++)
        {
            using var response = await Get($"https://api.polar.sh/v1/orders/?limit=100&page={page}&sorting=-created_at", secret);
            var rows = response.RootElement.GetProperty("items").EnumerateArray().ToArray();
            more = rows.Length == 100;
            foreach (var row in rows)
            {
                DateTimeOffset date = row.GetProperty("created_at").GetDateTimeOffset();
                if (date < cutoff) { more = false; break; }
                if (!row.GetProperty("paid").GetBoolean()) continue;
                string currency = String(row, "currency").ToUpperInvariant();
                decimal net = Number(row, "total_amount") - Number(row, "refunded_amount");
                lines.Add(new(date.LocalDateTime, net / Unit(currency), currency));
            }
        }
        return Revenue("Polar", lines.ToArray(), more);
    }

    private static async Task<Snapshot> Shopify(string secret, string input)
    {
        string host = input.Trim().ToLowerInvariant();
        if (!host.EndsWith(".myshopify.com", StringComparison.Ordinal)) host += ".myshopify.com";
        if (!Regex.IsMatch(host, @"\A[a-z0-9][a-z0-9-]{0,62}\.myshopify\.com\z"))
            throw new FormatException("Enter a Shopify store name or its myshopify.com host.");
        DateTime cutoff = DateTime.UtcNow.AddDays(-30);
        const string query = """
            query Sales($filter: String!, $cursor: String) {
              orders(first: 100, after: $cursor, sortKey: CREATED_AT, reverse: true, query: $filter) {
                nodes {
                  createdAt
                  totalReceivedSet { shopMoney { amount currencyCode } }
                  totalRefundedSet { shopMoney { amount currencyCode } }
                }
                pageInfo { hasNextPage endCursor }
              }
            }
            """;
        var lines = new List<Line>();
        bool more = true;
        string? cursor = null;
        for (int page = 0; page < 5 && more; page++)
        {
            using var response = await Get($"https://{host}/admin/api/2026-07/graphql.json", secret,
                new { query, variables = new { filter = $"created_at:>={cutoff:yyyy-MM-dd}", cursor } }, keyHeader: "X-Shopify-Access-Token");
            if (response.RootElement.TryGetProperty("errors", out _)) throw new InvalidOperationException("Shopify GraphQL error");
            var orders = response.RootElement.GetProperty("data").GetProperty("orders");
            foreach (var order in orders.GetProperty("nodes").EnumerateArray())
            {
                var received = order.GetProperty("totalReceivedSet").GetProperty("shopMoney");
                var refund = order.GetProperty("totalRefundedSet").GetProperty("shopMoney");
                if (Number(received, "amount") == 0) continue;
                string currency = String(received, "currencyCode");
                var at = order.GetProperty("createdAt").GetDateTimeOffset().LocalDateTime;
                lines.Add(new(at, Number(received, "amount") - Number(refund, "amount"), currency));
            }
            var info = orders.GetProperty("pageInfo");
            more = info.GetProperty("hasNextPage").GetBoolean();
            cursor = String(info, "endCursor");
        }
        return Revenue("Shopify", lines.ToArray(), more);
    }

    private static async Task<Snapshot> DataFast(string secret, string account)
    {
        var end = DateTime.UtcNow.Date;
        string query = account.Length > 0 ? "&websiteId=" + Uri.EscapeDataString(account) : "";
        using var response = await Get($"https://datafa.st/api/v1/analytics/overview?startAt={end.AddDays(-6):yyyy-MM-dd}&endAt={end:yyyy-MM-dd}{query}", secret);
        var data = response.RootElement;
        if (data.TryGetProperty("data", out var wrapped)) data = wrapped;
        if (data.TryGetProperty("metrics", out var metrics)) data = metrics;
        if (!data.TryGetProperty("visitors", out _)) throw new InvalidOperationException("No metrics returned");
        return new($"{Number(data, "visitors"):N0} visitors", "Last seven days",
            [new("Pageviews", $"{Number(data, "pageviews"):N0}"), new("Sessions", $"{Number(data, "sessions"):N0}"),
             new("Payments", $"{Number(data, "payments"):N0}")]);
    }

    private static async Task<Snapshot> TikTok(string secret)
    {
        using var response = await Get("https://open.tiktokapis.com/v2/user/info/?fields=display_name,follower_count,likes_count,video_count", secret);
        var profile = response.RootElement.GetProperty("data").GetProperty("user");
        if (!profile.TryGetProperty("follower_count", out _)) throw new InvalidOperationException("Statistics not authorized");
        return new($"{Number(profile, "follower_count"):N0} followers", String(profile, "display_name"),
            [new("Likes", $"{Number(profile, "likes_count"):N0}"), new("Videos", $"{Number(profile, "video_count"):N0}")]);
    }

    private static async Task<Snapshot> YouTube(string secret, string account)
    {
        if (account.Length is < 2 or > 125) throw new FormatException("Enter a YouTube channel ID or @handle.");
        string filter = account.StartsWith('@') ? "forHandle" : "id";
        using var response = await Get($"https://www.googleapis.com/youtube/v3/channels?part=snippet,statistics&{filter}={Uri.EscapeDataString(account)}",
            secret, keyHeader: "X-Goog-Api-Key");
        var channels = response.RootElement.GetProperty("items");
        if (channels.GetArrayLength() == 0) throw new FormatException("No channel was found.");
        var channel = channels[0];
        var profile = channel.GetProperty("statistics");
        bool hidden = profile.TryGetProperty("hiddenSubscriberCount", out var flag) && flag.GetBoolean();
        return new(hidden ? "Hidden" : $"{Number(profile, "subscriberCount"):N0} subscribers", String(channel.GetProperty("snippet"), "title"),
            [new("Views", $"{Number(profile, "viewCount"):N0}"), new("Videos", $"{Number(profile, "videoCount"):N0}")]);
    }

    private static async Task<Snapshot> XProfile(string token, string account)
    {
        string username = account.TrimStart('@');
        if (!Regex.IsMatch(username, @"\A[a-zA-Z0-9_]{1,15}\z")) throw new FormatException("Enter an X username.");
        using var response = await Get($"https://api.x.com/2/users/by/username/{username}?user.fields=public_metrics", token);
        var profile = response.RootElement.GetProperty("data").GetProperty("public_metrics");
        return new($"{Number(profile, "followers_count"):N0} followers", "@" + username,
            [new("Following", $"{Number(profile, "following_count"):N0}"), new("Posts", $"{Number(profile, "tweet_count"):N0}")]);
    }

    private static async Task<Snapshot> Instagram(string token)
    {
        using var response = await Get("https://graph.instagram.com/v24.0/me?fields=username,followers_count,follows_count,media_count", token);
        var profile = response.RootElement;
        if (!profile.TryGetProperty("followers_count", out _))
            throw new FormatException("The token must grant access to an Instagram professional account.");
        return new($"{Number(profile, "followers_count"):N0} followers", "@" + String(profile, "username"),
            [new("Following", $"{Number(profile, "follows_count"):N0}"), new("Posts", $"{Number(profile, "media_count"):N0}")]);
    }

    private static async Task<Snapshot> Dub(string token)
    {
        using var response = await Get("https://api.dub.co/analytics?interval=7d&groupBy=count", token);
        var summary = response.RootElement;
        return new($"{Number(summary, "clicks"):N0} clicks", "Last seven days",
            [new("Leads", $"{Number(summary, "leads"):N0}"), new("Sales", $"{Number(summary, "sales"):N0}")]);
    }

    private static async Task<Snapshot> Paddle(string token)
    {
        string date = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-30).ToString("O", CultureInfo.InvariantCulture));
        string url = $"https://api.paddle.com/transactions?status=completed&created_at%5BGTE%5D={date}&per_page=30";
        string cursor = "";
        bool more = true;
        var lines = new List<Line>();
        for (int page = 0; page < 5 && more; page++)
        {
            using var response = await Get(url + cursor, token);
            var root = response.RootElement;
            var rows = root.GetProperty("data").EnumerateArray().ToArray();
            foreach (var row in rows)
            {
                var details = row.GetProperty("details");
                var totals = details.TryGetProperty("adjusted_totals", out var adjusted) && adjusted.ValueKind == JsonValueKind.Object
                    ? adjusted : details.GetProperty("totals");
                string currency = String(row, "currency_code");
                lines.Add(new(row.GetProperty("created_at").GetDateTimeOffset().LocalDateTime, Number(totals, "grand_total") / Unit(currency), currency));
            }
            var pagination = root.GetProperty("meta").GetProperty("pagination");
            more = rows.Length > 0 && pagination.GetProperty("has_more").GetBoolean();
            if (more) cursor = "&after=" + Uri.EscapeDataString(String(rows[^1], "id"));
        }
        return Revenue("Paddle", lines.ToArray(), more);
    }

    private static async Task<Snapshot> Dodo(string token)
    {
        DateTimeOffset threshold = DateTimeOffset.UtcNow.AddDays(-30);
        var lines = new List<Line>();
        bool more = true;
        for (int page = 0; page < 5 && more; page++)
        {
            using var response = await Get($"https://live.dodopayments.com/payments?status=succeeded&page_number={page}&page_size=100&created_at_gte={Uri.EscapeDataString(threshold.ToString("O"))}", token);
            var rows = response.RootElement.GetProperty("items").EnumerateArray().ToArray();
            foreach (var row in rows)
            {
                var at = row.GetProperty("created_at").GetDateTimeOffset();
                if (at < threshold || String(row, "refund_status") is "refunded" or "fully_refunded") continue;
                string currency = String(row, "currency");
                lines.Add(new(at.LocalDateTime, Number(row, "total_amount") / Unit(currency), currency));
            }
            more = rows.Length == 100;
        }
        return Revenue("Dodo Payments", lines.ToArray(), more);
    }

    private static async Task<Snapshot> Gumroad(string token)
    {
        var threshold = DateTimeOffset.UtcNow.AddDays(-30);
        var lines = new List<Line>();
        string first = $"https://api.gumroad.com/v2/sales?after={threshold:yyyy-MM-dd}";
        string url = first;
        bool more = true;
        for (int page = 0; page < 5 && more; page++)
        {
            using var response = await Get(url, token);
            var root = response.RootElement;
            if (!root.GetProperty("success").GetBoolean()) throw new FormatException("Gumroad did not allow access to sales.");
            foreach (var row in root.GetProperty("sales").EnumerateArray())
            {
                var created = row.GetProperty("created_at").GetDateTimeOffset();
                if (created < threshold || True(row, "refunded") || True(row, "chargebacked")) continue;
                string currency = String(row, "currency").ToUpperInvariant();
                lines.Add(new(created.LocalDateTime, Number(row, "price") / Unit(currency), currency));
            }
            string next = String(root, "next_page_url");
            more = Uri.TryCreate(new Uri(first), next, out var location) && next.Length > 0 && location.Scheme == "https"
                && location.Host == "api.gumroad.com" && location.AbsolutePath == "/v2/sales";
            if (more) url = location!.AbsoluteUri;
        }
        return Revenue("Gumroad", lines.ToArray(), more);
    }

    private static bool True(JsonElement item, string name) => item.TryGetProperty(name, out var flag) && flag.ValueKind == JsonValueKind.True;

    private static decimal Number(JsonElement item, string key)
    {
        if (!item.TryGetProperty(key, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var parsed)) return parsed;
        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Number,
            CultureInfo.InvariantCulture, out parsed)) return parsed;
        return 0;
    }
}
