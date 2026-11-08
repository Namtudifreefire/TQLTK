$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Windows is required for the DPAPI environment check.'
}
Add-Type -AssemblyName System.Security
$sample = New-Object byte[] 32
$encrypted = $null
$decrypted = $null
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $random.GetBytes($sample)
    $encrypted = [Security.Cryptography.ProtectedData]::Protect(
        $sample, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    $decrypted = [Security.Cryptography.ProtectedData]::Unprotect(
        $encrypted, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    if ($decrypted.Length -ne $sample.Length) { throw 'DPAPI round-trip length mismatch.' }
    for ($index = 0; $index -lt $sample.Length; $index++) {
        if ($sample[$index] -ne $decrypted[$index]) { throw 'DPAPI round-trip data mismatch.' }
    }
    Write-Output 'PASS Windows DPAPI CurrentUser environment check (synthetic data).'
}
catch {
    throw 'Windows runner prerequisite failed: DPAPI CurrentUser protect/unprotect is unavailable in this logon session. Use a Windows runner with a working user profile and DPAPI access. No application regression checks have run; this is not a passing or skipped test.'
}
finally {
    $random.Dispose()
    foreach ($buffer in @($sample, $encrypted, $decrypted)) {
        if ($null -ne $buffer) { [Array]::Clear($buffer, 0, $buffer.Length) }
    }
}
