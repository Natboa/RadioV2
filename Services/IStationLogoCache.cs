namespace RadioV2.Services;

public interface IStationLogoCache
{
    string? GetCachedPath(int stationId);
    Task DownloadAsync(int stationId, string logoUrl, CancellationToken ct = default);
    void Delete(int stationId);
}
