using LightingController.Output;

namespace LightingController.Fixtures.Tests;

public sealed class FixtureProfileStoreTests
{
    [Fact]
    public void Profile_RoundTripsVirtualDimmer()
    {
        var fixture = BuiltInFixtureLibrary.Create().Single(item => item.Model == "RGB Fixture");
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{FixtureProfileStore.Extension}");
        try
        {
            new FixtureProfileStore().Save(fixture, path);
            var loaded = new FixtureProfileStore().Load(path);
            Assert.Equal(3, loaded.Modes[0].Channels.Count);
            Assert.Equal(["Red", "Green", "Blue"], Assert.Single(loaded.VirtualControlChannels).TargetChannels);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void VirtualDimmer_ScalesOnlyTargets()
    {
        var values = new Dictionary<string, byte> { ["Red"] = 255, ["Green"] = 128, ["Pan"] = 200 };
        VirtualDimmerProcessor.Apply(128, values, ["Red", "Green"]);
        Assert.Equal((byte)128, values["Red"]);
        Assert.Equal((byte)64, values["Green"]);
        Assert.Equal((byte)200, values["Pan"]);
    }

    [Fact]
    public void ArtNetPacket_ContainsHeaderUniverseAndSlots()
    {
        var packet = DmxPacketBuilder.CreateArtDmx(0x1234, [1, 2, 3], 7);
        Assert.Equal("Art-Net\0", System.Text.Encoding.ASCII.GetString(packet, 0, 8));
        Assert.Equal((byte)0x34, packet[14]);
        Assert.Equal((byte)0x12, packet[15]);
        Assert.Equal([1, 2, 3], packet[18..21]);
    }

    [Fact]
    public void SacnPacket_ContainsUniverseAndDmxSlots()
    {
        var packet = DmxPacketBuilder.CreateSacn(300, [10, 20], 4, Guid.Empty, "Test");
        Assert.Equal((byte)1, packet[113]);
        Assert.Equal((byte)44, packet[114]);
        Assert.Equal((byte)0, packet[125]);
        Assert.Equal([10, 20], packet[126..]);
    }
}
