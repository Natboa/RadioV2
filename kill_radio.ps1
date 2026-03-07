$procs = Get-Process | Where-Object { $_.ProcessName -like '*Radio*' -or $_.MainWindowTitle -like '*Radio*' }
if ($procs) {
    Write-Host "Found processes:"
    $procs | Select-Object Id, ProcessName, CPU | Format-Table
    $procs | Stop-Process -Force
    Write-Host "Killed all."
} else {
    Write-Host "No RadioV2 processes found."
}
