using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;

namespace RadioV2.Helpers;

public static class GroupImageHelper
{
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp"];

    // Null sentinel so we don't re-probe group names that have no image.
    private static readonly ConcurrentDictionary<string, BitmapImage?> _cache = new();

    /// <summary>
    /// Resolves a group name to a bundled image resource, or null if none exists.
    /// Result is cached and frozen for cross-thread use.
    /// </summary>
    public static BitmapImage? GetImage(string groupName)
        => _cache.GetOrAdd(Sanitize(groupName), LoadImageByKey);

    private static BitmapImage? LoadImageByKey(string key)
    {
        foreach (string ext in Extensions)
        {
            try
            {
                var info = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/Groups/{key}{ext}"));
                if (info is null) continue;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = info.Stream;
                bmp.CacheOption = BitmapCacheOption.OnLoad; // decode now, not at render time
                bmp.EndInit();
                info.Stream.Dispose();
                bmp.Freeze(); // make cross-thread safe for the cache
                return bmp;
            }
            catch { }
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
