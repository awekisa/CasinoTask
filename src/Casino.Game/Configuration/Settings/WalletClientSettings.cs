
namespace Casino.Game.Configuration.Settings;

public sealed class WalletClientSettings : EndpointSettings
{
    public string ReservationsPath { get; set; } = "";
    public string SettlementsPath { get; set; } = "";

    public override void Validate()
    {
        base.Validate();
        ValidatePath(ReservationsPath, "Wallet:ReservationsPath");
        ValidatePath(SettlementsPath, "Wallet:SettlementsPath");
    }
}
