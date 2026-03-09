using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;

namespace RadioV2.Helpers;

public static class GroupImageHelper
{
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp"];

    /// <summary>
    /// Resolves a group name to a bundled image resource, or null if none exists.
    /// </summary>
    public static BitmapImage? GetImage(string groupName)
    {
        string key = Sanitize(groupName);

        foreach (string ext in Extensions)
        {
            string uriStr = $"pack://application:,,,/Assets/Groups/{key}{ext}";
            try
            {
                var info = Application.GetResourceStream(new Uri(uriStr));
                if (info is null) continue;
                info.Stream.Dispose();
                return new BitmapImage(new Uri(uriStr));
            }
            catch (IOException) { }
            catch (Exception) { }
        }

        return null;
    }

    /// <summary>
    /// Converts a group name to a lowercase underscore key used for image filenames.
    /// "Classic Rock" → "classic_rock", "Hip-Hop" → "hip_hop", "R&B" → "r_b"
    /// </summary>
    public static string Sanitize(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');

        return Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
    }
}
