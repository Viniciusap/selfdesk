<#
.SYNOPSIS
    Atualiza o SelfDesk Sender a partir da latest release do GitHub.

.DESCRIPTION
    Uso (rodar no laptop-01 como administrador):
      .\update-sender.ps1
      .\update-sender.ps1 -InstallDir C:\tools\selfdesk-sender

    O que faz:
      1. Baixa selfdesk-sender-win-x64.zip da latest release (GitHub)
      2. Para o serviço SelfDesk.Sender
      3. Extrai o zip preservando .env e certs/
      4. Reinicia o serviço

.PARAMETER InstallDir
    Diretório de instalação do sender. Padrão: C:\tools\selfdesk-sender

.PARAMETER ServiceName
    Nome do serviço Windows. Padrão: SelfDesk.Sender
#>

param(
    [string]$InstallDir   = 'C:\tools\selfdesk-sender',
    [string]$ServiceName  = 'SelfDesk.Sender'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoOwner   = 'Viniciusap'
$RepoName    = 'selfdesk'
$AssetName   = 'selfdesk-sender-win-x64.zip'
$DownloadUrl = "https://github.com/$RepoOwner/$RepoName/releases/latest/download/$AssetName"

function Write-Step { param([string]$Msg) Write-Host "`n-> $Msg" -ForegroundColor Cyan }
function Write-OK   { param([string]$Msg) Write-Host "   OK  $Msg" -ForegroundColor Green }
function Write-Warn { param([string]$Msg) Write-Host "   WARN $Msg" -ForegroundColor Yellow }

# ── 0. Verificar admin ────────────────────────────────────────────────────────

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Execute como administrador (clique com botão direito → Executar como administrador).'
}

# ── 1. Baixar zip ─────────────────────────────────────────────────────────────

Write-Step "Baixando $AssetName..."
Write-Host "   URL: $DownloadUrl"

$tmpZip = Join-Path $env:TEMP "selfdesk-sender-update-$([System.Guid]::NewGuid().ToString('N')[0..7] -join '').zip"
$tmpDir = Join-Path $env:TEMP 'selfdesk-sender-update'

Invoke-WebRequest -Uri $DownloadUrl -OutFile $tmpZip -UseBasicParsing
Write-OK "Download concluído: $tmpZip"

# ── 2. Extrair em diretório temporário ───────────────────────────────────────

Write-Step 'Extraindo...'
if (Test-Path $tmpDir) { Remove-Item $tmpDir -Recurse -Force }
Expand-Archive -Path $tmpZip -DestinationPath $tmpDir
Remove-Item $tmpZip -ErrorAction SilentlyContinue

# O zip pode ter um subdiretório raiz ou não — normaliza
$extracted = Get-ChildItem -Path $tmpDir
if ($extracted.Count -eq 1 -and $extracted[0].PSIsContainer) {
    $srcDir = $extracted[0].FullName
} else {
    $srcDir = $tmpDir
}
Write-OK "Extraído em: $srcDir"

# ── 3. Verificar versão nova ──────────────────────────────────────────────────

$newExe = Join-Path $srcDir 'SelfDesk.Sender.exe'
if (Test-Path $newExe) {
    $newVer = (Get-Item $newExe).VersionInfo.ProductVersion
    Write-Host "   Nova versao : $newVer"
}

$curExe = Join-Path $InstallDir 'SelfDesk.Sender.exe'
if (Test-Path $curExe) {
    $curVer = (Get-Item $curExe).VersionInfo.ProductVersion
    Write-Host "   Versao atual: $curVer"
}

# ── 4. Parar serviço ──────────────────────────────────────────────────────────

Write-Step "Parando servico '$ServiceName'..."
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
    # Fallback para nome legado (instalacoes anteriores a v0.5.0)
    $legacy = Get-Service -Name 'SelfDesk.Agent' -ErrorAction SilentlyContinue
    if ($legacy) {
        Write-Warn "Servico encontrado com nome legado 'SelfDesk.Agent' — usando-o."
        $ServiceName = 'SelfDesk.Agent'
        $svc = $legacy
    }
}
if ($svc) {
    if ($svc.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        $timeout = 15
        while ((Get-Service -Name $ServiceName).Status -ne 'Stopped' -and $timeout -gt 0) {
            Start-Sleep -Seconds 1
            $timeout--
        }
        if ((Get-Service -Name $ServiceName).Status -ne 'Stopped') {
            throw "Servico nao parou em 15s. Mate o processo manualmente e tente novamente."
        }
    }
    Write-OK 'Servico parado.'
} else {
    Write-Warn "Servico '$ServiceName' nao encontrado — atualiza os arquivos sem parar/reiniciar servico."
}

# ── 5. Preservar .env e certs/ ───────────────────────────────────────────────

$envPath  = Join-Path $InstallDir '.env'
$certsDir = Join-Path $InstallDir 'certs'

$envContent  = $null
$certsTmpDir = $null

if (Test-Path $envPath) {
    $envContent = Get-Content $envPath -Raw
    Write-OK '.env salvo.'
}

if (Test-Path $certsDir) {
    $certsTmpDir = Join-Path $env:TEMP 'selfdesk-certs-backup'
    if (Test-Path $certsTmpDir) { Remove-Item $certsTmpDir -Recurse -Force }
    Copy-Item -Path $certsDir -Destination $certsTmpDir -Recurse
    Write-OK 'certs/ salvo.'
}

# ── 6. Copiar novos arquivos (exceto .env e certs/) ──────────────────────────

Write-Step "Copiando para $InstallDir..."
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

Get-ChildItem -Path $srcDir -Exclude '.env', 'certs' |
    Copy-Item -Destination $InstallDir -Recurse -Force

Write-OK 'Arquivos copiados.'

# ── 7. Restaurar .env e certs/ ───────────────────────────────────────────────

if ($null -ne $envContent) {
    Set-Content -Path $envPath -Value $envContent -NoNewline
    Write-OK '.env restaurado.'
} else {
    Write-Warn ".env nao encontrado em $InstallDir — crie antes de iniciar o servico."
    Write-Host "   Modelo: $(Join-Path $InstallDir '.env.example')"
}

if ($null -ne $certsTmpDir) {
    $certsDestDir = Join-Path $InstallDir 'certs'
    if (Test-Path $certsDestDir) { Remove-Item $certsDestDir -Recurse -Force }
    Copy-Item -Path $certsTmpDir -Destination $certsDestDir -Recurse
    Remove-Item $certsTmpDir -Recurse -ErrorAction SilentlyContinue
    Write-OK 'certs/ restaurado.'
}

# ── 8. Limpar temporários ─────────────────────────────────────────────────────

Remove-Item $tmpDir -Recurse -ErrorAction SilentlyContinue

# ── 9. Reiniciar serviço ──────────────────────────────────────────────────────

if ($svc) {
    Write-Step "Iniciando servico '$ServiceName'..."
    Start-Service -Name $ServiceName
    Start-Sleep -Seconds 2
    $status = (Get-Service -Name $ServiceName).Status
    if ($status -eq 'Running') {
        Write-OK "Servico rodando."
    } else {
        Write-Warn "Servico em estado '$status'. Verifique o Event Viewer para erros."
    }
}

Write-Host ''
Write-Host '=== Update concluido ===' -ForegroundColor Green
if ($newVer) { Write-Host "   Versao instalada: $newVer" }
Write-Host "   Diretorio       : $InstallDir"
Write-Host ''
