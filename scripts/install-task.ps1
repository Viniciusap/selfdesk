<#
.SYNOPSIS
    Registers SelfDesk Sender as a Windows Task Scheduler task (recommended over service).

.DESCRIPTION
    Usage (run as Administrator on the sender machine):
      .\scripts\install-task.ps1
      .\scripts\install-task.ps1 -Uninstall
      .\scripts\install-task.ps1 -InstallDir C:\tools\selfdesk-sender

    Why Task Scheduler instead of a Windows Service?
      Windows Services run in Session 0, which cannot use DXGI Desktop Duplication.
      A scheduled task runs in the user's interactive console session where DXGI works,
      enabling hardware-accelerated screen capture (DXGI + NVENC).

    The task triggers at logon for the current user and restarts on failure.

.PARAMETER InstallDir
    Sender installation directory. Default: C:\tools\selfdesk-sender

.PARAMETER Uninstall
    Remove the scheduled task.
#>

param(
    [string]$InstallDir = 'C:\tools\selfdesk-sender',
    [switch]$Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$TaskName = 'SelfDesk Sender'
$ExePath  = Join-Path $InstallDir 'SelfDesk.Sender.exe'

if ($Uninstall) {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask  -TaskName $TaskName -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "Task '$TaskName' removed."
    } else {
        Write-Host "Task '$TaskName' not found."
    }
    exit 0
}

if (-not (Test-Path $ExePath)) {
    throw "Sender executable not found at '$ExePath'. Run install-sender.ps1 first."
}

if (-not (Test-Path (Join-Path $InstallDir '.env'))) {
    throw ".env not found in '$InstallDir'. Run bootstrap.ps1 -Role sender first."
}

# Migrate: remove Windows Service if present (Session 0 = black screen)
$ServiceName = 'SelfDesk.Sender'
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Removing Windows Service '$ServiceName' (Session 0 — no screen access)..."
    Stop-Service  -Name $ServiceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $ServiceName | Out-Null
    Write-Host "  Service removed."
}

$action  = New-ScheduledTaskAction -Execute $ExePath -WorkingDirectory $InstallDir
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME

# Restart on failure: up to 3 times, 30 s apart
$settings = New-ScheduledTaskSettingsSet `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Seconds 30) `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -MultipleInstances IgnoreNew

# Run as the current user in the interactive session (required for DXGI)
$principal = New-ScheduledTaskPrincipal `
    -UserId $env:USERNAME `
    -LogonType Interactive `
    -RunLevel Highest

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Write-Host "Task '$TaskName' already exists — updating."
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

Register-ScheduledTask `
    -TaskName  $TaskName `
    -Action    $action `
    -Trigger   $trigger `
    -Settings  $settings `
    -Principal $principal `
    | Out-Null

Write-Host "Task '$TaskName' registered — starts at logon for user '$($env:USERNAME)'."
Write-Host ""
Write-Host "Run now:"
Write-Host "  Start-ScheduledTask -TaskName '$TaskName'"
Write-Host ""
Write-Host "Or start manually:"
Write-Host "  $ExePath"
