[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$taskName = 'PWMANAGER Autonomous Development Loop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$loopScript = Join-Path $scriptDir 'loop.ps1'
$powerShell = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"

$action = New-ScheduledTaskAction -Execute $powerShell -Argument "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$loopScript`"" -WorkingDirectory (Split-Path -Parent $scriptDir)
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$settings = New-ScheduledTaskSettingsSet -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero)
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Description 'Starts the fresh-session Codex development loop at logon and restarts it only after failure.' -Force | Out-Null
Disable-ScheduledTask -TaskName $taskName | Out-Null
Write-Output "Registered and disabled: $taskName"

