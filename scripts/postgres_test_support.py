"""Isolated PostgreSQL databases for the optional Python HTTP checks."""
import os, shutil, subprocess, uuid
from pathlib import Path
from contextlib import contextmanager

def settings():
    return dict(host=os.getenv('PGHOST','127.0.0.1'), port=os.getenv('PGPORT','5432'),
                username=os.getenv('PGUSER','casino_admin'), password=os.getenv('PGPASSWORD','casino_local_admin'))

def execute(sql):
    portable=Path(os.getenv('LOCALAPPDATA',''))/'BettyCasinoPostgres/pgsql/bin/psql.exe'
    executable=os.getenv('PSQL_PATH') or shutil.which('psql') or str(portable)
    config=settings()
    env=os.environ.copy(); env['PGPASSWORD']=config['password']
    subprocess.run([executable,'-h',config['host'],'-p',config['port'],'-U',config['username'],
                    '-d','postgres','-v','ON_ERROR_STOP=1','-c',sql],env=env,check=True,capture_output=True,text=True)

@contextmanager
def databases():
    created=[]; connections={}
    try:
        for service in ['Wallet','Game']:
            name='casino_test_'+service.lower()+'_'+uuid.uuid4().hex
            execute(f'CREATE DATABASE "{name}"')
            created.append(name)
            values={**settings(),'database':name}
            connections[service]=';'.join(key+'="'+value.replace('"','""')+'"' for key,value in values.items())
        yield connections
    finally:
        for name in created:
            execute(f'DROP DATABASE IF EXISTS "{name}" WITH (FORCE)')
