#Requires -RunAsAdministrator
# Removes DeepCoolDisplay and, if the DeepCool software is still installed, turns it back on.
# PawnIO stays installed (remove it via Settings > Apps if wanted).

$ErrorActionPreference = 'Stop'
$target = 'C:\Program Files\DeepCoolDisplay'
$state = "$env:ProgramData\DeepCoolDisplay"
$service = 'Deep Cool Helper Service'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Write-Host '1. Remove DeepCoolDisplay'
Get-ScheduledTask -TaskName DeepCoolDisplay -ErrorAction SilentlyContinue | Stop-ScheduledTask
Unregister-ScheduledTask -TaskName DeepCoolDisplay -Confirm:$false -ErrorAction SilentlyContinue
Get-Process DeepCoolDisplay -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue

Write-Host '2. Restore the DeepCool software (if installed)'
$svc = Get-CimInstance Win32_Service -Filter "Name='$service'" -ErrorAction SilentlyContinue
if ($svc) {
    Set-Service $service -StartupType Automatic
    Start-Service $service
}

# Autostart entry as it was before install.ps1, else rebuilt from the service location.
$run = Get-Content "$state\deepcool-autorun.txt" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $run -and $svc -and $svc.PathName -match '^"?(?<exe>[^"]+?\.exe)') {
    $dir = Split-Path $Matches.exe
    while ($dir -and -not (Test-Path (Join-Path $dir 'DeepCool.exe'))) { $dir = Split-Path $dir }
    if ($dir) { $run = "$dir\DeepCool.exe -autoRun" }
}
if ($run -match '^"?(?<exe>[^"]+?DeepCool\.exe)' -and (Test-Path $Matches.exe)) {
    Set-ItemProperty $runKey -Name DeepCool -Value $run
    Start-Process $Matches.exe -ArgumentList '-autoRun'
    Write-Host "   DeepCool restored ($($Matches.exe))"
} else {
    Write-Host '   DeepCool software not found; nothing to restore'
}

Remove-Item $state -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'Done.'
