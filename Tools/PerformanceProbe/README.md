# Native desktop performance and visual probe

Close RadioV2 first. This opt-in tool hosts the actual WPF App with the existing
catalog, navigation, controls, startup, image loader and services. It has no product
startup hook or background activity when not explicitly run.

```powershell
dotnet build Tools/PerformanceProbe/PerformanceProbe.csproj
$env:Logging__LogLevel__Default = 'Warning'
dotnet Tools/PerformanceProbe/bin/Debug/net8.0-windows/PerformanceProbe.dll Docs/profile.json
dotnet Tools/PerformanceProbe/bin/Debug/net8.0-windows/PerformanceProbe.dll Docs/review/review.json --review
```

The performance run waits 20 seconds for startup to settle, measures 10 seconds
of idle process CPU and resources, opens Brazil/Rock/Israel/Jazz and revisits two
groups, scrolls 30 times, searches and samples the existing image-loader cache.
`renderReadyMs` includes query, WPF layout/dispatcher draining and a 20 ms settling
delay. Scroll measurements are dispatcher/viewport response, **not display FPS**.
Realized row count checks that pagination does not realize the whole loaded list.
Network image latency is variable; compare cached successful images separately.
Startup is measured from App construction to the first loaded-window observation,
excluding process launch; use `Tools/measure-startup.ps1` for external launch timing.

The review run puts Unicode, long-name and missing/SVG-logo fixtures in the Browse
view model only. A refused local stream exercises VLC's real failure callback and
the actual Retry handler in both themes. It adds/removes one favourite through
SQLite and restores it in `finally`. It restores the original station and theme;
playback ends stopped. Run after a real favourite has been restored as the current
station. PNGs use the theme base brush because WPF's bitmap renderer cannot capture
DWM's Mica backdrop; inspect the native app separately for Mica and pointer motion.

For before/after comparisons use the same harness, Debug/Release configuration,
catalog, logging level and window dimensions. Run builds/tests outside the timed
portion. Repeat anomalous samples; do not infer causation from one noisy run.
