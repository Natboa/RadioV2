using System.Text.RegularExpressions;

namespace RadioV2.Helpers;

/// <summary>
/// Strips credentials out of anything written to the log.
///
/// The log exists to be shared when something breaks, so it must never carry stream
/// credentials. Aggregated station lists do contain URLs with embedded userinfo
/// (<c>http://user:pass@host/stream</c>) and token query strings, and VLC's own error
/// messages echo the full URL back verbatim.
/// </summary>
public static partial class LogRedaction
{
    // Matches "user:pass@" at the start of a token — after a slash (so it catches
    // scheme://user:pass@host) or after whitespace (so it catches a bare user:pass@host
    // in an unparseable string). The lookbehind stops it matching "http:" in "http://".
    [GeneratedRegex(@"(?<![^\s/])[^\s/@:]+:[^\s/@:]+@", RegexOptions.Compiled)]
    private static partial Regex UserInfoPattern();

    [GeneratedRegex(@"\?[^\s""']*", RegexOptions.Compiled)]
    private static partial Regex QueryPattern();

    /// <summary>Scheme, host, port and path only — no userinfo, query or fragment.</summary>
    public static string Url(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "(none)";

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
            return $"{uri.Scheme}://{uri.Host}{port}{uri.AbsolutePath}";
        }

        // Unparseable — fall back to pattern scrubbing rather than logging it raw.
        return Scrub(url);
    }

    /// <summary>
    /// Scrubs credentials out of free-form text such as a VLC log line. Query strings
    /// are only removed from text that looks like it contains a URL, so ordinary
    /// prose containing a question mark is left alone.
    /// </summary>
    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var scrubbed = UserInfoPattern().Replace(text, "***@");
        if (scrubbed.Contains("://", StringComparison.Ordinal))
            scrubbed = QueryPattern().Replace(scrubbed, "?***");

        return scrubbed;
    }
}
