using Casino.Wallet.Persistence.Enums;

namespace Casino.Wallet.Persistence.Entities;

public sealed class ReservationEntity
{
    public Guid Round { get; set; }
    public Guid Player { get; set; }
    public decimal Stake { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Held;
    public decimal? Payout { get; set; }
    public string Result { get; set; } = "";
}
