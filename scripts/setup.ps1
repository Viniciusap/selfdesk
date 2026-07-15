<#
.SYNOPSIS
    One-command SelfDesk setup for Windows (issue #2) — picks a role, installs
    the matching release build, generates its .env via bootstrap, and launches it.

.DESCRIPTION
    One-liner:
      irm https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts/setup.ps1 | iex

    Non-interactive:
      $env:SELFDESK_ROLE = 'sender'   # broker | sender | viewer
      irm .../setup.ps1 | iex

    This is a thin orchestrator over the existing per-role scripts — it does not
    duplicate their logic. It downloads and runs, in order:
      1. install-broker.ps1 / install-sender.ps1 / install-viewer.ps1
      2. the bootstrap.ps1 that install step bundles into the install directory
    then launches the installed app (sender/viewer) or prints the broker start
    command. Existing .env / certs/ are never touched without confirmation —
    that's bootstrap.ps1's Confirm-Overwrite, unchanged.

    For fine-grained control (custom install dir, autostart flags, etc.) run the
    per-role scripts directly instead — see README.md "Install / Update".

.PARAMETER Role
    broker, sender, or viewer. Prompted interactively if not given (or via
    $env:SELFDESK_ROLE, for scripted/non-interactive use).
#>

param(
    [ValidateSet('broker', 'sender', 'viewer')]
    [string]$Role = $env:SELFDESK_ROLE
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$BaseUrl = 'https://raw.githubusercontent.com/Viniciusap/selfdesk/master/scripts'

Write-Host ''
Write-Host '=== SelfDesk Setup ===' -ForegroundColor Cyan
Write-Host ''

if (-not $Role) {
    Write-Host 'Which role is this machine?'
    Write-Host '  [1] broker  — routes video/input between senders and viewers'
    Write-Host '  [2] sender  — the machine being controlled'
    Write-Host '  [3] viewer  — the control machine, where you watch/operate'
    Write-Host ''
    do {
        $choice = Read-Host 'Enter 1, 2, or 3'
    } while ($choice -notin @('1', '2', '3'))
    $Role = @{ '1' = 'broker'; '2' = 'sender'; '3' = 'viewer' }[$choice]
}

Write-Host ''
Write-Host "-> Role: $Role" -ForegroundColor Cyan

# bootstrap.ps1's -Role ValidateSet is broker|sender|receiver (protocol-level
# naming) — the "viewer" role above is the user-facing name for "receiver".
$bootstrapRole = if ($Role -eq 'viewer') { 'receiver' } else { $Role }

$InstallDir = switch ($Role) {
    'broker' { $env:INSTALL_DIR ?? "$env:USERPROFILE\selfdesk-broker" }
    'sender' { 'C:\tools\selfdesk-sender' }
    'viewer' { 'C:\tools\selfdesk-viewer' }
}

function Invoke-RemoteScript {
    param([string]$Url, [hashtable]$BoundParams = @{})
    $source = Invoke-RestMethod -Uri $Url
    $sb = [scriptblock]::Create($source)
    & $sb @BoundParams
}

Write-Host "-> Downloading and running install-$Role.ps1..." -ForegroundColor Cyan
Invoke-RemoteScript -Url "$BaseUrl/install-$Role.ps1" -BoundParams @{ InstallDir = $InstallDir }

$bootstrapScript = Join-Path $InstallDir 'scripts\bootstrap.ps1'
$envPath = Join-Path $InstallDir '.env'

if (-not (Test-Path $envPath)) {
    if (-not (Test-Path $bootstrapScript)) {
        Write-Warning "bootstrap.ps1 not found at '$bootstrapScript' — install step may have failed. Aborting setup."
        exit 1
    }

    # Bonus (issue #2): catch the most common first-connection failure before it happens.
    if ($Role -ne 'broker') {
        $caPath = Join-Path $InstallDir 'ca-cert.pem'
        if (-not (Test-Path $caPath) -and -not (Test-Path (Join-Path $InstallDir 'certs\ca-cert.pem'))) {
            Write-Host ''
            Write-Warning "ca-cert.pem not found in $InstallDir — copy it from the broker machine (certs\ca-cert.pem there) before continuing, or the TLS connection will fail on first launch."
            Write-Host "   Expected at: $caPath"
            Write-Host ''
        }
    }

    Write-Host ''
    Write-Host "-> Running bootstrap (Role=$bootstrapRole)..." -ForegroundColor Cyan
    & $bootstrapScript -Role $bootstrapRole
} else {
    Write-Host ''
    Write-Host ".env already exists at $envPath — skipping bootstrap (existing config kept)." -ForegroundColor Yellow
}

Write-Host ''
Write-Host '=== Setup complete ===' -ForegroundColor Green
Write-Host "   Role      : $Role"
Write-Host "   Directory : $InstallDir"
Write-Host ''

switch ($Role) {
    'broker' {
        Write-Host 'Start the broker:'
        Write-Host "   cd `"$InstallDir`"; node dist\index.js"
    }
    'sender' {
        $exe = Join-Path $InstallDir 'SelfDesk.Sender.exe'
        if (Test-Path $exe) {
            Write-Host 'Launching SelfDesk Sender...'
            Start-Process -FilePath $exe -WorkingDirectory $InstallDir
        }
    }
    'viewer' {
        $exe = Join-Path $InstallDir 'SelfDesk.Viewer.exe'
        if (Test-Path $exe) {
            Write-Host 'Launching SelfDesk Viewer...'
            Start-Process -FilePath $exe -WorkingDirectory $InstallDir
        }
    }
}
