#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Android', 'Mozilla', 'Windows')][string]$Component,
    [Parameter(Mandatory)][string]$GitHubAccount,
    [string]$Repository = 'hugo2006alm/bedside-remote',
    [string]$AndroidSigningDirectory = (Join-Path $env:LOCALAPPDATA 'BedsideRemote/Signing/android'),
    [string]$PfxFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Run this setup on Windows with PowerShell 7.' }
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid GitHub repository.' }
$ghPath = (Get-Command gh -ErrorAction Stop).Source
$activeAccount = & $ghPath api user --jq .login
if ($LASTEXITCODE -ne 0 -or $activeAccount -ne $GitHubAccount) {
    throw 'The active GitHub account differs from the explicitly selected account. Switch gh accounts before retrying.'
}

function Set-RepositorySecret([string]$Name, [string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { throw "Secret $Name cannot be empty." }
    $start = [Diagnostics.ProcessStartInfo]::new($ghPath)
    $start.UseShellExecute = $false
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('secret', 'set', $Name, '--repo', $Repository)) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.Write($Value)
        $process.StandardInput.Close()
        $process.WaitForExit()
        $null = $outputTask.GetAwaiter().GetResult()
        $null = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Could not save $Name in GitHub Actions secrets. No credential values were printed." }
    }
    finally { $process.Dispose() }
    Write-Output "Configured GitHub Actions secret: $Name"
}

function Read-SecretText([string]$Prompt) {
    $secure = Read-Host $Prompt -AsSecureString
    return [Net.NetworkCredential]::new('', $secure).Password
}

switch ($Component) {
    'Android' {
        $keytool = (Get-Command keytool -ErrorAction Stop).Source
        $directory = [IO.Path]::GetFullPath($AndroidSigningDirectory)
        $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
        if ($directory -eq $repositoryRoot -or $directory.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Store the signing identity outside the repository.'
        }
        $null = New-Item -ItemType Directory -Path $directory -Force
        # Set only the access ACL; reapplying an ACL read from disk can also
        # request audit/owner privileges that an ordinary publisher does not have.
        $acl = [Security.AccessControl.DirectorySecurity]::new()
        $acl.SetAccessRuleProtection($true, $false)
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
        foreach ($sid in @($identity, [Security.Principal.SecurityIdentifier]::new('S-1-5-18'))) {
            $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
        }
        [IO.FileSystemAclExtensions]::SetAccessControl([IO.DirectoryInfo]::new($directory), $acl)
        $keystore = Join-Path $directory 'release.p12'
        $passwordFile = Join-Path $directory 'password.clixml'
        $hasKeystore = Test-Path -LiteralPath $keystore
        $hasPassword = Test-Path -LiteralPath $passwordFile
        if ($hasKeystore -ne $hasPassword) { throw 'Incomplete signing identity. Restore its missing file; existing material will not be overwritten.' }
        if (-not $hasKeystore) {
            $randomPassword = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
            $credential = [PSCredential]::new('bedside-remote-release', (ConvertTo-SecureString $randomPassword -AsPlainText -Force))
            $credential | Export-Clixml -LiteralPath $passwordFile
            $env:BEDSIDE_KEYTOOL_PASSWORD = $randomPassword
            try {
                & $keytool -genkeypair -storetype PKCS12 -keystore $keystore -alias $credential.UserName -keyalg RSA -keysize 4096 -sigalg SHA256withRSA -validity 10950 -dname 'CN=Bedside Remote Android Release' -storepass:env BEDSIDE_KEYTOOL_PASSWORD -keypass:env BEDSIDE_KEYTOOL_PASSWORD
                if ($LASTEXITCODE -ne 0) { throw 'Android key generation failed. Inspect the protected directory before retrying.' }
            }
            finally { Remove-Item Env:BEDSIDE_KEYTOOL_PASSWORD -ErrorAction SilentlyContinue; $randomPassword = $null }
            Write-Output 'Created a persistent Android release identity with 30-year validity.'
        }
        else { Write-Output 'Reusing the existing Android signing identity; no key rotation performed.' }
        $credential = Import-Clixml -LiteralPath $passwordFile
        $password = $credential.GetNetworkCredential().Password
        $env:BEDSIDE_KEYTOOL_PASSWORD = $password
        try {
            & $keytool -list -keystore $keystore -alias $credential.UserName -storepass:env BEDSIDE_KEYTOOL_PASSWORD
            if ($LASTEXITCODE -ne 0) { throw 'The stored Android signing identity could not be opened.' }
            Set-RepositorySecret 'ANDROID_KEYSTORE_PASSWORD' $password
            Set-RepositorySecret 'ANDROID_KEY_PASSWORD' $password
            Set-RepositorySecret 'ANDROID_KEY_ALIAS' $credential.UserName
            Set-RepositorySecret 'ANDROID_KEYSTORE_BASE64' ([Convert]::ToBase64String([IO.File]::ReadAllBytes($keystore)))
        }
        finally { Remove-Item Env:BEDSIDE_KEYTOOL_PASSWORD -ErrorAction SilentlyContinue; $password = $null; $credential = $null }
        Write-Output "Signing identity stored outside the repository: $directory"
        Write-Output 'The password file uses current-user Windows DPAPI. Back up the keystore and recoverable password in a password manager before reinstalling Windows or losing this profile.'
    }
    'Mozilla' {
        $apiKey = Read-SecretText 'Mozilla Add-on Developer API key (hidden)'
        $apiSecret = Read-SecretText 'Mozilla Add-on Developer API secret (hidden)'
        try {
            Set-RepositorySecret 'MOZILLA_API_KEY' $apiKey
            Set-RepositorySecret 'MOZILLA_API_SECRET' $apiSecret
        }
        finally { $apiKey = $null; $apiSecret = $null }
        Write-Output 'Mozilla credentials configured. The next eligible release will request unlisted signing; Mozilla may require review.'
    }
    'Windows' {
        if (-not $PfxFile) { throw 'Supply -PfxFile with your existing Authenticode PFX. This script does not create a trusted publisher certificate.' }
        $pfxPath = (Resolve-Path -LiteralPath $PfxFile).Path
        $password = Read-SecretText 'Authenticode PFX password (hidden)'
        $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($pfxPath, $password, [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
        $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
        try {
            $chain.ChainPolicy.ApplicationPolicy.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
            if (-not $certificate.HasPrivateKey -or -not $chain.Build($certificate)) { throw 'A valid trusted code-signing certificate with its private key is required.' }
            Set-RepositorySecret 'WINDOWS_SIGNING_PASSWORD' $password
            Set-RepositorySecret 'WINDOWS_SIGNING_PFX_BASE64' ([Convert]::ToBase64String([IO.File]::ReadAllBytes($pfxPath)))
        }
        finally { $chain.Dispose(); $certificate.Dispose(); $password = $null }
    }
}
