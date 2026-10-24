# Desktop refinements — 2026-10-24

Reference: Android 1.0.8+10. Desktop base: 9353250 (v1.1.1). Lead design skill:
emil-design-eng. Prepared for the v1.1.2 release. Local installation is separate.

| Before | After | Why |
| --- | --- | --- |
| Empty artwork squares, cropped station logos | Shared 40×40 contained logos with neutral, theme-aware grapheme initials | Keep branding and a stable footprint while loading or after failure |
| Nonempty URL treated as artwork; name-only ties | Featured first, usable URL/artwork knowledge next, name then ID | Deterministic pagination and search without network preflight |
| Image failures unknown to ordering | Session-only knowledge, frozen once per group visit | Failures never remove or reorder a station already shown |
| Offline station status red; transient notice without an action | Neutral, bounded notice with Retry, dismissal and duplicate suppression | Routine recovery stays calm and actionable |
| 22% play pulse, 40% heart pulse, 850 ms carousel motion | 140 ms subtle pointer feedback; 240 ms carousel scroll | Frequent actions feel quick; keyboard playback remains immediate |
| Group art zoomed/cropped on hover | Square art fully visible, same assets and colours | Protect the existing desktop identity |

The artwork cache records only ordinary-request 404/410 responses and unusable
image data/size. It expires after six hours, holds at most 256 URL entries, and
does not persist, fetch, notify lists or add startup disk work. Authentication
responses, timeouts, offline/DNS failures, 429 and 5xx do not become remembered
failures. SVG paths are recognized without fetching. Other unsupported codecs
are learned from actual decoding. An unseen broken image can show initials on
its first encounter; the next visit can use that knowledge for ordering.

Group requests carry a generation/cancellation scope. Late group, featured and
search results cannot overwrite a newer visit/query. Featured rows stay pinned
even when a featured query finishes after a search. All ordering stays in SQL
before LIMIT/OFFSET. No all-station materialization or image preflight was added.

Native media keys, tray/media integration and serious-error handling remain.
There is no expanded playback screen in this desktop app; none was introduced.
Playing indicators stop on unload, hidden pages, inactive/minimized windows and
Windows reduced-motion preference, retaining a static playing indicator.

## Measurements

Same Debug harness, catalog, dimensions and logging setting. Repeated original
build from a temporary 9353250 source copy versus final refined source. These are
local observations, not statistically controlled speedup claims.

| Metric | Repeated baseline | Final |
| --- | ---: | ---: |
| Six group opens/revisits, query + WPF dispatcher/layout settle | 182–245 ms | 176–238 ms |
| Median across six opens | 207 ms | 190 ms |
| Scroll viewport/dispatcher response, median / p95 | 101 / 122 ms | 92 / 113 ms |
| Realized row containers after loading 157 items | 9 | 9 |
| Startup to loaded window, excluding process launch | 2.69 s | 2.46 s |
| Process CPU over settled 10-second idle sample | 2.20 s | 1.80 s |
| Settled working set / private bytes | 317 / 202 MB | 320 / 204 MB |
| Settled process handles | 2,633 | 2,641 |
| Successful warm thumbnail decoding samples | 1.3–2.1 ms | 1.1–2.1 ms |
| Thumbnail decode width | 96 px | 96 px |

The existing external startup script also measured actual executables: baseline
first launch 6.49 s, warm repeat 3.34 s; refined warm launch 2.65 s. External
10-second working-set snapshots varied 302–322 MB for baseline and 314 MB for
refined. The script's CPU total includes startup, so the settled probe delta above
is the idle CPU comparison. Do not compare the cold 6.49 s directly with warm
2.65 s as an improvement claim.

An initial comparison showed a 110/157 ms scroll median/p95 and noisy 6.53-second
idle CPU sample. Investigation found that SQL OR did not short-circuit URL path
parsing. Using CASE limits that work to SVG candidates. Repeated baseline and
final measurements above show no material regression; the first idle outlier's
cause was not established. The historic Discover redesign regression remains
attributed only to the rollback boundary, not to an assumed Android cause.

Raw local evidence: `Docs/performance-before-repeat.json`,
`Docs/performance-final.json`, `Docs/startup-before*.txt`,
`Docs/startup-after.txt`, `Docs/review/review-checks.json` and review PNGs.
`Docs/` is intentionally git-ignored; the harness and this report are reviewable
source files. See README.md for reproducible commands and timing definitions.

## Verification and limits

- Build succeeded with zero warnings/errors; 111 tests passed; `git diff --check`
  passed. Tests cover Unicode/graphemes, failure expiration/bounds, HTTP/cache
  behavior, deterministic ties, pagination/search snapshots, stale requests,
  featured-result races, message coalescing and retry of the current station.
- Native desktop inspected in both themes: favourites, working/fallback logos,
  genre cards, group search and scrolling. 100FM connected, played and paused.
- Review harness exercised real SQLite favourite addition/removal with restoration,
  actual VLC loopback failure and the real Retry button handler in both themes.
  Both themes were rendered with long names, Hebrew, Japanese, emoji and combining
  marks. Portable PNGs composite WPF over the theme base; Mica was inspected live.
- Lazy rows, 30-station group pages, existing bounded download concurrency, disk
  image cache, 96-pixel decoding and stable station IDs remain intact.
- Display/compositor FPS, long-duration resource soak, live Windows reduced-motion
  setting changes, high-DPI/multi-monitor variants, screen-reader announcements,
  and end-to-end hardware media-key/system-notification behavior were not verified.
  Their existing integrations were preserved. External image-host reliability
  cannot be guaranteed; unknown broken artwork is handled on first encounter.
