<#
.SYNOPSIS
    Installs or updates the SelfDesk Broker (Windows) from the latest GitHub release.

.DESCRIPTION
    Downloads selfdesk-broker-win-x64.zip, stops the running Node process,
    replaces the files (preserving .env and certs/), and restarts the broker.

    One-liner:
      irm https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/install-broker.ps1 | iex

    Custom install directory:
      $env:INSTALL_DIR = 'C:\selfdesk-broker'
      irm https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/install-broker.ps1 | iex

.PARAMETER InstallDir
    Broker installation directory.
    Default: $env:INSTALL_DIR if set, otherwise $env:USERPROFILE\selfdesk-broker
#>

param(
    [string]$InstallDir = ($env:INSTALL_DIR ?? "$env:USERPROFILE\selfdesk-broker")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Banner ────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host '/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\' -ForegroundColor Cyan
Write-Host '|                                                         |' -ForegroundColor Cyan
Write-Host '\  ___           _      ___  ___                 _        /' -ForegroundColor Cyan
Write-Host '- (  _`\        (_ )  /''___)(  _`\              ( )       -' -ForegroundColor Cyan
Write-Host '/ | (_(_)   __   | | | (__  | | ) |   __    ___ | |/'')    \' -ForegroundColor Cyan
Write-Host '| `\__ \  /''__`\ | | | ,__) | | | ) /''__`\/'',__)| , <     |' -ForegroundColor Cyan
Write-Host '\ ( )_) |(  ___/ | | | |    | |_) |(  ___/\__, \| |\`\    /' -ForegroundColor Cyan
Write-Host '- `\____)`\____)(___)(_)    (____/''`\____)(____/(_) (_)   -' -ForegroundColor Cyan
Write-Host '/                                                         \' -ForegroundColor Cyan
Write-Host '|                                                         |' -ForegroundColor Cyan
Write-Host '\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/' -ForegroundColor Cyan
Write-Host ""
Write-Host "  Broker Installer  —  github.com/Viniciusap/selfdesk" -ForegroundColor DarkCyan
Write-Host ""
# ─────────────────────────────────────────────────────────────────────────────

$RepoOwner   = 'Viniciusap'
$RepoName    = 'selfdesk'
$AssetName   = 'selfdesk-broker-win-x64.zip'
$DownloadUrl = "https://github.com/$RepoOwner/$RepoName/releases/latest/download/$AssetName"

function Write-Step { param([string]$Msg) Write-Host "`n-> $Msg" -ForegroundColor Cyan }
function Write-OK   { param([string]$Msg) Write-Host "   OK  $Msg" -ForegroundColor Green }
function Write-Warn { param([string]$Msg) Write-Host "   WARN $Msg" -ForegroundColor Yellow }

# ── 1. Download ───────────────────────────────────────────────────────────────

Write-Step "Downloading $AssetName..."
Write-Host "   URL: $DownloadUrl"

$tmpZip = Join-Path $env:TEMP "selfdesk-broker-install-$([System.Guid]::NewGuid().ToString('N').Substring(0,8)).zip"
$tmpDir = Join-Path $env:TEMP 'selfdesk-broker-install'

Invoke-WebRequest -Uri $DownloadUrl -OutFile $tmpZip -UseBasicParsing
Write-OK "Download complete: $tmpZip"

# ── 2. Extract ────────────────────────────────────────────────────────────────

Write-Step 'Extracting...'
if (Test-Path $tmpDir) { Remove-Item $tmpDir -Recurse -Force }
Expand-Archive -Path $tmpZip -DestinationPath $tmpDir
Remove-Item $tmpZip -ErrorAction SilentlyContinue

# Normalize: zip may contain a root subdirectory or files at the top level
$extracted = @(Get-ChildItem -Path $tmpDir)
if ($extracted.Count -eq 1 -and $extracted[0].PSIsContainer) {
    $srcDir = $extracted[0].FullName
} else {
    $srcDir = $tmpDir
}
Write-OK "Extracted to: $srcDir"

# ── 3. Stop broker ────────────────────────────────────────────────────────────

Write-Step 'Stopping broker...'
$nodeProcs = Get-WmiObject Win32_Process -Filter "Name='node.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -match 'dist[/\\]index\.js' }

if ($nodeProcs) {
    $nodeProcs | ForEach-Object {
        Write-Host "   Stopping PID $($_.ProcessId)..."
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 1
    Write-OK 'Broker stopped.'
} else {
    Write-Warn 'Broker was not running.'
}

# ── 4. Preserve .env and certs/ ──────────────────────────────────────────────

$envPath     = Join-Path $InstallDir '.env'
$certsDir    = Join-Path $InstallDir 'certs'
$envContent  = $null
$certsTmpDir = $null

if (Test-Path $envPath) {
    $envContent = Get-Content $envPath -Raw
    Write-OK '.env saved.'
}
if (Test-Path $certsDir) {
    $certsTmpDir = Join-Path $env:TEMP 'selfdesk-broker-certs-backup'
    if (Test-Path $certsTmpDir) { Remove-Item $certsTmpDir -Recurse -Force }
    Copy-Item -Path $certsDir -Destination $certsTmpDir -Recurse
    Write-OK 'certs/ saved.'
}

# ── 5. Update files ───────────────────────────────────────────────────────────

Write-Step "Updating $InstallDir..."
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

Get-ChildItem -Path $srcDir -Exclude '.env', 'certs' |
    Copy-Item -Destination $InstallDir -Recurse -Force

Write-OK 'Files updated.'

# ── 6. Restore .env and certs/ ───────────────────────────────────────────────

if ($null -ne $envContent) {
    Set-Content -Path $envPath -Value $envContent -NoNewline
    Write-OK '.env restored.'
} else {
    Write-Warn ".env not found in $InstallDir — run bootstrap before starting the broker."
    Write-Host "   Template: $(Join-Path $InstallDir '.env.example')"
}

if ($null -ne $certsTmpDir) {
    $certsDestDir = Join-Path $InstallDir 'certs'
    if (Test-Path $certsDestDir) { Remove-Item $certsDestDir -Recurse -Force }
    Copy-Item -Path $certsTmpDir -Destination $certsDestDir -Recurse
    Remove-Item $certsTmpDir -Recurse -ErrorAction SilentlyContinue
    Write-OK 'certs/ restored.'
}

# ── 7. Clean up temp files ────────────────────────────────────────────────────

Remove-Item $tmpDir -Recurse -ErrorAction SilentlyContinue

# ── 8. Start broker ───────────────────────────────────────────────────────────

Write-Host ''
Write-Host '=== Broker files installed ===' -ForegroundColor Green
Write-Host "   Directory : $InstallDir"
Write-Host ''

if (-not (Test-Path $envPath)) {
    Write-Host 'Next step — generate .env, SHARED_SECRET, and TLS certificates:' -ForegroundColor Yellow
    Write-Host "   cd `"$InstallDir`""
    Write-Host "   powershell -File scripts\bootstrap.ps1 -Role broker"
    Write-Host ''
    Write-Host 'Then start the broker:'
    Write-Host "   node dist\index.js"
} else {
    Write-Step 'Starting broker...'
    $nodeCmd = Get-Command node -ErrorAction SilentlyContinue
    if (-not $nodeCmd) {
        Write-Warn "node.exe not found in PATH. Install Node.js LTS from https://nodejs.org"
        Write-Host "   Then run: node dist\index.js  (from $InstallDir)"
    } else {
        $indexJs = Join-Path $InstallDir 'dist' 'index.js'
        if (Test-Path $indexJs) {
            $logFile    = Join-Path $InstallDir 'broker.log'
            $logErrFile = Join-Path $InstallDir 'broker-err.log'
            $proc = Start-Process -FilePath $nodeCmd.Source `
                -ArgumentList "`"$indexJs`"" `
                -WorkingDirectory $InstallDir `
                -RedirectStandardOutput $logFile `
                -RedirectStandardError  $logErrFile `
                -WindowStyle Hidden -PassThru
            Start-Sleep -Seconds 2
            if (-not $proc.HasExited) {
                Write-OK "Broker running (PID $($proc.Id))."
                Write-Host ''
                Write-Host "   Log : $InstallDir\broker.log"
                Write-Host "   Follow: Get-Content '$InstallDir\broker.log' -Wait"
            } else {
                Write-Warn "Broker exited immediately. Check broker.log for errors."
            }
        }
    }
}
