#!/usr/bin/env python3
"""
Radio Stream Checker
--------------------
Checks every station in radioapp_large_groups.db to see if the stream works.
Failing stations are saved to stream_check_results.db for later deletion.

Features:
  - Concurrent checking (configurable workers)
  - Stop & resume — already-checked stations are skipped on restart
  - Processes groups smallest-first
  - Internet outage detection — pauses and retries if your connection drops
  - Recheck mode (--recheck) — re-tests only previously-ok stations and downgrades any that now fail

Usage:
    python stream_checker.py                   # full scan
    python stream_checker.py --recheck         # re-test ok stations only
    python stream_checker.py --workers 30 --timeout 10
    python stream_checker.py --db Data/radioapp_large_groups.db --results stream_check_results.db

Requirements:
    pip install requests
"""

import sqlite3
import threading
import argparse
import time
from datetime import datetime, timezone
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from collections import deque

try:
    import requests
    from requests.exceptions import RequestException, Timeout, ConnectionError as ReqConnectionError
except ImportError:
    print("ERROR: 'requests' not installed. Run: pip install requests")
    exit(1)

# ── Defaults ──────────────────────────────────────────────────────────────────
DEFAULT_SRC_DB   = "Data/radioapp_large_groups.db"
DEFAULT_RESULTS  = "stream_check_results.db"
DEFAULT_WORKERS  = 60
DEFAULT_TIMEOUT  = 12   # seconds per station
READ_BYTES       = 1024  # bytes to read to confirm data flows

# Internet outage detection:
#   If this many of the last N results are timeout/error (not HTTP fail),
#   we suspect your internet is down and pause to wait for it to recover.
#   With high concurrency many results arrive at once, so use a wider window
#   to avoid false positives from a single burst of slow stations.
OUTAGE_WINDOW        = 30   # look at last N results
OUTAGE_THRESHOLD     = 24   # how many of the last N must be timeout/error to trigger
CONNECTIVITY_HOSTS   = ["https://1.1.1.1", "https://www.google.com"]
CONNECTIVITY_TIMEOUT = 5    # seconds for connectivity probe
RETRY_WAIT_SECS      = 15   # how long to wait between connectivity retries

HEADERS = {
    "User-Agent":    "Mozilla/5.0 (compatible; RadioStreamChecker/1.0)",
    "Icy-MetaData":  "0",
    "Accept":        "*/*",
    "Connection":    "close",
}


# ── Results DB ────────────────────────────────────────────────────────────────

def init_results_db(path: str) -> sqlite3.Connection:
    conn = sqlite3.connect(path, check_same_thread=False)
    conn.execute("""
        CREATE TABLE IF NOT EXISTS checked_stations (
            station_id  INTEGER PRIMARY KEY,
            name        TEXT,
            stream_url  TEXT,
            group_id    INTEGER,
            group_name  TEXT,
            status      TEXT,
            http_code   INTEGER,
            error_msg   TEXT,
            checked_at  TEXT
        )
    """)
    conn.execute("CREATE INDEX IF NOT EXISTS idx_status ON checked_stations (status)")
    conn.commit()
    return conn


def get_already_checked(conn: sqlite3.Connection) -> set:
    rows = conn.execute("SELECT station_id FROM checked_stations").fetchall()
    return {r[0] for r in rows}


def get_counts(conn: sqlite3.Connection) -> tuple[int, int]:
    ok   = conn.execute("SELECT COUNT(*) FROM checked_stations WHERE status = 'ok'").fetchone()[0]
    fail = conn.execute("SELECT COUNT(*) FROM checked_stations WHERE status != 'ok'").fetchone()[0]
    return ok, fail


# ── Internet connectivity check ───────────────────────────────────────────────

def internet_is_up() -> bool:
    """Try reaching a couple of reliable hosts. Returns True if any succeed."""
    for url in CONNECTIVITY_HOSTS:
        try:
            requests.get(url, timeout=CONNECTIVITY_TIMEOUT)
            return True
        except Exception:
            continue
    return False


def wait_for_internet():
    """Block until internet comes back. Prints status messages."""
    print("\n  !! INTERNET OUTAGE DETECTED — pausing until connection restores...")
    attempt = 0
    while True:
        attempt += 1
        print(f"  [connectivity check #{attempt}] waiting {RETRY_WAIT_SECS}s...")
        time.sleep(RETRY_WAIT_SECS)
        if internet_is_up():
            print("  !! Connection restored — resuming.\n")
            return


# ── Stream check ──────────────────────────────────────────────────────────────

def check_stream(url: str, timeout: int) -> tuple[str, int | None, str | None]:
    """
    Returns (status, http_code, error_msg)
      status: 'ok' | 'fail' | 'timeout' | 'error'

    'fail'    = server responded but with a bad code or empty body  (internet was up)
    'timeout' = no response within timeout                          (could be outage)
    'error'   = connection-level error: DNS, refused, etc.          (could be outage)

    The actual HTTP work runs in a daemon thread so that trickle-data streams
    (which reset the requests read-timeout on every byte) can never block
    indefinitely.  If the daemon thread doesn't finish within timeout+5 s we
    abandon it and return a hard-deadline timeout.
    """
    result: list = [None]

    def _do():
        try:
            with requests.get(
                url,
                headers=HEADERS,
                stream=True,
                timeout=(timeout, timeout),
                allow_redirects=True,
            ) as r:
                code = r.status_code
                if code == 200:
                    chunk = next(r.iter_content(READ_BYTES), b"")
                    if chunk:
                        result[0] = ("ok", code, None)
                    else:
                        result[0] = ("fail", code, "connected but empty response")
                else:
                    result[0] = ("fail", code, f"HTTP {code}")
        except Timeout:
            result[0] = ("timeout", None, "timed out")
        except ReqConnectionError as e:
            msg = str(e)
            if "Name or service not known" in msg or "getaddrinfo failed" in msg or "nodename nor servname" in msg:
                result[0] = ("error", None, "DNS lookup failed")
            elif "Connection refused" in msg:
                result[0] = ("error", None, "connection refused")
            else:
                result[0] = ("error", None, msg[:100])
        except RequestException as e:
            result[0] = ("error", None, str(e)[:100])
        except Exception as e:
            result[0] = ("error", None, str(e)[:100])

    t = threading.Thread(target=_do, daemon=True)
    t.start()
    t.join(timeout + 5)          # hard wall-clock deadline

    if result[0] is None:
        return "timeout", None, "hard deadline exceeded"
    return result[0]


# ── Outage tracker (thread-safe) ──────────────────────────────────────────────

class OutageTracker:
    """
    Watches the last N results. If too many are timeout/error (meaning internet
    could be down), triggers an outage flag so the main loop can pause and retry.

    Only 'timeout' and 'error' are suspicious — 'fail' (HTTP response received)
    means the internet was working fine for that station.
    """

    def __init__(self, window: int = OUTAGE_WINDOW, threshold: int = OUTAGE_THRESHOLD):
        self._lock     = threading.Lock()
        self._window   = window
        self._threshold = threshold
        self._recent: deque[bool] = deque(maxlen=window)  # True = suspicious result
        self.paused    = threading.Event()  # set while waiting for internet

    def record(self, status: str):
        suspicious = status in ("timeout", "error")
        with self._lock:
            self._recent.append(suspicious)
            if len(self._recent) == self._window and sum(self._recent) >= self._threshold:
                # Looks like an outage — clear the window so we don't re-trigger
                # immediately after recovery, and signal the main thread.
                self._recent.clear()
                return True  # caller should trigger outage handling
        return False

    def wait_if_paused(self):
        self.paused.wait()  # blocks if paused event is clear


# ── Main ──────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="Radio stream checker — checks all stations and records failures.")
    parser.add_argument("--workers",  type=int, default=DEFAULT_WORKERS,  help=f"Concurrent workers (default: {DEFAULT_WORKERS})")
    parser.add_argument("--timeout",  type=int, default=DEFAULT_TIMEOUT,  help=f"Timeout per station in seconds (default: {DEFAULT_TIMEOUT})")
    parser.add_argument("--db",       type=str, default=DEFAULT_SRC_DB,   help=f"Source database path (default: {DEFAULT_SRC_DB})")
    parser.add_argument("--results",  type=str, default=DEFAULT_RESULTS,  help=f"Results database path (default: {DEFAULT_RESULTS})")
    parser.add_argument("--recheck",  action="store_true",                help="Re-test only previously-ok stations; downgrade any that now fail")
    args = parser.parse_args()

    src_path = Path(args.db)
    if not src_path.exists():
        print(f"ERROR: Source DB not found: {src_path.resolve()}")
        print(f"       Run from the project root, or pass --db <path>")
        return

    print("=" * 60)
    print("  Radio Stream Checker")
    print("=" * 60)
    print(f"  Source DB  : {src_path.resolve()}")
    print(f"  Results DB : {Path(args.results).resolve()}")
    print(f"  Workers    : {args.workers}")
    print(f"  Timeout    : {args.timeout}s per station")
    print(f"  Mode       : {'RECHECK ok stations' if args.recheck else 'Full scan'}")
    print(f"  Outage det.: pauses if {OUTAGE_THRESHOLD}/{OUTAGE_WINDOW} recent results are timeout/error")
    print("=" * 60)

    # Open source DB read-only
    src_conn = sqlite3.connect(f"file:{src_path.resolve()}?mode=ro", uri=True)

    # Load groups ordered smallest → largest (by total stations in source DB)
    groups = src_conn.execute("""
        SELECT g.Id, g.Name, COUNT(s.Id) AS cnt
        FROM   Groups g
        JOIN   Stations s ON s.GroupId = g.Id
        GROUP  BY g.Id
        ORDER  BY cnt ASC
    """).fetchall()

    total_stations = sum(g[2] for g in groups)
    print(f"\n  Groups   : {len(groups)}")
    print(f"  Stations : {total_stations:,}")

    # Init results DB
    results_conn = init_results_db(args.results)
    results_lock = threading.Lock()
    outage       = OutageTracker()
    outage.paused.set()  # not paused initially (Event.set = "go ahead")

    ok_count, fail_count = get_counts(results_conn)

    if args.recheck:
        # Re-check mode: skip only confirmed failures. Everything else gets tested:
        #   - status = 'ok'      → re-test (might have gone offline since last run)
        #   - never checked      → test fresh (missed in first run)
        #   - status = fail/etc  → skip (already confirmed bad)
        # Resume works naturally: stations downgraded to a failure status during
        # this recheck run are already in confirmed_failures, so they get skipped
        # if we restart mid-run.
        confirmed_failures = set(
            row[0] for row in results_conn.execute(
                "SELECT station_id FROM checked_stations WHERE status != 'ok'"
            ).fetchall()
        )
        already_checked = set()   # stations checked in this session (for resume)
        checked_count   = 0
        ok_count_before = ok_count  # snapshot to compute downgrades at the end
        recheck_skip    = len(confirmed_failures)
        recheck_target  = total_stations - recheck_skip
        print(f"\n  Skipping {recheck_skip:,} confirmed failures.")
        print(f"  Will check {recheck_target:,} stations (ok + never checked).")
    else:
        already_checked = get_already_checked(results_conn)
        checked_count   = len(already_checked)
        if already_checked:
            print(f"\n  Resuming — {checked_count:,} already checked, {total_stations - checked_count:,} remaining.")

    print()

    # Stations that came back timeout/error during a suspected outage window
    # are held here and re-queued rather than saved as failures.
    retry_queue: list = []
    retry_lock = threading.Lock()

    def save(station_id, name, stream_url, group_id, group_name, status, http_code, error_msg):
        nonlocal checked_count, ok_count, fail_count
        with results_lock:
            results_conn.execute("""
                INSERT OR REPLACE INTO checked_stations
                  (station_id, name, stream_url, group_id, group_name, status, http_code, error_msg, checked_at)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """, (
                station_id, name, stream_url, group_id, group_name,
                status, http_code, error_msg,
                datetime.now(timezone.utc).isoformat()
            ))
            results_conn.commit()
            checked_count += 1
            if status == "ok":
                ok_count += 1
            else:
                fail_count += 1

    def handle_result(sid, sname, surl, group_id, group_name, status, http_code, error_msg):
        """
        Process one completed check result.
        If an outage is detected, hold suspicious results for retry instead of saving them.
        """
        outage_triggered = outage.record(status)

        if outage_triggered:
            # We just hit the threshold — pause the tracker
            outage.paused.clear()

        if not outage.paused.is_set():
            # Outage is currently active (or just triggered).
            # If this result is suspicious, hold it for retry.
            if status in ("timeout", "error"):
                with retry_lock:
                    retry_queue.append((sid, sname, surl))
                return  # don't save, don't mark as checked

        # Normal path — save the result
        already_checked.add(sid)
        save(sid, sname, surl, group_id, group_name, status, http_code, error_msg)

        symbol   = "✓" if status == "ok" else "✗"
        code_str = str(http_code) if http_code else "—"
        note     = f"  [{error_msg}]" if error_msg else ""
        print(f"  {checked_count:<6,}  {symbol} {status:<6}  {code_str:<5}  {sname[:60]}{note}")

    def run_batch(pending, group_id, group_name, timeout):
        """Submit a list of stations to the thread pool and collect results.

        check_stream() always returns within timeout+5 s (daemon-thread guarantee),
        so as_completed() never blocks indefinitely here.
        """
        with ThreadPoolExecutor(max_workers=args.workers) as executor:
            futures = {
                executor.submit(check_stream, surl, timeout): (sid, sname, surl)
                for sid, sname, surl in pending
            }
            for future in as_completed(futures):
                sid, sname, surl = futures[future]
                try:
                    status, http_code, error_msg = future.result()
                except Exception as e:
                    status, http_code, error_msg = "error", None, str(e)[:100]

                handle_result(sid, sname, surl, group_id, group_name, status, http_code, error_msg)

                if not outage.paused.is_set():
                    wait_for_internet()
                    outage.paused.set()
                    with retry_lock:
                        to_retry = list(retry_queue)
                        retry_queue.clear()
                    if to_retry:
                        print(f"\n  Retrying {len(to_retry)} stations that were affected by the outage...")
                        run_batch(to_retry, group_id, group_name, timeout)

    # Pre-compute which group IDs are fully done so we can skip them instantly.
    # A group is fully done when every station in it appears in already_checked
    # (or confirmed_failures in recheck mode). We get this cheaply from the DB
    # by comparing per-group counts rather than iterating station-by-station.
    if args.recheck:
        skip_set = confirmed_failures | already_checked
    else:
        skip_set = already_checked

    done_groups: set[int] = set()
    for gid, _, gsize in groups:
        ids_in_group = {row[0] for row in src_conn.execute(
            "SELECT Id FROM Stations WHERE GroupId = ?", (gid,)
        ).fetchall()}
        if ids_in_group and ids_in_group.issubset(skip_set):
            done_groups.add(gid)

    if done_groups:
        print(f"  Skipping {len(done_groups)} fully-checked group(s) — jumping to first remaining group.\n")

    try:
        for group_id, group_name, group_size in groups:
            stations = src_conn.execute(
                "SELECT Id, Name, StreamUrl FROM Stations WHERE GroupId = ?", (group_id,)
            ).fetchall()

            if args.recheck:
                # Skip confirmed failures; include ok + never-checked + not done this session
                pending = [
                    (sid, sname, surl) for sid, sname, surl in stations
                    if sid not in confirmed_failures and sid not in already_checked
                ]
                if not pending:
                    continue
                skipped   = group_size - len(pending)
                skip_note = f", {skipped} confirmed failures skipped" if skipped else ""
                print(f"\n[GROUP] {group_name}  ({len(pending)} to check{skip_note})")
            else:
                if group_id in done_groups:
                    continue  # already fully checked — skip silently
                pending = [(sid, sname, surl) for sid, sname, surl in stations if sid not in already_checked]
                if not pending:
                    continue
                skipped   = group_size - len(pending)
                skip_note = f", {skipped} already done" if skipped else ""
                print(f"\n[GROUP] {group_name}  ({len(pending)} to check{skip_note})")

            print(f"  {'#':<6}  {'Status':<8}  {'Code':<5}  Station")
            print(f"  {'-'*6}  {'-'*8}  {'-'*5}  {'-'*40}")

            run_batch(pending, group_id, group_name, args.timeout)

    except KeyboardInterrupt:
        print("\n\n  Interrupted — progress is saved. Run again to resume.")

    # ── Summary ───────────────────────────────────────────────────────────────
    print()
    print("=" * 60)
    if args.recheck:
        ok_now, fail_now = get_counts(results_conn)
        downgraded = ok_count_before - ok_now  # how many ok→fail during this recheck
        print(f"  Re-checked : {checked_count:,}")
        print(f"  Still ok   : {ok_now:,}")
        print(f"  Downgraded : {downgraded:,}  (were ok, now fail/timeout/error)")
        print(f"  Total fail : {fail_now:,}")
    else:
        print(f"  Checked : {checked_count:,} / {total_stations:,}")
        print(f"  OK      : {ok_count:,}")
        print(f"  Failed  : {fail_count:,}  (fail + timeout + error)")
    print(f"  Results : {Path(args.results).resolve()}")
    print("=" * 60)

    if fail_count:
        print(f"""
  To inspect failures:
    sqlite3 {args.results} "SELECT name, status, http_code, error_msg FROM checked_stations WHERE status != 'ok' LIMIT 20"

  To export failure list to CSV:
    sqlite3 -csv {args.results} "SELECT station_id, name, stream_url, group_name, status, error_msg FROM checked_stations WHERE status != 'ok'" > failed_stations.csv
""")

    src_conn.close()
    results_conn.close()


if __name__ == "__main__":
    main()
