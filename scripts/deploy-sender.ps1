<#
.SYNOPSIS
    Publishes the sender and deploys it to laptop-01 via a network share.

.DESCRIPTION
    Usage:
      .\scripts\deploy-sender.ps1
      .\scripts\deploy-sender.ps1 -Target "\\laptop-01\tools\selfdesk-sender"
      .\scripts\deploy-sender.ps1 -Target "C:\tools\selfdesk-sender"   # run on laptop-01 directly

    Steps:
      1. dotnet publish sender/ (self-contained, win-x64, Release)
      2. Downloads FFmpeg DLLs if missing
      3. Stops the SelfDesk.Sender service on the target host (via sc.exe)
      4. Copies files to -Target, preserving .env and certs/
      5. Restarts the service

.PARAMETER Target
    Destination path. Default: \\laptop-01\tools\selfdesk-sender
    Use C$\... for admin share: \\laptop-01\C$\tools\selfdesk-sender

.PARAMETER SkipFFmpeg
    Skip FFmpeg download (already installed at the destination).

.PARAMETER ServiceHost
    Hostname where sc.exe will stop/start the service. Default: laptop-01
    Use "." for the local machine.
#>

param(
    [string]$Target      = '\\laptop-01\tools\selfdesk-sender',
    [string]$ServiceHost = 'laptop-01',
    [switch]$SkipFFmpeg
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root        = Split-Path -Parent $PSScriptRoot
$SenderDir   = Join-Path $Root 'sender'
$PublishDir  = Join-Path $SenderDir 'publish'
$ServiceName = 'SelfDesk.Sender'

function Write-Step { param([string]$Msg) Write-Host "`n-> $Msg" -ForegroundColor Cyan }
function Write-OK   { param([string]$Msg) Write-Host "  OK  $Msg" -ForegroundColor Green }
function Write-Warn { param([string]$Msg) Write-Host "  WARN $Msg" -ForegroundColor Yellow }

# ── 1. Publish ────────────────────────────────────────────────────────────────

Write-Step 'Publishing sender (self-contained, win-x64, Release)...'
Push-Location $SenderDir
dotnet publish Sender.csproj -c Release -r win-x64 --self-contained true -o $PublishDir `
    /p:DebugType=none /p:DebugSymbols=false
Pop-Location
Write-OK "Published to $PublishDir"

# ── 2. FFmpeg DLLs ───────────────────────────────────────────────────────────

if (-not $SkipFFmpeg) {
    $marker = Join-Path $PublishDir 'avcodec-61.dll'
    if (-not (Test-Path $marker)) {
        Write-Step 'Downloading FFmpeg 7.1 DLLs (BtbN)...'
        $zipUrl = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n7.1-latest-win64-gpl-shared-7.1.zip'
        $tmpZip = Join-Path $env:TEMP 'ffmpeg-shared.zip'
        $tmpDir = Join-Path $env:TEMP 'ffmpeg-shared'

        Invoke-WebRequest -Uri $zipUrl -OutFile $tmpZip -UseBasicParsing
        if (Test-Path $tmpDir) { Remove-Item $tmpDir -Recurse -Force }
        Expand-Archive -Path $tmpZip -DestinationPath $tmpDir

        $binSrc = Get-ChildItem -Path $tmpDir -Recurse -Directory -Filter 'bin' | Select-Object -First 1
        if (-not $binSrc) { throw 'bin/ folder not found in FFmpeg zip.' }

        Get-ChildItem -Path $binSrc.FullName -Filter '*.dll' |
            Copy-Item -Destination $PublishDir -Force

        Remove-Item $tmpZip -ErrorAction SilentlyContinue
        Remove-Item $tmpDir -Recurse -ErrorAction SilentlyContinue
        Write-OK 'FFmpeg DLLs copied.'
    } else {
        Write-OK 'FFmpeg DLLs already present — skipping download.'
    }
}

# ── 3. Stop service ───────────────────────────────────────────────────────────

Write-Step "Stopping $ServiceName on $ServiceHost..."
$scStop = sc.exe "\\$ServiceHost" stop $ServiceName 2>&1
if ($LASTEXITCODE -ne 0 -and $scStop -notmatch '1062|already') {
    Write-Warn "Service did not stop (may already be stopped): $scStop"
} else {
    Start-Sleep -Seconds 2
    Write-OK 'Service stopped.'
}

# ── 4. Copy files ─────────────────────────────────────────────────────────────

Write-Step "Copying to $Target..."

if (-not (Test-Path $Target)) {
    New-Item -ItemType Directory -Path $Target -Force | Out-Null
}

# Preserve existing .env and certs/ — do not overwrite
$EnvDest  = Join-Path $Target '.env'
$CertDest = Join-Path $Target 'certs'

$envBackup  = $null
$certBackup = $null

if (Test-Path $EnvDest) {
    $envBackup = Get-Content $EnvDest -Raw
}
if (Test-Path $CertDest) {
    $certBackup = $CertDest
}

# Copy everything except .env and certs/
Get-ChildItem -Path $PublishDir -Exclude '.env', 'certs' |
    Copy-Item -Destination $Target -Recurse -Force

# Restore .env if one existed
if ($envBackup) {
    Set-Content -Path $EnvDest -Value $envBackup -NoNewline
    Write-OK '.env preserved.'
} else {
    Write-Warn '.env not found at destination — create one before starting the service.'
}

Write-OK "Files copied to $Target"

# ── 5. Restart service ────────────────────────────────────────────────────────

Write-Step "Starting $ServiceName on $ServiceHost..."
$scStart = sc.exe "\\$ServiceHost" start $ServiceName 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Warn "Could not start the service remotely: $scStart"
    Write-Host '  Start it manually on the target machine:' -ForegroundColor Yellow
    Write-Host "    sc start $ServiceName" -ForegroundColor Yellow
} else {
    Start-Sleep -Seconds 2
    $status = sc.exe "\\$ServiceHost" query $ServiceName | Select-String 'STATE'
    Write-OK "Service started: $status"
}

Write-Host ''
Write-Host '=== Deploy complete ===' -ForegroundColor Green
Write-Host "  Target  : $Target"
Write-Host "  Service : $ServiceName @ $ServiceHost"
Write-Host ''
Write-Host 'If .env was missing, edit it on the target machine before starting:'
Write-Host "  notepad $Target\.env"
