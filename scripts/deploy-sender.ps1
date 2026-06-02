<#
.SYNOPSIS
    Publica o sender e implanta em laptop-01 via compartilhamento de rede.

.DESCRIPTION
    Uso:
      .\scripts\deploy-sender.ps1
      .\scripts\deploy-sender.ps1 -Target "\\laptop-01\tools\selfdesk-sender"
      .\scripts\deploy-sender.ps1 -Target "C:\tools\selfdesk-sender"   # rodar no próprio laptop-01

    O que faz:
      1. dotnet publish sender/ (self-contained, win-x64, Release)
      2. Baixa DLLs FFmpeg se ausentes
      3. Para o serviço SelfDesk.Sender no host alvo (via sc.exe remoto)
      4. Copia arquivos para -Target, preservando .env e certs/
      5. Reinicia o serviço

.PARAMETER Target
    Caminho de destino. Padrão: \\laptop-01\tools\selfdesk-sender
    Use C$\... para admin share: \\laptop-01\C$\tools\selfdesk-sender

.PARAMETER SkipFFmpeg
    Não baixar FFmpeg (já instalado no destino).

.PARAMETER ServiceHost
    Hostname onde sc.exe vai parar/iniciar o serviço. Padrão: laptop-01
    Use "." para máquina local.
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

function Write-Step { param([string]$Msg) Write-Host "`n→ $Msg" -ForegroundColor Cyan }
function Write-OK   { param([string]$Msg) Write-Host "  ✔ $Msg" -ForegroundColor Green }
function Write-Warn { param([string]$Msg) Write-Host "  ⚠ $Msg" -ForegroundColor Yellow }

# ── 1. Publicar ──────────────────────────────────────────────────────────────

Write-Step 'Publicando sender (self-contained, win-x64, Release)...'
Push-Location $SenderDir
dotnet publish Sender.csproj -c Release -r win-x64 --self-contained true -o $PublishDir `
    /p:DebugType=none /p:DebugSymbols=false
Pop-Location
Write-OK "Publicado em $PublishDir"

# ── 2. DLLs FFmpeg ───────────────────────────────────────────────────────────

if (-not $SkipFFmpeg) {
    $marker = Join-Path $PublishDir 'avcodec-61.dll'
    if (-not (Test-Path $marker)) {
        Write-Step 'Baixando DLLs FFmpeg 7.1 (BtbN)...'
        $zipUrl = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n7.1-latest-win64-gpl-shared-7.1.zip'
        $tmpZip = Join-Path $env:TEMP 'ffmpeg-shared.zip'
        $tmpDir = Join-Path $env:TEMP 'ffmpeg-shared'

        Invoke-WebRequest -Uri $zipUrl -OutFile $tmpZip -UseBasicParsing
        if (Test-Path $tmpDir) { Remove-Item $tmpDir -Recurse -Force }
        Expand-Archive -Path $tmpZip -DestinationPath $tmpDir

        $binSrc = Get-ChildItem -Path $tmpDir -Recurse -Directory -Filter 'bin' | Select-Object -First 1
        if (-not $binSrc) { throw 'Pasta bin/ não encontrada no zip FFmpeg.' }

        Get-ChildItem -Path $binSrc.FullName -Filter '*.dll' |
            Copy-Item -Destination $PublishDir -Force

        Remove-Item $tmpZip -ErrorAction SilentlyContinue
        Remove-Item $tmpDir -Recurse -ErrorAction SilentlyContinue
        Write-OK 'DLLs FFmpeg copiadas.'
    } else {
        Write-OK 'DLLs FFmpeg já presentes — pulando download.'
    }
}

# ── 3. Parar serviço ─────────────────────────────────────────────────────────

Write-Step "Parando $ServiceName em $ServiceHost..."
$scStop = sc.exe "\\$ServiceHost" stop $ServiceName 2>&1
if ($LASTEXITCODE -ne 0 -and $scStop -notmatch '1062|already') {
    Write-Warn "Serviço não parou (pode já estar parado): $scStop"
} else {
    Start-Sleep -Seconds 2
    Write-OK 'Serviço parado.'
}

# ── 4. Copiar arquivos ───────────────────────────────────────────────────────

Write-Step "Copiando para $Target..."

if (-not (Test-Path $Target)) {
    New-Item -ItemType Directory -Path $Target -Force | Out-Null
}

# Preserva .env e certs/ existentes — não sobrescreve
$EnvDest  = Join-Path $Target '.env'
$CertDest = Join-Path $Target 'certs'

$envBackup  = $null
$certBackup = $null

if (Test-Path $EnvDest) {
    $envBackup = Get-Content $EnvDest -Raw
}
if (Test-Path $CertDest) {
    $certBackup = $CertDest  # só lembra o path, robocopy não toca
}

# Copia tudo exceto .env e certs/
Get-ChildItem -Path $PublishDir -Exclude '.env', 'certs' |
    Copy-Item -Destination $Target -Recurse -Force

# Restaura .env se havia um
if ($envBackup) {
    Set-Content -Path $EnvDest -Value $envBackup -NoNewline
    Write-OK '.env preservado.'
} else {
    Write-Warn '.env não encontrado no destino — crie um antes de iniciar o serviço.'
}

Write-OK "Arquivos copiados para $Target"

# ── 5. Reiniciar serviço ─────────────────────────────────────────────────────

Write-Step "Iniciando $ServiceName em $ServiceHost..."
$scStart = sc.exe "\\$ServiceHost" start $ServiceName 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Warn "Não foi possível iniciar o serviço remotamente: $scStart"
    Write-Host '  Inicie manualmente no laptop-01:' -ForegroundColor Yellow
    Write-Host "    sc start $ServiceName" -ForegroundColor Yellow
} else {
    Start-Sleep -Seconds 2
    $status = sc.exe "\\$ServiceHost" query $ServiceName | Select-String 'STATE'
    Write-OK "Serviço iniciado: $status"
}

Write-Host ''
Write-Host '=== Deploy concluído ===' -ForegroundColor Green
Write-Host "  Destino : $Target"
Write-Host "  Serviço : $ServiceName @ $ServiceHost"
Write-Host ''
Write-Host 'Se o .env estava vazio, edite-o no laptop-01 antes de iniciar:'
Write-Host "  notepad $Target\.env"
