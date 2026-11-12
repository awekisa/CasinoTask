"""Real HTTP/process tests. Run after dotnet build; uses isolated PostgreSQL databases (requires PostgreSQL and psql)."""
import json, os, socket, subprocess, tempfile, time, urllib.request, urllib.error, uuid
from pathlib import Path
from postgres_test_support import databases
from decimal import Decimal
ROOT = Path(__file__).resolve().parents[1]
def free_port():
    with socket.socket() as s:
        s.bind(('127.0.0.1', 0))
        return s.getsockname()[1]
wallet_port, game_port = free_port(), free_port()
wallet_url, game_url = f'http://127.0.0.1:{wallet_port}', f'http://127.0.0.1:{game_port}'
def call(base, path, data=None, method=None):
    req = urllib.request.Request(base+path, None if data is None else json.dumps(data).encode(),
        {'Content-Type':'application/json'}, method=method)
    with urllib.request.urlopen(req, timeout=12) as response:
        return response.status, json.load(response, parse_float=Decimal)
def wait_for(check, seconds=25):
    end=time.monotonic()+seconds
    while time.monotonic()<end:
        try:
            value=check()
            if value: return value
        except (OSError, urllib.error.URLError): pass
        time.sleep(.15)
    raise AssertionError('Condition did not become true')
processes=[]
with databases() as connections, tempfile.TemporaryDirectory(prefix='betty-http-') as directory:
    def start(name):
        port=wallet_port if name=='Wallet' else game_port
        dll=ROOT/f'src/Casino.{name}/bin/Debug/net10.0/Casino.{name}.dll'
        log=open(Path(directory)/f'{name}-{len(processes)}.log','w')
        p=subprocess.Popen(['dotnet',str(dll),'--urls',f'http://127.0.0.1:{port}',
            '--Wallet:BaseUrl',wallet_url+'/'],
            cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,env={**os.environ,"Database__ConnectionString":connections[name]})
        processes.append((p,log))
        wait_for(lambda: call(f'http://127.0.0.1:{port}','/health')[0]==200)
        return p
    def stop(p):
        p.terminate(); p.wait(timeout=10)
    try:
        # Configuration must load from the executable directory even with another cwd.
        # Invalid values must fail before a service starts processing requests.
        for name, arguments, message in [
            ('Wallet', ['--Database:TimeoutSeconds', '0'], 'Database requires'),
            ('Game', ['--Recovery:IntervalSeconds', '0'], 'Recovery:IntervalSeconds'),
            ('Web', ['--Wallet:BaseUrl', 'not-a-url'], 'Endpoint requires'),
        ]:
            dll=ROOT/f'src/Casino.{name}/bin/Debug/net10.0/Casino.{name}.dll'
            failed=subprocess.run(['dotnet', str(dll), *arguments], cwd=directory,
                capture_output=True, text=True, timeout=20,env={**os.environ,"Database__ConnectionString":connections.get(name,connections["Wallet"])})
            assert failed.returncode!=0 and message in failed.stdout+failed.stderr, failed.stdout+failed.stderr
        wallet=start('Wallet'); game=start('Game')
        player=str(uuid.uuid4())
        assert call(wallet_url,f'/wallets/{player}',method='PUT')[1]['total']==0
        deposit={'playerId':player,'requestId':str(uuid.uuid4()),'amount':10}
        assert call(wallet_url,'/deposits',deposit)[1]['balance']['total']==10
        assert call(wallet_url,'/deposits',deposit)[1]['balance']['total']==10
        # Both are genuinely independent OS processes. Wallet still works without Game.
        stop(game)
        assert call(wallet_url,f'/wallets/{player}')[1]['total']==10
        game=start('Game')
        # Wallet outage: persisted request must be pending, not falsely rejected.
        stop(wallet)
        bet={'playerId':player,'requestId':str(uuid.uuid4()),'amount':5}
        status,round=call(game_url,'/bets',bet)
        assert status==202 and round['state']=='Pending'
        round_id=round['id']
        stop(game)
        wallet=start('Wallet'); game=start('Game')
        def settled():
            result=call(game_url,f'/rounds/{round_id}')[1]
            return result if result['state']=='Settled' else None
        result=wait_for(settled)
        balance=call(wallet_url,f'/wallets/{player}')[1]
        assert balance['total']==5+result['payout'] and balance['held']==0
        assert call(game_url,'/bets',bet)[1]==result
        print('PASS: independent services, HTTP idempotency, Wallet outage, Game restart, background recovery, balance invariant, configuration loading and validation')
    except Exception:
        for _,log in processes:
            log.flush()
            print(Path(log.name).read_text())
        raise
    finally:
        for p,log in processes:
            if p.poll() is None: stop(p)
            log.close()
