using LightingController.Core;

namespace LightingController.Core.Tests;

public sealed class CueStackTests
{
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
}

