namespace LightingController.Core;

public enum GrandMasterMode { Reduce, Limit }
public enum ChaserRunOrder { Forward, Backward, Bounce }
public enum EffectWaveform { Sine, Triangle, Saw, Square }

public sealed class OutputMaster
{
    public byte Level { get; set; } = 255;
    public bool Blackout { get; set; }
    public GrandMasterMode Mode { get; set; } = GrandMasterMode.Reduce;

    public byte Apply(byte value, bool isIntensity)
    {
        if (Blackout) return 0;
        if (!isIntensity) return value;
        return Mode == GrandMasterMode.Limit
            ? Math.Min(value, Level)
            : (byte)Math.Round(value * Level / 255d, MidpointRounding.AwayFromZero);
    }
}

public sealed class FixtureSelectionGroup(string name, IEnumerable<int> fixtureIds)
{
    public string Name { get; set; } = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Group name is required.") : name.Trim();
    public HashSet<int> FixtureIds { get; } = fixtureIds.ToHashSet();
    public void Add(int fixtureId) { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fixtureId); FixtureIds.Add(fixtureId); }
    public void Remove(int fixtureId) => FixtureIds.Remove(fixtureId);
}

public static class FadeCalculator
{
    public static IReadOnlyDictionary<ParameterAddress, CueValue> Interpolate(
        IReadOnlyDictionary<ParameterAddress, CueValue> from,
        IReadOnlyDictionary<ParameterAddress, CueValue> to,
        double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        var result = new Dictionary<ParameterAddress, CueValue>();
        foreach (var address in from.Keys.Union(to.Keys))
        {
            var start = from.TryGetValue(address, out var startValue) ? startValue.Value : (byte)0;
            var target = to.TryGetValue(address, out var targetValue) ? targetValue : new CueValue(0, from[address].Kind);
            result[address] = new CueValue((byte)Math.Round(start + (target.Value - start) * progress, MidpointRounding.AwayFromZero), target.Kind);
        }
        return result;
    }
}

public sealed record ChaserStep(IReadOnlyDictionary<ParameterAddress, CueValue> Values, TimeSpan FadeIn, TimeSpan Hold);

public sealed class Chaser
{
    private readonly List<ChaserStep> _steps = [];
    public IReadOnlyList<ChaserStep> Steps => _steps;
    public ChaserRunOrder RunOrder { get; set; }
    public void Add(ChaserStep step) => _steps.Add(step ?? throw new ArgumentNullException(nameof(step)));
    public void Clear() => _steps.Clear();

    public ChaserStep? StepAt(TimeSpan elapsed)
    {
        if (_steps.Count == 0) return null;
        var order = RunOrder switch
        {
            ChaserRunOrder.Backward => _steps.AsEnumerable().Reverse().ToArray(),
            ChaserRunOrder.Bounce when _steps.Count > 1 => _steps.Concat(_steps.Skip(1).Take(_steps.Count - 2).Reverse()).ToArray(),
            _ => _steps.ToArray()
        };
        var total = order.Sum(step => Math.Max(1, (step.FadeIn + step.Hold).TotalMilliseconds));
        var position = ((elapsed.TotalMilliseconds % total) + total) % total;
        foreach (var step in order)
        {
            var duration = Math.Max(1, (step.FadeIn + step.Hold).TotalMilliseconds);
            if (position < duration) return step;
            position -= duration;
        }
        return order[^1];
    }
}

public sealed record EffectDefinition(EffectWaveform Waveform, double RateHz, byte Low, byte High, double PhaseDegrees = 0)
{
    public byte Sample(TimeSpan elapsed, double additionalPhaseDegrees = 0)
    {
        var phase = elapsed.TotalSeconds * RateHz + (PhaseDegrees + additionalPhaseDegrees) / 360d;
        phase -= Math.Floor(phase);
        var normalized = Waveform switch
        {
            EffectWaveform.Sine => (Math.Sin(phase * Math.PI * 2 - Math.PI / 2) + 1) / 2,
            EffectWaveform.Triangle => 1 - Math.Abs(phase * 2 - 1),
            EffectWaveform.Saw => phase,
            EffectWaveform.Square => phase < .5 ? 0 : 1,
            _ => 0
        };
        return (byte)Math.Round(Low + (High - Low) * normalized, MidpointRounding.AwayFromZero);
    }
}
