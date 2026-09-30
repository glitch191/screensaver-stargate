# Builds dist\ScreensaverStargate.scr: a self-contained, single-file Windows screensaver.
# Usage: .\build.ps1 [-SkipChecks]
# Needs the .NET 10 SDK (no administrator rights needed; it can live in %LOCALAPPDATA%\Microsoft\dotnet).
param([switch]$SkipChecks)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$name = "ScreensaverStargate"

function Find-Dotnet {
    $candidates = @()
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $candidates += $cmd.Source }
    $candidates += (Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe")
    $candidates += (Join-Path $env:ProgramFiles "dotnet\dotnet.exe")
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) {
            $sdks = & $c --list-sdks 2>$null
            if ($sdks | Where-Object { $_ -match '^10\.' }) { return $c }
        }
    }
    throw "The .NET 10 SDK was not found. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 (or with dotnet-install.ps1 -Channel 10.0 for a per-user install) and run build.ps1 again."
}

$dotnet = Find-Dotnet
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
Write-Host "Using $dotnet"

if (-not $SkipChecks) {
    Write-Host "Running timing checks..."
    & $dotnet run --project (Join-Path $root "tests\TimingCheck") -c Release
    if ($LASTEXITCODE -ne 0) { throw "Timing checks failed." }
}

$publish = Join-Path $root "publish"
$dist = Join-Path $root "dist"
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
Write-Host "Publishing..."
& $dotnet publish (Join-Path $root "$name.csproj") -c Release -o $publish -warnaserror
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

New-Item -ItemType Directory -Force $dist | Out-Null
$exe = Join-Path $publish "$name.exe"
$scr = Join-Path $dist "$name.scr"
Copy-Item $exe $scr -Force

$extra = Get-ChildItem $publish -File | Where-Object { $_.Extension -notin ".exe", ".pdb" }
if ($extra) { Write-Warning "Unexpected files next to the executable: $($extra.Name -join ', ')" }

$size = (Get-Item $scr).Length
Write-Host ("Built {0} ({1:N1} MB, {2:N0} bytes)" -f $scr, ($size / 1MB), $size)
Write-Host "Install: right-click the .scr file and choose Install."
