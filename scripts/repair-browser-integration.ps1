#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\Bedside Remote')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Run this repair on Windows.' }

$installPath = [System.IO.Path]::GetFullPath($InstallDirectory)
$executablePath = Join-Path $installPath 'BedsideRemote.exe'
$manifestPath = Join-Path $installPath 'native-host.json'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw 'The installed BedsideRemote.exe was not found. Install the Windows agent first, or specify -InstallDirectory.'
}

$hostName = 'com.bedside.remote'
$registryPath = 'Software\Mozilla\NativeMessagingHosts\' + $hostName
$registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
    [Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry64)
try {
    $key = $registry.CreateSubKey($registryPath)
    try {
        $alreadyMatched = $key.GetValue('') -ceq $manifestPath
        Write-Output "Registration matched before repair: $alreadyMatched"
        $manifest = [ordered]@{
            name = $hostName
            description = 'Bedside Remote browser bridge'
            path = $executablePath
            type = 'stdio'
            allowed_extensions = @('bedside-remote@hugo2006alm')
        }
        [System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Compress),
            [System.Text.UTF8Encoding]::new($false))
        $key.SetValue('', $manifestPath, [Microsoft.Win32.RegistryValueKind]::String)
        if ($key.GetValue('') -cne $manifestPath) { throw 'Native Messaging registration verification failed.' }
    } finally { $key.Dispose() }
} finally { $registry.Dispose() }

Write-Output 'Native Messaging manifest and current-user registration repaired.'
Write-Output 'Open the extension popup again; reconnection can take up to 30 seconds.'
