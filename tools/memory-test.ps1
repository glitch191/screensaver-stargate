# Runs the scene in a window with diagnostics and samples the process memory every minute.
# Usage: .\tools\memory-test.ps1 [-Minutes 30] [-Exe publish\ScreensaverStargate.exe] [-Size 1920x1080]
param(
    [int]$Minutes = 30,
    [string]$Exe = "publish\ScreensaverStargate.exe",
    [string]$Size = "1920x1080",
    [string]$Csv = "screenshots\raw\memory.csv"
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path $Exe)) { throw "$Exe not found. Run build.ps1 first. (A .scr started through the shell always runs /S, so the published .exe is used.)" }
New-Item -ItemType Directory -Force (Split-Path $Csv) | Out-Null

$p = Start-Process -FilePath $Exe -ArgumentList "--windowed --size $Size --diag" -PassThru
Start-Sleep -Seconds 5
"minute,private_mb,working_set_mb,handles,threads,cpu_seconds" | Out-File $Csv -Encoding utf8
for ($m = 0; $m -le $Minutes; $m++) {
    $p.Refresh()
    if ($p.HasExited) { throw "The process exited early with code $($p.ExitCode)." }
    $line = "{0},{1:N1},{2:N1},{3},{4},{5:N1}" -f $m, ($p.PrivateMemorySize64 / 1MB), ($p.WorkingSet64 / 1MB), $p.HandleCount, $p.Threads.Count, $p.TotalProcessorTime.TotalSeconds
    $line | Out-File $Csv -Append -Encoding utf8
    Write-Host $line
    if ($m -lt $Minutes) { Start-Sleep -Seconds 60 }
}
Stop-Process -Id $p.Id
Write-Host "Samples written to $Csv"
