using System.Text.Json;
using LightingController.Core;
using LightingController.Fixtures;

namespace LightingController.Persistence;

public sealed record ShowFile(
    int Version,
    string Name,
    IReadOnlyList<ShowFixture> Fixtures,
    IReadOnlyList<ShowCue> Cues,
    IReadOnlyList<ShowPreset> Presets,
    IReadOnlyList<ShowFixtureGroup> Groups,
    IReadOnlyList<ShowCueList>? CueLists = null);

public sealed record ShowFixture(PatchedFixture Patch, FixtureDefinition Definition, string ModeName);
public sealed record ShowCue(string Number, string Name, double FadeSeconds, IReadOnlyList<ShowValue> Values, IReadOnlyList<ShowPresetReference>? PresetReferences = null);
public sealed record ShowCueList(int Number, string Name, IReadOnlyList<ShowCue> Cues);
public sealed record ShowPresetReference(string Type, int Slot, PresetScope Scope, IReadOnlyList<int> FixtureIds);
public sealed record ShowPreset(int Slot, string Name, string Kind, string Swatch, IReadOnlyList<ShowValue> Values, PresetScope Scope = PresetScope.Global);
public sealed record ShowFixtureGroup(string Name, IReadOnlyList<int> FixtureIds);
public sealed record ShowValue(int FixtureId, int Channel, byte Value, ParameterKind Kind)
{
    public ParameterAddress Address => new(FixtureId, Channel);
}

public sealed class ShowFileStore
{
    public const string Extension = ".lcshow";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public void Save(ShowFile show, string path)
    {
        ArgumentNullException.ThrowIfNull(show);
        if (show.Version != 1) throw new InvalidDataException("Only show-file version 1 can be saved.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, show, Options);
    }

    public ShowFile Load(string path)
    {
        using var stream = File.OpenRead(path);
        var show = JsonSerializer.Deserialize<ShowFile>(stream, Options) ?? throw new InvalidDataException("The show file is empty or invalid.");
        if (show.Version != 1) throw new InvalidDataException($"Unsupported show-file version {show.Version}.");
        return show;
    }
}
