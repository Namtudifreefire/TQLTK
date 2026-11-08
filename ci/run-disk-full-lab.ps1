$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'The virtual-disk test is restricted to disposable GitHub-hosted Windows runners.'
}
$runnerRoot = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\')
$scratch = Join-Path $runnerRoot ('TQLTK-VhdLab-' + [Guid]::NewGuid().ToString('N'))
$vhd = Join-Path $scratch 'bounded-test.vhd'
$label = 'TQLTKLAB' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
$usedLetters = @(Get-Volume | Where-Object DriveLetter | ForEach-Object { [string]$_.DriveLetter })
$letter = @('Z', 'Y', 'X', 'W', 'V', 'U', 'T', 'S', 'R') | Where-Object {
    $_ -notin $usedLetters -and -not (Test-Path ($_ + ':\'))
} | Select-Object -First 1
if (-not $letter) { throw 'No unused lab drive letter is available.' }
if (-not $scratch.StartsWith($runnerRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe VHD path.' }
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    if (Test-Path -LiteralPath $vhd) { throw 'Refusing to reuse a virtual disk.' }
    $commands = @(
        ('create vdisk file="' + $vhd + '" maximum=96 type=expandable'),
        'attach vdisk',
        'create partition primary',
        ('format fs=ntfs label="' + $label + '" quick'),
        ('assign letter=' + $letter),
        'exit'
    )
    $createScript = Join-Path $scratch 'create.txt'
    $commands | Set-Content -LiteralPath $createScript -Encoding ascii
    & diskpart /s $createScript
    if ($LASTEXITCODE -ne 0) { throw 'Virtual disk preparation failed.' }
    $image = Get-DiskImage -ImagePath $vhd
    $disk = $image | Get-Disk
    $partition = Get-Partition -DriveLetter $letter
    $volume = Get-Volume -DriveLetter $letter
    if (-not $image.Attached -or $partition.DiskNumber -ne $disk.Number -or $volume.FileSystemLabel -cne $label -or $disk.Size -gt 100MB) {
        throw 'The assigned volume does not match the newly created bounded virtual disk.'
    }
    & (Join-Path $PSScriptRoot 'run-windows-security.ps1') -BackupLabMode 'disk-full' -LabPath ($letter + ':\')
}
finally {
    if (Test-Path -LiteralPath $vhd) {
        $detachScript = Join-Path $scratch 'detach.txt'
        @(('select vdisk file="' + $vhd + '"'), 'detach vdisk', 'exit') | Set-Content -LiteralPath $detachScript -Encoding ascii
        & diskpart /s $detachScript
        if ((Get-DiskImage -ImagePath $vhd).Attached) { throw 'The test VHD could not be detached; its directory was preserved.' }
    }
    $resolved = (Resolve-Path -LiteralPath $scratch).ProviderPath
    if ($resolved -cne $scratch -or -not $resolved.StartsWith($runnerRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
        -not ([IO.Path]::GetFileName($resolved)).StartsWith('TQLTK-VhdLab-', [StringComparison]::Ordinal)) {
        throw 'Refusing unsafe VHD workspace cleanup.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
