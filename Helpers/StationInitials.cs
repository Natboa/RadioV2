using System.Globalization;

namespace RadioV2.Helpers;

public static class StationInitials
{
    public static string For(string? name)
    {
        var words = (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w is not ("&" or "|" or "-")).Take(2).ToArray();
        if (words.Length == 0) return "?";
        static string First(string word) => StringInfo.GetNextTextElement(word);
        var text = words.Length > 1 ? First(words[0]) + First(words[1]) :
            string.Concat(TextElements(words[0]).Take(2));
        return text.ToUpperInvariant();
    }

    private static IEnumerable<string> TextElements(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) yield return enumerator.GetTextElement();
    }
}
