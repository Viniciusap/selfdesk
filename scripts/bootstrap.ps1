<#
.SYNOPSIS
    Bootstrap SelfDesk — generates the local .env for a component (Windows).

.DESCRIPTION
    Usage: .\scripts\bootstrap.ps1 -Role <broker|sender|receiver>

    Nothing generated here should be committed: .gitignore ignores .env and certs/.
    Run this script BEFORE 'dotnet run'.

.PARAMETER Role
    Role of this node: broker, sender, or receiver.
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
        $ans = Read-Host "$Question (required)"
    } while ([string]::IsNullOrWhiteSpace($ans))
    return $ans
}

function Confirm-Overwrite {
    param([string]$FilePath)
    if (Test-Path $FilePath) {
        $resp = Read-Host ".env already exists at '$FilePath'. Overwrite? (y/N)"
        if ($resp -notin @('y', 'Y')) {
            Write-Host 'Aborted.'
            exit 0
        }
    }
}

function New-RandomSecret {
    $bytes = New-Object byte[] 48
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes)
}

function Set-OwnerOnly {
    param([string]$FilePath)
    $acl = Get-Acl $FilePath
    $acl.SetAccessRuleProtection($true, $false)
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $rule = New-Object Security.AccessControl.FileSystemAccessRule(
        $currentUser,
        [Security.AccessControl.FileSystemRights]::FullControl,
        [Security.AccessControl.AccessControlType]::Allow)
    $acl.AddAccessRule($rule)
    Set-Acl -Path $FilePath -AclObject $acl
}

function New-Certs {
    if (-not (Test-Path $CertDir)) { New-Item -ItemType Directory -Path $CertDir | Out-Null }

    $serverCert = Join-Path $CertDir 'server-cert.pem'
    if (Test-Path $serverCert) {
        Write-Host 'Certificates already exist in certs/ — keeping them.'
        return
    }

    $openssl = Get-Command openssl -ErrorAction SilentlyContinue
    if (-not $openssl) {
        Write-Host 'openssl not found. Installing via winget...'
        if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
            Write-Warning 'winget not available. Install OpenSSL manually (winget install ShiningLight.OpenSSL) or generate certificates on the Linux broker and copy certs/ca-cert.pem.'
            return
        }
        winget install --id ShiningLight.OpenSSL -e --accept-source-agreements --accept-package-agreements
        $env:PATH = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' +
                    [System.Environment]::GetEnvironmentVariable('PATH', 'User')
        $openssl = Get-Command openssl -ErrorAction SilentlyContinue
        if (-not $openssl) {
            Write-Warning 'OpenSSL installed but not found in PATH for this session. Close and reopen the terminal, then run bootstrap again.'
            return
        }
        Write-Host 'OpenSSL installed.'
    }

    $ip = Prompt-Value -Question 'IP/hostname of this broker (used in the certificate SAN)' -Default '127.0.0.1'

    Write-Host 'Generating CA and server certificate...'

    $caKey  = Join-Path $CertDir 'ca-key.pem'
    $caCert = Join-Path $CertDir 'ca-cert.pem'
    $srvKey = Join-Path $CertDir 'server-key.pem'
    $srvCsr = Join-Path $CertDir 'server.csr'
    $srvCrt = Join-Path $CertDir 'server-cert.pem'
    $extFile = Join-Path $CertDir 'san.ext'

    & openssl req -x509 -newkey rsa:4096 -nodes -keyout $caKey -out $caCert -days 730 -subj '/CN=selfdesk-lan-ca' 2>$null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $caCert)) {
        throw "Failed to generate CA certificate. Check openssl installation."
    }
    & openssl req -newkey rsa:4096 -nodes -keyout $srvKey -out $srvCsr -subj "/CN=$ip" 2>$null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $srvCsr)) {
        throw "Failed to generate server CSR."
    }
    Set-Content -Path $extFile -Value "subjectAltName=IP:$ip"
    & openssl x509 -req -in $srvCsr -CA $caCert -CAkey $caKey -CAcreateserial -out $srvCrt -days 365 -extfile $extFile 2>$null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $srvCrt)) {
        throw "Failed to sign server certificate."
    }

    Remove-Item $srvCsr, $extFile -ErrorAction SilentlyContinue
    Write-Host "Done. Distribute '$caCert' to sender/receiver machines (TLS_CA_PATH pinning)."
}

# Detect whether we are in a pre-built release (exe next to scripts/) or source tree
$Prebuilt = (Test-Path (Join-Path $Root 'SelfDesk.Sender.exe')) -or
            (Test-Path (Join-Path $Root 'SelfDesk.Viewer.exe')) -or
            (Test-Path (Join-Path $Root 'dist' 'index.js'))

switch ($Role) {
    'broker' {
        # Pre-built: .env lives in $Root (next to dist/); source: in broker/
        $out     = if ($Prebuilt) { Join-Path $Root '.env' } else { Join-Path $Root 'broker' '.env' }
        $certRel = if ($Prebuilt) { 'certs/server-cert.pem' } else { '../certs/server-cert.pem' }
        $keyRel  = if ($Prebuilt) { 'certs/server-key.pem'  } else { '../certs/server-key.pem'  }
        Confirm-Overwrite $out

        $listenPort     = Prompt-Value -Question 'Broker listen port' -Default '7000'
        $allowedSenders = Prompt-Value -Question 'Allowed sender IDs (comma-separated)' -Default 'laptop-01'

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
        Set-OwnerOnly $out

        Write-Host ''
        Write-Host "Generated: $out (permissions restricted to owner)"
        Write-Host ''
        Write-Host '==> SHARED_SECRET saved to the .env file.'
        Write-Host '    Copy it manually to sender/receiver .env files.'
        Write-Host '    Do not share via chat, email, or screen recording.'
    }

    'sender' {
        # Pre-built: .env lives in $Root (next to exe); source: in sender/
        $out    = if ($Prebuilt) { Join-Path $Root '.env' } else { Join-Path $Root 'sender' '.env' }
        $caPath = if ($Prebuilt) {
            Prompt-Value -Question 'Path to ca-cert.pem (copied from broker)' -Default (Join-Path $Root 'ca-cert.pem')
        } else { '../certs/ca-cert.pem' }
        Confirm-Overwrite $out

        $senderId    = Prompt-Value -Question 'Unique ID for this sender' -Default 'laptop-01'
        $brokerHost  = Prompt-Value -Question 'Broker IP/hostname'
        $brokerPort  = Prompt-Value -Question 'Broker port' -Default '7000'
        $secret      = Prompt-Value -Question 'SHARED_SECRET (same as the broker)'
        if ($secret.Length -lt 32) {
            Write-Warning "SHARED_SECRET looks short ($($secret.Length) chars). Make sure you copied the full value from the broker."
            $c = Read-Host 'Continue anyway? (y/N)'
            if ($c -notin @('y','Y')) { Write-Host 'Aborted.'; exit 1 }
        }
        $targetFps   = Prompt-Value -Question 'Target FPS' -Default '30'
        $encoder     = Prompt-Value -Question 'Encoder (jpeg|qsv|nvenc)' -Default 'jpeg'
        $jpegQuality = Prompt-Value -Question 'JPEG quality (1-100)' -Default '75'

        if (-not $Prebuilt) {
            $dir = Join-Path $Root 'sender'
            if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
        }

        @"
ROLE=sender
SENDER_ID=$senderId
SHARED_SECRET=$secret
BROKER_HOST=$brokerHost
BROKER_PORT=$brokerPort
TLS_CA_PATH=$caPath
TARGET_FPS=$targetFps
ENCODER=$encoder
JPEG_QUALITY=$jpegQuality
"@ | Set-Content -Path $out -Encoding UTF8
        Set-OwnerOnly $out

        Write-Host ''
        Write-Host "Generated: $out (permissions restricted to owner)"
        if ($Prebuilt) {
            Write-Host "Copy ca-cert.pem from the broker to: $caPath"
        } else {
            Write-Host "Remember to copy certs/ca-cert.pem from the broker to this machine."
        }
    }

    'receiver' {
        # Pre-built: .env lives in $Root (next to exe); source: in viewer/
        $out    = if ($Prebuilt) { Join-Path $Root '.env' } else { Join-Path $Root 'viewer' '.env' }
        $caPath = if ($Prebuilt) {
            Prompt-Value -Question 'Path to ca-cert.pem (copied from broker)' -Default (Join-Path $Root 'ca-cert.pem')
        } else { '../certs/ca-cert.pem' }
        Confirm-Overwrite $out

        $brokerHost = Prompt-Value -Question 'Broker IP/hostname'
        $brokerPort = Prompt-Value -Question 'Broker port' -Default '7000'
        $secret     = Prompt-Value -Question 'SHARED_SECRET (same as the broker)'
        if ($secret.Length -lt 32) {
            Write-Warning "SHARED_SECRET looks short ($($secret.Length) chars). Make sure you copied the full value from the broker."
            $c = Read-Host 'Continue anyway? (y/N)'
            if ($c -notin @('y','Y')) { Write-Host 'Aborted.'; exit 1 }
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
        Set-OwnerOnly $out

        Write-Host ''
        Write-Host "Generated: $out (permissions restricted to owner)"
        if ($Prebuilt) {
            Write-Host "Copy ca-cert.pem from the broker to: $caPath"
        } else {
            Write-Host "Remember to copy certs/ca-cert.pem from the broker to this machine."
        }
    }
}
