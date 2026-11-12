using Casino.Game.Persistence.Entities;

using Microsoft.EntityFrameworkCore;

namespace Casino.Game.Persistence;

public sealed class GameDbContext(string connectionString, int timeoutSeconds) : DbContext
{
    public DbSet<RoundEntity> Rounds => Set<RoundEntity>();
    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseNpgsql(connectionString, provider => provider.CommandTimeout(timeoutSeconds));
    protected override void OnModelCreating(ModelBuilder model)
    {
        var rounds = model.Entity<RoundEntity>();
        rounds.ToTable("rounds");
        rounds.HasKey(e => e.Id);
        rounds.Property(e => e.Id).HasColumnName("id");
        rounds.Property(e => e.Player).HasColumnName("player");
        rounds.Property(e => e.Request).HasColumnName("request");
        rounds.Property(e => e.Stake).HasColumnName("stake").HasPrecision(18, 2);
        rounds.Property(e => e.State).HasColumnName("state").HasConversion<string>();
        rounds.Property(e => e.Payout).HasColumnName("payout").HasPrecision(18, 2);
        rounds.Property(e => e.Result).HasColumnName("result");
        rounds.Property(e => e.Error).HasColumnName("error");
        rounds.HasIndex(e => new { e.Player, e.Request }).IsUnique();
    }
}
