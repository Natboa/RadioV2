using RadioV2.Models;

namespace RadioV2.Services;

public interface IStationService
{
    Task<List<Station>> GetStationsAsync(int skip, int take, string? searchQuery = null, CancellationToken ct = default);
    Task<List<GroupWithCount>> GetGroupsWithCountsAsync(int skip, int take, CancellationToken ct = default);
    Task<List<Station>> GetStationsByGroupAsync(int groupId, int skip, int take, string? searchQuery = null, CancellationToken ct = default);
    bool CategoriesAreCached { get; }
    Task<List<CategoryWithGroups>> GetCategoriesWithGroupsAsync(CancellationToken ct = default);
    Task<List<Station>> GetFavouritesAsync(CancellationToken ct = default);
    Task ToggleFavouriteAsync(int stationId, CancellationToken ct = default);
    Task<string?> GetSettingAsync(string key, CancellationToken ct = default);
    Task SetSettingAsync(string key, string value, CancellationToken ct = default);
    Task<int> BulkImportStationsAsync(List<ParsedStation> stations, CancellationToken ct = default);
}
