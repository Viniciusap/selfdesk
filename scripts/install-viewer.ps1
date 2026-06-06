<#
.SYNOPSIS
    Installs or updates the SelfDesk Viewer from the latest GitHub release.

.DESCRIPTION
    Usage:
      irm https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/install-viewer.ps1 | iex
      .\install-viewer.ps1 -InstallDir C:\tools\selfdesk-viewer

    What it does:
      1. Downloads selfdesk-viewer-win-x64.zip from the latest release (GitHub)
      2. Stops any running SelfDesk.Viewer process
      3. Extracts the zip, preserving .env and certs/
      4. Launches the viewer

.PARAMETER InstallDir
    Viewer installation directory. Default: C:\tools\selfdesk-viewer
#>

param(
    [string]$InstallDir = 'C:\tools\selfdesk-viewer'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Banner ────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\" -ForegroundColor Cyan
Write-Host "|                                                         |" -ForegroundColor Cyan
Write-Host "\  ___           _      ___  ___                 _        /" -ForegroundColor Cyan
Write-Host "- (  _`\        (_ )  /'___)(  _`\              ( )       -" -ForegroundColor Cyan
Write-Host "/ | (_(_)   __   | | | (__  | | ) |   __    ___ | |/')    \" -ForegroundColor Cyan
Write-Host "| `\__ \  /'__`\ | | | ,__) | | | ) /'__`\/',__)| , <     |" -ForegroundColor Cyan
Write-Host "\ ( )_) |(  ___/ | | | |    | |_) |(  ___/\__, \| |\`\    /" -ForegroundColor Cyan
Write-Host "- `\____)`\____)(___)(_)    (____/'`\____)(____/(_) (_)   -" -ForegroundColor Cyan
Write-Host "/                                                         \" -ForegroundColor Cyan
Write-Host "|                                                         |" -ForegroundColor Cyan
Write-Host "\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Viewer Installer  —  github.com/Viniciusap/selfdesk" -ForegroundColor DarkCyan
Write-Host ""
# ─────────────────────────────────────────────────────────────────────────────

$RepoOwner   = 'Viniciusap'
$RepoName    = 'selfdesk'
$AssetName   = 'selfdesk-viewer-win-x64.zip'
$DownloadUrl = "https://github.com/$RepoOwner/$RepoName/releases/latest/download/$AssetName"

function Write-Step { param([string]$Msg) Write-Host "`n-> $Msg" -ForegroundColor Cyan }
function Write-OK   { param([string]$Msg) Write-Host "   OK  $Msg" -ForegroundColor Green }
function Write-Warn { param([string]$Msg) Write-Host "   WARN $Msg" -ForegroundColor Yellow }

# ── 1. Download zip ───────────────────────────────────────────────────────────

Write-Step "Downloading $AssetName..."
Write-Host "   URL: $DownloadUrl"

$tmpZip = Join-Path $env:TEMP "selfdesk-viewer-install-$([System.Guid]::NewGuid().ToString('N').Substring(0,8)).zip"
$tmpDir = Join-Path $env:TEMP 'selfdesk-viewer-install'

Invoke-WebRequest -Uri $DownloadUrl -OutFile $tmpZip -UseBasicParsing
Write-OK "Download complete: $tmpZip"

# ── 2. Extract to temp directory ─────────────────────────────────────────────

Write-Step 'Extracting...'
if (Test-Path $tmpDir) { Remove-Item $tmpDir -Recurse -Force }
Expand-Archive -Path $tmpZip -DestinationPath $tmpDir
Remove-Item $tmpZip -ErrorAction SilentlyContinue

# Normalize: zip may or may not have a root subdirectory
$extracted = Get-ChildItem -Path $tmpDir
if ($extracted.Count -eq 1 -and $extracted[0].PSIsContainer) {
    $srcDir = $extracted[0].FullName
} else {
    $srcDir = $tmpDir
}
Write-OK "Extracted to: $srcDir"

# ── 3. Check version ──────────────────────────────────────────────────────────

$newExe = Join-Path $srcDir 'SelfDesk.Viewer.exe'
if (Test-Path $newExe) {
    $newVer = (Get-Item $newExe).VersionInfo.ProductVersion
    Write-Host "   New version     : $newVer"
}

$curExe = Join-Path $InstallDir 'SelfDesk.Viewer.exe'
if (Test-Path $curExe) {
    $curVer = (Get-Item $curExe).VersionInfo.ProductVersion
    Write-Host "   Current version : $curVer"
}

# ── 4. Stop running viewer ────────────────────────────────────────────────────

Write-Step 'Stopping viewer...'
$proc = Get-Process -Name 'SelfDesk.Viewer' -ErrorAction SilentlyContinue
if ($proc) {
    $proc | Stop-Process -Force
    Start-Sleep -Seconds 1
    Write-OK 'SelfDesk.Viewer process terminated.'
} else {
    Write-Warn 'Viewer was not running.'
}

# ── 5. Preserve .env and certs/ ──────────────────────────────────────────────

$envPath  = Join-Path $InstallDir '.env'
$certsDir = Join-Path $InstallDir 'certs'

$envContent  = $null
$certsTmpDir = $null

if (Test-Path $envPath) {
    $envContent = Get-Content $envPath -Raw
    Write-OK '.env saved.'
}

if (Test-Path $certsDir) {
    $certsTmpDir = Join-Path $env:TEMP 'selfdesk-viewer-certs-backup'
    if (Test-Path $certsTmpDir) { Remove-Item $certsTmpDir -Recurse -Force }
    Copy-Item -Path $certsDir -Destination $certsTmpDir -Recurse
    Write-OK 'certs/ saved.'
}

# ── 6. Copy new files (excluding .env and certs/) ────────────────────────────

Write-Step "Copying to $InstallDir..."
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

Get-ChildItem -Path $srcDir -Exclude '.env', 'certs' |
    Copy-Item -Destination $InstallDir -Recurse -Force

Write-OK 'Files copied.'

# ── 7. Restore .env and certs/ ───────────────────────────────────────────────

if ($null -ne $envContent) {
    Set-Content -Path $envPath -Value $envContent -NoNewline
    Write-OK '.env restored.'
} else {
    Write-Warn ".env not found in $InstallDir — create it before launching."
    Write-Host "   Template: $(Join-Path $InstallDir '.env.example')"
}

if ($null -ne $certsTmpDir) {
    $certsDestDir = Join-Path $InstallDir 'certs'
    if (Test-Path $certsDestDir) { Remove-Item $certsDestDir -Recurse -Force }
    Copy-Item -Path $certsTmpDir -Destination $certsDestDir -Recurse
    Remove-Item $certsTmpDir -Recurse -ErrorAction SilentlyContinue
    Write-OK 'certs/ restored.'
}

# ── 8. Clean up temp files ────────────────────────────────────────────────────

Remove-Item $tmpDir -Recurse -ErrorAction SilentlyContinue

# ── 9. Launch viewer ──────────────────────────────────────────────────────────

Write-Host ''
Write-Host '=== Viewer install complete ===' -ForegroundColor Green
if ($newVer) { Write-Host "   Version installed : $newVer" }
Write-Host "   Directory         : $InstallDir"
Write-Host ''

$viewerExe = Join-Path $InstallDir 'SelfDesk.Viewer.exe'
if (Test-Path $viewerExe) {
    if (Test-Path $envPath) {
        Write-Host 'Launching SelfDesk Viewer...'
        Start-Process -FilePath $viewerExe -WorkingDirectory $InstallDir
    } else {
        Write-Host 'Run bootstrap before launching:'
        Write-Host "   powershell -File `"$InstallDir\scripts\bootstrap.ps1`" -Role receiver"
        Write-Host "   & `"$viewerExe`""
    }
}
