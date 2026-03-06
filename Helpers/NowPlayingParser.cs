namespace RadioV2.Helpers;

public static class NowPlayingParser
{
    private static readonly string[] Separators = [" - ", " \u2013 ", " \u2014 "];

    public static (string? Artist, string? Title) Parse(string? rawStreamTitle)
    {
        if (string.IsNullOrWhiteSpace(rawStreamTitle))
            return (null, null);

        var trimmed = rawStreamTitle.Trim();

        foreach (var sep in Separators)
        {
            var idx = trimmed.IndexOf(sep, StringComparison.Ordinal);
            if (idx > 0)
            {
                var artist = trimmed[..idx].Trim();
                var title  = trimmed[(idx + sep.Length)..].Trim();
                if (!string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(title))
                    return (artist, title);
            }
        }

        return (null, trimmed);
    }
}
