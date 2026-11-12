# Starts the portable PostgreSQL installation prepared for this local demo.
$ErrorActionPreference = 'Stop'
$pgRoot = Join-Path $env:LOCALAPPDATA 'BettyCasinoPostgres'
$pgCtl = Join-Path $pgRoot 'pgsql/bin/pg_ctl.exe'
$pgData = Join-Path $pgRoot 'data'
if (-not (Test-Path -LiteralPath $pgCtl)) {
    throw 'Portable PostgreSQL is not installed. Use docker compose up -d postgres, or install the PostgreSQL binaries under %LOCALAPPDATA%/BettyCasinoPostgres.'
}
if (-not (Test-Path -LiteralPath (Join-Path $pgData 'PG_VERSION'))) {
    throw 'PostgreSQL data directory is not initialized. See README for Docker setup on another machine.'
}
& $pgCtl -D $pgData status | Out-Null
if ($LASTEXITCODE -ne 0) {
    $process = Start-Process -FilePath $pgCtl -ArgumentList @('-D', ('"' + $pgData + '"'), '-l', ('"' + (Join-Path $pgRoot 'postgres.log') + '"'), '-o', '"-h 127.0.0.1 -p 5432"', '-w', 'start') -WindowStyle Hidden -PassThru
    # Wait for pg_ctl only; Start-Process -Wait also waits for the server it launches.
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw 'PostgreSQL did not start; check postgres.log.' }
}
Write-Output 'PostgreSQL is available on 127.0.0.1:5432.'
