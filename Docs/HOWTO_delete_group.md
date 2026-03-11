# How to Delete a Group

## Places to update

### 1. All 5 databases (SQLite)

The database exists in 5 locations — the source and 4 build outputs. Delete from all of them.

```bash
for db in \
  "Data/radioapp_large_groups.db" \
  "bin/Debug/net8.0-windows/Data/radioapp_large_groups.db" \
  "bin/Release/net8.0-windows/win-x64/Data/radioapp_large_groups.db" \
  "bin/Release/net8.0-windows/win-x64/publish/Data/radioapp_large_groups.db" \
  "publish/Data/radioapp_large_groups.db"; do
  sqlite3 "$db" "
    DELETE FROM Stations WHERE GroupId = (SELECT Id FROM Groups WHERE Name = 'GROUP_NAME');
    DELETE FROM Groups WHERE Name = 'GROUP_NAME';
  "
done
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
