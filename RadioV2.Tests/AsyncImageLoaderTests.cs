using System.IO;
using RadioV2.Helpers;
using Xunit;

namespace RadioV2.Tests;

/// <summary>
/// Station artwork URLs are untrusted: they come from the database, and M3U import
/// writes whatever tvg-logo the playlist contained.
/// </summary>
public class AsyncImageLoaderTests
{
    private static string AppDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RadioV2");

    [Fact]
    public void AllowsFilesInsideTheAppDataFolder()
    {
        var path = Path.Combine(AppDataRoot, "LogoCache", "123.cache");
        Assert.True(AsyncImageLoader.IsAllowedLocalFile(new Uri(path), out var resolved));
        Assert.Equal(path, resolved, ignoreCase: true);
    }

    [Fact]
    public void AllowsFilesInTheImageCache()
    {
        var path = Path.Combine(AppDataRoot, "ImageCache", "abcdef.img");
        Assert.True(AsyncImageLoader.IsAllowedLocalFile(new Uri(path), out _));
    }

    [Fact]
    public void RejectsArbitraryLocalFiles()
    {
        Assert.False(AsyncImageLoader.IsAllowedLocalFile(new Uri(@"C:\Windows\win.ini"), out _));
    }

    [Fact]
    public void RejectsUncPaths_WhichWouldLeakAnNtlmHash()
    {
        // The headline risk: Windows authenticates to the host before anything is read.
        Assert.False(AsyncImageLoader.IsAllowedLocalFile(new Uri(@"\\attacker.example.com\share\logo.png"), out _));
    }

    [Fact]
    public void RejectsTraversalOutOfTheAppDataFolder()
    {
        var escaped = Path.Combine(AppDataRoot, "LogoCache", "..", "..", "..", "Windows", "win.ini");
        Assert.False(AsyncImageLoader.IsAllowedLocalFile(new Uri(escaped), out _));
    }

    [Fact]
    public void RejectsASiblingFolderWithTheSamePrefix()
    {
        // "RadioV2Evil" must not pass a naive StartsWith against "RadioV2".
        var sibling = AppDataRoot + "Evil" + Path.DirectorySeparatorChar + "logo.png";
        Assert.False(AsyncImageLoader.IsAllowedLocalFile(new Uri(sibling), out _));
    }
}
