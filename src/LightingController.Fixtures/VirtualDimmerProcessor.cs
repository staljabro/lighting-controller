namespace LightingController.Fixtures;

public static class VirtualDimmerProcessor
{
    public static void Apply(byte master, IDictionary<string, byte> values, IEnumerable<string> targets)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(targets);

        foreach (var target in targets.Distinct(StringComparer.Ordinal))
        {
            if (values.TryGetValue(target, out var value))
                values[target] = (byte)Math.Round(value * master / 255d, MidpointRounding.AwayFromZero);
        }
    }
}
