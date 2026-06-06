<#
.SYNOPSIS
    Builds the broker locally and deploys it to the Linux server via SCP + SSH.

.DESCRIPTION
    Usage:
      .\scripts\deploy-broker.ps1 -User myuser -Server 192.168.1.100
      .\scripts\deploy-broker.ps1 -User myuser -Server myserver.local -RemotePath ~/selfdesk

    Steps:
      1. npm run build in broker/
      2. SCP broker/dist/ to $RemotePath/dist/ on the server
      3. SSH: kill old process + start new one via nohup

.PARAMETER User
    SSH user on the server. Required.

.PARAMETER Server
    Server IP or hostname. Required.

.PARAMETER RemotePath
    Broker directory on the server. Default: ~/selfdesk
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$User,

    [Parameter(Mandatory = $true)]
    [string]$Server,

    [string]$RemotePath = '~/selfdesk'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root      = Split-Path -Parent $PSScriptRoot
$BrokerDir = Join-Path $Root 'broker'
$DistDir   = Join-Path $BrokerDir 'dist'

function Write-Step { param([string]$Msg) Write-Host "`n-> $Msg" -ForegroundColor Cyan }
function Write-OK   { param([string]$Msg) Write-Host "  OK  $Msg" -ForegroundColor Green }
function Write-Warn { param([string]$Msg) Write-Host "  WARN $Msg" -ForegroundColor Yellow }

# ── 1. Local build ────────────────────────────────────────────────────────────

Write-Step 'Compiling broker (TypeScript)...'
Push-Location $BrokerDir
npm run build
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Pop-Location
Write-OK "Compiled to $DistDir"

# ── 2. SCP dist/ → server ─────────────────────────────────────────────────────

Write-Step "Copying dist/ to ${User}@${Server}:${RemotePath}/dist/ ..."
scp -r "$DistDir\*" "${User}@${Server}:${RemotePath}/dist/"
if ($LASTEXITCODE -ne 0) { throw 'SCP failed.' }
Write-OK 'Files copied.'

# ── 3. SSH: kill + restart ────────────────────────────────────────────────────

Write-Step 'Restarting broker on the server...'
$sshCmd = @"
set -e
pkill -f 'node dist/index.js' || true
sleep 1
cd $RemotePath
nohup node dist/index.js >> broker.log 2>&1 &
echo "Broker restarted (PID `$!)"
"@

ssh "${User}@${Server}" $sshCmd
if ($LASTEXITCODE -ne 0) { throw 'SSH restart failed.' }

Write-Host ''
Write-Host '=== Broker deploy complete ===' -ForegroundColor Green
Write-Host "  Server : ${User}@${Server}"
Write-Host "  Path   : $RemotePath"
Write-Host "  Log    : $RemotePath/broker.log"
Write-Host ''
Write-Host 'To follow logs:'
Write-Host "  ssh ${User}@${Server} 'tail -f $RemotePath/broker.log'"
