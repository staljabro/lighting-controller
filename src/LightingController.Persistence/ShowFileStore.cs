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
    IReadOnlyList<ShowCueList>? CueLists = null,
    IReadOnlyList<ShowEffect>? Effects = null,
    IReadOnlyList<ShowSelectionPreset>? SelectionPresets = null);

public sealed record ShowFixture(PatchedFixture Patch, FixtureDefinition Definition, string ModeName);
public sealed record ShowCue(string Number, string Name, double FadeSeconds, IReadOnlyList<ShowValue> Values, IReadOnlyList<ShowPresetReference>? PresetReferences = null, double? FollowSeconds = null, CueTriggerType TriggerType = CueTriggerType.Go, IReadOnlyList<int>? EffectSlots = null);
public sealed record ShowCueList(int Number, string Name, IReadOnlyList<ShowCue> Cues);
public sealed record ShowPresetReference(string Type, int Slot, PresetScope Scope, IReadOnlyList<int> FixtureIds);
public sealed record ShowPreset(int Slot, string Name, string Kind, string Swatch, IReadOnlyList<ShowValue> Values, PresetScope Scope = PresetScope.Global);
public sealed record ShowFixtureGroup(string Name, IReadOnlyList<int> FixtureIds, IReadOnlyList<ShowFixtureSelection>? Selections = null);
public sealed record ShowFixtureSelection(int FixtureId, string? PartName = null);
public sealed record ShowEffect(int Slot, string Name, EffectTarget Target, EffectWaveform Waveform, double RateHz, byte Low, byte High, double PhaseDegrees, IReadOnlyList<ShowFixtureSelection> Selections, EffectScope Scope = EffectScope.Selective, int Groups = 1, int Blocks = 1, int Wings = 1, IReadOnlyList<ShowEffectLine>? Lines = null, bool PhaseAcrossFixtureParts = false);
public sealed record ShowEffectLine(EffectAttribute Attribute, EffectWaveform Form, EffectValueMode Mode, double Low, double High, double SpeedHz, double PhaseFrom, double PhaseTo, double Width, double SineOffsetDegrees = 0);
public sealed record ShowSelectionPreset(int Slot, string Name, IReadOnlyList<ShowFixtureSelection> Selections);
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
