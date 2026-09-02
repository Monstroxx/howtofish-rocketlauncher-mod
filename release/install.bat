# RocketLauncherMod installer (Windows)
# Right-click -> "Run with PowerShell"
$ErrorActionPreference = "Stop"

$ModDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Dll = Join-Path $ModDir "RocketLauncherMod.dll"
$AssetsDir = Join-Path $ModDir "assets"

if (-not (Test-Path $Dll)) {
    Write-Host "[!] RocketLauncherMod.dll not found (must sit next to install.bat)." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# --- Locate the game ---------------------------------------------------
$Candidates = @(
    "$env:ProgramFiles(x86)\Steam\steamapps\common\How to Fish\How to Fish",
    "$env:ProgramFiles\Steam\steamapps\common\How to Fish\How to Fish",
    "${env:ProgramFiles(x86)}\Steam\steamapps\common\How to Fish\How to Fish",
    "$env:LOCALAPPDATA\Steam\steamapps\common\How to Fish\How to Fish"
)

# Steam library registry key
try {
    $SteamPath = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction Stop).SteamPath
    if ($SteamPath) {
        $Candidates += (Join-Path $SteamPath "steamapps\common\How to Fish\How to Fish")
        $Vdf = Join-Path $SteamPath "steamapps\libraryfolders.vdf"
        if (Test-Path $Vdf) {
            Get-Content $Vdf | Select-String '"path"' | ForEach-Object {
                $p = ($_ -split '"')[3] -replace '\\\\', '\'
                $Candidates += (Join-Path $p "steamapps\common\How to Fish\How to Fish")
            }
        }
    }
} catch {}

$GameDir = $null
foreach ($c in $Candidates) {
    if ($c -and (Test-Path $c)) { $GameDir = $c; break }
}

if (-not $GameDir) {
    $GameDir = Read-Host "[!] Game not found. Full path to the game folder (contains 'How to Fish_Data')"
    if (-not (Test-Path (Join-Path $GameDir "How to Fish_Data"))) {
        Write-Host "[!] Invalid path. Aborting." -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }
}

Write-Host "[+] Game found: $GameDir" -ForegroundColor Green
$DataDir = Join-Path $GameDir "How to Fish_Data"
$Plugins = Join-Path $GameDir "BepInEx\plugins"
$ModsAssets = Join-Path $DataDir "StreamingAssets\mods"

# --- Check BepInEx ------------------------------------------------------
if (-not (Test-Path (Join-Path $GameDir "BepInEx"))) {
    Write-Host "[!] BepInEx is NOT installed." -ForegroundColor Yellow
    Write-Host "    1. Download BepInEx 5.4.23+ (x64 Windows): https://github.com/BepInEx/BepInEx/releases"
    Write-Host "    2. Extract it into: $GameDir"
    Write-Host "    3. Start the game ONCE, then run the installer again."
    $bex = Read-Host "Or enter the path to an already-extracted BepInEx folder (Enter = abort)"
    if ($bex -and (Test-Path (Join-Path $bex "core\BepInEx.dll"))) {
        Copy-Item "$bex\*" $GameDir -Recurse -Force
        Write-Host "[+] BepInEx copied" -ForegroundColor Green
    } else {
        Read-Host "Aborting. Press Enter to exit"
        exit 1
    }
}

# --- Install plugin + assets ---------------------------------------------
New-Item -ItemType Directory -Force -Path $Plugins, $ModsAssets | Out-Null
Copy-Item $Dll $Plugins -Force
Write-Host "[+] RocketLauncherMod.dll installed" -ForegroundColor Green

if (Test-Path $AssetsDir) {
    Copy-Item "$AssetsDir\*.obj", "$AssetsDir\*.png" $ModsAssets -Force
    Write-Host "[+] Assets installed" -ForegroundColor Green
} else {
    Write-Host "[!] Warning: assets/ folder missing." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== DONE ===" -ForegroundColor Green
Write-Host "Start the game, as host, open chat, type '/rocket'."
Write-Host "Config after first launch: $GameDir\BepInEx\config\com.kimox.rocketlauncher.cfg"
Read-Host "Press Enter to exit"