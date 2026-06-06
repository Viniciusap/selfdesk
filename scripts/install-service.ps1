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

# Environment variables relevant to the service (read from .env)
$EnvVarsToSet = @('ROLE','SENDER_ID','SHARED_SECRET','BROKER_HOST','BROKER_PORT',
                  'TLS_CA_PATH','TARGET_FPS','ENCODER','JPEG_QUALITY','CAPTURER')

# Look for .env: first in publish/, then in sender/
$EnvFile = $null
foreach ($candidate in @(
    (Join-Path $PublishDir '.env'),
    (Join-Path $Root '.env'),
    (Join-Path $Root 'sender' '.env')
)) {
    if (Test-Path $candidate) { $EnvFile = $candidate; break }
}

if ($null -eq $EnvFile) {
    Write-Warning ".env not found. Run .\scripts\bootstrap.ps1 -Role sender before continuing."
    Write-Warning "After generating the .env, run install-service.ps1 again to configure the variables."
    exit 1
}

Write-Host ""
Write-Host "Configuring service environment variables from: $EnvFile"

foreach ($line in Get-Content $EnvFile) {
    $line = $line.Trim()
    if ($line -match '^\s*#' -or $line -notmatch '=') { continue }
    $idx = $line.IndexOf('=')
    $key = $line.Substring(0, $idx).Trim()
    $val = $line.Substring($idx + 1).Trim()
    if ($key -notin $EnvVarsToSet) { continue }

    [Environment]::SetEnvironmentVariable($key, $val, 'Machine')
    $display = if ($key -eq 'SHARED_SECRET') { '***' } else { $val }
    Write-Host "  $key = $display"
}

Write-Host ""
Write-Host "Starting service..."
Start-Service -Name $ServiceName
Write-Host "Service '$ServiceName' started."
