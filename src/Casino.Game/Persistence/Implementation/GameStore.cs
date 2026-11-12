using Casino.Contracts.Game.Enums;
using Casino.Contracts.Game.Responses;
using Casino.Contracts.Wallet.Requests;
using Casino.Contracts.Wallet.Responses;
using Casino.Game.Errors.Exceptions;
using Casino.Game.Finance;
using Casino.Game.Persistence.Entities;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Casino.Game.Persistence.Implementation;

public sealed class GameStore : IGameStore
{
    private readonly string connectionString;
    private readonly int timeoutSeconds;
    private readonly int maxTransactionRetries;

    public GameStore(string connectionString, int timeoutSeconds, int maxTransactionRetries = 20)
    {
        this.connectionString = connectionString;
        this.timeoutSeconds = timeoutSeconds;
        this.maxTransactionRetries = maxTransactionRetries;
        using var db = new GameDbContext(connectionString, timeoutSeconds);
        PostgresPersistence.Initialize(db);
    }

    private Task<T> Write<T>(Func<GameDbContext, Task<T>> action, CancellationToken ct)
    {
        return PostgresPersistence.Write(() => new GameDbContext(connectionString, timeoutSeconds), action, maxTransactionRetries, ct);
    }

    private static Round ToRound(RoundEntity row) => new(row.Id, row.Player, row.Request, row.Stake,
        row.State, row.Payout, row.Result is null ? null : JsonSerializer.Deserialize<Balance>(row.Result), row.Error);

    public Task<Round> GetOrCreate(MoneyRequest request, CancellationToken ct = default)
    {
        Money.Id(request.PlayerId);
        Money.Id(request.RequestId);
        var stake = Money.Validate(request.Amount);
        return Write(async db =>
        {
            var round = await db.Rounds.SingleOrDefaultAsync(r => r.Player == request.PlayerId && r.Request == request.RequestId, ct);
            if (round is null)
            {
                round = new RoundEntity { Id = Guid.NewGuid(), Player = request.PlayerId, Request = request.RequestId, Stake = stake };
                db.Rounds.Add(round);
            }
            if (round.Stake != stake) throw new DomainException("Request key reused with different bet.", 409);
            return ToRound(round);
        }, ct);
    }

    public async Task<Round> Get(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var db = new GameDbContext(connectionString, timeoutSeconds);
        var round = await db.Rounds.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new DomainException("Unknown round.", 404);
        return ToRound(round);
    }

    public async Task<Guid[]> Unfinished(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var db = new GameDbContext(connectionString, timeoutSeconds);
        return await db.Rounds.AsNoTracking().Where(r => r.State == RoundState.Pending || r.State == RoundState.Decided).Select(r => r.Id).ToArrayAsync(ct);
    }

    public Task Decide(Guid id, decimal payout, CancellationToken ct = default) => Write(db =>
        db.Rounds.Where(r => r.Id == id && r.State == RoundState.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, RoundState.Decided).SetProperty(r => r.Payout, payout), ct), ct);

    public Task Reject(Guid id, Operation result, CancellationToken ct = default) => Write(db =>
        db.Rounds.Where(r => r.Id == id && r.State == RoundState.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, RoundState.Rejected).SetProperty(r => r.Result, JsonSerializer.Serialize(result.Balance, (JsonSerializerOptions?)null)).SetProperty(r => r.Error, result.Error ?? "Bet rejected."), ct), ct);

    public Task Complete(Guid id, Balance balance, CancellationToken ct = default) => Write(db =>
        db.Rounds.Where(r => r.Id == id && r.State == RoundState.Decided)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, RoundState.Settled).SetProperty(r => r.Result, JsonSerializer.Serialize(balance, (JsonSerializerOptions?)null)), ct), ct);
}
