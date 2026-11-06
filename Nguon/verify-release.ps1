param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedVersion,
    [string]$ExpectedPublisherThumbprint = "",
    [string]$ExpectedPackageSha256 = ""
)

$ErrorActionPreference = "Stop"

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

$package = [IO.Path]::GetFullPath($PackagePath)
$manifestFile = [IO.Path]::GetFullPath($ManifestPath)
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw "Package file was not found." }
if (-not (Test-Path -LiteralPath $manifestFile -PathType Leaf)) { throw "Release manifest was not found." }
if ($ExpectedVersion -notmatch '^\d+\.\d+\.\d+$') { throw "Expected version must use major.minor.patch format." }

$thumbprint = ($ExpectedPublisherThumbprint -replace '\s', '').ToUpperInvariant()

$manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
if ($manifest.SchemaVersion -ne 1 -or $manifest.Product -ne "TQLTK") { throw "Unsupported release manifest." }
if ($manifest.Version -ne $ExpectedVersion) { throw "Manifest version does not match the expected version." }
if ($manifest.PackageFileName -ne [IO.Path]::GetFileName($package)) { throw "Manifest package name does not match the selected file." }
$signatureStatus = [string]$manifest.AuthenticodeStatus
$manifestThumbprint = ([string]$manifest.SignerThumbprint -replace '\s', '').ToUpperInvariant()
if ($manifest.ReleaseMode -and
    (($manifest.ReleaseMode -eq "Signed" -and $signatureStatus -ne "Valid") -or
     ($manifest.ReleaseMode -eq "Unsigned" -and $signatureStatus -ne "Unsigned"))) {
    throw "Release mode and Authenticode status do not match."
}
if ($signatureStatus -eq "Valid") {
    if ($thumbprint -notmatch '^[0-9A-F]{40}$') { throw "A pinned 40-character SHA-1 certificate thumbprint is required for signed releases." }
    if ($manifestThumbprint -ne $thumbprint) { throw "Manifest signer does not match the pinned publisher certificate." }
} elseif ($signatureStatus -eq "Unsigned") {
    if ($thumbprint) { throw "An expected publisher thumbprint was supplied for an unsigned release." }
    if ($manifestThumbprint) { throw "Unsigned release manifest unexpectedly names a signer." }
} else {
    throw "Manifest has an unsupported Authenticode status."
}

$actualPackageHash = Get-Sha256Hash -LiteralPath $package
if ($actualPackageHash -ne $manifest.PackageSha256.ToLowerInvariant()) { throw "Package SHA-256 does not match the release manifest." }
if ($ExpectedPackageSha256) {
    $expectedHash = ($ExpectedPackageSha256 -replace '\s', '').ToLowerInvariant()
    if ($expectedHash -notmatch '^[0-9a-f]{64}$' -or $actualPackageHash -ne $expectedHash) {
        throw "Package SHA-256 does not match the independently supplied expected hash."
    }
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($package)
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$verificationDirectory = [IO.Path]::GetFullPath((Join-Path $tempRoot ("TQLTK-Release-Verify-" + [Guid]::NewGuid().ToString("N"))))
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName } | Sort-Object)
    $expectedEntries = @("LICENSE", "PersonalInfoManager.exe") | Sort-Object
    if (($entries -join "|") -ne ($expectedEntries -join "|")) { throw "Release ZIP must contain only LICENSE and PersonalInfoManager.exe." }

    $executableEntry = $archive.GetEntry("PersonalInfoManager.exe")
    $licenseEntry = $archive.GetEntry("LICENSE")
    if (-not $executableEntry -or $executableEntry.Length -lt 1 -or $executableEntry.Length -gt 1GB) {
        throw "The executable entry is missing or has an invalid size."
    }
    if (-not $licenseEntry -or $licenseEntry.Length -lt 1 -or $licenseEntry.Length -gt 1MB) {
        throw "The LICENSE entry is missing or has an invalid size."
    }

    New-Item -ItemType Directory -Path $verificationDirectory | Out-Null
    if ((Get-Item -LiteralPath $verificationDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "The temporary verification directory cannot be a reparse point."
    }

    $executablePath = Join-Path $verificationDirectory "PersonalInfoManager.exe"
    $source = $executableEntry.Open()
    try {
        $destination = [IO.File]::Open($executablePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $source.CopyTo($destination) } finally { $destination.Dispose() }
    } finally { $source.Dispose() }

    $actualExecutableHash = Get-Sha256Hash -LiteralPath $executablePath
    if ($actualExecutableHash -ne $manifest.ExecutableSha256.ToLowerInvariant()) { throw "Executable SHA-256 does not match the release manifest." }

    $signature = Get-AuthenticodeSignature -LiteralPath $executablePath
    if ($signatureStatus -eq "Valid") {
        if ($signature.Status.ToString() -ne "Valid") {
            throw "Executable Authenticode signature is not valid: $($signature.Status)."
        }
        if (-not $signature.SignerCertificate -or
            $signature.SignerCertificate.Thumbprint.ToUpperInvariant() -ne $thumbprint) {
            throw "Executable signer does not match the pinned publisher certificate."
        }
    } elseif ($signature.Status.ToString() -ne "NotSigned") {
        throw "Manifest says the executable is unsigned, but its Authenticode status is $($signature.Status)."
    }

    $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath).ProductVersion
    if ($productVersion -notmatch '^([0-9]+\.[0-9]+\.[0-9]+)(?:\+[0-9A-Za-z.-]+)?$') { throw "Executable has no usable product version." }
    if ($Matches[1] -ne $ExpectedVersion -or $manifest.ProductVersion -ne $productVersion) {
        throw "Executable product version does not match the expected and manifested version."
    }

    if ($signatureStatus -eq "Valid") {
        Write-Host "Release verified: TQLTK $ExpectedVersion, SHA-256 and Authenticode signer $thumbprint."
    } else {
        Write-Host "Unsigned release verified: TQLTK $ExpectedVersion and SHA-256."
    }
} finally {
    $archive.Dispose()
    try {
        $full = [IO.Path]::GetFullPath($verificationDirectory)
        if ($full.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
            [IO.Path]::GetFileName($full).StartsWith("TQLTK-Release-Verify-", [StringComparison]::Ordinal)) {
            Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction SilentlyContinue
        }
    } catch { }
}
