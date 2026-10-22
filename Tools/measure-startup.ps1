<#
.SYNOPSIS
    Measures RadioV2 startup time and idle resource footprint.

.DESCRIPTION
    Launches the app, times how long until its window is actually visible,
    then samples threads, handles, working set, private bytes and CPU once a
    second while it sits idle. Prints a summary and stops the process.

    This is the acceptance check for the performance work in
    Docs/STABILITY_PLAN.md. Baseline on v1.0.6 was:
        window 3.27 s | threads 24-34 | handles 2591 | WS 731 MB | priv 523 MB

.EXAMPLE
    .\Tools\measure-startup.ps1
    .\Tools\measure-startup.ps1 -ExePath .\publish\RadioV2.exe -IdleSeconds 120
#>
[CmdletBinding()]
param(
    [string] $ExePath = (Join-Path $env:LOCALAPPDATA 'RadioV2\RadioV2.exe'),
    [int]    $IdleSeconds = 45,
    [switch] $KeepRunning
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ExePath)) {
    throw "Executable not found: $ExePath"
}

if (Get-Process RadioV2 -ErrorAction SilentlyContinue) {
    throw "RadioV2 is already running. Close it first - it is single-instance, so a second launch would exit immediately and measure nothing."
}

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;

public class RadioV2WindowProbe {
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowText(IntPtr h, StringBuilder s, int c);
    delegate bool EnumProc(IntPtr h, IntPtr l);

    // Process.MainWindowHandle is unreliable for WPF FluentWindow during startup,
    // so enumerate instead and look for a visible window owned by the process.
    public static bool HasVisibleWindow(int pid) {
        bool found = false;
        EnumWindows((h, l) => {
            uint p;
            GetWindowThreadProcessId(h, out p);
            if (p == pid && IsWindowVisible(h)) {
                var t = new StringBuilder(64);
                GetWindowText(h, t, 64);
                if (t.ToString() == "RadioV2") found = true;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@

Write-Host "Launching $ExePath" -ForegroundColor Cyan

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process $ExePath -PassThru
$windowAt = $null

while ($sw.Elapsed.TotalSeconds -lt 60) {
    if ($proc.HasExited) { throw "Process exited during startup with code $($proc.ExitCode)." }
    if ([RadioV2WindowProbe]::HasVisibleWindow($proc.Id)) {
        $windowAt = $sw.Elapsed.TotalSeconds
        break
    }
    Start-Sleep -Milliseconds 100
}

if (-not $windowAt) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    throw "No visible window appeared within 60 s."
}

Write-Host ("Window visible after {0:N2} s" -f $windowAt) -ForegroundColor Green
Write-Host ""
Write-Host "  time  threads  handles        WS      private      CPU"
Write-Host "  ----  -------  -------  --------  -----------  -------"

$samples = @()
for ($i = 0; $i -lt $IdleSeconds; $i++) {
    if ($proc.HasExited) { Write-Warning "Process exited after $i s."; break }
    $proc.Refresh()
    $s = [pscustomobject]@{
        Threads = $proc.Threads.Count
        Handles = $proc.HandleCount
        WsMB    = $proc.WorkingSet64 / 1MB
        PrivMB  = $proc.PrivateMemorySize64 / 1MB
        Cpu     = $proc.TotalProcessorTime.TotalSeconds
    }
    $samples += $s
    # Only print every fifth sample so a long run stays readable.
    if ($i % 5 -eq 0) {
        Write-Host ("  {0,3}s  {1,7}  {2,7}  {3,6:N0}MB  {4,9:N0}MB  {5,5:N1}s" -f $i, $s.Threads, $s.Handles, $s.WsMB, $s.PrivMB, $s.Cpu)
    }
    Start-Sleep -Seconds 1
}

$last = $samples[-1]
$cpuAt45 = ($samples | Where-Object { $true } | Select-Object -Last 1).Cpu

Write-Host ""
Write-Host "SUMMARY" -ForegroundColor Cyan
Write-Host ("  Time to visible window : {0,8:N2} s     (baseline 3.27, target < 1.00)" -f $windowAt)
Write-Host ("  CPU over {0,3} s idle     : {1,8:N1} s     (baseline 9.5,  target < 3.0)" -f $samples.Count, $cpuAt45)
Write-Host ("  Working set            : {0,8:N0} MB    (baseline 731,  target < 200)" -f $last.WsMB)
Write-Host ("  Private bytes          : {0,8:N0} MB    (baseline 523,  target < 150)" -f $last.PrivMB)
Write-Host ("  Handles                : {0,8:N0}       (baseline 2591, target < 800)" -f $last.Handles)
Write-Host ("  Threads                : {0,8:N0}       (baseline 24-34, not a defect)" -f $last.Threads)
# Growth is measured from a settled sample, not from sample 0. The first seconds are
# still startup — schema init, session restore, pre-warm — and diffing against them
# reports ordinary startup allocation as a leak. A 10 minute soak that was flat to
# within 1 MB still showed "76 MB growth" when this diffed against sample 0.
$settleIndex = [Math]::Min(20, $samples.Count - 1)
$settled = $samples[$settleIndex]
Write-Host ("  Working set growth     : {0,8:N1} MB    (from {1}s to end; should be ~0)" -f ($last.WsMB - $settled.WsMB), $settleIndex)

if ($KeepRunning) {
    Write-Host ""
    Write-Host "Leaving the app running (-KeepRunning)." -ForegroundColor Yellow
} else {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
}
