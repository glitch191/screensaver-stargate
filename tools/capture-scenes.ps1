# Captures the scene in both wall modes at the reference sizes.
# Usage: .\tools\capture-scenes.ps1 [-Exe path] [-OutDir folder] [-Sizes 1920x1080,...] [-Extra "--seed 7"]
param(
    [string]$Exe = "",
    [string]$OutDir = "screenshots",
    [string[]]$Sizes = @("1920x1080", "2560x1440", "2560x1080", "3440x1440"),
    [string[]]$Modes = @("perspective", "flat"),
    [string]$Seed = "7"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Exe) {
    $Exe = Get-ChildItem -Path (Join-Path $root "bin") -Recurse -Filter "ScreensaverStargate.exe" |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Exe -or -not (Test-Path $Exe)) { throw "ScreensaverStargate.exe not found. Build the project first." }

New-Item -ItemType Directory -Force $OutDir | Out-Null
foreach ($mode in $Modes) {
    foreach ($size in $Sizes) {
        $file = Join-Path $OutDir "$mode-$size.png"
        $p = Start-Process -FilePath $Exe -ArgumentList "--screenshot `"$file`" --size $size --seed $Seed --mode $mode" -PassThru -Wait
        if ($p.ExitCode -ne 0) { throw "Capture failed for $mode $size (exit code $($p.ExitCode))." }
        Write-Host "Saved $file"
    }
}
