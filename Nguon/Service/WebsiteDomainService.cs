using System.Globalization;
using System.Net;
namespace PersonalInfoManager.Services;
public static class WebsiteDomainService
{
    private static readonly string[] PopularDomains =
    {
        "google.com", "microsoft.com", "apple.com", "facebook.com", "instagram.com",
        "amazon.com", "paypal.com", "github.com", "linkedin.com", "netflix.com",
        "yahoo.com", "discord.com", "dropbox.com", "steamcommunity.com", "steampowered.com"
    };
    public static bool TryNormalizeUrl(string value, out Uri? uri, out string host)
    {
        uri = null;
        host = "";
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp) ||
            string.IsNullOrWhiteSpace(parsed.Host) ||
            !string.IsNullOrEmpty(parsed.UserInfo))
            return false;
        try
        {
            string rawHost = parsed.DnsSafeHost.TrimEnd('.');
            host = IPAddress.TryParse(rawHost, out var address)
                ? address.ToString().ToLowerInvariant()
                : new IdnMapping().GetAscii(rawHost).ToLowerInvariant();
            if (host.Length == 0) return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        try
        {
            uri = new UriBuilder(parsed) { Host = host }.Uri;
        }
        catch (UriFormatException)
        {
            host = "";
            return false;
        }
        return true;
    }
    public static bool IsExactHostMatch(string expectedUrl, string currentUrl)
    {
        return TryNormalizeUrl(expectedUrl, out _, out string expectedHost) &&
               TryNormalizeUrl(currentUrl, out _, out string currentHost) &&
               string.Equals(expectedHost, currentHost, StringComparison.OrdinalIgnoreCase);
    }
    public static bool IsExactWebOriginMatch(string expectedUrl, string currentUrl)
    {
        return TryNormalizeUrl(expectedUrl, out var expected, out string expectedHost) &&
               TryNormalizeUrl(currentUrl, out var current, out string currentHost) &&
               expected != null && current != null &&
               string.Equals(expected.Scheme, current.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(expectedHost, currentHost, StringComparison.OrdinalIgnoreCase) &&
               expected.Port == current.Port;
    }
    public static bool TryGetHost(string value, out string host)
    {
        return TryNormalizeUrl(value, out _, out host);
    }
    public static IReadOnlyList<string> GetRiskWarnings(string value)
    {
        var warnings = new List<string>();
        if (!TryNormalizeUrl(value, out Uri? uri, out string host) || uri == null) return warnings;
        if (host.Contains("xn--", StringComparison.OrdinalIgnoreCase))
            warnings.Add("Tên miền chứa ký hiệu Punycode.");
        string displayHost = GetUnicodeHost(value);
        if (HasMixedScripts(displayHost))
            warnings.Add("Tên miền trộn nhiều bảng chữ cái.");
        if (IPAddress.TryParse(host, out _))
            warnings.Add("Website dùng địa chỉ IP trực tiếp.");
        if (uri.Scheme == Uri.UriSchemeHttp && !IsLocalHost(host))
            warnings.Add("Website không dùng HTTPS.");
        if (!host.Contains("xn--", StringComparison.OrdinalIgnoreCase) && !HasMixedScripts(displayHost))
        {
            string candidate = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
            string[] labels = candidate.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (labels.Length > 2)
                candidate = string.Join('.', labels[^2..]);
            string? similar = PopularDomains.FirstOrDefault(domain =>
                !IsHostOrSubdomain(host, domain) && IsLikelyLookalike(candidate, domain));
            similar ??= PopularDomains.FirstOrDefault(domain =>
                !IsHostOrSubdomain(host, domain) && ContainsLookalikeBrandLabel(host, domain));
            if (similar != null)
                warnings.Add($"Tên miền gần giống {similar}.");
        }
        return warnings.Distinct(StringComparer.Ordinal).ToList();
    }
    private static string GetUnicodeHost(string value)
    {
        try
        {
            var parsed = new Uri(value.Trim(), UriKind.Absolute);
            return parsed.GetComponents(UriComponents.Host, UriFormat.Unescaped).TrimEnd('.');
        }
        catch { return ""; }
    }
    private static bool HasMixedScripts(string host)
    {
        bool latin = false;
        bool cyrillic = false;
        bool greek = false;
        foreach (char character in host)
        {
            latin |= character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '\u00C0' and <= '\u024F';
            cyrillic |= character is >= '\u0400' and <= '\u052F';
            greek |= character is >= '\u0370' and <= '\u03FF';
        }
        int scripts = (latin ? 1 : 0) + (cyrillic ? 1 : 0) + (greek ? 1 : 0);
        return scripts > 1;
    }
    private static bool IsLocalHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    private static bool IsHostOrSubdomain(string host, string known) =>
        string.Equals(host, known, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + known, StringComparison.OrdinalIgnoreCase);
    private static bool IsLikelyLookalike(string host, string known)
    {
        int threshold = known.Length >= 12 ? 2 : 1;
        return host.Length == known.Length && EditDistance(host, known) <= threshold ||
               host.EndsWith(".com", StringComparison.OrdinalIgnoreCase) &&
               known.EndsWith(".com", StringComparison.OrdinalIgnoreCase) &&
               host.Length == known.Length && EditDistance(host[..^4], known[..^4]) <= threshold;
    }
    private static bool ContainsLookalikeBrandLabel(string host, string known)
    {
        string brand = known[..known.LastIndexOf('.')];
        string[] labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return labels.Take(Math.Max(0, labels.Length - 1))
            .Any(label => IsLikelyLookalike(label + ".com", brand + ".com"));
    }
    private static int EditDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= right.Length; j++)
            {
                int substitution = previous[j - 1] + (char.ToLowerInvariant(left[i - 1]) == char.ToLowerInvariant(right[j - 1]) ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }
}
