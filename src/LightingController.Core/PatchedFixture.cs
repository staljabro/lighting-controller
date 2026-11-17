namespace LightingController.Core;

public sealed record PatchedFixture
{
    public PatchedFixture(
        int id,
        string name,
        string manufacturer,
        string model,
        string mode,
        int universe,
        int address,
        int channelCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(manufacturer);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(universe);
        ArgumentOutOfRangeException.ThrowIfLessThan(address, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(address, 512);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channelCount);
        if (address + channelCount - 1 > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(channelCount), "Fixture channels exceed the DMX universe.");
        }

        Id = id;
        Name = name.Trim();
        Manufacturer = manufacturer.Trim();
        Model = model.Trim();
        Mode = mode.Trim();
        Universe = universe;
        Address = address;
        ChannelCount = channelCount;
    }

    public int Id { get; }
    public string Name { get; }
    public string Manufacturer { get; }
    public string Model { get; }
    public string Mode { get; }
    public int Universe { get; }
    public int Address { get; }
    public int ChannelCount { get; }
    public int LastAddress => Address + ChannelCount - 1;
}

