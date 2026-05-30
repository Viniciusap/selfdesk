<#
.SYNOPSIS
    Instala o SelfDesk Agent como serviço do Windows (Fase 5).

.DESCRIPTION
    Requer execução como administrador.
    Uso: .\scripts\install-service.ps1 [-Uninstall]

.NOTES
    Após instalar, configure as variáveis de ambiente do serviço via:
    sc.exe config SelfDesk.Agent depend= "" start= auto
    [Environment]::SetEnvironmentVariable("SHARED_SECRET", "...", "Machine")
    ... (demais variáveis)
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
Write-Host ""
Write-Host "Configure as variáveis de ambiente do serviço (Machine scope):"
Write-Host '  [Environment]::SetEnvironmentVariable("SHARED_SECRET", "<secret>", "Machine")'
Write-Host '  [Environment]::SetEnvironmentVariable("BROKER_HOST",   "<ip>",     "Machine")'
Write-Host '  [Environment]::SetEnvironmentVariable("BROKER_PORT",   "7000",     "Machine")'
Write-Host '  [Environment]::SetEnvironmentVariable("TLS_CA_PATH",   "<path>",   "Machine")'
Write-Host '  [Environment]::SetEnvironmentVariable("AGENT_ID",      "laptop-01","Machine")'
Write-Host ""
Write-Host "Depois inicie o serviço:"
Write-Host "  Start-Service -Name $ServiceName"
