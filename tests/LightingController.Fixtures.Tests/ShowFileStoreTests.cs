using LightingController.Core;
using LightingController.Persistence;
using System.Text.Json.Nodes;

namespace LightingController.Fixtures.Tests;

public sealed class ShowFileStoreTests
{
    [Fact]
    public void ShowFile_RoundTripsLineFormsAndSineOffsetAndDefaultsMissingOffsetToZero()
    {
        var line = new ShowEffectLine(EffectAttribute.Dimmer, EffectWaveform.Sine, EffectValueMode.Absolute,
            0, 255, 1, 0, 360, 100, SineOffsetDegrees: 135);
        var effect = new ShowEffect(1, "Pulse", EffectTarget.Intensity, EffectWaveform.Sine,
            1, 0, 255, 0, [], Lines: [line, line with { Attribute = EffectAttribute.Red, Form = EffectWaveform.SmoothPulse, Width = 20 }],
            PhaseAcrossFixtureParts: true);
        var show = new ShowFile(1, "Test", [], [], [], [], Effects: [effect]);
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{ShowFileStore.Extension}");
        var store = new ShowFileStore();
        try
        {
            store.Save(show, path);
            var loadedEffect = store.Load(path).Effects![0];
            Assert.True(loadedEffect.PhaseAcrossFixtureParts);
            var loadedLines = loadedEffect.Lines!;
            Assert.Equal([EffectWaveform.Sine, EffectWaveform.SmoothPulse], loadedLines.Select(item => item.Form));
            Assert.Equal(20, loadedLines[1].Width);
            Assert.Equal(135, loadedLines[0].SineOffsetDegrees);

            var saved = JsonNode.Parse(File.ReadAllText(path))!;
            saved["Effects"]![0]!.AsObject().Remove("PhaseAcrossFixtureParts");
            saved["Effects"]![0]!["Lines"]![0]!.AsObject().Remove("SineOffsetDegrees");
            File.WriteAllText(path, saved.ToJsonString());
            var legacyEffect = store.Load(path).Effects![0];
            Assert.False(legacyEffect.PhaseAcrossFixtureParts);
            Assert.Equal(0, legacyEffect.Lines![0].SineOffsetDegrees);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ShowFile_RoundTripsPatchCuesPresetsAndGroups()
    {
        var definition = BuiltInFixtureLibrary.Create()[0];
        var patch = new PatchedFixture(1, "Dimmer 1", definition.Manufacturer, definition.Model, definition.Modes[0].Name, 1, 1, 1);
        var value = new ShowValue(1, 0, 200, ParameterKind.Intensity);
        var show = new ShowFile(1, "Test", [new(patch, definition, definition.Modes[0].Name)], [new("1", "Look", 2, [value])], [new(1, "Warm", "Colour", "#FFAA00", [value])], [new("Front", [1])]);
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{ShowFileStore.Extension}");
        try
        {
            new ShowFileStore().Save(show, path);
            var loaded = new ShowFileStore().Load(path);
            Assert.Equal("Test", loaded.Name);
            Assert.Equal((byte)200, loaded.Cues[0].Values[0].Value);
            Assert.Equal("Front", loaded.Groups[0].Name);
        }
        finally { File.Delete(path); }
    }
}
