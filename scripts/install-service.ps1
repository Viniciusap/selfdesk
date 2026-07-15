<#
.SYNOPSIS
    Installs the SelfDesk Sender as a Windows service (Phase 5).

.DESCRIPTION
    Requires Administrator privileges.
    Usage: .\scripts\install-service.ps1 [-Uninstall]

    Reads .env and automatically configures the service environment variables
    in Machine scope.

    WARNING: Windows Services run in Session 0 and cannot use DXGI Desktop
    Duplication (DXGI_ERROR_UNSUPPORTED). The sender will fall back to GDI,
    which also returns a black frame in Session 0. For DXGI capture to work,
    use install-task.ps1 (Task Scheduler) instead — it runs in the user's
    interactive session where DXGI is available.
#>

param([switch]$Uninstall)

$ServiceName = 'SelfDesk.Sender'
$Root        = Split-Path -Parent $PSScriptRoot

# Support two layouts:
#   repo:      $Root = repo root, exe at sender/publish/SelfDesk.Sender.exe
#   installed: $Root = C:\tools\selfdesk-sender, exe at SelfDesk.Sender.exe
$PublishDir = if (Test-Path (Join-Path $Root 'sender' 'publish' 'SelfDesk.Sender.exe')) {
    Join-Path $Root 'sender' 'publish'
} else {
    $Root
}
$ExePath = Join-Path $PublishDir 'SelfDesk.Sender.exe'

if ($Uninstall) {
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Stop-Service -Name $ServiceName -Force
        & sc.exe delete $ServiceName
        Write-Host "Service '$ServiceName' removed."
    } else {
        Write-Host "Service '$ServiceName' not found."
    }
    exit 0
}

if (-not (Test-Path $ExePath)) {
    Write-Host "Publishing sender..."
    Push-Location (Join-Path $Root 'sender')
    dotnet publish Sender.csproj -c Release -r win-x64 --self-contained false -o publish
    Pop-Location
}

# ── DXGI warning ──────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!' -ForegroundColor Yellow
Write-Host '!                        WARNING                                !' -ForegroundColor Yellow
Write-Host '!                                                               !' -ForegroundColor Yellow
Write-Host '!  Windows Services run in Session 0 (no display access).      !' -ForegroundColor Yellow
Write-Host '!  DXGI Desktop Duplication will fail -> GDI fallback -> BLACK  !' -ForegroundColor Yellow
Write-Host '!  SCREEN sent to the viewer.                                   !' -ForegroundColor Yellow
Write-Host '!                                                               !' -ForegroundColor Yellow
Write-Host '!  Use install-task.ps1 instead (Task Scheduler).              !' -ForegroundColor Yellow
Write-Host '!  It runs in the interactive session where DXGI works.        !' -ForegroundColor Yellow
Write-Host '!                                                               !' -ForegroundColor Yellow
Write-Host '!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!' -ForegroundColor Yellow
Write-Host ''
$answer = Read-Host 'Continue installing as service anyway? [y/N]'
if ($answer -notmatch '^[Yy]$') {
    Write-Host 'Aborted. Run .\scripts\install-task.ps1 for the recommended setup.' -ForegroundColor Cyan
    exit 0
}
Write-Host ''
# ─────────────────────────────────────────────────────────────────────────────

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Warning "Service '$ServiceName' already exists. Use -Uninstall first to reinstall."
    exit 1
}

# S54: run as a dedicated least-privilege local account instead of LocalSystem.
# LocalSystem has full control of the machine; this service only needs to read
# its own install directory and call SendInput/BitBlt, which any account can do.
$SvcAccount = "$env:COMPUTERNAME\SelfDeskSvc"
$SvcUser    = 'SelfDeskSvc'

Add-Type -Name LsaUtil -Namespace SelfDesk -MemberDefinition @'
[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern uint LsaOpenPolicy(ref LSA_UNICODE_STRING SystemName, ref LSA_OBJECT_ATTRIBUTES ObjectAttributes, int AccessMask, out IntPtr PolicyHandle);
[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern uint LsaAddAccountRights(IntPtr PolicyHandle, byte[] AccountSid, LSA_UNICODE_STRING[] UserRights, int CountOfRights);
[DllImport("advapi32.dll")]
public static extern int LsaClose(IntPtr ObjectHandle);
[StructLayout(LayoutKind.Sequential)]
public struct LSA_UNICODE_STRING { public ushort Length; public ushort MaximumLength; public IntPtr Buffer; }
[StructLayout(LayoutKind.Sequential)]
public struct LSA_OBJECT_ATTRIBUTES { public int Length; public IntPtr RootDirectory; public IntPtr ObjectName; public int Attributes; public IntPtr SecurityDescriptor; public IntPtr SecurityQualityOfService; }
'@ -UsingNamespace System.Runtime.InteropServices

function Grant-LogonAsServiceRight([System.Security.Principal.SecurityIdentifier]$Sid) {
    $sidBytes = New-Object byte[] ($Sid.BinaryLength)
    $Sid.GetBinaryForm($sidBytes, 0)

    $objAttr = New-Object SelfDesk.LsaUtil+LSA_OBJECT_ATTRIBUTES
    $sysName = New-Object SelfDesk.LsaUtil+LSA_UNICODE_STRING

    [IntPtr]$policyHandle = [IntPtr]::Zero
    $result = [SelfDesk.LsaUtil]::LsaOpenPolicy([ref]$sysName, [ref]$objAttr, 0x00000800, [ref]$policyHandle) # POLICY_CREATE_ACCOUNT | POLICY_LOOKUP_NAMES-ish (ALL_ACCESS below is simpler)
    if ($result -ne 0) { throw "LsaOpenPolicy failed: $result" }

    $rightStr = 'SeServiceLogonRight'
    $rightBuf = [Runtime.InteropServices.Marshal]::StringToHGlobalUni($rightStr)
    $right = New-Object SelfDesk.LsaUtil+LSA_UNICODE_STRING
    $right.Buffer = $rightBuf
    $right.Length = [uint16]($rightStr.Length * 2)
    $right.MaximumLength = [uint16](($rightStr.Length + 1) * 2)

    $rights = [SelfDesk.LsaUtil+LSA_UNICODE_STRING[]]@($right)
    $addResult = [SelfDesk.LsaUtil]::LsaAddAccountRights($policyHandle, $sidBytes, $rights, 1)
    [Runtime.InteropServices.Marshal]::FreeHGlobal($rightBuf)
    [SelfDesk.LsaUtil]::LsaClose($policyHandle) | Out-Null
    if ($addResult -ne 0) { throw "LsaAddAccountRights failed: $addResult" }
}

$existingUser = Get-LocalUser -Name $SvcUser -ErrorAction SilentlyContinue
if (-not $existingUser) {
    $bytes = New-Object byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $svcPassword = ConvertTo-SecureString -String ([Convert]::ToBase64String($bytes) + 'Aa1!') -AsPlainText -Force
    New-LocalUser -Name $SvcUser -Password $svcPassword -PasswordNeverExpires -UserMayNotChangePassword `
        -Description 'Least-privilege account for the SelfDesk Sender service (S54)' | Out-Null
    Write-Host "Created local service account '$SvcUser'."
} else {
    $svcPassword = $null
    Write-Warning "Local account '$SvcUser' already exists — reusing it. If you don't have its password, run with -Uninstall then re-run to regenerate it."
}

$svcSid = (Get-LocalUser -Name $SvcUser).SID
Grant-LogonAsServiceRight -Sid $svcSid
Write-Host "Granted 'Log on as a service' right to '$SvcAccount'."

# Restrict the install directory to this account + Administrators/SYSTEM (for updates)
$acl = Get-Acl $PublishDir
$acl.SetAccessRuleProtection($true, $false)
foreach ($sid in @('S-1-5-18', 'S-1-5-32-544', $svcSid.Value)) {  # SYSTEM, Administrators, service account
    $rule = New-Object Security.AccessControl.FileSystemAccessRule(
        $sid, 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
}
foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {  # SYSTEM, Administrators keep full control
    $rule = New-Object Security.AccessControl.FileSystemAccessRule(
        $sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
}
Set-Acl $PublishDir $acl

if ($svcPassword) {
    $cred = New-Object Management.Automation.PSCredential($SvcAccount, $svcPassword)
    New-Service -Name $ServiceName `
                -BinaryPathName $ExePath `
                -DisplayName 'SelfDesk Sender' `
                -Description 'Captures the screen and injects input for remote access via SelfDesk.' `
                -StartupType Automatic `
                -Credential $cred
} else {
    Write-Warning "No password available for existing account — configure the service LogonAs manually via services.msc, then Start-Service."
    New-Service -Name $ServiceName `
                -BinaryPathName $ExePath `
                -DisplayName 'SelfDesk Sender' `
                -Description 'Captures the screen and injects input for remote access via SelfDesk.' `
                -StartupType Automatic
}

Write-Host "Service '$ServiceName' installed (account: $SvcAccount)."

# S53: restrict service control to Administrators and SYSTEM only (prevent non-admin stop/start)
& sc.exe sdset $ServiceName 'D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)' | Out-Null
Write-Host "Service DACL restricted to Administrators and SYSTEM."

# S46: service reads config from .env in PublishDir directly (no Machine-scope env vars)
# Machine-scope env vars are readable by all local users — keep secrets only in .env with ACLs
Write-Host ""
Write-Host "NOTE: Service reads config from .env in $PublishDir"
Write-Host "      Run bootstrap.ps1 -Role sender to generate it if not done yet."

Write-Host ""
Write-Host "Starting service..."
Start-Service -Name $ServiceName
Write-Host "Service '$ServiceName' started."
