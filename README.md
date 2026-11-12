# Betty Casino

**Game** runs rounds; **Wallet** owns balances and money operations. Both are independent .NET 10 APIs sharing only HTTP contracts. A browser UI and YARP proxy provide a playable, multi-instance demo.

## Run

Requires .NET 10 SDK and PostgreSQL 18. The Windows launcher requires PowerShell 7.7. Run commands from the repository root.

**1. Prepare PostgreSQL — choose one option.**

With Docker:

```sh
docker compose up -d --wait postgres
```

On an empty volume, this creates both service databases and their users using `database/init.sql`. Data survives restarts.

Without Docker, install/start PostgreSQL on port 5432 and initialize it once as an administrator:

```sh
psql -h localhost -U postgres -d postgres -v ON_ERROR_STOP=1 -f database/init.sql
```

Use your administrator username if different; `psql` must be on PATH. Run outside a transaction. Services create their tables at startup. `start-postgres.ps1` only starts a previously prepared portable installation.

**2. Start the application.**

On Windows, in PowerShell 7.7:

```powershell
./scripts/start-demo.ps1
```

This builds and starts two instances of each API, YARP, and Web. Open **http://localhost:5103**. Stop with `./scripts/stop-demo.ps1`; PostgreSQL stays running. Logs: `.demo/`.

Without the Windows launcher, run `dotnet build`, then use three terminals for a single-instance setup without YARP:

```sh
dotnet run --no-build --no-launch-profile --project src/Casino.Wallet
dotnet run --no-build --no-launch-profile --project src/Casino.Game
dotnet run --no-build --no-launch-profile --project src/Casino.Web
```

Use one setup at a time to avoid port conflicts. Settings are in each application's `appsettings.json`; rebuild after editing. Change each service's `Database:ConnectionString` for another server. Credentials are demo-only.

## Play

1. Open the UI to create a player and wallet, or reuse an ID under **Your player**.
2. Deposit money and bet **$1–$10**, with up to two decimal places.
3. View the result or withdraw available funds.

Payouts: 50% zero; 40% $0.01–2× stake; 10% above 2× through 10× stake. `new total = old total - stake + payout`; held funds cannot be spent.

For interrupted/pending requests, use **Retry saved request**. The original request ID survives reloads; do not clear browser storage to retry.

Demonstrate failover with `./scripts/stop-demo.ps1 -Name Wallet-5201` (or `Game-5202`). Health checks redirect new requests; interrupted requests need retrying. Re-run the launcher to restore all instances.

## Design decisions and alternatives

- **PostgreSQL + EF Core:** each service owns a database shared by its replicas. Chosen over in-memory/per-instance file storage for durable recovery and real concurrency; EF Core provides mappings with explicit transaction control.
- **Decimal money:** `decimal` and PostgreSQL `numeric(18,2)` avoid floating-point rounding. Integer cents are used only for payout sampling.
- **Persisted workflow:** reserve stake, save outcome, settle. Rounds move `Pending -> Decided -> Settled`, or `Rejected`. Recovery and content-bound idempotency allow safe retries after lost responses, without distributed transactions or a message broker.
- **Database coordination:** serializable transactions, constraints, conditional updates, and bounded transaction retries protect concurrent writes across replicas. Process-local locks cannot do this; stronger isolation trades throughput for simpler correctness.
- **Standard proxy:** YARP replaces custom balancing code; no sticky sessions are needed. Web only forwards API calls. Transactions and idempotency provide financial correctness.

## Tests

```sh
dotnet test
```

Unit tests mock dependencies. Integration tests use isolated PostgreSQL databases and actual service processes to check idempotency, concurrent money operations, rollback, failover, and recovery. Only generated test databases are dropped.

Docker's test credentials work automatically. For manual PostgreSQL, set `CASINO_TEST_POSTGRES` to an administrator connection string allowing database creation/deletion. Database-free tests: `dotnet test tests/Casino.Game.UnitTests` and `dotnet test tests/Casino.Wallet.UnitTests`.

Optional browser checks: `node scripts/browser_integration.cjs` requires Playwright, Chrome, and `psql` (`PLAYWRIGHT_MODULE`/`PSQL_PATH` overrides available).

## Deliberately left out and limitations

- No authentication, real payments, or production security hardening; player IDs are not authentication.
- No autoscaling or high availability: proxy/database are single points of failure. Health endpoints do not check dependencies.
- No schema migrations: `EnsureCreated()` initializes empty schemas only.
- No automatic hold expiry, which could race settlement. Permanent failures need investigation; HTTP error classification and uniform unexpected-error responses remain incomplete.
