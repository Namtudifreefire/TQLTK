[CmdletBinding()]
param(
    [switch]$InstallSdk,
    [ValidateSet('', 'rollback', 'crash', 'disk-full', 'transfer-export', 'transfer-import', 'windows-paths')]
    [string]$BackupLabMode = '',
    [string]$LabPath = '.'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'This suite requires Windows, DPAPI, and WPF; it cannot run on Linux.'
}
if ($env:TQLTK_REGRESSION_FILTER) {
    throw 'Remove TQLTK_REGRESSION_FILTER: CI must run the complete regression suite.'
}
if ($env:CI_DEBUG_TRACE -eq 'true') {
    throw 'Disable CI_DEBUG_TRACE before running security tests.'
}
$repositoryRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'test-windows-dpapi.ps1')
$originalLocation = Get-Location
$globalJson = Join-Path $repositoryRoot 'global.json'
$createdGlobalJson = $false
$sdkConfiguration = $null
try {
    Set-Location -LiteralPath $repositoryRoot
    if ($InstallSdk) {
        $sdkDirectory = Join-Path ([IO.Path]::GetTempPath()) ('TQLTK-CI-SDK-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $sdkDirectory | Out-Null
        $installer = Join-Path $sdkDirectory 'dotnet-install.ps1'
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
        & $installer -Channel '10.0' -Quality GA -InstallDir $sdkDirectory -NoPath
        if (-not (Test-Path -LiteralPath (Join-Path $sdkDirectory 'dotnet.exe'))) {
            throw 'The .NET 10 SDK installer did not produce dotnet.exe.'
        }
        $env:DOTNET_ROOT = $sdkDirectory
        $env:PATH = $sdkDirectory + [IO.Path]::PathSeparator + $env:PATH
    }
    $sdkList = @(& dotnet --list-sdks)
    if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate installed .NET SDKs.' }
    $sdkVersions = @($sdkList | ForEach-Object {
        if ($_ -match '^(10\.0\.\d+)\s') { [version]$Matches[1] }
    } | Sort-Object -Descending)
    if ($sdkVersions.Count -eq 0) { throw 'A stable .NET 10 SDK is required.' }
    if (-not (Test-Path -LiteralPath $globalJson)) {
        $sdkConfiguration = @{ sdk = @{ version = $sdkVersions[0].ToString(); rollForward = 'latestPatch'; allowPrerelease = $false } } | ConvertTo-Json
        [IO.File]::WriteAllText($globalJson, $sdkConfiguration)
        $createdGlobalJson = $true
    }
    $selectedSdk = & dotnet --version
    if ($LASTEXITCODE -ne 0 -or $selectedSdk -notmatch '^10\.0\.\d+$') {
        throw 'SDK selection must resolve to stable .NET 10.'
    }
    Write-Output ('Running Windows security checks with SDK ' + $selectedSdk)
    & dotnet restore Nguon/PersonalInfoManager.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked application restore failed.' }
    if ($BackupLabMode) {
        & dotnet run --project Nguon/tests/PersonalInfoManager.SecurityRegression.csproj --configuration Release -- --backup-lab $BackupLabMode $LabPath
    }
    else {
        & dotnet run --project Nguon/tests/PersonalInfoManager.SecurityRegression.csproj --configuration Release
    }
    if ($LASTEXITCODE -ne 0) { throw 'Security regression checks failed; inspect the named failing check.' }
}
finally {
    if ($createdGlobalJson -and (Test-Path -LiteralPath $globalJson)) {
        if ([IO.File]::ReadAllText($globalJson) -ceq $sdkConfiguration) {
            Remove-Item -LiteralPath $globalJson
        }
        else { Write-Warning 'global.json changed during the run; it was preserved.' }
    }
    Set-Location -LiteralPath $originalLocation.Path
}
