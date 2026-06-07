<#
.SYNOPSIS
    Installs or updates the SelfDesk Sender from the latest GitHub release.

.DESCRIPTION
    Usage (run on the sender machine as Administrator):
      irm https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/install-sender.ps1 | iex
      .\install-sender.ps1 -InstallDir C:\tools\selfdesk-sender

    What it does:
      1. Downloads selfdesk-sender-win-x64.zip from the latest release
      2. Stops the running instance (service or scheduled task)
      3. Extracts, preserving .env and certs/
      4. Asks how to start: Task Scheduler (recommended) or Windows Service

.PARAMETER InstallDir
    Sender installation directory. Default: C:\tools\selfdesk-sender
#>

param(
    [string]$InstallDir = 'C:\tools\selfdesk-sender'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Banner ────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host '/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\|/-\' -ForegroundColor Cyan
Write-Host '|                                                             |' -ForegroundColor Cyan
Write-Host '\  ___           _      ___  ___                 _            /' -ForegroundColor Cyan
Write-Host '- (  _`\        (_ )  /''___)(  _`\              ( )          -' -ForegroundColor Cyan
Write-Host '/ | (_(_)   __   | | | (__  | | ) |   __    ___ | |/'')       \' -ForegroundColor Cyan
Write-Host '| `\__ \  /''__`\ | | | ,__) | | | ) /''__`\/'',__)| , <      |' -ForegroundColor Cyan
Write-Host '\ ( )_) |(  ___/ | | | |    | |_) |(  ___/\__, \| |\`\        /' -ForegroundColor Cyan
Write-Host '- `\____)`\____)(___)(_)    (____/''`\____)(____/(_) (_)      -' -ForegroundColor Cyan
Write-Host '/                                                             \' -ForegroundColor Cyan
Write-Host '|                                                             |' -ForegroundColor Cyan
Write-Host '\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|\-/|/-\' -ForegroundColor Cyan
Write-Host ""
Write-Host "  Sender Installer  —  github.com/Viniciusap/selfdesk" -ForegroundColor DarkCyan
Write-Host ""
# ─────────────────────────────────────────────────────────────────────────────

$RepoOwner   = 'Viniciusap'
$RepoName    = 'selfdesk'
$AssetName   = 'selfdesk-sender-win-x64.zip'
$DownloadUrl = "https://github.com/$RepoOwner/$RepoName/releases/latest/download/$AssetName"
$ServiceName = 'SelfDesk.Sender'
$TaskName    = 'SelfDesk Sender'

function Write-Step { param([string]$Msg) Write-Host "`n-> $Msg" -ForegroundColor Cyan }
function Write-OK   { param([string]$Msg) Write-Host "   OK  $Msg" -ForegroundColor Green }
function Write-Warn { param([string]$Msg) Write-Host "   WARN $Msg" -ForegroundColor Yellow }

# Sets a key in a .env file — creates it if missing, replaces+deduplicates if present
function Set-EnvKey([string]$File, [string]$Key, [string]$Value) {
    $lines   = Get-Content $File
    $written = $false
    $out     = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $lines) {
        if ($line -match "^\s*$Key\s*=") {
            if (-not $written) { $out.Add("$Key = $Value"); $written = $true }
            # drop duplicates
        } else {
            $out.Add($line)
        }
    }
    if (-not $written) { $out.Add("$Key = $Value") }
    Set-Content $File $out
}

# ── 0. Check admin ────────────────────────────────────────────────────────────

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run as Administrator (right-click -> Run as administrator).'
}

# ── 1. Download zip ───────────────────────────────────────────────────────────

Write-Step "Downloading $AssetName..."
Write-Host "   URL: $DownloadUrl"

$tmpZip = Join-Path $env:TEMP "selfdesk-sender-install-$([System.Guid]::NewGuid().ToString('N').Substring(0,8)).zip"
$tmpDir = Join-Path $env:TEMP 'selfdesk-sender-install'

Invoke-WebRequest -Uri $DownloadUrl -OutFile $tmpZip -UseBasicParsing
Write-OK "Download complete: $tmpZip"

# ── 2. Extract to temp directory ─────────────────────────────────────────────

Write-Step 'Extracting...'
if (Test-Path $tmpDir) { Remove-Item $tmpDir -Recurse -Force }
Expand-Archive -Path $tmpZip -DestinationPath $tmpDir
Remove-Item $tmpZip -ErrorAction SilentlyContinue

$extracted = @(Get-ChildItem -Path $tmpDir)
if ($extracted.Count -eq 1 -and $extracted[0].PSIsContainer) {
    $srcDir = $extracted[0].FullName
} else {
    $srcDir = $tmpDir
}
Write-OK "Extracted to: $srcDir"

# ── 3. Check version ──────────────────────────────────────────────────────────

$newVer = $null
$newExe = Join-Path $srcDir 'SelfDesk.Sender.exe'
if (Test-Path $newExe) {
    $newVer = (Get-Item $newExe).VersionInfo.ProductVersion
    Write-Host "   New version     : $newVer"
}

$curExe = Join-Path $InstallDir 'SelfDesk.Sender.exe'
if (Test-Path $curExe) {
    $curVer = (Get-Item $curExe).VersionInfo.ProductVersion
    Write-Host "   Current version : $curVer"
}

# ── 4. Stop current runner (service or task) ──────────────────────────────────

Write-Step 'Stopping current runner...'

# Legacy service name migration (pre-v0.5.0)
if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
    $legacy = Get-Service -Name 'SelfDesk.Agent' -ErrorAction SilentlyContinue
    if ($legacy) {
        Write-Warn "Found legacy service 'SelfDesk.Agent' — will migrate."
        $ServiceName = 'SelfDesk.Agent'
    }
}

$svc  = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

if ($svc -and $svc.Status -ne 'Stopped') {
    Stop-Service -Name $ServiceName -Force
    $timeout = 15
    while ((Get-Service -Name $ServiceName).Status -ne 'Stopped' -and $timeout-- -gt 0) {
        Start-Sleep -Seconds 1
    }
    if ((Get-Service -Name $ServiceName).Status -ne 'Stopped') {
        throw "Service did not stop within 15s. Kill the process manually and retry."
    }
    Write-OK 'Service stopped.'
} elseif ($task -and $task.State -eq 'Running') {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Write-OK 'Scheduled task stopped.'
} elseif (-not $svc -and -not $task) {
    $proc = Get-Process -Name 'SelfDesk.Sender' -ErrorAction SilentlyContinue
    if ($proc) {
        $proc | Stop-Process -Force
        Start-Sleep -Seconds 1
        Write-OK 'SelfDesk.Sender process terminated.'
    } else {
        Write-Warn 'No running instance found — copying files directly.'
    }
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
    $certsTmpDir = Join-Path $env:TEMP 'selfdesk-certs-backup'
    if (Test-Path $certsTmpDir) { Remove-Item $certsTmpDir -Recurse -Force }
    Copy-Item -Path $certsDir -Destination $certsTmpDir -Recurse
    Write-OK 'certs/ saved.'
}

# ── 6. Copy new files ────────────────────────────────────────────────────────

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
    Write-Warn ".env not found in $InstallDir — create it before starting."
    Write-Host "   Template: $(Join-Path $InstallDir '.env.example')"
}

if ($null -ne $certsTmpDir) {
    $certsDestDir = Join-Path $InstallDir 'certs'
    if (Test-Path $certsDestDir) { Remove-Item $certsDestDir -Recurse -Force }
    Copy-Item -Path $certsTmpDir -Destination $certsDestDir -Recurse
    Remove-Item $certsTmpDir -Recurse -ErrorAction SilentlyContinue
    Write-OK 'certs/ restored.'
}

# ── 8. Clean up temp ──────────────────────────────────────────────────────────

Remove-Item $tmpDir -Recurse -ErrorAction SilentlyContinue

# ── 9. Detect hardware and configure .env ─────────────────────────────────────

Write-Step 'Detecting hardware capabilities...'

$gpus   = @(Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue)
$nvidia = $gpus | Where-Object { $_.Name -match 'NVIDIA' }  | Select-Object -First 1
$intel  = $gpus | Where-Object { $_.Name -match 'Intel' }   | Select-Object -First 1
$amd    = $gpus | Where-Object { $_.Name -match 'AMD|Radeon' } | Select-Object -First 1
$anyGpu = if ($nvidia) { $nvidia } elseif ($intel) { $intel } elseif ($amd) { $amd } else { $null }

if ($anyGpu) { Write-Host "   GPU      : $($anyGpu.Name)" }
else          { Write-Host "   GPU      : none detected" -ForegroundColor Yellow }

# DXGI Desktop Duplication: requires any GPU in interactive session
$bestCapturer = if ($anyGpu) { 'dxgi' } else { 'gdi' }
$dxgiColor    = if ($anyGpu) { 'Green' } else { 'Yellow' }
Write-Host "   DXGI     : $(if ($anyGpu) { 'available' } else { 'unavailable — no GPU detected' })" -ForegroundColor $dxgiColor

# NVENC: NVIDIA GPU + nvEncodeAPI64.dll (installed by NVIDIA drivers alongside NVENC support)
$nvencDll   = "$env:SystemRoot\System32\nvEncodeAPI64.dll"
$nvencAvail = ($null -ne $nvidia) -and (Test-Path $nvencDll)
if ($null -ne $nvidia) {
    $nvencMsg   = if ($nvencAvail) { 'available' } else { 'unavailable (nvEncodeAPI64.dll not found — update NVIDIA drivers)' }
    $nvencColor = if ($nvencAvail) { 'Green' } else { 'Yellow' }
    Write-Host "   NVENC    : $nvencMsg" -ForegroundColor $nvencColor
}

# QSV (Intel): no reliable probe without ffmpeg CLI — skipped, set manually if needed
# AMD AMF: not yet implemented in sender

$bestEncoder = if ($nvencAvail) { 'nvenc' } else { 'jpeg' }
$bestFps     = if ($nvencAvail) { 60 } else { 30 }

Write-Host ''
Write-Host '   Recommended settings:' -ForegroundColor White
Write-Host "     CAPTURER   = $bestCapturer"
Write-Host "     ENCODER    = $bestEncoder"
Write-Host "     TARGET_FPS = $bestFps"
Write-Host ''

if (Test-Path $envPath) {
    $apply = (Read-Host '   Apply to .env? [Y/n]').Trim().ToLower()
    if ($apply -ne 'n') {
        Set-EnvKey $envPath 'CAPTURER'   $bestCapturer
        Set-EnvKey $envPath 'ENCODER'    $bestEncoder
        Set-EnvKey $envPath 'TARGET_FPS' "$bestFps"
        Write-OK '.env updated with detected settings.'
    }
} else {
    Write-Warn '.env not found — run bootstrap.ps1 first, then re-run install-sender.ps1 to apply detected settings.'
}

# ── 10. Configure autostart ───────────────────────────────────────────────────

$ExePath = Join-Path $InstallDir 'SelfDesk.Sender.exe'

Write-Host ''
Write-Host '-> How should the Sender start?' -ForegroundColor Cyan
Write-Host ''
Write-Host '   [T] Task Scheduler  (recommended — runs in your session, DXGI + NVENC work)' -ForegroundColor Green
Write-Host '   [S] Windows Service (Session 0 — screen capture returns black video)' -ForegroundColor Yellow
Write-Host '   [N] None            (start manually)' -ForegroundColor Gray
Write-Host ''
$choice = (Read-Host '   Choice [T/s/n]').Trim().ToUpper()
if ($choice -eq '') { $choice = 'T' }

switch ($choice) {
    'T' {
        # Remove Windows Service if present
        $activeSvc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($activeSvc) {
            Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
            & sc.exe delete $ServiceName | Out-Null
            Write-OK "Windows Service '$ServiceName' removed."
        }

        # Register scheduled task
        $action    = New-ScheduledTaskAction -Execute $ExePath -WorkingDirectory $InstallDir
        $trigger   = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
        $settings  = New-ScheduledTaskSettingsSet `
            -RestartCount 3 `
            -RestartInterval (New-TimeSpan -Seconds 30) `
            -ExecutionTimeLimit ([TimeSpan]::Zero) `
            -MultipleInstances IgnoreNew
        $principal = New-ScheduledTaskPrincipal `
            -UserId $env:USERNAME `
            -LogonType Interactive `
            -RunLevel Highest

        if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
            Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        }
        Register-ScheduledTask `
            -TaskName  $TaskName `
            -Action    $action `
            -Trigger   $trigger `
            -Settings  $settings `
            -Principal $principal `
            | Out-Null

        Start-ScheduledTask -TaskName $TaskName
        Write-OK "Task '$TaskName' registered and started."
    }

    'S' {
        Write-Host ''
        Write-Host '   WARNING: Windows Services run in Session 0.' -ForegroundColor Yellow
        Write-Host '   DXGI Desktop Duplication will fail — GDI fallback also returns BLACK.' -ForegroundColor Yellow
        Write-Host ''
        $confirm = (Read-Host '   Continue anyway? [y/N]').Trim().ToLower()
        if ($confirm -ne 'y') {
            Write-Host '   Aborted. Files installed but not started.' -ForegroundColor Cyan
        } else {
            # Remove task if present
            if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
                Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
                Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
                Write-OK "Scheduled task '$TaskName' removed."
            }

            # Create service
            if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
                New-Service -Name $ServiceName `
                    -BinaryPathName $ExePath `
                    -DisplayName 'SelfDesk Sender' `
                    -Description 'Captures the screen and injects input for remote access via SelfDesk.' `
                    -StartupType Automatic `
                    | Out-Null
            }

            # Configure env vars from .env
            $envVarsToSet = @('ROLE','SENDER_ID','SHARED_SECRET','BROKER_HOST','BROKER_PORT',
                              'TLS_CA_PATH','TARGET_FPS','ENCODER','JPEG_QUALITY','CAPTURER')
            if (Test-Path $envPath) {
                foreach ($line in Get-Content $envPath) {
                    $line = $line.Trim()
                    if ($line -match '^\s*#' -or $line -notmatch '=') { continue }
                    $idx = $line.IndexOf('=')
                    $key = $line.Substring(0, $idx).Trim()
                    $val = $line.Substring($idx + 1).Trim()
                    if ($key -notin $envVarsToSet) { continue }
                    [Environment]::SetEnvironmentVariable($key, $val, 'Machine')
                }
            }

            Start-Service -Name $ServiceName
            Write-OK "Service '$ServiceName' started."
        }
    }

    default {
        Write-Host ''
        Write-Host '   No autostart configured.' -ForegroundColor Gray
        Write-Host "   Start manually: $ExePath" -ForegroundColor Gray
    }
}

# ── Summary ───────────────────────────────────────────────────────────────────

Write-Host ''
Write-Host '=== Sender install complete ===' -ForegroundColor Green
if ($newVer) { Write-Host "   Version installed : $newVer" }
Write-Host "   Directory         : $InstallDir"
Write-Host ''
