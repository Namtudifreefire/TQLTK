using System.Globalization;
using System.Net;

namespace PersonalInfoManager.Services;

public static class WebsiteDomainService
{
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
}
