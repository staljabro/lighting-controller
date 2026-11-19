using LightingController.Core;

namespace LightingController.Fixtures;

public sealed class FixtureProgrammingEngine
{
    private readonly Dictionary<int, RuntimeFixture> _fixtures = [];

    public void Register(PatchedFixture patch, FixtureDefinition definition, FixtureMode mode) =>
        _fixtures[patch.Id] = new RuntimeFixture(patch, definition, mode);

    public void Remove(int fixtureId) => _fixtures.Remove(fixtureId);

    public IReadOnlyList<string> GetParts(int fixtureId) => _fixtures.TryGetValue(fixtureId, out var runtime)
        ? runtime.Definition.FixtureParts.Select(part => part.Name).ToArray() : [];

    public IReadOnlyList<FixtureSelection> ExpandWholeFixturesToParts(IEnumerable<FixtureSelection> selections)
    {
        var result = new List<FixtureSelection>();
        var seen = new HashSet<FixtureSelection>();
        foreach (var selection in selections)
        {
            if (!string.IsNullOrWhiteSpace(selection.PartName) || !_fixtures.TryGetValue(selection.FixtureId, out var runtime) || runtime.Definition.FixtureParts.Count == 0)
            {
                if (seen.Add(selection)) result.Add(selection);
                continue;
            }
            foreach (var part in runtime.Definition.FixtureParts)
            {
                var expanded = new FixtureSelection(selection.FixtureId, part.Name);
                if (seen.Add(expanded)) result.Add(expanded);
            }
        }
        return result;
    }

    public bool BelongsToSelection(ParameterAddress address, IEnumerable<FixtureSelection> selections)
    {
        foreach (var selection in selections)
        {
            if (selection.FixtureId != address.FixtureId) continue;
            if (string.IsNullOrWhiteSpace(selection.PartName)) return true;
            if (!_fixtures.TryGetValue(address.FixtureId, out var runtime)) continue;
            var part = runtime.Definition.FixtureParts.FirstOrDefault(item => item.Name == selection.PartName);
            if (part is not null && address.Channel < runtime.Mode.Channels.Count && part.ChannelNames.Contains(runtime.Mode.Channels[address.Channel].ChannelName)) return true;
        }
        return false;
    }

    public IReadOnlyDictionary<ParameterAddress, CueValue> ResolvePresetValues(
        IReadOnlyDictionary<ParameterAddress, CueValue> stored,
        PresetScope scope,
        IEnumerable<FixtureSelection> selections)
    {
        var selectedIds = selections.Select(item => item.FixtureId).Distinct().ToArray();
        if (scope != PresetScope.FixtureType)
            return stored.Where(item => selectedIds.Contains(item.Key.FixtureId)).ToDictionary(item => item.Key, item => item.Value);

        var result = new Dictionary<ParameterAddress, CueValue>();
        foreach (var selectedId in selectedIds)
        {
            if (!_fixtures.TryGetValue(selectedId, out var target)) continue;
            var source = stored.FirstOrDefault(item => _fixtures.TryGetValue(item.Key.FixtureId, out var candidate) &&
                candidate.Definition.Manufacturer == target.Definition.Manufacturer && candidate.Definition.Model == target.Definition.Model);
            if (source.Equals(default(KeyValuePair<ParameterAddress, CueValue>))) continue;
            foreach (var item in stored.Where(item => item.Key.FixtureId == source.Key.FixtureId))
                result[new ParameterAddress(selectedId, item.Key.Channel)] = item.Value;
        }
        return result;
    }

    public void SetIntensity(IDictionary<ParameterAddress, CueValue> values, IEnumerable<int> fixtureIds, byte level)
        => SetIntensity(values, fixtureIds.Select(id => new FixtureSelection(id)), level);

    public void SetIntensity(IDictionary<ParameterAddress, CueValue> values, IEnumerable<FixtureSelection> selections, byte level)
    {
        foreach (var (runtime, part) in GetFixtures(selections))
        {
            var virtualIndex = runtime.Definition.VirtualControlChannels.Select((channel, index) => (channel, index))
                .FirstOrDefault(item => (item.channel.Preset is "IntensityMasterDimmer" or "IntensityDimmer") && MatchesPart(item.channel.TargetChannels, part));
            if (virtualIndex.channel is not null)
            {
                Set(values, runtime, runtime.Mode.Channels.Count + virtualIndex.index, level, ParameterKind.Intensity);
                continue;
            }
            var physical = runtime.Mode.Channels.Select((channel, index) => (channel, index))
                .FirstOrDefault(item => MatchesPart([item.channel.ChannelName], part) && (runtime.Definition.Channels[item.channel.ChannelName].Preset is "IntensityMasterDimmer" or "IntensityDimmer"));
            if (physical.channel is not null) Set(values, runtime, physical.index, level, ParameterKind.Intensity);
        }
    }

    public void SetColour(IDictionary<ParameterAddress, CueValue> values, IEnumerable<int> fixtureIds, byte red, byte green, byte blue)
        => SetColour(values, fixtureIds.Select(id => new FixtureSelection(id)), red, green, blue);

    public void SetColour(IDictionary<ParameterAddress, CueValue> values, IEnumerable<FixtureSelection> selections, byte red, byte green, byte blue)
    {
        foreach (var (runtime, part) in GetFixtures(selections))
        {
            SetPreset(values, runtime, part, "IntensityRed", red, ParameterKind.Colour);
            SetPreset(values, runtime, part, "IntensityGreen", green, ParameterKind.Colour);
            SetPreset(values, runtime, part, "IntensityBlue", blue, ParameterKind.Colour);
        }
    }

    public void SetPosition(IDictionary<ParameterAddress, CueValue> values, IEnumerable<int> fixtureIds, byte pan, byte tilt)
        => SetPosition(values, fixtureIds.Select(id => new FixtureSelection(id)), pan, tilt);

    public void SetPosition(IDictionary<ParameterAddress, CueValue> values, IEnumerable<FixtureSelection> selections, byte pan, byte tilt)
    {
        foreach (var (runtime, part) in GetFixtures(selections))
        {
            SetPreset(values, runtime, part, "PositionPan", pan, ParameterKind.Position);
            SetPreset(values, runtime, part, "PositionTilt", tilt, ParameterKind.Position);
        }
    }

    public void SetEffectAttribute(IDictionary<ParameterAddress, CueValue> values, FixtureSelection selection, EffectAttribute attribute, double value, EffectValueMode mode)
    {
        if (!_fixtures.TryGetValue(selection.FixtureId, out var runtime)) return;
        var part = runtime.Definition.FixtureParts.FirstOrDefault(item => item.Name == selection.PartName);
        var (preset, kind) = attribute switch
        {
            EffectAttribute.Dimmer => ("IntensityDimmer", ParameterKind.Intensity), EffectAttribute.Pan => ("PositionPan", ParameterKind.Position), EffectAttribute.Tilt => ("PositionTilt", ParameterKind.Position),
            EffectAttribute.Red => ("IntensityRed", ParameterKind.Colour), EffectAttribute.Green => ("IntensityGreen", ParameterKind.Colour), EffectAttribute.Blue => ("IntensityBlue", ParameterKind.Colour), _ => ("", ParameterKind.Other)
        };
        if (attribute == EffectAttribute.Dimmer)
        {
            var virtualIndex = runtime.Definition.VirtualControlChannels.Select((channel, index) => (channel, index)).FirstOrDefault(item => (item.channel.Preset is "IntensityMasterDimmer" or "IntensityDimmer") && MatchesPart(item.channel.TargetChannels, part));
            if (virtualIndex.channel is not null)
            {
                var address = new ParameterAddress(runtime.Patch.Id, runtime.Mode.Channels.Count + virtualIndex.index); var output = mode == EffectValueMode.Relative ? (values.TryGetValue(address, out var current) ? current.Value : virtualIndex.channel.DefaultValue) + value : value;
                values[address] = new CueValue((byte)Math.Clamp(Math.Round(output), 0, 255), ParameterKind.Intensity); return;
            }
        }
        for (var index = 0; index < runtime.Mode.Channels.Count; index++)
        {
            var channel = runtime.Definition.Channels[runtime.Mode.Channels[index].ChannelName];
            if (!MatchesPart([runtime.Mode.Channels[index].ChannelName], part) || channel.Preset != preset) continue;
            var address = new ParameterAddress(runtime.Patch.Id, index); var output = mode == EffectValueMode.Relative ? (values.TryGetValue(address, out var current) ? current.Value : channel.DefaultValue) + value : value;
            values[address] = new CueValue((byte)Math.Clamp(Math.Round(output), 0, 255), kind);
        }
    }

    public byte[] RenderUniverse(int universe, IReadOnlyDictionary<ParameterAddress, CueValue> values, OutputMaster? master = null)
    {
        var frame = new byte[512];
        foreach (var runtime in _fixtures.Values.Where(item => item.Patch.Universe == universe))
        {
            var physical = runtime.Mode.Channels.Select(channel => runtime.Definition.Channels[channel.ChannelName].DefaultValue).ToArray();
            for (var index = 0; index < physical.Length; index++)
                if (values.TryGetValue(new ParameterAddress(runtime.Patch.Id, index), out var value)) physical[index] = value.Value;

            for (var virtualIndex = 0; virtualIndex < runtime.Definition.VirtualControlChannels.Count; virtualIndex++)
            {
                var virtualChannel = runtime.Definition.VirtualControlChannels[virtualIndex];
                var virtualLevel = values.TryGetValue(new ParameterAddress(runtime.Patch.Id, physical.Length + virtualIndex), out var virtualMaster) ? virtualMaster.Value : virtualChannel.DefaultValue;
                foreach (var target in virtualChannel.TargetChannels)
                {
                    var targetIndex = runtime.Mode.Channels.ToList().FindIndex(channel => channel.ChannelName == target);
                    if (targetIndex >= 0) physical[targetIndex] = (byte)Math.Round(physical[targetIndex] * virtualLevel / 255d, MidpointRounding.AwayFromZero);
                }
            }
            for (var index = 0; index < physical.Length; index++)
            {
                var channel = runtime.Definition.Channels[runtime.Mode.Channels[index].ChannelName];
                var isIntensity = channel.Group == "Intensity" || channel.Preset?.StartsWith("Intensity", StringComparison.Ordinal) == true;
                frame[runtime.Patch.Address - 1 + index] = master?.Apply(physical[index], isIntensity) ?? physical[index];
            }
        }
        return frame;
    }

    public IReadOnlyList<FixtureAttributeTarget> ResolveAttributeTargets(IEnumerable<FixtureSelection> selections, EffectAttribute attribute)
    {
        var targets = new List<FixtureAttributeTarget>();
        var (preset, kind) = attribute switch
        {
            EffectAttribute.Dimmer => ("IntensityDimmer", ParameterKind.Intensity), EffectAttribute.Pan => ("PositionPan", ParameterKind.Position), EffectAttribute.Tilt => ("PositionTilt", ParameterKind.Position),
            EffectAttribute.Red => ("IntensityRed", ParameterKind.Colour), EffectAttribute.Green => ("IntensityGreen", ParameterKind.Colour), EffectAttribute.Blue => ("IntensityBlue", ParameterKind.Colour), _ => ("", ParameterKind.Other)
        };
        foreach (var (runtime, part) in GetFixtures(selections))
        {
            if (attribute == EffectAttribute.Dimmer)
            {
                var virtualIndex = runtime.Definition.VirtualControlChannels.Select((channel, index) => (channel, index))
                    .FirstOrDefault(item => (item.channel.Preset is "IntensityMasterDimmer" or "IntensityDimmer") && MatchesPart(item.channel.TargetChannels, part));
                if (virtualIndex.channel is not null)
                {
                    targets.Add(new(new(runtime.Patch.Id, runtime.Mode.Channels.Count + virtualIndex.index), virtualIndex.channel.DefaultValue, kind));
                    continue;
                }
            }
            for (var index = 0; index < runtime.Mode.Channels.Count; index++)
            {
                var modeChannel = runtime.Mode.Channels[index];
                var channel = runtime.Definition.Channels[modeChannel.ChannelName];
                if (MatchesPart([modeChannel.ChannelName], part) && channel.Preset == preset)
                    targets.Add(new(new(runtime.Patch.Id, index), channel.DefaultValue, kind));
            }
        }
        return targets.DistinctBy(item => item.Address).ToArray();
    }

    private IEnumerable<(RuntimeFixture Runtime, FixturePart? Part)> GetFixtures(IEnumerable<FixtureSelection> selections)
    {
        foreach (var selection in selections.Distinct())
            if (_fixtures.TryGetValue(selection.FixtureId, out var runtime))
                yield return (runtime, runtime.Definition.FixtureParts.FirstOrDefault(part => part.Name == selection.PartName));
    }

    private static void SetPreset(IDictionary<ParameterAddress, CueValue> values, RuntimeFixture runtime, FixturePart? part, string preset, byte value, ParameterKind kind)
    {
        for (var index = 0; index < runtime.Mode.Channels.Count; index++)
            if (MatchesPart([runtime.Mode.Channels[index].ChannelName], part) && runtime.Definition.Channels[runtime.Mode.Channels[index].ChannelName].Preset == preset) Set(values, runtime, index, value, kind);
    }

    private static bool MatchesPart(IEnumerable<string> channels, FixturePart? part) => part is null || channels.Any(part.ChannelNames.Contains);

    private static void Set(IDictionary<ParameterAddress, CueValue> values, RuntimeFixture runtime, int channel, byte value, ParameterKind kind) =>
        values[new ParameterAddress(runtime.Patch.Id, channel)] = new CueValue(value, kind);

    private sealed record RuntimeFixture(PatchedFixture Patch, FixtureDefinition Definition, FixtureMode Mode);
}

public readonly record struct FixtureSelection(int FixtureId, string? PartName = null);
public readonly record struct FixtureAttributeTarget(ParameterAddress Address, byte DefaultValue, ParameterKind Kind);
