# This test changes desktop cursors; run only on a disposable CI runner.
param([Parameter(Mandatory=$true)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'This test requires a disposable GitHub Actions runner.' }
$exe = Join-Path $PackageDirectory 'HearthGloveCursor.exe'
$installed = Join-Path $env:LOCALAPPDATA 'HearthGloveCursor'
$installedExe = Join-Path $installed 'HearthGloveCursor.exe'
function Invoke-Setup([string]$Command) {
    # Wait for the setup process only, not its deliberately long-lived guard children.
    $p = Start-Process -FilePath $exe -ArgumentList "$Command --quiet" -PassThru
    if (-not $p.WaitForExit(30000)) { $p.Kill(); throw 'Setup command timed out.' }
    if ($p.ExitCode -ne 0) { throw (Get-Content (Join-Path $env:TEMP 'HearthGloveCursor-management.txt') -Raw) }
}
function Guards {
    @(Get-CimInstance Win32_Process -Filter "Name='HearthGloveCursor.exe'" | Where-Object { $_.ExecutablePath -eq $installedExe })
}
function Assert-Pair {
    $p = Guards
    if (@($p | Where-Object { $_.CommandLine -notlike '*--watchdog*' }).Count -ne 1 -or @($p | Where-Object { $_.CommandLine -like '*--watchdog*' }).Count -ne 1) { throw 'Expected one main process and one watchdog.' }
}
try {
    Invoke-Setup '--self-test'
    Invoke-Setup '--install --no-startup'
    Assert-Pair
    $backupBefore = (Get-FileHash (Join-Path $installed 'original-backup-v2.xml')).Hash
    Invoke-Setup '--install --no-startup'
    if ((Get-FileHash (Join-Path $installed 'original-backup-v2.xml')).Hash -ne $backupBefore) { throw 'Upgrade overwrote the original baseline.' }
    Assert-Pair
    $main = Guards | Where-Object { $_.CommandLine -notlike '*--watchdog*' }
    Stop-Process -Id $main.ProcessId -Force
    Start-Sleep -Seconds 5
    Assert-Pair
    $watchdog = Guards | Where-Object { $_.CommandLine -like '*--watchdog*' }
    Stop-Process -Id $watchdog.ProcessId -Force
    Start-Sleep -Seconds 3
    Assert-Pair
    Invoke-Setup '--stop'
    if ((Guards).Count -ne 0) { throw 'Stop left processes running.' }
    Invoke-Setup '--start'
    Assert-Pair
    Invoke-Setup '--uninstall'
    if ((Guards).Count -ne 0) { throw 'Uninstall left processes running.' }
    Write-Host 'PASS: install, upgrade, recovery, stop, start, uninstall.'
} finally {
    if (Test-Path (Join-Path $installed 'original-backup-v2.xml')) { Invoke-Setup '--uninstall' }
}
