#!/usr/bin/env python3
"""
Station Deduplicator
--------------------
Finds stations in the same group that share the same name AND the same logo URL,
and deletes all but one (keeping the lowest Id).

Rules:
  - Same group + same name + same logo  →  keep 1 (lowest Id), delete the rest
  - Same group + same name + diff logos →  leave all alone (different pictures = different stations)

By default runs in DRY-RUN mode — nothing is deleted until you pass --execute.

Usage:
    python dedup_stations.py                        # dry run, show what would be deleted
    python dedup_stations.py --execute              # actually delete duplicates
    python dedup_stations.py --db Data/radioapp_large_groups.db
    python dedup_stations.py --execute --verbose    # print every kept/deleted station
"""

import sqlite3
import argparse
from pathlib import Path
from collections import defaultdict

DEFAULT_DB = "Data/radioapp_large_groups.db"


def normalise_logo(logo: str | None) -> str:
    """Treat NULL / empty string as the same bucket."""
    return (logo or "").strip()


def find_duplicates(conn: sqlite3.Connection) -> dict:
    """
    Returns a dict keyed by (group_id, group_name, station_name, logo_bucket)
    whose values are lists of station Ids in that bucket.
    Only buckets with more than one entry are included.
    """
    rows = conn.execute("""
        SELECT s.Id, s.Name, s.LogoUrl, s.GroupId, g.Name AS GroupName
        FROM   Stations s
        JOIN   Groups   g ON g.Id = s.GroupId
        ORDER  BY s.GroupId, s.Name, s.Id
    """).fetchall()

    buckets: dict = defaultdict(list)
    for sid, name, logo, gid, gname in rows:
        key = (gid, gname, name, normalise_logo(logo))
        buckets[key].append(sid)

    return {k: v for k, v in buckets.items() if len(v) > 1}


def main():
    parser = argparse.ArgumentParser(
        description="Remove duplicate stations (same group + name + logo)."
    )
    parser.add_argument("--db",      type=str, default=DEFAULT_DB,
                        help=f"Database path (default: {DEFAULT_DB})")
    parser.add_argument("--execute", action="store_true",
                        help="Actually delete duplicates (omit for dry-run)")
    parser.add_argument("--verbose", action="store_true",
                        help="Print every kept and deleted station")
    args = parser.parse_args()

    db_path = Path(args.db)
    if not db_path.exists():
        print(f"ERROR: DB not found: {db_path.resolve()}")
        print( "       Run from the project root, or pass --db <path>")
        return

    mode = "EXECUTE" if args.execute else "DRY-RUN"

    print("=" * 64)
    print("  Station Deduplicator")
    print("=" * 64)
    print(f"  DB   : {db_path.resolve()}")
    print(f"  Mode : {mode}")
    print("=" * 64)

    conn = sqlite3.connect(str(db_path.resolve()))

    duplicates = find_duplicates(conn)

    if not duplicates:
        print("\n  No duplicates found. Database is clean.")
        conn.close()
        return

    total_to_delete = sum(len(ids) - 1 for ids in duplicates.values())
    print(f"\n  Duplicate buckets : {len(duplicates):,}")
    print(f"  Stations to delete: {total_to_delete:,}")
    print()

    ids_to_delete: list[int] = []

    for (gid, gname, sname, logo), ids in sorted(duplicates.items()):
        keep_id   = ids[0]          # lowest Id — guaranteed by ORDER BY s.Id in query
        delete_ids = ids[1:]
        ids_to_delete.extend(delete_ids)

        if args.verbose:
            logo_display = logo if logo else "(no logo)"
            print(f"  [GROUP] {gname} (id={gid})")
            print(f"    Name : {sname}")
            print(f"    Logo : {logo_display[:80]}")
            print(f"    KEEP    id={keep_id}")
            for did in delete_ids:
                print(f"    DELETE  id={did}")
            print()

    if not args.verbose:
        # Show a concise sample (first 20 buckets)
        sample = list(duplicates.items())[:20]
        for (gid, gname, sname, logo), ids in sample:
            keep_id    = ids[0]
            delete_ids = ids[1:]
            logo_short = (logo[:50] + "…") if len(logo) > 50 else (logo or "(no logo)")
            print(f"  [{gname}] \"{sname}\"  logo={logo_short}")
            print(f"    keep={keep_id}  delete={delete_ids}")
        if len(duplicates) > 20:
            print(f"  … and {len(duplicates) - 20} more buckets (use --verbose to see all)")
        print()

    if not args.execute:
        print("─" * 64)
        print(f"  DRY-RUN: {total_to_delete:,} stations would be deleted.")
        print( "  Run with --execute to apply changes.")
        print("─" * 64)
        conn.close()
        return

    # ── Execute deletions ──────────────────────────────────────────────────────
    print(f"  Deleting {total_to_delete:,} duplicate stations…")

    # Delete in batches of 500 to avoid hitting SQLite's variable limit
    BATCH = 500
    deleted = 0
    for i in range(0, len(ids_to_delete), BATCH):
        batch = ids_to_delete[i : i + BATCH]
        placeholders = ",".join("?" * len(batch))
        conn.execute(f"DELETE FROM Stations WHERE Id IN ({placeholders})", batch)
        deleted += len(batch)
        print(f"  … deleted {deleted:,} / {total_to_delete:,}", end="\r")

    conn.commit()
    print()

    print()
    print("=" * 64)
    print(f"  Done. {deleted:,} duplicate station(s) removed.")
    print("=" * 64)

    conn.close()


if __name__ == "__main__":
    main()
