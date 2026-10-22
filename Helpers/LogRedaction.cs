using System.Text.RegularExpressions;

namespace RadioV2.Helpers;

/// <summary>
/// Strips credentials out of anything written to the log.
///
/// The log exists to be shared when something breaks, so it must never carry stream
/// credentials. Aggregated station lists do contain URLs with embedded userinfo
/// (<c>http://user:pass@host/stream</c>) and token query strings, and VLC's own error
/// messages echo the full URL back verbatim.
///
/// <para>
/// <see cref="Scrub"/> rewrites whole URL tokens through <see cref="Url"/> rather than
/// pattern-matching credentials itself. One redaction path means there is no second
/// parser to disagree with the first — the class of bug where a string is safe
/// according to one and leaky according to the other.
/// </para>
/// </summary>
public static partial class LogRedaction
{
    /// <summary>
    /// Schemes a radio stream can legitimately use. Anything else is treated as
    /// unparseable, because <see cref="Uri.TryCreate(string, UriKind, out Uri)"/>
    /// happily accepts <c>user:pass@host/path</c> as a URI with scheme "user" — which
    /// would otherwise be echoed back with the password intact.
    /// </summary>
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "rtsp", "rtmp", "rtmps", "mms", "mmsh", "mmst", "icyx", "rtp", "udp"
    };

    [GeneratedRegex(@"[a-zA-Z][a-zA-Z0-9+.\-]*://[^\s""'<>]+", RegexOptions.Compiled)]
    private static partial Regex UrlTokenPattern();

    // "user:pass@" at the start of a token. The password class allows ':' so that
    // user:pa:ss@host is caught too; the lookbehind stops it matching "http:" in
    // "http://" because '/' is excluded from both classes.
    [GeneratedRegex(@"(?<![^\s/])[^\s/@]+:[^\s/@]*@", RegexOptions.Compiled)]
    private static partial Regex UserInfoPattern();

    // Two complementary patterns, because either alone leaves a gap.
    //
    // Name-based, matched anywhere with no delimiter required, so a bare
    // "password=x" or "http-password=x" in a VLC config dump is caught. The
    // surrounding [\w.-]* makes it tolerant of prefixes and suffixes, so session_id
    // and api_key_v2 match too.
    [GeneratedRegex(@"(?i)([\w.\-]*(?:token|key|auth|sig|signature|password|passwd|pwd|secret|session|sid)[\w.\-]*)=[^\s&""'<>]*", RegexOptions.Compiled)]
    private static partial Regex SecretNamePattern();

    // Generic and fail-closed: the value of every parameter after a ? & # or ;
    // delimiter, whatever its name. Names are kept because they are the useful half
    // for diagnosis.
    [GeneratedRegex(@"([?&#;][^\s=&""'<>]+)=[^\s&""'<>]*", RegexOptions.Compiled)]
    private static partial Regex QueryValuePattern();

    /// <summary>
    /// Scheme, host, port and path only — no userinfo, query or fragment. Returns
    /// <c>(unparseable url)</c> rather than guessing when the input is not a URL in a
    /// scheme we recognise.
    /// </summary>
    public static string Url(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "(none)";

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || !AllowedSchemes.Contains(uri.Scheme)
            || string.IsNullOrEmpty(uri.Host)
            || uri.HostNameType == UriHostNameType.Unknown)
        {
            return "(unparseable url)";
        }

        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";

        // Defence in depth: a path should never contain '@'. If it does, the input was
        // shaped oddly enough that we stop trusting our own parse of it.
        var path = uri.AbsolutePath.Contains('@') ? "/***" : uri.AbsolutePath;

        return $"{uri.Scheme}://{uri.Host}{port}{path}";
    }

    /// <summary>
    /// Scrubs credentials out of free-form text such as a VLC log line.
    /// </summary>
    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // Whole URLs first, through the single redaction path.
        var scrubbed = UrlTokenPattern().Replace(text, m => Url(m.Value));

        // Then credential pairs and query values in whatever was not part of a
        // recognisable URL token. These run after, as a backstop for text the token
        // pattern did not consume.
        scrubbed = UserInfoPattern().Replace(scrubbed, "***@");
        scrubbed = SecretNamePattern().Replace(scrubbed, "$1=***");
        scrubbed = QueryValuePattern().Replace(scrubbed, "$1=***");

        return scrubbed;
    }
}
