param(
    [Parameter(Mandatory = $true)][string]$Installer,
    [Parameter(Mandatory = $true)][string]$InstallDirectory
)
$ErrorActionPreference = 'Stop'
$setupPath = (Resolve-Path -LiteralPath $Installer).Path
$smokePath = [IO.Path]::GetFullPath($InstallDirectory)
$hostKey = 'HKCU:\Software\Mozilla\NativeMessagingHosts\com.bedside.remote'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{6036D5F6-3B81-41FC-9943-C6E98D1855D8}_is1'
if ((Test-Path -LiteralPath $hostKey) -or (Test-Path -LiteralPath $uninstallKey) -or (Test-Path -LiteralPath $smokePath)) {
    throw 'An existing installation, native registration or target directory is present. Refusing to overwrite it.'
}
$dataDirectory = Join-Path $env:LOCALAPPDATA 'BedsideRemote'
$dataExisted = Test-Path -LiteralPath $dataDirectory
New-Item -ItemType Directory -Force -Path $dataDirectory | Out-Null
$sentinel = Join-Path $dataDirectory ('installer-smoke-' + [Guid]::NewGuid().ToString('N') + '.txt')
Set-Content -LiteralPath $sentinel -Value 'Preserve data through installation, upgrade and uninstall.' -Encoding utf8
$uninstaller = Join-Path $smokePath 'unins000.exe'
$uninstalled = $false
function Invoke-Silent([string]$Executable, [string[]]$Arguments) {
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($null -ne $process.ExitCode -and $process.ExitCode -ne 0) { throw "Installer returned $($process.ExitCode)." }
}
try {
    for ($pass = 0; $pass -lt 2; $pass++) {
        Invoke-Silent $setupPath @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/DIR="' + $smokePath + '"'))
        $manifestPath = Join-Path $smokePath 'native-host.json'
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
        if ($manifest.path -ne (Join-Path $smokePath 'BedsideRemote.exe')) { throw 'Native manifest path is incorrect or Unicode was corrupted.' }
        if ($manifest.allowed_extensions.Count -ne 1 -or $manifest.allowed_extensions[0] -ne 'bedside-remote@hugo2006alm') { throw 'Native extension allowlist is incorrect.' }
        if ((Get-Item -LiteralPath $hostKey).GetValue('') -ne $manifestPath) { throw 'Current-user native host registration is incorrect.' }
        if (-not (Test-Path -LiteralPath $sentinel)) { throw 'Installation or upgrade removed app data.' }
    }
    Invoke-Silent $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    $uninstalled = $true
    if (Test-Path -LiteralPath $hostKey) { throw 'Uninstall did not remove native host registration.' }
    if (-not (Test-Path -LiteralPath $sentinel)) { throw 'Uninstall removed app data.' }
    Write-Output 'PASS: per-user installation, Unicode native manifest, HKCU registration, upgrade, uninstall and app-data preservation.'
}
finally {
    if (-not $uninstalled -and (Test-Path -LiteralPath $uninstaller)) { Invoke-Silent $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') }
    Remove-Item -LiteralPath $sentinel -ErrorAction SilentlyContinue
    if (-not $dataExisted -and (Get-ChildItem -LiteralPath $dataDirectory -Force | Measure-Object).Count -eq 0) { Remove-Item -LiteralPath $dataDirectory }
}
