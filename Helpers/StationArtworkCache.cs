using System.Net;

namespace RadioV2.Helpers;

/// <summary>Session-only knowledge from ordinary image requests. No networking,
/// timers, disk IO or change events. A visit owns a copy, never this live map.</summary>
public sealed class StationArtworkCache(Func<DateTimeOffset>? clock = null)
{
    public static StationArtworkCache Shared { get; } = new();
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Dictionary<string, DateTimeOffset> _failures = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public static bool Supports(string? url) =>
        !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme is "http" or "https" || uri.IsFile)
        && !uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

    public string[] Snapshot()
    {
        lock (_sync)
        {
            Prune();
            return _failures.Keys.ToArray();
        }
    }

    public bool IsUnavailable(string url)
    {
        lock (_sync) return _failures.TryGetValue(url.Trim(), out var until) && until > _clock();
    }

    public void RecordHttpFailure(string url, HttpStatusCode status)
    {
        // Authentication gates, DNS, timeouts, rate limiting and server failures
        // may recover quickly. Only explicit missing/gone responses are remembered.
        if (status is HttpStatusCode.NotFound or HttpStatusCode.Gone) RecordDecodeFailure(url);
    }

    public void RecordDecodeFailure(string url)
    {
        var key = url.Trim();
        if (key.Length is 0 or > 2048) return;
        lock (_sync)
        {
            Prune();
            if (_failures.ContainsKey(key)) return;
            if (_failures.Count >= 256) _failures.Remove(_failures.MinBy(p => p.Value).Key);
            _failures[key] = _clock().AddHours(6);
        }
    }

    private void Prune()
    {
        var now = _clock();
        foreach (var key in _failures.Where(p => p.Value <= now).Select(p => p.Key).ToArray()) _failures.Remove(key);
    }
}
