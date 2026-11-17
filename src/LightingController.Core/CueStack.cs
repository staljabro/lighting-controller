namespace LightingController.Core;

public sealed class CueStack
{
    private readonly List<Cue> _cues = [];

    public CueStack(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public string Name { get; set; }

    public bool Tracking { get; set; } = true;

    public IReadOnlyList<Cue> Cues => _cues;

    public void Add(Cue cue) => _cues.Add(cue ?? throw new ArgumentNullException(nameof(cue)));

    public void Insert(int index, Cue cue) =>
        _cues.Insert(index, cue ?? throw new ArgumentNullException(nameof(cue)));

    public void Replace(int index, Cue cue) =>
        _cues[index] = cue ?? throw new ArgumentNullException(nameof(cue));

    public void RemoveAt(int index) => _cues.RemoveAt(index);

    public void Update(int index, IEnumerable<KeyValuePair<ParameterAddress, CueValue>> programmerValues)
    {
        ArgumentNullException.ThrowIfNull(programmerValues);
        var cue = _cues[index];
        foreach (var (address, value) in programmerValues)
        {
            cue.SetValue(address, value.Value, value.Kind);
        }
    }

    public IReadOnlyDictionary<ParameterAddress, CueValue> Resolve(int index)
        => Resolve(index, null);

    public IReadOnlyDictionary<ParameterAddress, CueValue> Resolve(
        int index,
        Func<CuePresetReference, IReadOnlyDictionary<ParameterAddress, CueValue>>? presetResolver)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index >= _cues.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var result = new Dictionary<ParameterAddress, CueValue>();
        var first = Tracking ? 0 : index;
        for (var cueIndex = first; cueIndex <= index; cueIndex++)
        {
            if (presetResolver is not null)
                foreach (var reference in _cues[cueIndex].PresetReferences)
                    foreach (var (address, value) in presetResolver(reference)) result[address] = value;
            foreach (var (address, value) in _cues[cueIndex].Values)
            {
                result[address] = value;
            }
        }

        return result;
    }
}

