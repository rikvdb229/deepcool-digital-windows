#Requires -RunAsAdministrator
# Replaces the DeepCool software with DeepCoolDisplay:
#   installs the PawnIO driver, stops/disables DeepCool (keeping it installed),
#   deletes its runaway service.log, installs DeepCoolDisplay as a SYSTEM startup task.
# Undo with uninstall.ps1.
#
# Optional display settings (passed to the task; leave out for the defaults):
#   -ChMode temp|usage|auto   CH case display (default temp)
#   -Smooth 10                averaging time in seconds (default 5; 0 = raw)
#   -DeadbandTemp 2           °C (default 1)
#   -DeadbandUsage 5          %  (default 3)
#   -DeadbandPower 5          W  (default 3)
param(
    [string]$Publish = "$PSScriptRoot\publish",
    [ValidateSet('temp', 'usage', 'auto')][string]$ChMode,
    [double]$Smooth = -1,
    [double]$DeadbandTemp = -1,
    [double]$DeadbandUsage = -1,
    [double]$DeadbandPower = -1
)

$taskArgs = @()
if ($ChMode) { $taskArgs += "--ch-mode $ChMode" }
$inv = [Globalization.CultureInfo]::InvariantCulture
if ($Smooth -ge 0)        { $taskArgs += '--smooth ' + $Smooth.ToString($inv) }
if ($DeadbandTemp -ge 0)  { $taskArgs += '--deadband-temp ' + $DeadbandTemp.ToString($inv) }
if ($DeadbandUsage -ge 0) { $taskArgs += '--deadband-usage ' + $DeadbandUsage.ToString($inv) }
if ($DeadbandPower -ge 0) { $taskArgs += '--deadband-power ' + $DeadbandPower.ToString($inv) }

$ErrorActionPreference = 'Stop'
$target = 'C:\Program Files\DeepCoolDisplay'
$state = "$env:ProgramData\DeepCoolDisplay"
$service = 'Deep Cool Helper Service'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

# Locate the DeepCool installation (folder containing DeepCool.exe): from its service,
# its autostart entry, or its uninstall entry. Returns $null if DeepCool is not installed.
function Find-DeepCool {
    $svc = Get-CimInstance Win32_Service -Filter "Name='$service'" -ErrorAction SilentlyContinue
    if ($svc -and $svc.PathName -match '^"?(?<exe>[^"]+?\.exe)') {
        $dir = Split-Path $Matches.exe
        while ($dir -and -not (Test-Path (Join-Path $dir 'DeepCool.exe'))) { $dir = Split-Path $dir }
        if ($dir) { return $dir }
    }
    $run = (Get-ItemProperty $runKey -Name DeepCool -ErrorAction SilentlyContinue).DeepCool
    if ($run -match '^"?(?<exe>[^"]+?DeepCool\.exe)') { return Split-Path $Matches.exe }
    $entry = Get-ChildItem 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
                           'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
                           'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue |
        Get-ItemProperty | Where-Object { $_.DisplayName -match '^DeepCool' -and $_.InstallLocation } | Select-Object -First 1
    if ($entry -and (Test-Path (Join-Path $entry.InstallLocation 'DeepCool.exe'))) { return $entry.InstallLocation.TrimEnd('\') }
    return $null
}

$deepcool = Find-DeepCool
if ($deepcool) { Write-Host "DeepCool software found in $deepcool" } else { Write-Host 'DeepCool software not found (fine if it is not installed)' }

Write-Host '1. PawnIO driver'
if (-not (Get-CimInstance Win32_SystemDriver -Filter "Name='PawnIO'" -ErrorAction SilentlyContinue)) {
    winget install --id namazso.PawnIO --exact --silent --accept-package-agreements --accept-source-agreements
}

Write-Host '2. Stop DeepCool app + service (it stays installed; uninstall.ps1 turns it back on)'
New-Item -ItemType Directory -Force $state | Out-Null
$run = (Get-ItemProperty $runKey -Name DeepCool -ErrorAction SilentlyContinue).DeepCool
if ($run) { Set-Content "$state\deepcool-autorun.txt" $run }   # remembered for uninstall.ps1
Get-Process DeepCool -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty $runKey -Name DeepCool -ErrorAction SilentlyContinue
if (Get-Service $service -ErrorAction SilentlyContinue) {
    Stop-Service $service -Force
    Set-Service $service -StartupType Disabled
}

Write-Host '3. Delete DeepCool service logs (never rotated, can grow to tens of GB)'
if ($deepcool) {
    Get-ChildItem (Join-Path $deepcool 'resources\service') -Recurse -Filter 'service.log' -ErrorAction SilentlyContinue | ForEach-Object {
        $gb = $_.Length / 1GB
        Remove-Item $_.FullName -Force
        Write-Host ("   removed {0} ({1:N1} GB)" -f $_.FullName, $gb)
    }
}

Write-Host "4. Copy to $target"
Get-ScheduledTask -TaskName DeepCoolDisplay -ErrorAction SilentlyContinue | Stop-ScheduledTask
Get-Process DeepCoolDisplay -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item "$Publish\*" $target -Recurse -Force

Write-Host '5. Scheduled task (SYSTEM, at startup)'
$action = if ($taskArgs) {
    New-ScheduledTaskAction -Execute "$target\DeepCoolDisplay.exe" -Argument ($taskArgs -join ' ') -WorkingDirectory $target
} else {
    New-ScheduledTaskAction -Execute "$target\DeepCoolDisplay.exe" -WorkingDirectory $target
}
if ($taskArgs) { Write-Host "   options: $($taskArgs -join ' ')" }
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
Register-ScheduledTask -TaskName DeepCoolDisplay -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName DeepCoolDisplay

Start-Sleep -Seconds 5
Write-Host '6. Check'
& "$target\DeepCoolDisplay.exe" --list
Get-Content "$env:ProgramData\DeepCoolDisplay\events.log" -Tail 5 -ErrorAction SilentlyContinue
