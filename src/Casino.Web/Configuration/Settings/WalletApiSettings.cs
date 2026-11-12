
namespace Casino.Web.Configuration.Settings;

public sealed class WalletApiSettings : EndpointSettings
{
    public string WalletsPath { get; set; } = "";
    public string DepositsPath { get; set; } = "";
    public string WithdrawalsPath { get; set; } = "";

    public override void Validate()
    {
        base.Validate();
        ValidatePath(WalletsPath, "Wallet:WalletsPath");
        ValidatePath(DepositsPath, "Wallet:DepositsPath");
        ValidatePath(WithdrawalsPath, "Wallet:WithdrawalsPath");
    }
}
