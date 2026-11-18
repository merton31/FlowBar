using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace LumaDock;

internal sealed class BrowserSites
{
    private static readonly HashSet<string> browsers = new(StringComparer.OrdinalIgnoreCase)
        { "chrome", "msedge", "firefox", "brave", "vivaldi", "opera", "chromium", "zen" };
    private static readonly HashSet<string> addressIds = new(StringComparer.OrdinalIgnoreCase)
        { "omnibox", "addressEditBox", "urlbar-input", "urlbar", "address-bar", "addressBar" };
    private static readonly string[] addressNames = ["address and search bar", "address bar", "search or enter address",
        "search with google or enter address", "адресная строка", "поиск или ввод адреса", "barre d’adresse", "barre d'adresse"];
    private static readonly string[] privateNames = ["incognito", "inprivate", "private browsing", "private window",
        "режим инкогнито", "приватное окно", "приватный просмотр", "navigation privée"];

    private nint foreground;
    private nint observed;
    private string? domain;
    private DateTimeOffset checkedAt;
    private bool busy;
    private int serial;

    public string? Observe(nint handle, string? application, DateTimeOffset now)
    {
        nint candidate = application is not null && browsers.Contains(application) ? handle : 0;
        if (candidate != foreground)
        {
            foreground = candidate;
            observed = 0;
            domain = null;
            serial++;
        }
        if (candidate == 0) return null;
        if (!busy && (observed != candidate || now - checkedAt >= TimeSpan.FromSeconds(2)))
        {
            busy = true;
            _ = ObserveAsync(candidate, serial);
        }
        return observed == candidate && now - checkedAt <= TimeSpan.FromSeconds(5) ? domain : null;
    }

    public void Reset()
    {
        foreground = observed = 0;
        domain = null;
        serial++;
    }

    private async Task ObserveAsync(nint handle, int version)
    {
        try
        {
            string? value = await Task.Run(() => Read(handle));
            if (version != serial) return;
            observed = handle;
            checkedAt = DateTimeOffset.Now;
            domain = value;
        }
        finally { busy = false; }
    }

    internal static string? Read(nint handle)
    {
        try
        {
            var root = AutomationElement.FromHandle(handle);
            if (IsPrivate(root.Current.Name)) return null;
            var walker = TreeWalker.ControlViewWalker;
            var queue = new Queue<AutomationElement>();
            queue.Enqueue(root);
            string? address = null;
            for (int inspected = 0; queue.TryDequeue(out var item) && inspected < 180; inspected++)
            {
                var details = item.Current;
                if (details.ControlType == ControlType.Document) continue;
                if (details.ControlType == ControlType.Button && IsPrivate(details.Name)) return null;
                if (details.ControlType == ControlType.Edit && !details.HasKeyboardFocus &&
                    (addressIds.Contains(details.AutomationId) ||
                     addressNames.Contains(details.Name, StringComparer.OrdinalIgnoreCase)) &&
                    item.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
                    address ??= Domain(((ValuePattern)pattern).Current.Value);
                for (var child = walker.GetFirstChild(item); child is not null && queue.Count < 180; child = walker.GetNextSibling(child))
                    queue.Enqueue(child);
            }
            return address;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException
            or ArgumentException or UnauthorizedAccessException) { return null; }
    }

    private static bool IsPrivate(string name) =>
        privateNames.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));

    internal static string? Domain(string? value)
    {
        string text = value?.Trim() ?? "";
        if (text.Length is 0 or > 4096 || text.Any(char.IsWhiteSpace)) return null;
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https")
            || address.HostNameType != UriHostNameType.Dns || !string.IsNullOrEmpty(address.UserInfo)) return null;
        string host = address.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (!host.Contains('.') || host.EndsWith(".localhost", StringComparison.Ordinal) || host.EndsWith(".local", StringComparison.Ordinal)) return null;
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
}
