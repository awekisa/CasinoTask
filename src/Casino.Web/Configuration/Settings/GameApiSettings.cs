
namespace Casino.Web.Configuration.Settings;

public sealed class GameApiSettings : EndpointSettings
{
    public string BetsPath { get; set; } = "";

    public override void Validate()
    {
        base.Validate();
        ValidatePath(BetsPath, "Game:BetsPath");
    }
}
