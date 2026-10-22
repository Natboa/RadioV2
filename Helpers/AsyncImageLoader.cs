using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Serilog;
// This project enables WinForms too, so bare Image is ambiguous with System.Drawing.Image.
using Image = System.Windows.Controls.Image;

namespace RadioV2.Helpers;

/// <summary>
/// Loads station artwork into an <see cref="Image"/> with a timeout, a cache, bounded
/// concurrency and — the important part — cancellation when the row is recycled.
///
/// <para>
/// Binding a remote URL straight to <c>Image.Source</c> hands WPF an unbounded,
/// untimed, uncancellable download per realised row, against a station list whose
/// artwork lives on 138,925 rows' worth of third-party hosts, many of them long dead.
/// A single fast scroll opened hundreds of sockets that nothing ever closed; the idle
/// handle count sat at roughly 2,600.
/// </para>
/// </summary>
public static class AsyncImageLoader
{
    /// <summary>Row artwork renders at 48–64 px; 96 covers 200% DPI.</summary>
    private const int DecodeWidth = 96;

    /// <summary>Enough to fill a viewport quickly, few enough that a scroll through
    /// dead hosts cannot exhaust sockets.</summary>
    private const int MaxConcurrentDownloads = 6;

    private const long MaxBytes = 500 * 1024;

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly SemaphoreSlim _gate = new(MaxConcurrentDownloads, MaxConcurrentDownloads);
    private static readonly ConditionalWeakTable<Image, CancellationTokenSource> _inFlight = new();

    private static readonly string _appDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RadioV2");

    private static readonly string _cacheDir = Path.Combine(_appDataRoot, "ImageCache");

    // ── Attached property ─────────────────────────────────────────────────────

    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.RegisterAttached(
            "Source", typeof(string), typeof(AsyncImageLoader),
            new PropertyMetadata(null, OnSourceChanged));

    public static void SetSource(DependencyObject obj, string? value) => obj.SetValue(SourceProperty, value);

    public static string? GetSource(DependencyObject obj) => (string?)obj.GetValue(SourceProperty);

    private static async void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image) return;

        // Cancel whatever this Image was already fetching. Virtualisation recycles
        // containers as the user scrolls, so this is what stops an abandoned row's
        // request from occupying a socket until it times out.
        if (_inFlight.TryGetValue(image, out var previous))
        {
            previous.Cancel();
            previous.Dispose();
            _inFlight.Remove(image);
        }

        image.Source = null;

        if (e.NewValue is not string url || string.IsNullOrWhiteSpace(url)) return;

        var cts = new CancellationTokenSource();
        _inFlight.Add(image, cts);

        try
        {
            var bitmap = await LoadAsync(url, cts.Token);

            // Re-check: the row may have been recycled while we were waiting.
            if (bitmap is not null && !cts.IsCancellationRequested
                && string.Equals(GetSource(image), url, StringComparison.Ordinal))
                image.Source = bitmap;
        }
        catch (OperationCanceledException)
        {
            // Recycled mid-flight; nothing to do.
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "image: could not load {Url}", LogRedaction.Url(url));
        }
        finally
        {
            if (_inFlight.TryGetValue(image, out var current) && ReferenceEquals(current, cts))
                _inFlight.Remove(image);
            cts.Dispose();
        }
    }

    // ── Loading ───────────────────────────────────────────────────────────────

    private static async Task<BitmapImage?> LoadAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        // Already a local file (a favourite whose logo was cached previously).
        if (uri.IsFile)
        {
            if (!IsAllowedLocalFile(uri, out var localPath))
            {
                Log.Warning("image: refusing local path from station data {Url}", LogRedaction.Url(url));
                return null;
            }

            return await Task.Run(() => DecodeLocalFile(localPath), ct);
        }

        // Station artwork URLs come from the database, and M3U import lets anyone put
        // anything in there. Anything but http(s) is refused.
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            Log.Warning("image: refusing scheme {Scheme}", uri.Scheme);
            return null;
        }

        var cachePath = CachePathFor(url);
        if (File.Exists(cachePath))
            return await Task.Run(() => Decode(File.ReadAllBytes(cachePath)), ct);

        await _gate.WaitAsync(ct);
        try
        {
            // Another row may have fetched the same URL while we queued.
            if (File.Exists(cachePath))
                return await Task.Run(() => Decode(File.ReadAllBytes(cachePath)), ct);

            var bytes = await DownloadAsync(url, ct);
            if (bytes is null) return null;

            await WriteCacheAsync(cachePath, bytes, ct);
            return await Task.Run(() => Decode(bytes), ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<byte[]?> DownloadAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return null;
        if (response.Content.Headers.ContentLength > MaxBytes) return null;

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();

        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBytes) return null;
        }

        return buffer.ToArray();
    }

    private static async Task WriteCacheAsync(string path, byte[] bytes, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);

            // Write-then-rename so a cancelled write cannot leave a half file that
            // every later load would try to decode.
            var temporary = path + ".tmp";
            await File.WriteAllBytesAsync(temporary, bytes, ct);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "image: could not cache to {Path}", path);
        }
    }

    /// <summary>
    /// A <c>file:</c> URL is only honoured when it resolves inside the app's own data
    /// folder.
    ///
    /// <para>
    /// This matters because the URL is not ours: <c>Station.DisplayLogoSource</c>
    /// falls back to <c>LogoUrl</c>, which comes from the database, and M3U import
    /// writes whatever <c>tvg-logo</c> the playlist contained. Without this check a
    /// crafted playlist could read an arbitrary local file, or — far worse on
    /// Windows — point at a UNC path and make the machine authenticate to the
    /// attacker's SMB server, handing over an NTLM hash.
    /// </para>
    /// </summary>
    internal static bool IsAllowedLocalFile(Uri uri, out string fullPath)
    {
        fullPath = string.Empty;

        // \\host\share authenticates to host before any path check matters.
        if (uri.IsUnc) return false;

        try
        {
            fullPath = Path.GetFullPath(uri.LocalPath);
        }
        catch
        {
            return false;
        }

        var root = Path.GetFullPath(_appDataRoot);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
            root += Path.DirectorySeparatorChar;

        // GetFullPath has already collapsed any ".." segments.
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static BitmapImage? DecodeLocalFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaxBytes) return null;
        return Decode(File.ReadAllBytes(path));
    }

    /// <summary>Decodes at display size and freezes so the bitmap can cross threads.</summary>
    private static BitmapImage? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = DecodeWidth;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            // Stations serve all sorts of things that are not images.
            return null;
        }
    }

    private static string CachePathFor(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        var name = Convert.ToHexString(hash, 0, 16);
        return Path.Combine(_cacheDir, name + ".img");
    }

    // ── Maintenance ───────────────────────────────────────────────────────────

    /// <summary>
    /// Trims the cache to <paramref name="maxBytes"/>, oldest first. Every station's
    /// artwork is cached now, not just favourites', so the directory needs a ceiling.
    /// </summary>
    public static void TrimCache(long maxBytes = 200L * 1024 * 1024)
    {
        try
        {
            var dir = new DirectoryInfo(_cacheDir);
            if (!dir.Exists) return;

            var files = dir.GetFiles("*.img");
            long total = files.Sum(f => f.Length);
            if (total <= maxBytes) return;

            foreach (var file in files.OrderBy(f => f.LastAccessTimeUtc))
            {
                if (total <= maxBytes) break;
                total -= file.Length;
                try { file.Delete(); } catch { /* next launch will retry */ }
            }

            Log.Information("image cache: trimmed to {Bytes} bytes", total);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "image cache: trim failed");
        }
    }
}
