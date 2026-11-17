namespace LightingController.Core;

public readonly record struct ParameterAddress
{
    public ParameterAddress(int fixtureId, int channel)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fixtureId);
        ArgumentOutOfRangeException.ThrowIfNegative(channel);
        FixtureId = fixtureId;
        Channel = channel;
    }

    public int FixtureId { get; }

    public int Channel { get; }
}

