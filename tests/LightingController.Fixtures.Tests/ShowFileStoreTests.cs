using LightingController.Core;
using LightingController.Persistence;

namespace LightingController.Fixtures.Tests;

public sealed class ShowFileStoreTests
{
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
