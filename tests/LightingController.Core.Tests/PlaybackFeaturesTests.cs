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
        Assert.Equal(["clear"], ConsoleCommandParser.Parse("x"));
        Assert.Equal(["clear", "clear", "clear"], ConsoleCommandParser.Parse("x x x"));
        Assert.Equal(["clear", "clear", "clear"], ConsoleCommandParser.Parse("xxx"));
    }

    [Theory]
    [InlineData("STORE PRESET COLOUR 1", "STORE PRESET COLOUR ")]
    [InlineData("RENAME ", "")]
    [InlineData("fixture 12", "fixture ")]
    [InlineData("   ", "")]
    public void CommandParser_BackspaceRemovesTheLastCommandStroke(string input, string expected)
        => Assert.Equal(expected, ConsoleCommandParser.RemoveLastStroke(input));

    [Fact]
    public void ConsoleEffect_DistributesPhaseInSelectionOrderAndBlocks()
    {
        var line = new ConsoleEffectLine { PhaseFrom = 0, PhaseTo = 360 };
        var effect = new ConsoleEffectDefinition { Blocks = 2, Lines = [line] };
        Assert.Equal(0, effect.PhaseFor(0, 6, line));
        Assert.Equal(0, effect.PhaseFor(1, 6, line));
        Assert.Equal(180, effect.PhaseFor(2, 6, line));
        Assert.Equal(360, effect.PhaseFor(4, 6, line));
    }

    [Fact]
    public void ConsoleEffect_WingsMirrorAdjacentParts()
    {
        var line = new ConsoleEffectLine { PhaseFrom = 0, PhaseTo = 360 };
        var effect = new ConsoleEffectDefinition { Wings = 2, Lines = [line] };
        Assert.Equal(0, effect.PhaseFor(0, 6, line));
        Assert.Equal(360, effect.PhaseFor(2, 6, line));
        Assert.Equal(360, effect.PhaseFor(3, 6, line));
        Assert.Equal(0, effect.PhaseFor(5, 6, line));
    }

    [Fact]
    public void SineOffset_ShiftsOnlyItsLineAndCombinesWithFixturePhase()
    {
        var baseline = new ConsoleEffectLine { High = 100 };
        var shifted = baseline.Copy();
        shifted.SineOffsetDegrees = -180;
        var effect = new ConsoleEffectDefinition { Lines = [baseline, shifted] };

        Assert.Equal(0, baseline.Sample(TimeSpan.Zero, 0), 8);
        Assert.Equal(100, shifted.Sample(TimeSpan.Zero, 0), 8);
        Assert.Equal(50, shifted.Sample(TimeSpan.FromSeconds(0.125), effect.PhaseFor(1, 9, shifted)), 8);

        shifted.Form = EffectWaveform.Saw;
        Assert.Equal(25, shifted.Sample(TimeSpan.Zero, 90), 8);
    }

    [Fact]
    public void EffectLines_SampleTheirOwnWaveformsAtTheSamePhase()
    {
        var effect = new ConsoleEffectDefinition
        {
            Lines = [new() { Form = EffectWaveform.Sine, High = 100 },
                new() { Form = EffectWaveform.Square, High = 100, Width = 50 }]
        };
        var elapsed = TimeSpan.FromSeconds(0.25);

        Assert.Equal(50, effect.Lines[0].Sample(elapsed, effect.PhaseFor(0, 1, effect.Lines[0])), 8);
        Assert.Equal(100, effect.Lines[1].Sample(elapsed, effect.PhaseFor(0, 1, effect.Lines[1])), 8);
    }

    [Theory]
    [InlineData(25, 0, 0, 10)]
    [InlineData(25, 1.0 / 16, 0, 60)]
    [InlineData(25, 0, 45, 110)]
    [InlineData(25, 3.0 / 16, 0, 60)]
    [InlineData(25, 0.25, 0, 10)]
    [InlineData(25, 0.99, 0, 10)]
    [InlineData(25, 1.125, 0, 110)]
    [InlineData(0, 0, 0, 10)]
    [InlineData(100, 0.5, 0, 110)]
    public void SmoothPulse_FadesWithinWidthAndHoldsLowForRestOfCycle(
        double width, double seconds, double phase, double expected)
    {
        var line = new ConsoleEffectLine { Form = EffectWaveform.SmoothPulse, Low = 10, High = 110, Width = width };
        Assert.Equal(expected, line.Sample(TimeSpan.FromSeconds(seconds), phase), 8);
    }

    [Fact]
    public void RelativeEffectLineProducesSignedModulation()
    {
        var line = new ConsoleEffectLine { Mode = EffectValueMode.Relative, Form = EffectWaveform.Square, Low = -20, High = 20, Width = 50 };
        Assert.Equal(20, line.Sample(TimeSpan.Zero, 0));
        Assert.Equal(-20, line.Sample(TimeSpan.FromSeconds(.75), 0));
    }
}
