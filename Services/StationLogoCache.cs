using System.IO;
using System.Net.Http;
using Serilog;

namespace RadioV2.Services;

public class StationLogoCache : IStationLogoCache
{
    private const long MaxBytes = 500 * 1024; // 500 KB

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly string _cacheDir;

    public StationLogoCache()
    {
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RadioV2", "LogoCache");
        Directory.CreateDirectory(_cacheDir);
    }

    private string FilePath(int stationId) => Path.Combine(_cacheDir, $"{stationId}.cache");

    public string? GetCachedPath(int stationId)
    {
        var path = FilePath(stationId);
        return File.Exists(path) ? path : null;
    }

    public async Task DownloadAsync(int stationId, string logoUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(logoUrl)) return;

        try
        {
            using var response = await _http.GetAsync(logoUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return;

            // Reject if Content-Length header alone exceeds cap
            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                Log.Warning("Station {Id} logo exceeds 500 KB cap (Content-Length) — skipping", stationId);
                return;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);

            var tmp = FilePath(stationId) + ".tmp";
            long written = 0;

            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[8192];
                int read;
                while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    written += read;
                    if (written > MaxBytes)
                    {
                        Log.Warning("Station {Id} logo exceeds 500 KB cap during download — aborting", stationId);
                        fs.Close();
                        TryDelete(tmp);
                        return;
                    }
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }

            var dest = FilePath(stationId);
            TryDelete(dest);
            File.Move(tmp, dest);

            Log.Debug("Cached logo for station {Id} ({Bytes} bytes)", stationId, written);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Failed to cache logo for station {Id}", stationId);
        }
    }

    public void Delete(int stationId)
    {
        TryDelete(FilePath(stationId));
        Log.Debug("Deleted cached logo for station {Id}", stationId);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Log.Warning(ex, "Could not delete file {Path}", path); }
    }
}
