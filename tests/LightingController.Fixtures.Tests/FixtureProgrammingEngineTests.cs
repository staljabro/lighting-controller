using LightingController.Core;

namespace LightingController.Fixtures.Tests;

public sealed class FixtureProgrammingEngineTests
{
    [Fact]
    public void RgbVirtualDimmer_ScalesColourWithoutConsumingDmxAddress()
    {
        var definition = BuiltInFixtureLibrary.Create().Single(item => item.Model == "RGB Fixture");
        var mode = definition.Modes.Single();
        var patch = new PatchedFixture(1, "RGB 1", definition.Manufacturer, definition.Model, mode.Name, 1, 10, mode.Channels.Count);
        var engine = new FixtureProgrammingEngine();
        engine.Register(patch, definition, mode);
        var values = new Dictionary<ParameterAddress, CueValue>();

        engine.SetColour(values, [1], 255, 128, 64);
        engine.SetIntensity(values, [1], 128);
        var frame = engine.RenderUniverse(1, values);

        Assert.Equal((byte)128, frame[9]);
        Assert.Equal((byte)64, frame[10]);
        Assert.Equal((byte)32, frame[11]);
        Assert.Equal(4, values.Count);
        Assert.Equal(3, patch.ChannelCount);
    }

    [Fact]
    public void PhysicalDimmer_MapsIntensityToPatchedAddress()
    {
        var definition = BuiltInFixtureLibrary.Create().Single(item => item.Model == "Single Channel Dimmer");
        var mode = definition.Modes.Single();
        var patch = new PatchedFixture(7, "Dimmer", definition.Manufacturer, definition.Model, mode.Name, 2, 101, 1);
        var engine = new FixtureProgrammingEngine();
        engine.Register(patch, definition, mode);
        var values = new Dictionary<ParameterAddress, CueValue>();

        engine.SetIntensity(values, [7], 200);
        var frame = engine.RenderUniverse(2, values);

        Assert.Equal((byte)200, frame[100]);
        Assert.Equal(0, engine.RenderUniverse(1, values).Sum(value => value));
    }

    [Fact]
    public void CueTrackingValues_RenderThroughFixtureDefinition()
    {
        var definition = BuiltInFixtureLibrary.Create().Single(item => item.Model == "RGB Fixture");
        var mode = definition.Modes.Single();
        var patch = new PatchedFixture(1, "RGB", definition.Manufacturer, definition.Model, mode.Name, 1, 1, 3);
        var engine = new FixtureProgrammingEngine();
        engine.Register(patch, definition, mode);
        var programmer = new Dictionary<ParameterAddress, CueValue>();
        engine.SetColour(programmer, [1], 255, 0, 0);
        engine.SetIntensity(programmer, [1], 255);
        var stack = new CueStack("Main");
        var cue = new Cue("1");
        foreach (var item in programmer) cue.SetValue(item.Key, item.Value.Value, item.Value.Kind);
        stack.Add(cue);

        var frame = engine.RenderUniverse(1, stack.Resolve(0));

        Assert.Equal([(byte)255, (byte)0, (byte)0], frame[..3]);
    }

    [Fact]
    public void MultiCellFixture_CanProgramOneCellIndependently()
    {
        var channels = new Dictionary<string, FixtureChannel>();
        foreach (var cell in new[] { "Cell 1", "Cell 2" })
        {
            channels[$"{cell} Red"] = new($"{cell} Red", "IntensityRed", "Colour");
            channels[$"{cell} Green"] = new($"{cell} Green", "IntensityGreen", "Colour");
            channels[$"{cell} Blue"] = new($"{cell} Blue", "IntensityBlue", "Colour");
        }
        var names = channels.Keys.ToArray();
        var fixture = new FixtureDefinition("Test", "Two Cell", "LED Bar", channels,
            [new("6 channel", names.Select((name, index) => new FixtureModeChannel(index, name)).ToArray())],
            [new("Cell 1 Dimmer", "IntensityMasterDimmer", "Intensity", names[..3]), new("Cell 2 Dimmer", "IntensityMasterDimmer", "Intensity", names[3..])],
            [new("Cell 1", names[..3]), new("Cell 2", names[3..])]);
        var patch = new PatchedFixture(1, "Bar", "Test", "Two Cell", "6 channel", 1, 1, 6);
        var engine = new FixtureProgrammingEngine(); engine.Register(patch, fixture, fixture.Modes[0]);
        var values = new Dictionary<ParameterAddress, CueValue>();

        engine.SetColour(values, [new FixtureSelection(1, "Cell 2")], 100, 50, 25);
        engine.SetIntensity(values, [new FixtureSelection(1, "Cell 2")], 255);
        var frame = engine.RenderUniverse(1, values);

        Assert.Equal([(byte)0, (byte)0, (byte)0], frame[..3]);
        Assert.Equal([(byte)100, (byte)50, (byte)25], frame[3..6]);
    }
}
