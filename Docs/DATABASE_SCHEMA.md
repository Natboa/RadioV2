# RadioApp Data Structure & Database Schema

This document explains how RadioApp stores and expects radio station data. The application uses **SQLite** for persistence and supports importing stations via **M3U/M3U8** playlists.

---

## 1. Database Overview
- **File Name**: `radioapp.db` (located in the application's executable directory).
- **Engine**: SQLite 3.
- **ORM**: Entity Framework Core.

---

## 2. Table Structures

### `Stations` Table
Stores the core information for each radio station.

| Property | Data Type | Description |
| :--- | :--- | :--- |
| `Id` | Integer (PK) | Auto-incrementing unique identifier. |
| `Name` | String | The display name of the station. |
| `StreamUrl` | String (Unique) | The direct URL to the audio stream (AAC, MP3, etc.). |
| `LogoUrl` | String (Nullable) | URL to the station's logo image. |
| `GroupId` | Integer (FK) | Reference to the `Groups` table. |
| `IsFavorite` | Boolean | Whether the station is marked as a favorite. |

### `Groups` Table
Used to categorize stations (e.g., "News", "Rock", "Pop").

| Property | Data Type | Description |
| :--- | :--- | :--- |
| `Id` | Integer (PK) | Auto-incrementing unique identifier. |
| `Name` | String | The name of the group. |

### `Settings` Table
Stores application-wide preferences.

| Property | Data Type | Description |
| :--- | :--- | :--- |
| `Key` | String (PK) | The setting name (e.g., "Volume", "LastPlayedStationId"). |
| `Value` | String | The value of the setting. |

---

## 3. Supported Import Format (M3U/M3U8)

The app's `M3UParserService` expects standard Extended M3U files. It specifically looks for the following attributes in the `#EXTINF` line:

### Example File Structure:
```m3u
#EXTM3U
#EXTINF:-1 tvg-logo="https://example.com/logo.png" group-title="Rock",Classic Rock Radio
http://stream.example.com/rock.mp3

#EXTINF:-1 tvg-logo="https://example.com/jazz.png" group-title="Jazz",Smooth Jazz FM
http://stream.example.com/jazz.aac
```

### Property Mapping:
- **Station Name**: Extracted from the text following the last comma in the `#EXTINF` line.
- **Stream URL**: Extracted from the line immediately following the `#EXTINF` line.
- **Logo URL**: Extracted from the `tvg-logo="..."` attribute.
- **Group Name**: Extracted from the `group-title="..."` attribute. If missing, it defaults to `"Uncategorized"`.

---

## 4. Key Constraints & Behavior
1. **Unique Streams**: The `StreamUrl` is indexed as **Unique**. Importing a station with a URL that already exists will update the existing station's metadata (Name, Logo, Group) rather than creating a duplicate.
2. **Relational Integrity**: Deleting a `Group` does not automatically delete its stations (behavior depends on EF Core configuration, currently defaults to Restrict/NoAction).
3. **Mica Support**: The database must be initialized (`EnsureDatabaseMigratedAsync`) on startup to ensure the schema is present before the UI loads.
