# How to Delete a Group

## Places to update

### 1. All databases (SQLite)

The database exists in multiple locations — the source, build outputs, and AppData. Delete from all of them.

```bash
# Source + build outputs
for db in \
  "Data/radioapp_large_groups.db" \
  "bin/Debug/net8.0-windows/Data/stations.db" \
  "bin/Release/net8.0-windows/win-x64/Data/radioapp_large_groups.db" \
  "bin/Release/net8.0-windows/win-x64/publish/Data/radioapp_large_groups.db" \
  "publish/Data/radioapp_large_groups.db"; do
  [ -f "$db" ] && sqlite3 "$db" "
    DELETE FROM Stations WHERE GroupId = (SELECT Id FROM Groups WHERE Name = 'GROUP_NAME');
    DELETE FROM Groups WHERE Name = 'GROUP_NAME';
  "
done

# AppData (live copy the app actually reads)
APPDATA_DB="$LOCALAPPDATA/RadioV2/Data/stations.db"
sqlite3 "$APPDATA_DB" "
  DELETE FROM Stations WHERE GroupId = (SELECT Id FROM Groups WHERE Name = 'GROUP_NAME');
  DELETE FROM Groups WHERE Name = 'GROUP_NAME';
"

# Legacy AppData DB (used as fallback seed if stations.db is missing — must also be clean)
LEGACY_DB="$LOCALAPPDATA/RadioV2/radioapp_large_groups.db"
[ -f "$LEGACY_DB" ] && sqlite3 "$LEGACY_DB" "
  DELETE FROM Stations WHERE GroupId = (SELECT Id FROM Groups WHERE Name = 'GROUP_NAME');
  DELETE FROM Groups WHERE Name = 'GROUP_NAME';
"
```

Replace `GROUP_NAME` with the exact group name as stored in the DB (e.g. `north_america`).

**Important:** Always delete Stations first, then the Group — stations reference the group by ID.

### 2. `Services/CategorySeeder.cs`

Remove the group key from the relevant category array so it won't be re-added on next seed.

### 3. `Assets/Groups/` (optional)

If there is a matching `.png` image (e.g. `north_america.png`), delete it too.

## Verify the deletion

```bash
sqlite3 Data/radioapp_large_groups.db "SELECT name FROM Groups WHERE name = 'GROUP_NAME';"
# Should return nothing
```

## Notes

- The source DB is `Data/radioapp_large_groups.db`. **Never run migrations against it.**
- Build outputs are copies made at compile time — they must be updated manually or by rebuilding.
- If the app is running, restart it after editing the DB.

## Known Bug: Deleted Groups Reappear After Resetting AppData

**Symptom:** After deleting groups from all known DBs and wiping `%LocalAppData%\RadioV2\Data\stations.db*`, the app still shows the deleted groups on next launch.

**Root cause:** `App.xaml.cs` has a legacy migration path — on startup, if `stations.db` is missing, it first checks for `%LocalAppData%\RadioV2\radioapp_large_groups.db` (old single-DB layout) and copies from it instead of from the build output seed. If that legacy file still has the old groups, they come back.

**Fix applied (2026-03-12):** `App.xaml.cs` now deletes the legacy DB after migrating from it, so a future reset always falls through to the clean seed copy:
```csharp
File.Copy(legacyDbPath, stationsDbPath);
File.Delete(legacyDbPath); // prevent stale legacy data from re-seeding
```

**If you hit this again:** Check `%LocalAppData%\RadioV2\radioapp_large_groups.db` — if it exists and has the deleted group, either delete the file or run the sqlite3 delete against it directly.
