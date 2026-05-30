<#
.SYNOPSIS
    Bootstrap SelfDesk — gera o .env local de um componente (Windows).

.DESCRIPTION
    Uso: .\scripts\bootstrap.ps1 -Role <broker|sender|receiver>

    Nada gerado aqui deve ser commitado: o .gitignore ignora .env e certs/.
    Rode este script ANTES de 'dotnet run'.

.PARAMETER Role
    Papel deste nó: broker, sender ou receiver.
#>

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('broker', 'sender', 'receiver')]
    [string]$Role
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $PSScriptRoot
$CertDir = Join-Path $Root 'certs'

function Prompt-Value {
    param([string]$Question, [string]$Default = '')
    if ($Default) {
        $ans = Read-Host "$Question [$Default]"
        if ([string]::IsNullOrWhiteSpace($ans)) { return $Default }
        return $ans
    }
    do {
        $ans = Read-Host "$Question (obrigatório)"
    } while ([string]::IsNullOrWhiteSpace($ans))
    return $ans
}

function Confirm-Overwrite {
    param([string]$FilePath)
    if (Test-Path $FilePath) {
        $resp = Read-Host ".env já existe em '$FilePath'. Sobrescrever? (y/N)"
        if ($resp -notin @('y', 'Y')) {
            Write-Host 'Abortado.'
            exit 0
        }
    }
}

function New-RandomSecret {
    $bytes = New-Object byte[] 48
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes)
}

function New-Certs {
    if (-not (Test-Path $CertDir)) { New-Item -ItemType Directory -Path $CertDir | Out-Null }

    $serverCert = Join-Path $CertDir 'server-cert.pem'
    if (Test-Path $serverCert) {
        Write-Host 'Certificados já existem em certs/ — mantendo.'
        return
    }

    $openssl = Get-Command openssl -ErrorAction SilentlyContinue
    if (-not $openssl) {
        Write-Host 'openssl não encontrado. Instalando via winget...'
        if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
            Write-Warning 'winget não disponível. Instale o OpenSSL manualmente (winget install ShiningLight.OpenSSL) ou gere os certificados no broker Linux e copie certs/ca-cert.pem.'
            return
        }
        winget install --id ShiningLight.OpenSSL -e --accept-source-agreements --accept-package-agreements
        $env:PATH = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' +
                    [System.Environment]::GetEnvironmentVariable('PATH', 'User')
        $openssl = Get-Command openssl -ErrorAction SilentlyContinue
        if (-not $openssl) {
            Write-Warning 'OpenSSL instalado mas não encontrado no PATH desta sessão. Feche e reabra o terminal e rode o bootstrap novamente.'
            return
        }
        Write-Host 'OpenSSL instalado.'
    }

    $ip = Prompt-Value -Question 'IP/hostname deste broker (vai no SAN do certificado)' -Default '127.0.0.1'

    Write-Host 'Gerando CA e certificado de servidor...'

    $caKey  = Join-Path $CertDir 'ca-key.pem'
    $caCert = Join-Path $CertDir 'ca-cert.pem'
    $srvKey = Join-Path $CertDir 'server-key.pem'
    $srvCsr = Join-Path $CertDir 'server.csr'
    $srvCrt = Join-Path $CertDir 'server-cert.pem'
    $extFile = Join-Path $CertDir 'san.ext'

    & openssl req -x509 -newkey rsa:4096 -nodes -keyout $caKey -out $caCert -days 730 -subj '/CN=selfdesk-lan-ca' 2>$null
    & openssl req -newkey rsa:4096 -nodes -keyout $srvKey -out $srvCsr -subj "/CN=$ip" 2>$null
    Set-Content -Path $extFile -Value "subjectAltName=IP:$ip"
    & openssl x509 -req -in $srvCsr -CA $caCert -CAkey $caKey -CAcreateserial -out $srvCrt -days 825 -extfile $extFile 2>$null

    Remove-Item $srvCsr, $extFile -ErrorAction SilentlyContinue
    Write-Host "OK. Distribua '$caCert' para as máquinas sender/receiver (pinning via TLS_CA_PATH)."
}

# Detecta se estamos num release pré-compilado (exe ao lado de scripts/) ou no source tree
$Prebuilt = (Test-Path (Join-Path $Root 'SelfDesk.Agent.exe')) -or
            (Test-Path (Join-Path $Root 'SelfDesk.Viewer.exe')) -or
            (Test-Path (Join-Path $Root 'dist' 'index.js'))

switch ($Role) {
    'broker' {
        # Pré-compilado: .env fica em $Root (ao lado de dist/); source: em broker/
        $out     = if ($Prebuilt) { Join-Path $Root '.env' } else { Join-Path $Root 'broker' '.env' }
        $certRel = if ($Prebuilt) { 'certs/server-cert.pem' } else { '../certs/server-cert.pem' }
        $keyRel  = if ($Prebuilt) { 'certs/server-key.pem'  } else { '../certs/server-key.pem'  }
        Confirm-Overwrite $out

        $listenPort     = Prompt-Value -Question 'Porta de escuta do broker' -Default '7000'
        $allowedSenders = Prompt-Value -Question 'IDs de emissores permitidos (CSV)' -Default 'laptop-01'

        New-Certs

        $secret = New-RandomSecret
        if (-not $Prebuilt) {
            $dir = Join-Path $Root 'broker'
            if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
        }

        @"
ROLE=broker
SHARED_SECRET=$secret
LISTEN_PORT=$listenPort
ALLOWED_SENDERS=$allowedSenders
TLS_CERT_PATH=$certRel
TLS_KEY_PATH=$keyRel
LOG_LEVEL=info
"@ | Set-Content -Path $out -Encoding UTF8

        Write-Host ''
        Write-Host "Gerado: $out"
        Write-Host ''
        Write-Host '==> SHARED_SECRET (cole nos .env de sender e receiver):'
        Write-Host "    $secret"
    }

    'sender' {
        # Pré-compilado: .env fica em $Root (ao lado do exe); source: em agent/
        $out    = if ($Prebuilt) { Join-Path $Root '.env' } else { Join-Path $Root 'agent' '.env' }
        $caPath = if ($Prebuilt) {
            Prompt-Value -Question 'Caminho para ca-cert.pem (copiado do broker)' -Default (Join-Path $Root 'ca-cert.pem')
        } else { '../certs/ca-cert.pem' }
        Confirm-Overwrite $out

        $agentId     = Prompt-Value -Question 'ID único deste emissor' -Default 'laptop-01'
        $brokerHost  = Prompt-Value -Question 'IP/hostname do broker'
        $brokerPort  = Prompt-Value -Question 'Porta do broker' -Default '7000'
        $secret      = Prompt-Value -Question 'SHARED_SECRET (idêntico ao do broker)'
        if ($secret.Length -lt 32) {
            Write-Warning "SHARED_SECRET parece curto ($($secret.Length) chars). Certifique-se de copiar o valor completo gerado pelo broker."
            $c = Read-Host 'Continuar mesmo assim? (y/N)'
            if ($c -notin @('y','Y')) { Write-Host 'Abortado.'; exit 1 }
        }
        $targetFps   = Prompt-Value -Question 'FPS alvo' -Default '30'
        $encoder     = Prompt-Value -Question 'Encoder (jpeg|qsv|nvenc)' -Default 'jpeg'
        $jpegQuality = Prompt-Value -Question 'Qualidade JPEG (1-100)' -Default '75'

        if (-not $Prebuilt) {
            $dir = Join-Path $Root 'agent'
            if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
        }

        @"
ROLE=sender
AGENT_ID=$agentId
SHARED_SECRET=$secret
BROKER_HOST=$brokerHost
BROKER_PORT=$brokerPort
TLS_CA_PATH=$caPath
TARGET_FPS=$targetFps
ENCODER=$encoder
JPEG_QUALITY=$jpegQuality
"@ | Set-Content -Path $out -Encoding UTF8

        Write-Host ''
        Write-Host "Gerado: $out"
        if ($Prebuilt) {
            Write-Host "Copie ca-cert.pem do broker para: $caPath"
        } else {
            Write-Host "Lembre-se de copiar certs/ca-cert.pem do broker para esta máquina."
        }
    }

    'receiver' {
        # Pré-compilado: .env fica em $Root (ao lado do exe); source: em viewer/
        $out    = if ($Prebuilt) { Join-Path $Root '.env' } else { Join-Path $Root 'viewer' '.env' }
        $caPath = if ($Prebuilt) {
            Prompt-Value -Question 'Caminho para ca-cert.pem (copiado do broker)' -Default (Join-Path $Root 'ca-cert.pem')
        } else { '../certs/ca-cert.pem' }
        Confirm-Overwrite $out

        $brokerHost = Prompt-Value -Question 'IP/hostname do broker'
        $brokerPort = Prompt-Value -Question 'Porta do broker' -Default '7000'
        $secret     = Prompt-Value -Question 'SHARED_SECRET (idêntico ao do broker)'
        if ($secret.Length -lt 32) {
            Write-Warning "SHARED_SECRET parece curto ($($secret.Length) chars). Certifique-se de copiar o valor completo gerado pelo broker."
            $c = Read-Host 'Continuar mesmo assim? (y/N)'
            if ($c -notin @('y','Y')) { Write-Host 'Abortado.'; exit 1 }
        }

        if (-not $Prebuilt) {
            $dir = Join-Path $Root 'viewer'
            if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
        }

        @"
ROLE=receiver
SHARED_SECRET=$secret
BROKER_HOST=$brokerHost
BROKER_PORT=$brokerPort
TLS_CA_PATH=$caPath
"@ | Set-Content -Path $out -Encoding UTF8

        Write-Host ''
        Write-Host "Gerado: $out"
        if ($Prebuilt) {
            Write-Host "Copie ca-cert.pem do broker para: $caPath"
        } else {
            Write-Host "Lembre-se de copiar certs/ca-cert.pem do broker para esta máquina."
        }
    }
}
