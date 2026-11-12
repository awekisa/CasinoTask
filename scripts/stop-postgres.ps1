$pgRoot = Join-Path $env:LOCALAPPDATA 'BettyCasinoPostgres'
& (Join-Path $pgRoot 'pgsql/bin/pg_ctl.exe') -D (Join-Path $pgRoot 'data') -m fast -w stop
