param(
    [string]$Version = "3.6.7",
    [string]$CertificateThumbprint = $env:TQLTK_SIGNING_CERT_THUMBPRINT,
    [string]$TimestampUrl = "https://timestamp.digicert.com",
    [switch]$RequireSignature
)

$ErrorActionPreference = "Stop"
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts"))
$workDirectory = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "v$Version"))
$artifactsPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

if (-not $workDirectory.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Release output path escaped the project artifacts directory."
}

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
if ((Split-Path -Leaf $workDirectory) -ne "v$Version") {
    throw "Unexpected release output directory."
}

if (Test-Path -LiteralPath $workDirectory) {
    Remove-Item -LiteralPath $workDirectory -Recurse -Force
}
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
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$executable = Join-Path $publishDirectory "PersonalInfoManager.exe"
$license = Join-Path $projectRoot "LICENSE"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Published executable was not found: $executable"
}
if (-not (Test-Path -LiteralPath $license -PathType Leaf)) {
    throw "LICENSE was not found: $license"
}

$thumbprint = ($CertificateThumbprint -replace '\s', '').ToUpperInvariant()
if ($thumbprint) {
    $certificate = Get-ChildItem -Path "Cert:\CurrentUser\My" |
        Where-Object { $_.Thumbprint -eq $thumbprint } |
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

    & $signTool sign /sha1 $thumbprint /s My /fd SHA256 /tr $TimestampUrl /td SHA256 $executable
    if ($LASTEXITCODE -ne 0) { throw "Authenticode signing failed with exit code $LASTEXITCODE." }
    & $signTool verify /pa /all $executable
    if ($LASTEXITCODE -ne 0) { throw "Authenticode verification failed with exit code $LASTEXITCODE." }
    Write-Host "Authenticode signature verified using certificate $thumbprint."
} elseif ($RequireSignature) {
    throw "A trusted code-signing certificate is required. Set TQLTK_SIGNING_CERT_THUMBPRINT or pass -CertificateThumbprint."
} else {
    Write-Warning "No code-signing certificate was provided. The executable and ZIP will be unsigned; SHA-256 checksums will still be generated."
}

Copy-Item -LiteralPath $executable -Destination (Join-Path $packageDirectory "PersonalInfoManager.exe")
Copy-Item -LiteralPath $license -Destination (Join-Path $packageDirectory "LICENSE")

$zipPath = Join-Path $workDirectory "PersonalInfoManager-$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $packageDirectory "*") -DestinationPath $zipPath -CompressionLevel Optimal

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName } | Sort-Object)
    $expectedEntries = @("LICENSE", "PersonalInfoManager.exe") | Sort-Object
    if (($entries -join "|") -ne ($expectedEntries -join "|")) {
        throw "Release ZIP contents are invalid: $($entries -join ', ')"
    }
} finally {
    $archive.Dispose()
}

$exeHash = (Get-FileHash -LiteralPath (Join-Path $packageDirectory "PersonalInfoManager.exe") -Algorithm SHA256).Hash.ToLowerInvariant()
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = Join-Path $workDirectory "SHA256SUMS.txt"
$checksumText = "$zipHash  $(Split-Path -Leaf $zipPath)`r`n$exeHash  PersonalInfoManager.exe`r`n"
[IO.File]::WriteAllText($checksumPath, $checksumText, [Text.UTF8Encoding]::new($false))

Write-Host "Release artifacts created under: $workDirectory"
Write-Host "ZIP: $zipPath"
Write-Host "SHA256: $checksumPath"
Write-Host "PersonalInfoManager.exe SHA256: $exeHash"
Write-Host "ZIP SHA256: $zipHash"
