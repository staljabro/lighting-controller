using System.Text;

namespace LightingController.Fixtures.Tests;

public sealed class QlcFixtureImporterTests
{
    [Fact]
    public void Import_ReadsChannelsAndModesFromQxf()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <FixtureDefinition xmlns="http://www.qlcplus.org/FixtureDefinition">
              <Manufacturer>Generic</Manufacturer>
              <Model>Generic RGB</Model>
              <Type>Color Changer</Type>
              <Channel Name="Red" Preset="IntensityRed" />
              <Channel Name="Dimmer" Preset="IntensityDimmer" />
              <Mode Name="RGB Dimmer">
                <Channel Number="0">Red</Channel>
                <Channel Number="1">Dimmer</Channel>
              </Mode>
            </FixtureDefinition>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        var fixture = new QlcFixtureImporter().Import(stream);

        Assert.Equal("Generic", fixture.Manufacturer);
        Assert.Equal("Generic RGB", fixture.Model);
        Assert.Equal("IntensityDimmer", fixture.Channels["Dimmer"].Preset);
        Assert.Equal(2, fixture.Modes[0].Channels.Count);
        Assert.Equal("Dimmer", fixture.Modes[0].Channels[1].ChannelName);
    }

    [Fact]
    public void Import_RejectsModeWithUnknownChannel()
    {
        const string xml = """
            <FixtureDefinition xmlns="http://www.qlcplus.org/FixtureDefinition">
              <Manufacturer>Generic</Manufacturer>
              <Model>Broken</Model>
              <Type>Other</Type>
              <Mode Name="Broken"><Channel Number="0">Missing</Channel></Mode>
            </FixtureDefinition>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        var exception = Assert.Throws<InvalidDataException>(() => new QlcFixtureImporter().Import(stream));

        Assert.Contains("unknown channel", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
