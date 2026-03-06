using RadioV2.Models;

namespace RadioV2.Services;

public interface IFavouritesIOService
{
    Task ExportAsync(string filePath, string format, List<Station> favourites);
    Task<int> ImportAsync(string filePath, string format); // returns count of stations matched & marked favourite
}
