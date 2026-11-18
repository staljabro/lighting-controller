using System.Collections.ObjectModel;
using System.Windows;
using LightingController.Core;

namespace LightingController.App;
public partial class EffectEditorWindow : Window
{
    private readonly ObservableCollection<ConsoleEffectLine> _lines;
    public EffectEditorWindow(ConsoleEffectDefinition program, bool running)
    {
        InitializeComponent(); ScopeCombo.ItemsSource = Enum.GetValues<EffectScope>(); AttributeColumn.ItemsSource = Enum.GetValues<EffectAttribute>(); FormColumn.ItemsSource = Enum.GetValues<EffectWaveform>(); ModeColumn.ItemsSource = Enum.GetValues<EffectValueMode>();
        ScopeCombo.SelectedItem = program.Scope; GroupsText.Text = program.Groups.ToString(); BlocksText.Text = program.Blocks.ToString(); WingsText.Text = program.Wings.ToString(); RunningCheck.IsChecked = running;
        _lines = new(program.Lines.Select(Clone)); LineGrid.ItemsSource = _lines;
    }
    public EffectEditorWindow(EffectTarget target, EffectDefinition definition, bool running) : this(FromLegacy(target, definition), running) { }
    public ConsoleEffectDefinition Program => new() { Scope = (EffectScope)(ScopeCombo.SelectedItem ?? EffectScope.Selective), Groups = Positive(GroupsText.Text), Blocks = Positive(BlocksText.Text), Wings = Positive(WingsText.Text), Lines = _lines.Select(Clone).ToList() };
    public bool Running => RunningCheck.IsChecked == true;
    public EffectTarget Target => Program.Lines.FirstOrDefault()?.Attribute switch { EffectAttribute.Pan or EffectAttribute.Tilt => EffectTarget.Position, EffectAttribute.Red or EffectAttribute.Green or EffectAttribute.Blue => EffectTarget.Colour, _ => EffectTarget.Intensity };
    public EffectDefinition Definition { get { var line = Program.Lines.FirstOrDefault() ?? new(); return new(line.Form, line.SpeedHz, (byte)Math.Clamp(line.Low, 0, 255), (byte)Math.Clamp(line.High, 0, 255), line.PhaseFrom); } }
    private void AddLine_Click(object sender, RoutedEventArgs e) => _lines.Add(new ConsoleEffectLine());
    private void DeleteLine_Click(object sender, RoutedEventArgs e) { if (LineGrid.SelectedItem is ConsoleEffectLine line && _lines.Count > 1) _lines.Remove(line); }
    private void Apply_Click(object sender, RoutedEventArgs e) { LineGrid.CommitEdit(); if (_lines.Count == 0) { MessageBox.Show(this, "An effect needs at least one line."); return; } DialogResult = true; }
    private static int Positive(string text) => int.TryParse(text, out var value) ? Math.Max(1, value) : 1;
    private static ConsoleEffectLine Clone(ConsoleEffectLine line) => new() { Attribute = line.Attribute, Form = line.Form, Mode = line.Mode, Low = line.Low, High = line.High, SpeedHz = line.SpeedHz, PhaseFrom = line.PhaseFrom, PhaseTo = line.PhaseTo, Width = line.Width };
    private static ConsoleEffectDefinition FromLegacy(EffectTarget target, EffectDefinition definition) => new() { Lines = [new ConsoleEffectLine { Attribute = target switch { EffectTarget.Position => EffectAttribute.Pan, EffectTarget.Colour => EffectAttribute.Red, _ => EffectAttribute.Dimmer }, Form = definition.Waveform, SpeedHz = definition.RateHz, Low = definition.Low, High = definition.High, PhaseFrom = definition.PhaseDegrees, PhaseTo = definition.PhaseDegrees + 360 }] };
}
