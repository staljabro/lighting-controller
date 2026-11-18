namespace LightingController.Fixtures;

public sealed record FixtureDefinition(
    string Manufacturer,
    string Model,
    string Type,
    IReadOnlyDictionary<string, FixtureChannel> Channels,
    IReadOnlyList<FixtureMode> Modes,
    IReadOnlyList<VirtualFixtureChannel>? VirtualChannels = null,
    IReadOnlyList<FixturePart>? Parts = null)
{
    public IReadOnlyList<VirtualFixtureChannel> VirtualControlChannels { get; } = VirtualChannels ?? [];
    public IReadOnlyList<FixturePart> FixtureParts { get; } = Parts ?? [];
}

public sealed record FixturePart(string Name, IReadOnlyList<string> ChannelNames);

public sealed record VirtualFixtureChannel(
    string Name,
    string? Preset,
    string? Group,
    IReadOnlyList<string> TargetChannels,
    byte DefaultValue = 0);

public sealed record FixtureChannel(
    string Name,
    string? Preset,
    string? Group = null,
    int GroupByte = 0,
    byte DefaultValue = 0,
    IReadOnlyList<FixtureCapability>? Capabilities = null)
{
    public IReadOnlyList<FixtureCapability> CapabilityRanges { get; } = Capabilities ?? [];
}

public sealed record FixtureCapability(byte Minimum, byte Maximum, string Name, string? Preset = null);

public sealed record FixtureMode(string Name, IReadOnlyList<FixtureModeChannel> Channels);

public sealed record FixtureModeChannel(int Number, string ChannelName);

