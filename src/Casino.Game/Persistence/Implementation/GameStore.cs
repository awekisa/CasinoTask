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

    private T Write<T>(Func<GameDbContext, T> action)
    {
        return PostgresPersistence.Write(() => new GameDbContext(connectionString, timeoutSeconds), action, maxTransactionRetries);
    }

    private static Round ToRound(RoundEntity row) => new(Guid.Parse(row.Id), Guid.Parse(row.Player), Guid.Parse(row.Request), row.Stake,
        row.State, row.Payout, row.Result is null ? null : JsonSerializer.Deserialize<Balance>(row.Result), row.Error);

    public Round GetOrCreate(MoneyRequest request)
    {
        Money.Id(request.PlayerId);
        Money.Id(request.RequestId);
        var stake = Money.Validate(request.Amount);
        return Write(db =>
        {
            var round = db.Rounds.SingleOrDefault(r => r.Player == request.PlayerId.ToString() && r.Request == request.RequestId.ToString());
            if (round is null)
            {
                round = new RoundEntity { Id = Guid.NewGuid().ToString(), Player = request.PlayerId.ToString(), Request = request.RequestId.ToString(), Stake = stake };
                db.Rounds.Add(round);
            }
            if (round.Stake != stake) throw new DomainException("Request key reused with different bet.", 409);
            return ToRound(round);
        });
    }

    public Round Get(Guid id)
    {
        using var db = new GameDbContext(connectionString, timeoutSeconds);
        var round = db.Rounds.AsNoTracking().SingleOrDefault(r => r.Id == id.ToString()) ?? throw new DomainException("Unknown round.", 404);
        return ToRound(round);
    }

    public Guid[] Unfinished()
    {
        using var db = new GameDbContext(connectionString, timeoutSeconds);
        return db.Rounds.AsNoTracking().Where(r => r.State == RoundState.Pending || r.State == RoundState.Decided).Select(r => r.Id).ToArray().Select(Guid.Parse).ToArray();
    }

    public void Decide(Guid id, decimal payout) => Write(db =>
        db.Rounds.Where(r => r.Id == id.ToString() && r.State == RoundState.Pending)
            .ExecuteUpdate(s => s.SetProperty(r => r.State, RoundState.Decided).SetProperty(r => r.Payout, payout)));

    public void Reject(Guid id, Operation result) => Write(db =>
        db.Rounds.Where(r => r.Id == id.ToString() && r.State == RoundState.Pending)
            .ExecuteUpdate(s => s.SetProperty(r => r.State, RoundState.Rejected).SetProperty(r => r.Result, JsonSerializer.Serialize(result.Balance, (JsonSerializerOptions?)null)).SetProperty(r => r.Error, result.Error ?? "Bet rejected.")));

    public void Complete(Guid id, Balance balance) => Write(db =>
        db.Rounds.Where(r => r.Id == id.ToString() && r.State == RoundState.Decided)
            .ExecuteUpdate(s => s.SetProperty(r => r.State, RoundState.Settled).SetProperty(r => r.Result, JsonSerializer.Serialize(balance, (JsonSerializerOptions?)null))));
}
