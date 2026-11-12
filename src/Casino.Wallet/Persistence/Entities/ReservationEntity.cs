using Casino.Wallet.Persistence.Enums;

namespace Casino.Wallet.Persistence.Entities;

public sealed class ReservationEntity
{
    public string Round { get; set; } = "";
    public string Player { get; set; } = "";
    public decimal Stake { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Held;
    public decimal? Payout { get; set; }
    public string Result { get; set; } = "";
}
