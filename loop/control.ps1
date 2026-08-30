[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('on', 'off', 'status')]
    [string]$Command
)

$ErrorActionPreference = 'Stop'
$taskName = 'PWMANAGER Autonomous Development Loop'

switch ($Command) {
    'on' {
        Enable-ScheduledTask -TaskName $taskName | Out-Null
        Start-ScheduledTask -TaskName $taskName
        Write-Output 'Autonomous loop enabled and started.'
    }
    'off' {
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        Disable-ScheduledTask -TaskName $taskName | Out-Null
        Write-Output 'Autonomous loop stopped and disabled.'
    }
    'status' {
        Get-ScheduledTask -TaskName $taskName |
            Select-Object TaskName, State, @{Name='Enabled'; Expression = { $_.Settings.Enabled }}
        Get-ScheduledTaskInfo -TaskName $taskName |
            Select-Object LastRunTime, LastTaskResult, NextRunTime, NumberOfMissedRuns
    }
}

