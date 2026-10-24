namespace RadioV2.Helpers;

/// <summary>Coalesces repeated callbacks without a growing queue or metadata map.</summary>
public sealed class TransientMessageGate(Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private int? _lastStation;
    private DateTimeOffset _lastShown;
    public bool ShouldShow(int stationId)
    {
        var now = _clock();
        if (_lastStation == stationId && now - _lastShown < TimeSpan.FromSeconds(10)) return false;
        _lastStation = stationId;
        _lastShown = now;
        return true;
    }
    public void Reset() => _lastStation = null;
}
