# Betty Casino

**Game** runs rounds; **Wallet** owns balances and money operations. Both are independent .NET 10 APIs sharing only HTTP contracts. Wallet and Game are the assessed services. Web, the YARP proxy, and the multi-instance launcher are demo scaffolding for play, horizontal scaling, and failover.

## Run

Requires .NET 10 SDK and PostgreSQL 18. The Windows launcher requires PowerShell 7. Run commands from the repository root.

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

The baseline setup is one instance of each service. Run `dotnet build`, then start Wallet, Game, and the browser frontend in three separate terminals:

```sh
dotnet run --no-build --no-launch-profile --project src/Casino.Wallet
dotnet run --no-build --no-launch-profile --project src/Casino.Game
dotnet run --no-build --no-launch-profile --project src/Casino.Web
```

Open **http://localhost:5103**. Stop each process with Ctrl+C.

Optionally, demonstrate scaling on Windows with PowerShell 7:

```powershell
./scripts/start-demo.ps1
```

This builds and starts two instances of each API, YARP, and Web. Stop with `./scripts/stop-demo.ps1`; PostgreSQL stays running. Logs: `.demo/`.

Use one setup at a time to avoid port conflicts. Settings are in each application's `appsettings.json`; rebuild after editing. Change each service's `Database:ConnectionString` for another server. Credentials are demo-only.

## Play

1. Open the UI to create a player and wallet, or reuse an ID under **Your player**.
2. Deposit money and bet **$1–$10**, with up to two decimal places.
3. View the result, withdraw available funds, or select **Leave** to finish. Leave preserves your player ID and requires any pending request to be resolved first.

Payouts: 50% zero; 40% $0.01–2× stake; 10% above 2× through 10× stake. `new total = old total - stake + payout`; held funds cannot be spent.

For interrupted/pending requests, use **Retry saved request**. The original request ID survives reloads; do not clear browser storage to retry.

Demonstrate failover with `./scripts/stop-demo.ps1 -Name Wallet-5201` (or `Game-5202`). Health checks redirect new requests; interrupted requests need retrying. Re-run the launcher to restore all instances.

## Design decisions and alternatives

### How Wallet and Game work together

- **Split:** Wallet owns every balance and money movement; Game owns rounds and outcomes and never modifies balances. Letting Game calculate and write balances was rejected because coordinating money writers across the two services would require distributed coordination.
- **Communication:** Game orchestrates the round through synchronous request/response HTTP to Wallet. A broker or outbox was rejected for this small-volume demo: the player is waiting for a result, and persisted round state, idempotent Wallet operations, and a recovery worker provide safe retries without extra infrastructure.
- **Reserve, then settle:** Game reserves the stake, persists the outcome, then settles. The hold prevents withdrawal or reuse of the stake while the round is open; rejected reservations never reach outcome generation. A single "apply bet result" call after drawing was rejected: it would draw outcomes for potentially unfunded bets and leave the stake unprotected in flight.

- **PostgreSQL + EF Core:** each service owns a database shared by its replicas. Chosen over in-memory/per-instance file storage for durable recovery and real concurrency; EF Core provides mappings with explicit transaction control.
- **Decimal money:** `decimal` and PostgreSQL `numeric(18,2)` avoid floating-point rounding. Integer cents are used only for payout sampling.
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
- No automatic hold expiry, which could race settlement. Permanent failures need investigation: recovery retries permanent Wallet 4xx responses indefinitely. Unexpected server errors lack uniform JSON responses.
- Database calls are synchronous and block request threads.
