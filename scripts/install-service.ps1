<#
.SYNOPSIS
    Instala o SelfDesk Agent como serviço do Windows (Fase 5).

.DESCRIPTION
    Requer execução como administrador.
    Uso: .\scripts\install-service.ps1 [-Uninstall]

    Lê agent/.env (ou agent/publish/.env) e configura automaticamente
    as variáveis de ambiente do serviço em Machine scope.
#>

param([switch]$Uninstall)

$ServiceName = 'SelfDesk.Agent'
$Root        = Split-Path -Parent $PSScriptRoot
$PublishDir  = Join-Path $Root 'agent' 'publish'
$ExePath     = Join-Path $PublishDir 'SelfDesk.Agent.exe'

if ($Uninstall) {
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Stop-Service -Name $ServiceName -Force
        & sc.exe delete $ServiceName
        Write-Host "Serviço '$ServiceName' removido."
    } else {
        Write-Host "Serviço '$ServiceName' não encontrado."
    }
    exit 0
}

if (-not (Test-Path $ExePath)) {
    Write-Host "Publicando agent..."
    Push-Location (Join-Path $Root 'agent')
    dotnet publish -c Release -r win-x64 --self-contained false -o publish
    Pop-Location
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Warning "Serviço '$ServiceName' já existe. Use -Uninstall primeiro para reinstalar."
    exit 1
}

New-Service -Name $ServiceName `
            -BinaryPathName $ExePath `
            -DisplayName 'SelfDesk Remote Agent' `
            -Description 'Captura a tela e injeta input para acesso remoto via SelfDesk.' `
            -StartupType Automatic

Write-Host "Serviço '$ServiceName' instalado."

# Variáveis relevantes para o serviço (lidas do .env)
$EnvVarsToSet = @('ROLE','AGENT_ID','SHARED_SECRET','BROKER_HOST','BROKER_PORT',
                  'TLS_CA_PATH','TARGET_FPS','ENCODER','JPEG_QUALITY')

# Procura o .env: primeiro publish/, depois agent/
$EnvFile = $null
foreach ($candidate in @(
    (Join-Path $PublishDir '.env'),
    (Join-Path $Root 'agent' '.env')
)) {
    if (Test-Path $candidate) { $EnvFile = $candidate; break }
}

if ($null -eq $EnvFile) {
    Write-Warning ".env não encontrado. Execute .\scripts\bootstrap.ps1 -Role sender antes de continuar."
    Write-Warning "Após gerar o .env, rode install-service.ps1 novamente para configurar as variáveis."
    exit 1
}

Write-Host ""
Write-Host "Configurando variáveis de ambiente do serviço a partir de: $EnvFile"

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
Write-Host "Iniciando serviço..."
Start-Service -Name $ServiceName
Write-Host "Serviço '$ServiceName' iniciado."
