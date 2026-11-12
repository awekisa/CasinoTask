
namespace Casino.Game.Configuration.Settings;

public sealed class RecoverySettings
{
    public const string SectionName = "Recovery";
    public int IntervalSeconds { get; set; }
    public void Validate()
    {
        if (IntervalSeconds <= 0 || IntervalSeconds > int.MaxValue / 1000)
            throw new InvalidOperationException("Recovery:IntervalSeconds must be a valid positive interval.");
    }
}
