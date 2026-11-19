using LightingController.Core;

namespace LightingController.Core.Tests;

public sealed class CueStackTests
{
    [Fact]
    public void BakePresetReference_PreservesPlaybackAndDetachesReusedSlot()
    {
        var baked = new ParameterAddress(1, 0);
        var explicitValue = new ParameterAddress(1, 1);
        var laterReference = new ParameterAddress(1, 2);
        var deleted = new CuePresetReference("Position", 1, PresetScope.Individual, [1]);
        var retained = new CuePresetReference("Position", 2, PresetScope.Individual, [1]);
        var cue = new Cue("1");
        cue.AddPresetReference(deleted);
        cue.AddPresetReference(retained);
        cue.SetValue(explicitValue, 0, ParameterKind.Position);
        var stack = new CueStack("Main");
        stack.Add(cue);

        var deletedValues = new Dictionary<ParameterAddress, CueValue>
        {
            [baked] = new(80, ParameterKind.Position),
            [explicitValue] = new(90, ParameterKind.Position),
            [laterReference] = new(100, ParameterKind.Position)
        };
        var retainedValues = new Dictionary<ParameterAddress, CueValue>
        {
            [laterReference] = new(110, ParameterKind.Position)
        };
        IReadOnlyDictionary<ParameterAddress, CueValue> Resolve(CuePresetReference reference) =>
            reference.Slot == deleted.Slot ? deletedValues : retainedValues;

        Assert.True(cue.BakePresetReference(deleted.Type, deleted.Slot, Resolve));
        Assert.Equal(retained, Assert.Single(cue.PresetReferences));
        deletedValues[baked] = new(200, ParameterKind.Position);
        retainedValues[laterReference] = new(120, ParameterKind.Position);

        var output = stack.Resolve(0, Resolve);
        Assert.Equal((byte)80, output[baked].Value);
        Assert.Equal((byte)0, output[explicitValue].Value);
        Assert.Equal((byte)120, output[laterReference].Value);
    }

    [Fact]
    public void Resolve_TracksValuesAndPreservesExplicitZero()
    {
        var dimmer = new ParameterAddress(1, 0);
        var red = new ParameterAddress(1, 1);
        var stack = new CueStack("Main");

        var first = new Cue("1", "Preset") { FadeIn = TimeSpan.FromSeconds(2) };
        first.SetValue(dimmer, 255, ParameterKind.Intensity);
        first.SetValue(red, 100, ParameterKind.Colour);
        stack.Add(first);

        var second = new Cue("2", "Blackout");
        second.SetValue(dimmer, 0, ParameterKind.Intensity);
        stack.Add(second);

        var resolved = stack.Resolve(1);

        Assert.Equal((byte)0, resolved[dimmer].Value);
        Assert.Equal((byte)100, resolved[red].Value);
    }

    [Fact]
    public void Update_MergesProgrammerValuesWithoutReplacingCue()
    {
        var stack = new CueStack("Main");
        var cue = new Cue("1", "Look");
        var dimmer = new ParameterAddress(1, 0);
        var pan = new ParameterAddress(1, 1);
        cue.SetValue(dimmer, 100, ParameterKind.Intensity);
        stack.Add(cue);

        stack.Update(0, new Dictionary<ParameterAddress, CueValue>
        {
            [pan] = new CueValue(127, ParameterKind.Position)
        });

        Assert.Equal(2, stack.Cues[0].Values.Count);
        Assert.Equal((byte)100, stack.Cues[0].Values[dimmer].Value);
        Assert.Equal((byte)127, stack.Cues[0].Values[pan].Value);
    }

    [Fact]
    public void Resolve_WhenTrackingDisabled_ReturnsOnlySelectedCue()
    {
        var stack = new CueStack("Main") { Tracking = false };
        var first = new Cue("1");
        first.SetValue(new ParameterAddress(1, 0), 255, ParameterKind.Intensity);
        stack.Add(first);
        var second = new Cue("2");
        second.SetValue(new ParameterAddress(1, 1), 50, ParameterKind.Colour);
        stack.Add(second);

        var resolved = stack.Resolve(1);

        Assert.Single(resolved);
        Assert.False(resolved.ContainsKey(new ParameterAddress(1, 0)));
    }

    [Fact]
    public void Merge_UsesHtpForIntensityAndLatestForOtherAttributes()
    {
        var intensity = new ParameterAddress(1, 0);
        var pan = new ParameterAddress(1, 1);

        var merged = DmxMerger.Merge(
        [
            new PlaybackValue(intensity, 100, ParameterKind.Intensity, 1),
            new PlaybackValue(intensity, 200, ParameterKind.Intensity, 2),
            new PlaybackValue(pan, 40, ParameterKind.Position, 1),
            new PlaybackValue(pan, 10, ParameterKind.Position, 2)
        ]);

        Assert.Equal((byte)200, merged[intensity]);
        Assert.Equal((byte)10, merged[pan]);
    }

    [Fact]
    public void Resolve_PresetReferenceUsesLatestPresetValuesAndHardValuesOverride()
    {
        var address = new ParameterAddress(1, 0);
        var reference = new CuePresetReference("Position", 1, PresetScope.Individual, [1]);
        var cue = new Cue("1"); cue.AddPresetReference(reference);
        var stack = new CueStack("List 1"); stack.Add(cue);
        byte presetValue = 80;

        var first = stack.Resolve(0, _ => new Dictionary<ParameterAddress, CueValue> { [address] = new(presetValue, ParameterKind.Position) });
        presetValue = 160;
        var second = stack.Resolve(0, _ => new Dictionary<ParameterAddress, CueValue> { [address] = new(presetValue, ParameterKind.Position) });
        cue.SetValue(address, 200, ParameterKind.Position);
        var hardOverride = stack.Resolve(0, _ => new Dictionary<ParameterAddress, CueValue> { [address] = new(160, ParameterKind.Position) });

        Assert.Equal((byte)80, first[address].Value);
        Assert.Equal((byte)160, second[address].Value);
        Assert.Equal((byte)200, hardOverride[address].Value);
    }

    [Fact]
    public void Move_ReordersCuesForTimelineEditing()
    {
        var stack = new CueStack("Main"); stack.Add(new Cue("1")); stack.Add(new Cue("2")); stack.Add(new Cue("3"));
        stack.Move(0, 2);
        Assert.Equal(["2", "3", "1"], stack.Cues.Select(cue => cue.Number));
    }

    [Fact]
    public void Copy_PreservesCueTypeFollowAndEffectReferences()
    {
        var cue = new Cue("1") { TriggerType = CueTriggerType.Follow, Follow = TimeSpan.FromSeconds(3) }; cue.AddEffectReference(4);
        var copy = cue.Copy();
        Assert.Equal(CueTriggerType.Follow, copy.TriggerType); Assert.Equal(TimeSpan.FromSeconds(3), copy.Follow); Assert.Equal([4], copy.EffectSlots);
    }
}

public sealed class FixturePatchTests
{
    [Fact]
    public void Add_StoresNonOverlappingFixturesInIdOrder()
    {
        var patch = new FixturePatch();
        patch.Add(new PatchedFixture(2, "Wash 1", "Generic", "Wash", "16ch", 1, 20, 16));
        patch.Add(new PatchedFixture(1, "Beam 1", "Generic", "Beam", "16ch", 1, 1, 16));

        Assert.Equal([1, 2], patch.Fixtures.Select(fixture => fixture.Id));
    }

    [Fact]
    public void Add_RejectsAddressOverlap()
    {
        var patch = new FixturePatch();
        patch.Add(new PatchedFixture(1, "Beam 1", "Generic", "Beam", "16ch", 1, 1, 16));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            patch.Add(new PatchedFixture(2, "Beam 2", "Generic", "Beam", "16ch", 1, 16, 16)));

        Assert.Contains("overlaps", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fixture_RejectsFootprintOutsideUniverse()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PatchedFixture(1, "Beam", "Generic", "Beam", "16ch", 1, 500, 16));
    }
}

