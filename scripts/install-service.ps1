<#
.SYNOPSIS
    Installs the SelfDesk Sender as a Windows service (Phase 5).

.DESCRIPTION
    Requires Administrator privileges.
    Usage: .\scripts\install-service.ps1 [-Uninstall]

    Reads .env and automatically configures the service environment variables
    in Machine scope.

    WARNING: Windows Services run in Session 0 and cannot use DXGI Desktop
    Duplication (DXGI_ERROR_UNSUPPORTED). The sender will fall back to GDI,
    which also returns a black frame in Session 0. For DXGI capture to work,
    use install-task.ps1 (Task Scheduler) instead — it runs in the user's
    interactive session where DXGI is available.
#>

param([switch]$Uninstall)

$ServiceName = 'SelfDesk.Sender'
$Root        = Split-Path -Parent $PSScriptRoot

# Support two layouts:
#   repo:      $Root = repo root, exe at sender/publish/SelfDesk.Sender.exe
#   installed: $Root = C:\tools\selfdesk-sender, exe at SelfDesk.Sender.exe
$PublishDir = if (Test-Path (Join-Path $Root 'sender' 'publish' 'SelfDesk.Sender.exe')) {
    Join-Path $Root 'sender' 'publish'
} else {
    $Root
}
$ExePath = Join-Path $PublishDir 'SelfDesk.Sender.exe'

if ($Uninstall) {
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Stop-Service -Name $ServiceName -Force
        & sc.exe delete $ServiceName
        Write-Host "Service '$ServiceName' removed."
    } else {
        Write-Host "Service '$ServiceName' not found."
    }
    exit 0
}

if (-not (Test-Path $ExePath)) {
    Write-Host "Publishing sender..."
    Push-Location (Join-Path $Root 'sender')
    dotnet publish Sender.csproj -c Release -r win-x64 --self-contained false -o publish
    Pop-Location
}

# ── DXGI warning ──────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!' -ForegroundColor Yellow
Write-Host '!                        WARNING                                !' -ForegroundColor Yellow
Write-Host '!                                                               !' -ForegroundColor Yellow
Write-Host '!  Windows Services run in Session 0 (no display access).      !' -ForegroundColor Yellow
Write-Host '!  DXGI Desktop Duplication will fail -> GDI fallback -> BLACK  !' -ForegroundColor Yellow
Write-Host '!  SCREEN sent to the viewer.                                   !' -ForegroundColor Yellow
Write-Host '!                                                               !' -ForegroundColor Yellow
Write-Host '!  Use install-task.ps1 instead (Task Scheduler).              !' -ForegroundColor Yellow
Write-Host '!  It runs in the interactive session where DXGI works.        !' -ForegroundColor Yellow
Write-Host '!                                                               !' -ForegroundColor Yellow
Write-Host '!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!' -ForegroundColor Yellow
Write-Host ''
$answer = Read-Host 'Continue installing as service anyway? [y/N]'
if ($answer -notmatch '^[Yy]$') {
    Write-Host 'Aborted. Run .\scripts\install-task.ps1 for the recommended setup.' -ForegroundColor Cyan
    exit 0
}
Write-Host ''
# ─────────────────────────────────────────────────────────────────────────────

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Warning "Service '$ServiceName' already exists. Use -Uninstall first to reinstall."
    exit 1
}

New-Service -Name $ServiceName `
            -BinaryPathName $ExePath `
            -DisplayName 'SelfDesk Sender' `
            -Description 'Captures the screen and injects input for remote access via SelfDesk.' `
            -StartupType Automatic

Write-Host "Service '$ServiceName' installed."

# S53: restrict service control to Administrators and SYSTEM only (prevent non-admin stop/start)
& sc.exe sdset $ServiceName 'D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)' | Out-Null
Write-Host "Service DACL restricted to Administrators and SYSTEM."

# S46: service reads config from .env in PublishDir directly (no Machine-scope env vars)
# Machine-scope env vars are readable by all local users — keep secrets only in .env with ACLs
Write-Host ""
Write-Host "NOTE: Service reads config from .env in $PublishDir"
Write-Host "      Run bootstrap.ps1 -Role sender to generate it if not done yet."

Write-Host ""
Write-Host "Starting service..."
Start-Service -Name $ServiceName
Write-Host "Service '$ServiceName' started."
