# Checks settings robustness and the frame rate options by writing settings.json,
# running the scene in a window with diagnostics, and reading the log.
# Any existing settings file is backed up and restored at the end.
# Usage: .\tools\test-settings.ps1 [-Exe publish\ScreensaverStargate.exe]
param([string]$Exe = "publish\ScreensaverStargate.exe")

$ErrorActionPreference = "Stop"
$folder = Join-Path $env:APPDATA "ScreensaverStargate"
$file = Join-Path $folder "settings.json"
$log = Join-Path $folder "log.txt"
$backup = "$file.testbackup"
New-Item -ItemType Directory -Force $folder | Out-Null
if (Test-Path $file) { Copy-Item $file $backup -Force }

function Run-Windowed([int]$seconds) {
    $p = Start-Process -FilePath $Exe -ArgumentList "--windowed --size 1280x720 --diag" -PassThru
    Start-Sleep -Seconds $seconds
    $p.CloseMainWindow() | Out-Null
    if (-not $p.WaitForExit(5000)) { Stop-Process -Id $p.Id; throw "The window did not close." }
    return $p.Id
}
function Log-Lines([int]$id) { Get-Content $log | Where-Object { $_ -match "\[$id\]" } }

try {
    # 1. Corrupted file: must fall back to defaults without an error dialog.
    Set-Content -Path $file -Value "{ this is not json" -Encoding utf8
    $id = Run-Windowed 3
    Log-Lines $id | Where-Object { $_ -match "Settings file|Render loop ended" } | ForEach-Object { Write-Host "corrupted file:  $_" }

    # 2. Out-of-range and unknown values are clamped or replaced.
    Set-Content -Path $file -Value '{ "Speed": 999, "WallMode": "Sideways", "CenterLineWidth": -4 }' -Encoding utf8
    $id = Run-Windowed 3
    Log-Lines $id | Where-Object { $_ -match "Settings file|Render loop ended" } | ForEach-Object { Write-Host "invalid values:  $_" }

    # 3. Frame rate options.
    $cases = @(
        @{ Name = "vsync on, automatic"; Json = '{ "VerticalSync": true, "FrameRateLimit": "Automatic" }' },
        @{ Name = "vsync off, custom 144"; Json = '{ "VerticalSync": false, "FrameRateLimit": "Custom", "CustomFrameRate": 144 }' },
        @{ Name = "vsync off, custom 60"; Json = '{ "VerticalSync": false, "FrameRateLimit": "Custom", "CustomFrameRate": 60 }' },
        @{ Name = "vsync off, automatic"; Json = '{ "VerticalSync": false, "FrameRateLimit": "Automatic" }' },
        @{ Name = "vsync off, unlimited"; Json = '{ "VerticalSync": false, "FrameRateLimit": "Unlimited" }' }
    )
    foreach ($c in $cases) {
        Set-Content -Path $file -Value $c.Json -Encoding utf8
        $id = Run-Windowed 6
        $line = Log-Lines $id | Where-Object { $_ -match "Diagnostics, last" } | Select-Object -Last 1
        Write-Host ("{0,-24} {1}" -f $c.Name, ($line -replace '^.*Diagnostics, last 2 s: ', ''))
    }
}
finally {
    Remove-Item $file -ErrorAction SilentlyContinue
    if (Test-Path $backup) { Move-Item $backup $file -Force }
}
