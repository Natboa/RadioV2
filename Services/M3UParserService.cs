using System.IO;
using System.Text.RegularExpressions;

namespace RadioV2.Services;

public class ParsedStation
{
    public string Name { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string GroupName { get; set; } = "Uncategorized";
}

public class M3UParserService
{
    private static readonly Regex LogoRegex = new(@"tvg-logo=""([^""]*)""", RegexOptions.Compiled);
    private static readonly Regex GroupRegex = new(@"group-title=""([^""]*)""", RegexOptions.Compiled);

    public List<ParsedStation> Parse(string filePath)
    {
        var results = new List<ParsedStation>();
        var lines = File.ReadAllLines(filePath);

        ParsedStation? pending = null;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
                continue;

            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                pending = new ParsedStation();

                var logoMatch = LogoRegex.Match(line);
                if (logoMatch.Success && !string.IsNullOrWhiteSpace(logoMatch.Groups[1].Value))
                    pending.LogoUrl = logoMatch.Groups[1].Value;

                var groupMatch = GroupRegex.Match(line);
                pending.GroupName = groupMatch.Success && !string.IsNullOrWhiteSpace(groupMatch.Groups[1].Value)
                    ? groupMatch.Groups[1].Value
                    : "Uncategorized";

                // Station name is after the last comma on the #EXTINF line
                var commaIdx = line.LastIndexOf(',');
                pending.Name = commaIdx >= 0 && commaIdx < line.Length - 1
                    ? line[(commaIdx + 1)..].Trim()
                    : string.Empty;
            }
            else if (!line.StartsWith('#') && pending != null)
            {
                pending.StreamUrl = line;
                if (!string.IsNullOrEmpty(pending.StreamUrl))
                    results.Add(pending);
                pending = null;
            }
        }

        return results;
    }
}
