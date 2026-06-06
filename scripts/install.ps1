<#
.SYNOPSIS
    Installs dependencies and compiles a SelfDesk component (Windows).

.DESCRIPTION
    Usage: .\scripts\install.ps1 -Role <broker|sender|receiver> [-SkipFFmpeg] [-Publish]

    Missing prerequisites (Node.js LTS, .NET 10 SDK) are installed
    automatically via winget.

    For sender and receiver with ENCODER=qsv|nvenc (Phase 4), FFmpeg 7.x
    shared DLLs are downloaded from BtbN and copied to the build output.

    After this script, run:
        .\scripts\bootstrap.ps1 -Role <role>

.PARAMETER Role
    Role of this node: broker, sender, or receiver.

.PARAMETER SkipFFmpeg
    Skip FFmpeg DLL download (not needed when ENCODER=jpeg).

.PARAMETER Publish
    For sender/receiver: publish as self-contained instead of just building.
    Required for installing as a Windows service (Phase 5).
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
        throw 'winget not found. Install App Installer from the Microsoft Store or update Windows 10/11.'
    }
}

function Ensure-Command {
    param([string]$Bin, [string]$WingetId, [string]$Label)
    if (Get-Command $Bin -ErrorAction SilentlyContinue) {
        Write-Host "  $Label already installed."
        return
    }
    Ensure-Winget
    Write-Host "  Installing $Label via winget ($WingetId)..."
    winget install --id $WingetId -e --accept-source-agreements --accept-package-agreements
    # Reload PATH for the current process
    $env:PATH = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' +
                [System.Environment]::GetEnvironmentVariable('PATH', 'User')
    if (-not (Get-Command $Bin -ErrorAction SilentlyContinue)) {
        Write-Warning "  $Label installed but '$Bin' is not yet in PATH for this session."
        Write-Warning "  Close and reopen the terminal, then run this script again."
        exit 1
    }
    Write-Host "  $Label installed."
}

function Install-FFmpeg {
    param([string]$TargetDir)

    $marker = Join-Path $TargetDir 'avcodec-61.dll'
    if (Test-Path $marker) {
        Write-Host '  FFmpeg DLLs already present — skipping download.'
        return
    }

    $zipUrl = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n7.1-latest-win64-gpl-shared-7.1.zip'
    $tmpZip = Join-Path $env:TEMP 'ffmpeg-shared.zip'
    $tmpDir = Join-Path $env:TEMP 'ffmpeg-shared'

    Write-Host '  Downloading FFmpeg 7.1 shared (BtbN)...'
    Invoke-WebRequest -Uri $zipUrl -OutFile $tmpZip -UseBasicParsing

    Write-Host '  Extracting...'
    if (Test-Path $tmpDir) { Remove-Item $tmpDir -Recurse -Force }
    Expand-Archive -Path $tmpZip -DestinationPath $tmpDir

    # The inner folder name varies; find the subdirectory that contains bin/
    $binSrc = Get-ChildItem -Path $tmpDir -Recurse -Directory -Filter 'bin' |
              Select-Object -First 1

    if (-not $binSrc) {
        throw 'Unexpected structure in FFmpeg zip — bin/ folder not found.'
    }

    if (-not (Test-Path $TargetDir)) { New-Item -ItemType Directory -Path $TargetDir | Out-Null }

    Write-Host "  Copying DLLs to $TargetDir..."
    Get-ChildItem -Path $binSrc.FullName -Filter '*.dll' |
        Copy-Item -Destination $TargetDir -Force

    Remove-Item $tmpZip -ErrorAction SilentlyContinue
    Remove-Item $tmpDir -Recurse -ErrorAction SilentlyContinue

    Write-Host '  FFmpeg DLLs copied.'
}

# ── Install by role ───────────────────────────────────────────────────────────

switch ($Role) {

    'broker' {
        Write-Host ''
        Write-Host '=== SelfDesk Install — broker ==='
        Write-Host ''
        Write-Host '-> Checking Node.js LTS...'
        Ensure-Command -Bin 'node' -WingetId 'OpenJS.NodeJS.LTS' -Label 'Node.js LTS'

        Write-Host '-> Installing npm dependencies...'
        Push-Location (Join-Path $Root 'broker')
        npm install
        Write-Host '-> Compiling TypeScript...'
        npm run build
        Pop-Location

        Write-Host ''
        Write-Host '✔ Broker compiled to broker/dist/'
        Write-Host ''
        Write-Host 'Next step:'
        Write-Host '  .\scripts\bootstrap.ps1 -Role broker'
        Write-Host '  cd broker && npm start'
    }

    'sender' {
        Write-Host ''
        Write-Host '=== SelfDesk Install — sender ==='
        Write-Host ''
        Write-Host '-> Checking .NET 10 SDK...'
        Ensure-Command -Bin 'dotnet' -WingetId 'Microsoft.DotNet.SDK.10' -Label '.NET 10 SDK'

        if ($Publish) {
            Write-Host '-> Publishing sender (self-contained)...'
            $OutDir = Join-Path $Root 'sender' 'publish'
            Push-Location (Join-Path $Root 'sender')
            dotnet publish Sender.csproj -c Release -r win-x64 --self-contained false -o $OutDir
            Pop-Location
            $BinDir = $OutDir
        } else {
            Write-Host '-> Building sender...'
            Push-Location (Join-Path $Root 'sender')
            dotnet build Sender.csproj -c Release
            Pop-Location
            $BinDir = Join-Path $Root 'sender' 'bin' 'Release' 'net10.0-windows'
        }

        if (-not $SkipFFmpeg) {
            Write-Host '-> Installing FFmpeg DLLs (Phase 4 — H264)...'
            Install-FFmpeg -TargetDir $BinDir
        }

        Write-Host ''
        Write-Host "✔ Sender built to $BinDir"
        Write-Host ''
        Write-Host 'Next step:'
        Write-Host '  .\scripts\bootstrap.ps1 -Role sender'
        Write-Host '  cd sender && dotnet run'
    }

    'receiver' {
        Write-Host ''
        Write-Host '=== SelfDesk Install — receiver (viewer) ==='
        Write-Host ''
        Write-Host '-> Checking .NET 10 SDK...'
        Ensure-Command -Bin 'dotnet' -WingetId 'Microsoft.DotNet.SDK.10' -Label '.NET 10 SDK'

        if ($Publish) {
            Write-Host '-> Publishing viewer (self-contained)...'
            $OutDir = Join-Path $Root 'viewer' 'publish'
            Push-Location (Join-Path $Root 'viewer')
            dotnet publish Viewer.csproj -c Release -r win-x64 --self-contained false -o $OutDir
            Pop-Location
            $BinDir = $OutDir
        } else {
            Write-Host '-> Building viewer...'
            Push-Location (Join-Path $Root 'viewer')
            dotnet build Viewer.csproj -c Release
            Pop-Location
            $BinDir = Join-Path $Root 'viewer' 'bin' 'Release' 'net10.0-windows'
        }

        if (-not $SkipFFmpeg) {
            Write-Host '-> Installing FFmpeg DLLs (Phase 4 — H264)...'
            Install-FFmpeg -TargetDir $BinDir
        }

        Write-Host ''
        Write-Host "✔ Viewer built to $BinDir"
        Write-Host ''
        Write-Host 'Next step:'
        Write-Host '  .\scripts\bootstrap.ps1 -Role receiver'
        Write-Host '  cd viewer && dotnet run'
    }
}
