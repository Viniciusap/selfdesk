<#
.SYNOPSIS
    Compila o broker localmente e implanta no servidor Linux via SCP + SSH.

.DESCRIPTION
    Uso:
      .\scripts\deploy-broker.ps1
      .\scripts\deploy-broker.ps1 -User your-user -Server your-server-ip -RemotePath ~/selfdesk

    O que faz:
      1. npm run build em broker/
      2. SCP broker/dist/ para $RemotePath/dist/ no servidor
      3. SSH: mata processo antigo + inicia novo via nohup

.PARAMETER User
    Usuário SSH no servidor. Default: your-user

.PARAMETER Server
    IP ou hostname do servidor. Padrão: your-server-ip

.PARAMETER RemotePath
    Diretório do broker no servidor. Padrão: ~/selfdesk
#>

param(
    [string]$User       = 'your-user',
    [string]$Server     = 'your-server-ip',
    [string]$RemotePath = '~/selfdesk'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root      = Split-Path -Parent $PSScriptRoot
$BrokerDir = Join-Path $Root 'broker'
$DistDir   = Join-Path $BrokerDir 'dist'

function Write-Step { param([string]$Msg) Write-Host "`n→ $Msg" -ForegroundColor Cyan }
function Write-OK   { param([string]$Msg) Write-Host "  ✔ $Msg" -ForegroundColor Green }
function Write-Warn { param([string]$Msg) Write-Host "  ⚠ $Msg" -ForegroundColor Yellow }

# ── 1. Build local ────────────────────────────────────────────────────────────

Write-Step 'Compilando broker (TypeScript)...'
Push-Location $BrokerDir
npm run build
if ($LASTEXITCODE -ne 0) { throw 'Build falhou.' }
Pop-Location
Write-OK "Compilado em $DistDir"

# ── 2. SCP dist/ → servidor ───────────────────────────────────────────────────

Write-Step "Copiando dist/ para ${User}@${Server}:${RemotePath}/dist/ ..."
scp -r "$DistDir\*" "${User}@${Server}:${RemotePath}/dist/"
if ($LASTEXITCODE -ne 0) { throw 'SCP falhou.' }
Write-OK 'Arquivos copiados.'

# ── 3. SSH: kill + restart ────────────────────────────────────────────────────

Write-Step 'Reiniciando broker no servidor...'
$sshCmd = @"
set -e
pkill -f 'node dist/index.js' || true
sleep 1
cd $RemotePath
nohup node dist/index.js >> broker.log 2>&1 &
echo "Broker reiniciado (PID \$!)"
"@

ssh "${User}@${Server}" $sshCmd
if ($LASTEXITCODE -ne 0) { throw 'SSH restart falhou.' }

Write-Host ''
Write-Host '=== Deploy broker concluído ===' -ForegroundColor Green
Write-Host "  Servidor : ${User}@${Server}"
Write-Host "  Caminho  : $RemotePath"
Write-Host "  Log      : $RemotePath/broker.log"
Write-Host ''
Write-Host 'Para acompanhar o log:'
Write-Host "  ssh ${User}@${Server} 'tail -f $RemotePath/broker.log'"
