[CmdletBinding()]
param(
    [int]$MaxCyclesOverride = -1
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoDir = Split-Path -Parent $scriptDir
$envFile = Join-Path $scriptDir 'env.sh'
$promptFile = Join-Path $scriptDir 'PROMPT.md'
$stopFile = Join-Path $scriptDir 'STOP'
$logDir = Join-Path $repoDir 'logs'

function Read-LoopEnvironment {
    param([string]$Path)

    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path -Encoding utf8) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith('#')) { continue }
        if ($trimmed -notmatch '^([A-Z_][A-Z0-9_]*)=(.*)$') {
            throw "Invalid setting in ${Path}: $line"
        }
        $values[$Matches[1]] = $Matches[2].Trim()
    }
    return $values
}

function Require-IntegerSetting {
    param([hashtable]$Settings, [string]$Name, [int]$Minimum)

    $value = 0
    if (-not $Settings.ContainsKey($Name) -or
        -not [int]::TryParse($Settings[$Name], [ref]$value) -or
        $value -lt $Minimum) {
        throw "$Name must be an integer greater than or equal to $Minimum."
    }
    return $value
}

$settings = Read-LoopEnvironment -Path $envFile
if (-not $settings.ContainsKey('MODEL') -or [string]::IsNullOrWhiteSpace($settings.MODEL)) {
    throw 'MODEL must not be empty.'
}

$maxTurns = Require-IntegerSetting -Settings $settings -Name 'MAX_TURNS' -Minimum 1
$waitSeconds = Require-IntegerSetting -Settings $settings -Name 'WAIT_SECONDS' -Minimum 0
$maxCycles = Require-IntegerSetting -Settings $settings -Name 'MAX_CYCLES' -Minimum 0
if ($MaxCyclesOverride -ge 0) { $maxCycles = $MaxCyclesOverride }

if ($maxTurns -ne 1) {
    throw 'This Codex CLI has no max-turns option. Keep MAX_TURNS=1; each cycle is one fresh codex exec session.'
}

$requiredPathEntries = @(
    (Join-Path $env:APPDATA 'npm'),
    (Join-Path $env:ProgramFiles 'nodejs'),
    (Join-Path $env:ProgramFiles 'Git\cmd'),
    "$env:SystemRoot\System32",
    "$env:SystemRoot\System32\WindowsPowerShell\v1.0"
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -Unique
$env:PATH = ($requiredPathEntries -join [IO.Path]::PathSeparator)

$codexCommand = Get-Command 'codex.cmd' -ErrorAction SilentlyContinue
if (-not $codexCommand) { $codexCommand = Get-Command 'codex' -ErrorAction SilentlyContinue }
if (-not $codexCommand) { throw 'Codex CLI was not found on the explicit scheduled-task PATH.' }

New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$cycle = 0

while ($true) {
    if ($maxCycles -gt 0 -and $cycle -ge $maxCycles) { break }
    $cycle++

    $dayDir = Join-Path $logDir (Get-Date -Format 'yyyy-MM-dd')
    New-Item -ItemType Directory -Force -Path $dayDir | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    $logFile = Join-Path $dayDir ("cycle-{0:D4}-{1}.log" -f $cycle, $stamp)

    "[$(Get-Date -Format o)] cycle=$cycle model=$($settings.MODEL) session=fresh" |
        Set-Content -LiteralPath $logFile -Encoding utf8

    Get-Content -Raw -LiteralPath $promptFile -Encoding utf8 |
        & $codexCommand.Source exec --ephemeral --approve-for-me --model $settings.MODEL --cd $repoDir --color never - 2>&1 |
        Tee-Object -FilePath $logFile -Append
    $exitCode = $LASTEXITCODE
    "[$(Get-Date -Format o)] cycle=$cycle exit_code=$exitCode" |
        Add-Content -LiteralPath $logFile -Encoding utf8

    if (Test-Path -LiteralPath $stopFile) {
        "[$(Get-Date -Format o)] STOP detected after cycle=$cycle" |
            Add-Content -LiteralPath $logFile -Encoding utf8
        break
    }
    if ($maxCycles -gt 0 -and $cycle -ge $maxCycles) { break }
    if ($waitSeconds -gt 0) { Start-Sleep -Seconds $waitSeconds }
}
