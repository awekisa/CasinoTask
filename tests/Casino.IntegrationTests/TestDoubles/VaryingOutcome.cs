using Casino.Game.Rounds;

namespace Casino.IntegrationTests.TestDoubles;

internal sealed class VaryingOutcome : IOutcome { private int count; public decimal Draw(decimal stake) => Interlocked.Increment(ref count) * 0.10m; }
