namespace LightingController.Fixtures;

public sealed record FixtureDefinition(
    string Manufacturer,
    string Model,
    string Type,
    IReadOnlyDictionary<string, FixtureChannel> Channels,
    IReadOnlyList<FixtureMode> Modes);

public sealed record FixtureChannel(string Name, string? Preset);

public sealed record FixtureMode(string Name, IReadOnlyList<FixtureModeChannel> Channels);

public sealed record FixtureModeChannel(int Number, string ChannelName);

