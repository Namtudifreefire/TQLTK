param(
    [string]$Version,
    [string]$CertificateThumbprint = $env:TQLTK_SIGNING_CERT_THUMBPRINT,
    [string]$TimestampUrl = "https://timestamp.digicert.com",
    [switch]$UnsignedRelease
)
$ErrorActionPreference = "Stop"
[xml]$projectDefinition = Get-Content -LiteralPath (Join-Path $PSScriptRoot "PersonalInfoManager.csproj") -Raw
$projectVersions = @($projectDefinition.Project.PropertyGroup.Version | Where-Object { $_ })
if ($projectVersions.Count -ne 1) { throw "The project must define exactly one Version." }
$projectVersion = [string]$projectVersions[0]
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $projectVersion }
if ($Version -ne $projectVersion) { throw "Release version must match the project Version ($projectVersion). Update the project before releasing." }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must use major.minor.patch format." }
function Get-Sha256Hash {
    param([Parameter(Mandatory = $true)][string]$LiteralPath)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $stream = [IO.File]::OpenRead($LiteralPath)
        try {
            $digest = $algorithm.ComputeHash($stream)
            return [BitConverter]::ToString($digest).Replace("-", "").ToLowerInvariant()
        } finally { $stream.Dispose() }
    } finally { $algorithm.Dispose() }
}
$thumbprint = ($CertificateThumbprint -replace '\s', '').ToUpperInvariant()
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts"))
$workDirectoryName = "v$Version"
if (-not $thumbprint) { $workDirectoryName += "-unsigned" }
$workDirectory = [IO.Path]::GetFullPath((Join-Path $artifactsRoot $workDirectoryName))
$artifactsPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $workDirectory.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Release output path escaped the project artifacts directory."
}
if ((Split-Path -Leaf $workDirectory) -ne $workDirectoryName) { throw "Unexpected release output directory." }
$signTool = $null
$certificate = $null
if (-not $thumbprint -and -not $UnsignedRelease) {
    throw "A code-signing certificate is required for direct release-script execution; pass -UnsignedRelease to create an unsigned release package."
}
if ($thumbprint) {
    if ($thumbprint -notmatch '^[0-9A-F]{40}$') { throw "The certificate thumbprint must be 40 hexadecimal characters." }
    $certificate = Get-ChildItem -Path "Cert:\CurrentUser\My" |
        Where-Object { $_.Thumbprint.ToUpperInvariant() -eq $thumbprint } |
        Select-Object -First 1
    if (-not $certificate) { throw "Signing certificate was not found in CurrentUser\My." }
    if (-not $certificate.HasPrivateKey) { throw "Signing certificate has no private key." }
    $now = Get-Date
    if ($certificate.NotBefore -gt $now -or $certificate.NotAfter -le $now) {
        throw "Signing certificate is not currently valid."
    }
    $ekuExtension = $certificate.Extensions |
        Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } |
        Select-Object -First 1
    if (-not $ekuExtension -or -not ($ekuExtension.EnhancedKeyUsages | Where-Object { $_.Value -eq "1.3.6.1.5.5.7.3.3" })) {
        throw "Certificate must explicitly allow Code Signing (1.3.6.1.5.5.7.3.3)."
    }
    $timestampUri = [Uri]$TimestampUrl
    if ($timestampUri.Scheme -ne [Uri]::UriSchemeHttps) { throw "The timestamp URL must use HTTPS." }
    $signTool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
    if (-not $signTool) {
        $sdkBin = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
        $signTool = Get-ChildItem -Path $sdkBin -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName "x64\signtool.exe" } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
            Select-Object -First 1
    }
    if (-not $signTool) { throw "SignTool was not found. Install the Windows SDK or add SignTool to PATH." }
}
$testProject = Join-Path $projectRoot "tests\PersonalInfoManager.SecurityRegression.csproj"
if (-not (Test-Path -LiteralPath $testProject -PathType Leaf)) { throw "Security regression test project was not found." }
& dotnet run --project $testProject --configuration Release
if ($LASTEXITCODE -ne 0) { throw "Security regression suite failed with exit code $LASTEXITCODE." }
$artifactsRootExists = Test-Path -LiteralPath $artifactsRoot -PathType Container
if ($artifactsRootExists -and
    ((Get-Item -LiteralPath $artifactsRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw "The artifacts directory cannot be a reparse point."
}
$workDirectoryExists = Test-Path -LiteralPath $workDirectory -PathType Container
if ($workDirectoryExists -and
    ((Get-Item -LiteralPath $workDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw "The release output directory cannot be a reparse point."
}
if (Test-Path -LiteralPath $workDirectory) { Remove-Item -LiteralPath $workDirectory -Recurse -Force }
$publishDirectory = Join-Path $workDirectory "publish"
$packageDirectory = Join-Path $workDirectory "package"
New-Item -ItemType Directory -Path $publishDirectory, $packageDirectory -Force | Out-Null
$projectFile = Join-Path $projectRoot "PersonalInfoManager.csproj"
$publishArguments = @(
    "publish", $projectFile,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:Version=$Version",
    "-o", $publishDirectory
)
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
$executable = Join-Path $publishDirectory "PersonalInfoManager.exe"
$license = Join-Path $projectRoot "LICENSE"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Published executable was not found." }
if (-not (Test-Path -LiteralPath $license -PathType Leaf)) { throw "LICENSE was not found." }
if ($thumbprint) {
    & $signTool sign /sha1 $thumbprint /s My /fd SHA256 /tr $TimestampUrl /td SHA256 $executable
    if ($LASTEXITCODE -ne 0) { throw "Authenticode signing failed with exit code $LASTEXITCODE." }
    & $signTool verify /pa /all /v $executable
    if ($LASTEXITCODE -ne 0) { throw "Authenticode verification failed with exit code $LASTEXITCODE." }
    $signature = Get-AuthenticodeSignature -LiteralPath $executable
    if ($signature.Status.ToString() -ne "Valid" -or
        -not $signature.SignerCertificate -or
        $signature.SignerCertificate.Thumbprint.ToUpperInvariant() -ne $thumbprint) {
        throw "The signed executable did not verify with the selected publisher certificate."
    }
    $signatureStatus = "Valid"
} else {
    Write-Warning "Creating an unsigned release artifact. Windows may display a publisher or SmartScreen warning."
    $signatureStatus = "Unsigned"
}
$productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable).ProductVersion
if (-not $productVersion -or $productVersion -notmatch '^([0-9]+\.[0-9]+\.[0-9]+)(?:\+[0-9A-Za-z.-]+)?$' -or $Matches[1] -ne $Version) {
    throw "Published executable product version does not match $Version."
}
Copy-Item -LiteralPath $executable -Destination (Join-Path $packageDirectory "PersonalInfoManager.exe")
Copy-Item -LiteralPath $license -Destination (Join-Path $packageDirectory "LICENSE")
$zipPath = Join-Path $workDirectory "PersonalInfoManager-$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $packageDirectory "*") -DestinationPath $zipPath -CompressionLevel Optimal
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName } | Sort-Object)
    $expectedEntries = @("LICENSE", "PersonalInfoManager.exe") | Sort-Object
    if (($entries -join "|") -ne ($expectedEntries -join "|")) {
        throw "Release ZIP contents are invalid: $($entries -join ', ')"
    }
} finally { $archive.Dispose() }
$packagedExecutable = Join-Path $packageDirectory "PersonalInfoManager.exe"
$exeHash = Get-Sha256Hash -LiteralPath $packagedExecutable
$zipHash = Get-Sha256Hash -LiteralPath $zipPath
if ($thumbprint) { $releaseMode = "Signed" } else { $releaseMode = "Unsigned" }
$manifest = [ordered]@{
    SchemaVersion = 1
    Product = "TQLTK"
    Version = $Version
    ReleaseMode = $releaseMode
    Runtime = "win-x64"
    Architecture = "x64"
    PackageFileName = [IO.Path]::GetFileName($zipPath)
    PackageSha256 = $zipHash
    ExecutableFileName = "PersonalInfoManager.exe"
    ExecutableSha256 = $exeHash
    ProductVersion = $productVersion
    SignerThumbprint = $thumbprint
    AuthenticodeStatus = $signatureStatus
}
$manifestPath = Join-Path $workDirectory "release-manifest.json"
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
$manifestHash = Get-Sha256Hash -LiteralPath $manifestPath
$checksumPath = Join-Path $workDirectory "SHA256SUMS.txt"
$checksumText = "$zipHash  $(Split-Path -Leaf $zipPath)`r`n$exeHash  PersonalInfoManager.exe`r`n$manifestHash  release-manifest.json`r`n"
[IO.File]::WriteAllText($checksumPath, $checksumText, [Text.UTF8Encoding]::new($false))
Write-Host "Release artifacts created under: $workDirectory"
Write-Host "ZIP: $zipPath"
Write-Host "Manifest: $manifestPath"
Write-Host "SHA256: $checksumPath"
Write-Host "PersonalInfoManager.exe SHA256: $exeHash"
Write-Host "ZIP SHA256: $zipHash"
