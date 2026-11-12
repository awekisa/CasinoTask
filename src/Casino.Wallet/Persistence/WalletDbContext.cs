using Casino.Wallet.Persistence.Entities;

using Microsoft.EntityFrameworkCore;

namespace Casino.Wallet.Persistence;

public sealed class WalletDbContext(string connectionString, int timeoutSeconds) : DbContext
{
    public DbSet<WalletEntity> Wallets => Set<WalletEntity>();
    public DbSet<OperationEntity> Operations => Set<OperationEntity>();
    public DbSet<ReservationEntity> Reservations => Set<ReservationEntity>();
    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseNpgsql(connectionString, provider => provider.CommandTimeout(timeoutSeconds));
    protected override void OnModelCreating(ModelBuilder model)
    {
        var wallets = model.Entity<WalletEntity>();
        wallets.ToTable("wallets", table => { table.HasCheckConstraint("CK_wallets_total", "total >= 0"); table.HasCheckConstraint("CK_wallets_held", "held >= 0 AND held <= total"); });
        wallets.HasKey(e => e.Player);
        wallets.Property(e => e.Player).HasColumnName("player");
        wallets.Property(e => e.Total).HasColumnName("total").HasPrecision(18, 2);
        wallets.Property(e => e.Held).HasColumnName("held").HasPrecision(18, 2);
        var operations = model.Entity<OperationEntity>();
        operations.ToTable("operations");
        operations.HasKey(e => new { e.Player, e.Request });
        operations.Property(e => e.Player).HasColumnName("player");
        operations.Property(e => e.Request).HasColumnName("request");
        operations.Property(e => e.Kind).HasColumnName("kind").HasConversion<string>();
        operations.Property(e => e.Amount).HasColumnName("amount").HasPrecision(18, 2);
        operations.Property(e => e.Result).HasColumnName("result");
        var reservations = model.Entity<ReservationEntity>();
        reservations.ToTable("reservations");
        reservations.HasKey(e => e.Round);
        reservations.Property(e => e.Round).HasColumnName("round");
        reservations.Property(e => e.Player).HasColumnName("player");
        reservations.Property(e => e.Stake).HasColumnName("stake").HasPrecision(18, 2);
        reservations.Property(e => e.Status).HasColumnName("status").HasConversion<string>();
        reservations.Property(e => e.Payout).HasColumnName("payout").HasPrecision(18, 2);
        reservations.Property(e => e.Result).HasColumnName("result");
    }
}
