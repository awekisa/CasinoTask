#requires -Version 7.0
param([string]$Name)
$ErrorActionPreference = 'Stop'
$statePath = Join-Path (Split-Path $PSScriptRoot -Parent) '.demo/processes.json'
if (-not (Test-Path -LiteralPath $statePath)) { return }
$records = @(Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json)
if ($Name -and $Name -notin $records.Name) { throw "No demo process named '$Name' is recorded." }
$remaining = [Collections.Generic.List[object]]::new()
foreach ($record in $records) {
    if ($Name -and $record.Name -ne $Name) { $remaining.Add($record); continue }
    $process = Get-Process -Id $record.ProcessId -ErrorAction SilentlyContinue
    if ($process) {
        # A reused PID must never stop an unrelated application.
        if ($process.StartTime.ToUniversalTime().Ticks -ne ([DateTime]$record.StartTimeUtc).ToUniversalTime().Ticks) {
            Write-Warning "Skipped $($record.Name): process ID has been reused."
        } else {
            $process.Kill($true)
            $process.WaitForExit()
            Write-Host "Stopped $($record.Name)."
        }
        $process.Dispose()
    }
}
if ($remaining.Count) {
    ConvertTo-Json -InputObject @($remaining.ToArray()) | Set-Content -LiteralPath $statePath
} else { Remove-Item -LiteralPath $statePath }
