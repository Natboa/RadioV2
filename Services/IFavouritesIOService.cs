using RadioV2.Models;

namespace RadioV2.Services;

public interface IFavouritesIOService
{
    Task ExportAsync(string filePath, List<Station> favourites);
    Task<int> ImportAsync(string filePath); // returns count of stations matched & marked favourite
}
