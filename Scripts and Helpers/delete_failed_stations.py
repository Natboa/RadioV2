"""
Delete all stations from the main database that failed the stream check.
Only deletes stations explicitly marked as non-'ok' in stream_check_results.db.
Stations that were never checked are left untouched.
"""

import sqlite3
import os

RESULTS_DB = "stream_check_results.db"
MAIN_DB    = "Data/radioapp_large_groups.db"

def main():
    if not os.path.exists(RESULTS_DB):
        print(f"ERROR: {RESULTS_DB} not found.")
        return
    if not os.path.exists(MAIN_DB):
        print(f"ERROR: {MAIN_DB} not found.")
        return

    # Collect failed station IDs from results DB
    print("Reading failed station IDs from stream_check_results.db ...")
    with sqlite3.connect(RESULTS_DB) as res:
        rows = res.execute(
            "SELECT station_id FROM checked_stations WHERE status != 'ok'"
        ).fetchall()

    failed_ids = [r[0] for r in rows]
    print(f"  Found {len(failed_ids):,} failed stations to delete.")

    if not failed_ids:
        print("Nothing to delete.")
        return

    # Delete in batches to avoid SQLite variable limit (999)
    BATCH = 900
    total_deleted = 0

    with sqlite3.connect(MAIN_DB) as main:
        main.execute("PRAGMA journal_mode=WAL")
        for i in range(0, len(failed_ids), BATCH):
            batch = failed_ids[i : i + BATCH]
            placeholders = ",".join("?" * len(batch))
            cur = main.execute(
                f"DELETE FROM Stations WHERE Id IN ({placeholders})", batch
            )
            total_deleted += cur.rowcount
            if (i // BATCH) % 50 == 0:
                print(f"  Progress: {min(i + BATCH, len(failed_ids)):,} / {len(failed_ids):,} processed ...")

        main.commit()

    print(f"\nDone. Deleted {total_deleted:,} stations from the main database.")

    # Show remaining counts
    with sqlite3.connect(MAIN_DB) as main:
        remaining = main.execute("SELECT COUNT(*) FROM Stations").fetchone()[0]
        print(f"Stations remaining in main DB: {remaining:,}")

if __name__ == "__main__":
    main()
