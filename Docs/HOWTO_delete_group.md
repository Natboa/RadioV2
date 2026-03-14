# How to Delete a Group

## Places to update

### 1. All databases (SQLite)

The database exists in multiple locations — the source, build outputs, and AppData. Delete from **each one individually** using separate sqlite3 calls. Do NOT use a bash loop with variables — variable expansion inside sqlite3 string arguments is unreliable and will silently do nothing.

**Run each command separately, one at a time:**

```bash
# 1. Source DB
sqlite3 "c:/VS Code repos/radioV2/Data/radioapp_large_groups.db" "DELETE FROM Stations WHERE GroupId IN (SELECT Id FROM Groups WHERE Name IN ('GROUP_NAME')); DELETE FROM Groups WHERE Name IN ('GROUP_NAME');"

# 2. Debug build DB (if it exists)
sqlite3 "c:/VS Code repos/radioV2/bin/Debug/net8.0-windows/Data/stations.db" "DELETE FROM Stations WHERE GroupId IN (SELECT Id FROM Groups WHERE Name IN ('GROUP_NAME')); DELETE FROM Groups WHERE Name IN ('GROUP_NAME');"

# 3. Publish DB (if it exists)
sqlite3 "c:/VS Code repos/radioV2/publish/Data/radioapp_large_groups.db" "DELETE FROM Stations WHERE GroupId IN (SELECT Id FROM Groups WHERE Name IN ('GROUP_NAME')); DELETE FROM Groups WHERE Name IN ('GROUP_NAME');"

# 4. AppData — the live DB the running app reads
sqlite3 "$LOCALAPPDATA/RadioV2/Data/stations.db" "DELETE FROM Stations WHERE GroupId IN (SELECT Id FROM Groups WHERE Name IN ('GROUP_NAME')); DELETE FROM Groups WHERE Name IN ('GROUP_NAME');"

# 5. Legacy AppData DB (only exists if app was never launched after the 2026-03-12 fix)
[ -f "$LOCALAPPDATA/RadioV2/radioapp_large_groups.db" ] && sqlite3 "$LOCALAPPDATA/RadioV2/radioapp_large_groups.db" "DELETE FROM Stations WHERE GroupId IN (SELECT Id FROM Groups WHERE Name IN ('GROUP_NAME')); DELETE FROM Groups WHERE Name IN ('GROUP_NAME');"
```

To delete **multiple groups at once**, list them comma-separated in the `IN` clause:
```bash
sqlite3 "c:/VS Code repos/radioV2/Data/radioapp_large_groups.db" "DELETE FROM Stations WHERE GroupId IN (SELECT Id FROM Groups WHERE Name IN ('Pop Rock','Reggaeton','Instrumental')); DELETE FROM Groups WHERE Name IN ('Pop Rock','Reggaeton','Instrumental');"
```

**Important:** Always delete Stations first, then the Group — stations reference the group by ID.

### 2. Verify each DB immediately after deleting

**Always verify — silent failures happen.** Run this after each delete:

```bash
sqlite3 "c:/VS Code repos/radioV2/Data/radioapp_large_groups.db" "SELECT Name FROM Groups WHERE Name IN ('GROUP_NAME');"
# Must return nothing. If it returns rows, the delete failed — run it again directly.
```

Do the same check for the AppData DB:
```bash
sqlite3 "$LOCALAPPDATA/RadioV2/Data/stations.db" "SELECT Name FROM Groups WHERE Name IN ('GROUP_NAME');"
```

### 3. `Services/CategorySeeder.cs`

Remove the group key from the relevant category array so it won't be re-added on next seed.

### 4. `Assets/Groups/`

Delete the matching `.png` file (e.g. `Pop Rock.png`, `instrumental.png`). Check both capitalizations — filenames are inconsistent in this folder.

---

## Notes

- The source DB is `Data/radioapp_large_groups.db`. **Never run migrations against it.**
- Build outputs are copies made at compile time — they must be updated manually.
- If the app is running, restart it after editing the AppData DB.

---

## Known Bug: Deleted Groups Reappear After Resetting AppData

**Symptom:** After deleting groups from all known DBs and wiping `%LocalAppData%\RadioV2\Data\stations.db*`, the app still shows the deleted groups on next launch.

**Root cause:** `App.xaml.cs` has a legacy migration path — on startup, if `stations.db` is missing, it first checks for `%LocalAppData%\RadioV2\radioapp_large_groups.db` (old single-DB layout) and copies from it instead of from the build output seed. If that legacy file still has the old groups, they come back.

**Fix applied (2026-03-12):** `App.xaml.cs` now deletes the legacy DB after migrating from it, so a future reset always falls through to the clean seed copy:
```csharp
File.Copy(legacyDbPath, stationsDbPath);
File.Delete(legacyDbPath); // prevent stale legacy data from re-seeding
```

**If you hit this again:** Check `%LocalAppData%\RadioV2\radioapp_large_groups.db` — if it exists and has the deleted group, either delete the file or run the sqlite3 delete against it directly.
