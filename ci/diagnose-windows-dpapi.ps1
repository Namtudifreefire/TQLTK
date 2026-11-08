$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'This diagnostic requires Windows.'
}
if ($env:CI_DEBUG_TRACE -eq 'true') { throw 'Disable CI_DEBUG_TRACE before running diagnostics.' }
Add-Type -AssemblyName System.Security
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    Write-Output ('Windows version: ' + [Environment]::OSVersion.Version)
    Write-Output ('PowerShell version: ' + $PSVersionTable.PSVersion)
    Write-Output ('Identity: ' + $identity.Name)
    Write-Output ('SID: ' + $identity.User.Value)
    Write-Output ('Authentication type: ' + $identity.AuthenticationType)
    Write-Output ('Impersonation level: ' + $identity.ImpersonationLevel)
    Write-Output ('User profile directory exists: ' + (Test-Path -LiteralPath $env:USERPROFILE))
    Write-Output ('Local application data directory exists: ' + (Test-Path -LiteralPath ([Environment]::GetFolderPath('LocalApplicationData'))))
    Write-Output ('HKCU Software accessible: ' + (Test-Path -LiteralPath 'HKCU:\Software'))
    Write-Output ('User registry hive visible: ' + (Test-Path -LiteralPath ('Registry::HKEY_USERS\' + $identity.User.Value)))
}
finally { $identity.Dispose() }
$sample = New-Object byte[] 32
$encrypted = $null
$decrypted = $null
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
$operation = 'Protect'
try {
    $random.GetBytes($sample)
    $encrypted = [Security.Cryptography.ProtectedData]::Protect(
        $sample, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    Write-Output 'PASS DPAPI CurrentUser Protect (synthetic data).'
    $operation = 'Unprotect'
    $decrypted = [Security.Cryptography.ProtectedData]::Unprotect(
        $encrypted, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    if ($decrypted.Length -ne $sample.Length) { throw 'Round-trip length mismatch.' }
    for ($index = 0; $index -lt $sample.Length; $index++) {
        if ($sample[$index] -ne $decrypted[$index]) { throw 'Round-trip data mismatch.' }
    }
    Write-Output 'PASS DPAPI CurrentUser full round trip.'
}
catch {
    $cause = $_.Exception
    while ($null -ne $cause.InnerException) { $cause = $cause.InnerException }
    Write-Output ('FAIL DPAPI CurrentUser ' + $operation)
    Write-Output ('Exception type: ' + $cause.GetType().FullName)
    Write-Output ('HRESULT: 0x' + $cause.HResult.ToString('X8'))
    Write-Output ('Windows error: ' + $cause.Message)
    throw 'DPAPI diagnostic failed. No application tests were run; inspect the environment evidence above.'
}
finally {
    $random.Dispose()
    foreach ($buffer in @($sample, $encrypted, $decrypted)) {
        if ($null -ne $buffer) { [Array]::Clear($buffer, 0, $buffer.Length) }
    }
}
