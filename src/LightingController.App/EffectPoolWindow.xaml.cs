using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using LightingController.Core;
using LightingController.Fixtures;

namespace LightingController.App;

public partial class EffectPoolWindow : Window
{
    private readonly ObservableCollection<StoredEffectRow> _effects; private readonly Func<StoredEffectRow> _current; private readonly Action<int, bool> _setActive; private readonly Action<StoredEffectRow> _select;
    private readonly Func<int, bool> _isActive;
    public EffectPoolWindow(ObservableCollection<StoredEffectRow> effects, Func<StoredEffectRow> current, Action<int, bool> setActive, Action<StoredEffectRow> select, Func<int, bool> isActive)
    { _effects = effects; _current = current; _setActive = setActive; _select = select; _isActive = isActive; InitializeComponent(); EffectGrid.ItemsSource = effects; }
    private void Store_Click(object sender, RoutedEventArgs e) { var row = _current(); row.Slot = _effects.Select(item => item.Slot).DefaultIfEmpty(0).Max() + 1; row.Name = $"Effect {row.Slot}"; _effects.Add(row); EffectGrid.SelectedItem = row; }
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (EffectGrid.SelectedItem is not StoredEffectRow row) return;
        var dialog = new EffectEditorWindow(row.Program, _isActive(row.Slot)) { Owner = this };
        dialog.ChangesApplied += (program, running) =>
        {
            row.Program = program;
            if (running != _isActive(row.Slot)) _setActive(row.Slot, running);
        };
        dialog.ShowDialog();
    }
    private void Apply_Click(object sender, RoutedEventArgs e) { if (EffectGrid.SelectedItem is StoredEffectRow row) _setActive(row.Slot, true); }
    private void Release_Click(object sender, RoutedEventArgs e) { if (EffectGrid.SelectedItem is StoredEffectRow row) _setActive(row.Slot, false); }
    private void Delete_Click(object sender, RoutedEventArgs e) { if (EffectGrid.SelectedItem is StoredEffectRow row) { _setActive(row.Slot, false); _effects.Remove(row); } }
    private void EffectGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { if (EffectGrid.SelectedItem is not StoredEffectRow row) return; if (row.Program.Scope == EffectScope.Template) _setActive(row.Slot, true); else _select(row); }
    private void EffectGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (EffectGrid.SelectedItem is StoredEffectRow row) _setActive(row.Slot, true); }
}

public sealed class StoredEffectRow : INotifyPropertyChanged
{
    private ConsoleEffectDefinition _program = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public int Slot { get; set; }
    public string Name { get; set; } = "";
    public ConsoleEffectDefinition Program
    {
        get => _program;
        set
        {
            _program = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }
    public EffectTarget Target { get => Program.Lines.FirstOrDefault()?.Attribute switch { EffectAttribute.Pan or EffectAttribute.Tilt => EffectTarget.Position, EffectAttribute.Red or EffectAttribute.Green or EffectAttribute.Blue => EffectTarget.Colour, _ => EffectTarget.Intensity }; set { var line = Program.Lines.FirstOrDefault() ?? new ConsoleEffectLine(); line.Attribute = value switch { EffectTarget.Position => EffectAttribute.Pan, EffectTarget.Colour => EffectAttribute.Red, _ => EffectAttribute.Dimmer }; if (Program.Lines.Count == 0) Program.Lines.Add(line); } }
    public EffectDefinition Definition { get { var line = Program.Lines.FirstOrDefault() ?? new(); return new(line.Form, line.SpeedHz, (byte)Math.Clamp(line.Low, 0, 255), (byte)Math.Clamp(line.High, 0, 255), line.PhaseFrom); } set { var line = Program.Lines.FirstOrDefault() ?? new ConsoleEffectLine(); line.Form = value.Waveform; line.SpeedHz = value.RateHz; line.Low = value.Low; line.High = value.High; line.PhaseFrom = value.PhaseDegrees; line.PhaseTo = value.PhaseDegrees + 360; if (Program.Lines.Count == 0) Program.Lines.Add(line); } }
    public IReadOnlyList<FixtureSelection> Selections { get; set; } = [];
    public string SelectionDescription => string.Join(", ", Selections.Select(item => item.PartName is null ? item.FixtureId.ToString() : $"{item.FixtureId}.{item.PartName}"));
    public string ScopeShort => Program.Scope == EffectScope.Selective ? "S" : "T";
    public string Waveforms => string.Join(", ", Program.Lines.Select(line => line.Form).Distinct()
        .Select(form => form == EffectWaveform.SmoothPulse ? "Smooth Pulse" : form.ToString()));
    public string LineSummary => $"{Program.Lines.Count} line{(Program.Lines.Count == 1 ? "" : "s")} · {string.Join("/", Program.Lines.Select(line => line.Attribute).Distinct())}";
}
