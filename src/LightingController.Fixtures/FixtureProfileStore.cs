using System.Text.Json;

namespace LightingController.Fixtures;

public sealed class FixtureProfileStore
{
    public const string Extension = ".lcfixture";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        IgnoreReadOnlyProperties = true
    };

    public FixtureDefinition Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    public FixtureDefinition Load(Stream stream)
    {
        var fixture = JsonSerializer.Deserialize<FixtureDefinition>(stream, Options)
            ?? throw new InvalidDataException("The fixture profile is empty or invalid.");
        Validate(fixture);
        return fixture;
    }

    public void Save(FixtureDefinition fixture, string path)
    {
        Validate(fixture);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, fixture, Options);
    }

    public static void Validate(FixtureDefinition fixture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixture.Manufacturer);
        ArgumentException.ThrowIfNullOrWhiteSpace(fixture.Model);
        if (fixture.Channels.Count == 0 || fixture.Modes.Count == 0)
            throw new InvalidDataException("A fixture requires physical channels and at least one mode.");
        foreach (var mode in fixture.Modes)
            foreach (var channel in mode.Channels)
                if (!fixture.Channels.ContainsKey(channel.ChannelName))
                    throw new InvalidDataException($"Mode '{mode.Name}' refers to unknown channel '{channel.ChannelName}'.");
        foreach (var channel in fixture.VirtualControlChannels)
            foreach (var target in channel.TargetChannels)
                if (!fixture.Channels.ContainsKey(target))
                    throw new InvalidDataException($"Virtual channel '{channel.Name}' targets unknown channel '{target}'.");
        foreach (var part in fixture.FixtureParts)
        {
            if (string.IsNullOrWhiteSpace(part.Name) || part.ChannelNames.Count == 0)
                throw new InvalidDataException("Every fixture part requires a name and at least one channel.");
            foreach (var channel in part.ChannelNames)
                if (!fixture.Channels.ContainsKey(channel))
                    throw new InvalidDataException($"Fixture part '{part.Name}' refers to unknown channel '{channel}'.");
        }
    }
}

public static class BuiltInFixtureLibrary
{
    public static IReadOnlyList<FixtureDefinition> Create() =>
    [
        CreateDimmer(),
        CreateRgb()
    ];

    private static FixtureDefinition CreateDimmer()
    {
        var channels = new Dictionary<string, FixtureChannel> { ["Dimmer"] = new("Dimmer", "IntensityDimmer", "Intensity") };
        return new("Generic", "Single Channel Dimmer", "Dimmer", channels, [new("1 channel", [new(0, "Dimmer")])]);
    }

    private static FixtureDefinition CreateRgb()
    {
        var channels = new Dictionary<string, FixtureChannel>
        {
            ["Red"] = new("Red", "IntensityRed", "Colour"),
            ["Green"] = new("Green", "IntensityGreen", "Colour"),
            ["Blue"] = new("Blue", "IntensityBlue", "Colour")
        };
        return new("Generic", "RGB Fixture", "Color Changer", channels,
            [new("RGB", [new(0, "Red"), new(1, "Green"), new(2, "Blue")])],
            [new("Virtual Dimmer", "IntensityMasterDimmer", "Intensity", ["Red", "Green", "Blue"])]);
    }
}
