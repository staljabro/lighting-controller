namespace LightingController.Core;

public enum PresetScope { Global, FixtureType, Individual }

public sealed record CuePresetReference(
    string Type,
    int Slot,
    PresetScope Scope,
    IReadOnlyList<int> FixtureIds);
