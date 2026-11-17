namespace LightingController.Core;

public readonly record struct PlaybackValue(
    ParameterAddress Address,
    byte Value,
    ParameterKind Kind,
    long Sequence);

public static class DmxMerger
{
    public static IReadOnlyDictionary<ParameterAddress, byte> Merge(IEnumerable<PlaybackValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return values
            .GroupBy(value => value.Address)
            .ToDictionary(
                group => group.Key,
                group => group.First().Kind == ParameterKind.Intensity
                    ? group.Max(value => value.Value)
                    : group.MaxBy(value => value.Sequence).Value);
    }
}
