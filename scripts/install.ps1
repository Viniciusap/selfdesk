<#
.SYNOPSIS
    Instala dependências e compila um componente do SelfDesk (Windows).

.DESCRIPTION
    Uso: .\scripts\install.ps1 -Role <broker|sender|receiver> [-SkipFFmpeg] [-Publish]

    Pré-requisitos ausentes (Node.js LTS, .NET 10 SDK) são instalados
    automaticamente via winget.

    Para sender e receiver com ENCODER=qsv|nvenc (Fase 4), as DLLs do
    FFmpeg 7.x são baixadas do BtbN e copiadas para a saída do build.

    Após este script, rode:
        .\scripts\bootstrap.ps1 -Role <papel>

.PARAMETER Role
    Papel deste nó: broker, sender ou receiver.

.PARAMETER SkipFFmpeg
    Não baixar DLLs FFmpeg (ENCODER=jpeg não precisa delas).

.PARAMETER Publish
    Para sender/receiver: publicar self-contained em vez de só compilar.
    Necessário para instalar como serviço (Fase 5).
#>

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('broker', 'sender', 'receiver')]
    [string]$Role,

    [switch]$SkipFFmpeg,
    [switch]$Publish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $PSScriptRoot

# ── Helpers ──────────────────────────────────────────────────────────────────

function Ensure-Winget {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw 'winget não encontrado. Instale o App Installer pela Microsoft Store ou atualize o Windows 10/11.'
    }
}

function Ensure-Command {
    param([string]$Bin, [string]$WingetId, [string]$Label)
    if (Get-Command $Bin -ErrorAction SilentlyContinue) {
        Write-Host "  $Label já instalado."
        return
    }
    Ensure-Winget
    Write-Host "  Instalando $Label via winget ($WingetId)..."
    winget install --id $WingetId -e --accept-source-agreements --accept-package-agreements
    # Recarrega PATH para o processo atual
    $env:PATH = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' +
                [System.Environment]::GetEnvironmentVariable('PATH', 'User')
    if (-not (Get-Command $Bin -ErrorAction SilentlyContinue)) {
        Write-Warning "  $Label instalado mas '$Bin' ainda não está no PATH desta sessão."
        Write-Warning "  Feche e reabra o terminal, depois rode este script novamente."
        exit 1
    }
    Write-Host "  $Label instalado."
}

function Install-FFmpeg {
    param([string]$TargetDir)

    $marker = Join-Path $TargetDir 'avcodec-61.dll'
    if (Test-Path $marker) {
        Write-Host '  DLLs FFmpeg já presentes — pulando download.'
        return
    }

    $zipUrl  = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n7.1-latest-win64-gpl-shared-7.1.zip'
    $tmpZip  = Join-Path $env:TEMP 'ffmpeg-shared.zip'
    $tmpDir  = Join-Path $env:TEMP 'ffmpeg-shared'

    Write-Host '  Baixando FFmpeg 7.1 shared (BtbN)...'
    Invoke-WebRequest -Uri $zipUrl -OutFile $tmpZip -UseBasicParsing

    Write-Host '  Extraindo...'
    if (Test-Path $tmpDir) { Remove-Item $tmpDir -Recurse -Force }
    Expand-Archive -Path $tmpZip -DestinationPath $tmpDir

    # A pasta interna tem nome variável; acha o subdiretório que contém bin/
    $binSrc = Get-ChildItem -Path $tmpDir -Recurse -Directory -Filter 'bin' |
              Select-Object -First 1

    if (-not $binSrc) {
        throw 'Estrutura inesperada no zip FFmpeg — pasta bin/ não encontrada.'
    }

    if (-not (Test-Path $TargetDir)) { New-Item -ItemType Directory -Path $TargetDir | Out-Null }

    Write-Host "  Copiando DLLs para $TargetDir..."
    Get-ChildItem -Path $binSrc.FullName -Filter '*.dll' |
        Copy-Item -Destination $TargetDir -Force

    Remove-Item $tmpZip  -ErrorAction SilentlyContinue
    Remove-Item $tmpDir  -Recurse -ErrorAction SilentlyContinue

    Write-Host '  DLLs FFmpeg copiadas.'
}

# ── Instalação por papel ──────────────────────────────────────────────────────

switch ($Role) {

    'broker' {
        Write-Host ''
        Write-Host '=== SelfDesk Install — broker ==='
        Write-Host ''
        Write-Host '→ Verificando Node.js LTS...'
        Ensure-Command -Bin 'node' -WingetId 'OpenJS.NodeJS.LTS' -Label 'Node.js LTS'

        Write-Host '→ Instalando dependências npm...'
        Push-Location (Join-Path $Root 'broker')
        npm install
        Write-Host '→ Compilando TypeScript...'
        npm run build
        Pop-Location

        Write-Host ''
        Write-Host '✔ Broker compilado em broker/dist/'
        Write-Host ''
        Write-Host 'Próximo passo:'
        Write-Host '  .\scripts\bootstrap.ps1 -Role broker'
        Write-Host '  cd broker && npm start'
    }

    'sender' {
        Write-Host ''
        Write-Host '=== SelfDesk Install — sender ==='
        Write-Host ''
        Write-Host '→ Verificando .NET 10 SDK...'
        Ensure-Command -Bin 'dotnet' -WingetId 'Microsoft.DotNet.SDK.10' -Label '.NET 10 SDK'

        if ($Publish) {
            Write-Host '→ Publicando sender (self-contained)...'
            $OutDir = Join-Path $Root 'sender' 'publish'
            Push-Location (Join-Path $Root 'sender')
            dotnet publish Sender.csproj -c Release -r win-x64 --self-contained false -o $OutDir
            Pop-Location
            $BinDir = $OutDir
        } else {
            Write-Host '→ Compilando sender...'
            Push-Location (Join-Path $Root 'sender')
            dotnet build Sender.csproj -c Release
            Pop-Location
            $BinDir = Join-Path $Root 'sender' 'bin' 'Release' 'net10.0-windows'
        }

        if (-not $SkipFFmpeg) {
            Write-Host '→ Instalando DLLs FFmpeg (Fase 4 — H264)...'
            Install-FFmpeg -TargetDir $BinDir
        }

        Write-Host ''
        Write-Host "✔ Sender compilado em $BinDir"
        Write-Host ''
        Write-Host 'Próximo passo:'
        Write-Host '  .\scripts\bootstrap.ps1 -Role sender'
        Write-Host '  cd sender && dotnet run'
    }

    'receiver' {
        Write-Host ''
        Write-Host '=== SelfDesk Install — receiver (viewer) ==='
        Write-Host ''
        Write-Host '→ Verificando .NET 10 SDK...'
        Ensure-Command -Bin 'dotnet' -WingetId 'Microsoft.DotNet.SDK.10' -Label '.NET 10 SDK'

        if ($Publish) {
            Write-Host '→ Publicando viewer (self-contained)...'
            $OutDir = Join-Path $Root 'viewer' 'publish'
            Push-Location (Join-Path $Root 'viewer')
            dotnet publish Viewer.csproj -c Release -r win-x64 --self-contained false -o $OutDir
            Pop-Location
            $BinDir = $OutDir
        } else {
            Write-Host '→ Compilando viewer...'
            Push-Location (Join-Path $Root 'viewer')
            dotnet build Viewer.csproj -c Release
            Pop-Location
            $BinDir = Join-Path $Root 'viewer' 'bin' 'Release' 'net10.0-windows'
        }

        if (-not $SkipFFmpeg) {
            Write-Host '→ Instalando DLLs FFmpeg (Fase 4 — H264)...'
            Install-FFmpeg -TargetDir $BinDir
        }

        Write-Host ''
        Write-Host "✔ Viewer compilado em $BinDir"
        Write-Host ''
        Write-Host 'Próximo passo:'
        Write-Host '  .\scripts\bootstrap.ps1 -Role receiver'
        Write-Host '  cd viewer && dotnet run'
    }
}
