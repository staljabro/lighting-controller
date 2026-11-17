using LightingController.Core;

namespace LightingController.Core.Tests;

public sealed class PlaybackFeaturesTests
{
    [Fact]
    public void GrandMaster_ReducesIntensityButLeavesLtpAttributes()
    {
        var master = new OutputMaster { Level = 128 };
        Assert.Equal((byte)100, master.Apply(200, true));
        Assert.Equal((byte)200, master.Apply(200, false));
        master.Blackout = true;
        Assert.Equal((byte)0, master.Apply(200, false));
    }

    [Fact]
    public void FadeCalculator_InterpolatesTrackedValues()
    {
        var address = new ParameterAddress(1, 0);
        var result = FadeCalculator.Interpolate(
            new Dictionary<ParameterAddress, CueValue> { [address] = new(0, ParameterKind.Intensity) },
            new Dictionary<ParameterAddress, CueValue> { [address] = new(200, ParameterKind.Intensity) }, .25);
        Assert.Equal((byte)50, result[address].Value);
    }

    [Fact]
    public void Chaser_SelectsStepFromElapsedTime()
    {
        var chaser = new Chaser();
        chaser.Add(new(new Dictionary<ParameterAddress, CueValue>(), TimeSpan.Zero, TimeSpan.FromMilliseconds(100)));
        var second = new ChaserStep(new Dictionary<ParameterAddress, CueValue>(), TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
        chaser.Add(second);
        Assert.Same(second, chaser.StepAt(TimeSpan.FromMilliseconds(150)));
    }

    [Fact]
    public void EffectWaveformsProduceExpectedRange()
    {
        var effect = new EffectDefinition(EffectWaveform.Square, 1, 10, 210);
        Assert.Equal((byte)10, effect.Sample(TimeSpan.Zero));
        Assert.Equal((byte)210, effect.Sample(TimeSpan.FromMilliseconds(750)));
    }

    [Fact]
    public void CommandParser_ExpandsConsoleShortcuts()
    {
        Assert.Equal(["store", "preset", "colour", "1"], ConsoleCommandParser.Parse("s p c 1"));
        Assert.Equal(["intensity", "128"], ConsoleCommandParser.Parse("i 128"));
    }
}
