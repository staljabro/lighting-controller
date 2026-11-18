namespace LightingController.Core;

public enum CueTriggerType { Go, Follow, Wait }

public sealed class Cue
{
    private readonly Dictionary<ParameterAddress, CueValue> _values = [];
    private readonly List<CuePresetReference> _presetReferences = [];
    private readonly List<int> _effectSlots = [];

    public Cue(string number, string name = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        Number = number.Trim();
        Name = name;
    }

    public string Number { get; set; }

    public string Name { get; set; }

    public string Notes { get; set; } = "";

    public TimeSpan FadeIn { get; set; }

    public TimeSpan FadeOut { get; set; }

    public TimeSpan Delay { get; set; }

    public TimeSpan? Follow { get; set; }

    public CueTriggerType TriggerType { get; set; } = CueTriggerType.Go;

    public IReadOnlyDictionary<ParameterAddress, CueValue> Values => _values;
    public IReadOnlyList<CuePresetReference> PresetReferences => _presetReferences;
    public IReadOnlyList<int> EffectSlots => _effectSlots;

    public void SetValue(ParameterAddress address, byte value, ParameterKind kind) =>
        _values[address] = new CueValue(value, kind);

    public bool RemoveValue(ParameterAddress address) => _values.Remove(address);

    public bool TryGetValue(ParameterAddress address, out CueValue value) =>
        _values.TryGetValue(address, out value);

    public void AddPresetReference(CuePresetReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        _presetReferences.RemoveAll(item => item.Type == reference.Type && item.Slot == reference.Slot);
        _presetReferences.Add(reference);
    }

    public void AddEffectReference(int slot) { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slot); if (!_effectSlots.Contains(slot)) _effectSlots.Add(slot); }

    public Cue Copy()
    {
        var copy = new Cue(Number, Name)
        {
            Notes = Notes,
            FadeIn = FadeIn,
            FadeOut = FadeOut,
            Delay = Delay,
            Follow = Follow,
            TriggerType = TriggerType
        };

        foreach (var (address, value) in _values)
        {
            copy._values[address] = value;
        }
        foreach (var reference in _presetReferences) copy._presetReferences.Add(reference);
        foreach (var slot in _effectSlots) copy._effectSlots.Add(slot);

        return copy;
    }
}

