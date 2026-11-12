#requires -Version 7.0
param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$demoDirectory = Join-Path $repoRoot '.demo'
$statePath = Join-Path $demoDirectory 'processes.json'
& (Join-Path $PSScriptRoot 'stop-demo.ps1')
$probe = [Net.Sockets.TcpClient]::new()
try {
    $null = $probe.ConnectAsync('127.0.0.1', 5432).WaitAsync([TimeSpan]::FromSeconds(3)).GetAwaiter().GetResult()
} catch {
    throw 'PostgreSQL is not reachable on 127.0.0.1:5432. Run docker compose up -d --wait postgres first, or start your manually installed PostgreSQL server.'
} finally { $probe.Dispose() }
$ports = @(5101, 5102, 5103, 5201, 5301, 5202, 5302)
$occupied = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() |
    Where-Object Port -in $ports
if ($occupied) { throw "Demo ports are already occupied: $($occupied.Port -join ', '). Stop those applications first." }
if (-not $NoBuild) {
    & dotnet build (Join-Path $repoRoot 'BettyCasino.sln')
    if ($LASTEXITCODE -ne 0) { throw 'Build failed; demo was not started.' }
}
New-Item -ItemType Directory -Path $demoDirectory -Force | Out-Null
$started = [Collections.Generic.List[object]]::new()

function Wait-Healthy([string]$address, [Diagnostics.Process]$process, [string]$name) {
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($process.HasExited) { throw "$name exited. Check $demoDirectory\$name.err.log and $name.out.log. Verify PostgreSQL databases and credentials." }
        try {
            $response = Invoke-WebRequest "$address/health" -TimeoutSec 2
            if ($response.StatusCode -eq 200) { return }
        } catch { }
        Start-Sleep -Milliseconds 200
    }
    throw "$name did not become healthy. Check its logs in $demoDirectory."
}

function Start-ServiceInstance([string]$component, [string]$name, [string]$urls) {
    $project = Join-Path $repoRoot "src/Casino.$component"
    $arguments = 'run --no-build --no-launch-profile --project "' + $project + '" -- --urls "' + $urls + '"'
    $options = @{
        FilePath = 'dotnet'; ArgumentList = $arguments; WorkingDirectory = $repoRoot
        WindowStyle = 'Hidden'; PassThru = $true
        RedirectStandardOutput = (Join-Path $demoDirectory "$name.out.log")
        RedirectStandardError = (Join-Path $demoDirectory "$name.err.log")
    }
    $process = Start-Process @options
    $started.Add([pscustomobject]@{ Name = $name; ProcessId = $process.Id; StartTimeUtc = $process.StartTime.ToUniversalTime().ToString('O'); Urls = $urls })
    ConvertTo-Json -InputObject @($started.ToArray()) | Set-Content -LiteralPath $statePath
    foreach ($address in $urls.Split(';')) { Wait-Healthy $address $process $name }
    Write-Host "$name ready at $urls (PID $($process.Id))"
}
try {
    Start-ServiceInstance 'Wallet' 'Wallet-5201' 'http://localhost:5201'
    Start-ServiceInstance 'Wallet' 'Wallet-5301' 'http://localhost:5301'
    Start-ServiceInstance 'Game' 'Game-5202' 'http://localhost:5202'
    Start-ServiceInstance 'Game' 'Game-5302' 'http://localhost:5302'
    Start-ServiceInstance 'LoadBalancer' 'LoadBalancer' 'http://localhost:5101;http://localhost:5102'
    Start-ServiceInstance 'Web' 'Web' 'http://localhost:5103'
} catch {
    & (Join-Path $PSScriptRoot 'stop-demo.ps1')
    throw
}
Write-Host "Open http://localhost:5103. Separate logs are in $demoDirectory."
Write-Host 'Watch distribution: Get-Content .demo/LoadBalancer.out.log -Wait'
Write-Host 'Stop one instance: ./scripts/stop-demo.ps1 -Name Wallet-5201'
Write-Host 'Stop everything: ./scripts/stop-demo.ps1'
