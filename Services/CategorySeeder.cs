using Microsoft.EntityFrameworkCore;
using RadioV2.Data;
using RadioV2.Helpers;
using RadioV2.Models;

namespace RadioV2.Services;

public static class CategorySeeder
{
    // Ordered list — carousel rows appear in exactly this sequence on the Discover page
    private static readonly (string CategoryName, string[] GroupKeys)[] SeedData =
    [
        ("Rock & Metal",
        [
            "alternative_rock","classic","classic_rock","gothic",
            "hard_rock","heavy_metal","metal","new_wave","progressive_rock","punk",
            "rock","soft_rock"
        ]),
        ("Electronic & Dance",
        [
            "ambient","club","dance","deep_house","drum_and_bass","electronic",
            "house","techno","trance","trap","underground"
        ]),
        ("Pop, Charts & Decades",
        [
            "00s","10s","50s","60s","70s","80s","90s","oldies",
            "adult_contemporary","charts","hits","pop","pop_rock","top_40"
        ]),
        ("Urban & Latin",
        [
            "disco","funk","hip_hop","rap","reggae","r_b","soul","urban",
            "espanol","latin","reggaeton","salsa","sertaneja","tropical"
        ]),
        ("Jazz, Chill & Instrumental",
        [
            "ballads","blues","chillout","classical","easy_listening","instrumental",
            "jazz","jazz_funk","smooth_jazz","swing","trip_hop","vocal"
        ]),
        ("News, Talk & Sports",
        [
            "comedy","entertainment","news","news_talk","sports","talk"
        ]),
        ("Specialty & Mood",
        [
            "christmas","eclectic","holidays","kids","party","religious","romantic","soundtrack"
        ]),
        ("Global & Cultural",
        [
            "country","english","folk","schlager","traditional","world"
        ]),
        ("Asia, Pacific & Africa",
        [
            "australia","india","indonesia","israel","japan","new_zealand","philippines",
            "saudi_arabia","singapore","south_africa","thailand","uae","uganda",
        ]),
        ("Americas",
        [
            "argentina","brazil","canada","chile","colombia","ecuador","guatemala",
            "mexico","paraguay","peru","puerto_rico","uruguay","usa","venezuela",
            "north_america","south_america",
        ]),
        ("Europe",
        [
            "austria","belgium","croatia","czech_republic","france","germany",
            "greece","hungary","ireland","italy","netherlands","norway","poland",
            "portugal","romania","russia","serbia","slovakia","spain","sweden",
            "switzerland","turkey","uk","ukraine","europe",
        ]),
    ];

    /// <summary>
    /// Inserts the 9 categories and assigns groups to them. Safe to call on every
    /// startup — exits immediately if categories already exist.
    /// </summary>
    public static async Task SeedAsync(StationsDbContext db)
    {
        if (await db.Categories.AnyAsync()) return;

        // Build lookup: sanitized group name → Group entity
        var allGroups = await db.Groups.ToListAsync();
        var groupsByKey = new Dictionary<string, Group>(StringComparer.Ordinal);
        foreach (var g in allGroups)
        {
            var key = GroupImageHelper.Sanitize(g.Name);
            groupsByKey.TryAdd(key, g); // skip duplicates if any
        }

        int displayOrder = 0;
        foreach (var (categoryName, keys) in SeedData)
        {
            var category = new Category { Name = categoryName, DisplayOrder = displayOrder++ };
            db.Categories.Add(category);
            await db.SaveChangesAsync(); // get generated Id before assigning it

            foreach (var key in keys)
            {
                if (groupsByKey.TryGetValue(key, out var group))
                    group.CategoryId = category.Id;
            }
        }

        await db.SaveChangesAsync();
    }
}
