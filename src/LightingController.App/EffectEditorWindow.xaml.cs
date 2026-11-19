using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using LightingController.Core;

namespace LightingController.App;
public partial class EffectEditorWindow : Window
{
    private readonly ObservableCollection<ConsoleEffectLine> _lines;
    private bool _hasUnappliedChanges;
    public EffectEditorWindow(ConsoleEffectDefinition program, bool running)
    {
        InitializeComponent(); ScopeCombo.ItemsSource = Enum.GetValues<EffectScope>(); AttributeColumn.ItemsSource = Enum.GetValues<EffectAttribute>(); ModeColumn.ItemsSource = Enum.GetValues<EffectValueMode>();
        FormColumn.ItemsSource = Enum.GetValues<EffectWaveform>().Select(form => new { Value = form, Label = form == EffectWaveform.SmoothPulse ? "Smooth Pulse" : form.ToString() }).ToArray();
        ScopeCombo.SelectedItem = program.Scope; GroupsText.Text = program.Groups.ToString(); BlocksText.Text = program.Blocks.ToString(); WingsText.Text = program.Wings.ToString(); RunningCheck.IsChecked = running;
        _lines = new(program.Lines.Select(line => line.Copy())); LineGrid.ItemsSource = _lines;
        PhaseAcrossFixturePartsCheck.IsChecked = program.PhaseAcrossFixtureParts;
        ScopeCombo.SelectionChanged += (_, _) => OnEdited();
        GroupsText.TextChanged += (_, _) => OnEdited();
        BlocksText.TextChanged += (_, _) => OnEdited();
        WingsText.TextChanged += (_, _) => OnEdited();
        RunningCheck.Click += (_, _) => OnEdited();
        PhaseAcrossFixturePartsCheck.Click += (_, _) => OnEdited();
        LineGrid.SourceUpdated += (_, _) => OnEdited();
        AutoApplyCheck.Click += (_, _) =>
        {
            CancelButton.Content = AutoApplyCheck.IsChecked == true ? "CLOSE" : "CANCEL";
            AutoApply();
        };
    }
    public event Action<ConsoleEffectDefinition, bool>? ChangesApplied;
    public EffectEditorWindow(EffectTarget target, EffectDefinition definition, bool running) : this(FromLegacy(target, definition), running) { }
    public ConsoleEffectDefinition Program => new()
    {
        Scope = (EffectScope)(ScopeCombo.SelectedItem ?? EffectScope.Selective),
        Groups = Positive(GroupsText.Text),
        Blocks = Positive(BlocksText.Text),
        Wings = Positive(WingsText.Text),
        PhaseAcrossFixtureParts = PhaseAcrossFixturePartsCheck.IsChecked == true,
        Lines = _lines.Select(line => line.Copy()).ToList()
    };
    public bool Running => RunningCheck.IsChecked == true;
    public EffectTarget Target => Program.Lines.FirstOrDefault()?.Attribute switch { EffectAttribute.Pan or EffectAttribute.Tilt => EffectTarget.Position, EffectAttribute.Red or EffectAttribute.Green or EffectAttribute.Blue => EffectTarget.Colour, _ => EffectTarget.Intensity };
    public EffectDefinition Definition { get { var line = Program.Lines.FirstOrDefault() ?? new(); return new(line.Form, line.SpeedHz, (byte)Math.Clamp(line.Low, 0, 255), (byte)Math.Clamp(line.High, 0, 255), line.PhaseFrom); } }
    private void AddLine_Click(object sender, RoutedEventArgs e)
    {
        _lines.Add(new ConsoleEffectLine());
        OnEdited();
    }

    private void DeleteLine_Click(object sender, RoutedEventArgs e)
    {
        if (LineGrid.SelectedItem is not ConsoleEffectLine line || _lines.Count <= 1) return;
        _lines.Remove(line);
        OnEdited();
    }

    private void OnEdited()
    {
        _hasUnappliedChanges = true;
        AutoApply();
    }

    private void AutoApply()
    {
        if (_hasUnappliedChanges && AutoApplyCheck.IsChecked == true && ValidationError() is null)
            PublishChanges();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!LineGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !LineGrid.CommitEdit(DataGridEditingUnit.Row, true)) return;
        if (ValidationError() is { } error)
        {
            MessageBox.Show(this, error);
            return;
        }
        if (AutoApplyCheck.IsChecked != true || _hasUnappliedChanges) PublishChanges();
        DialogResult = true;
    }

    private void PublishChanges()
    {
        _hasUnappliedChanges = false;
        ChangesApplied?.Invoke(Program, Running);
    }

    private string? ValidationError()
    {
        if (_lines.Count == 0) return "An effect needs at least one line.";
        if (!IsPositive(GroupsText.Text) || !IsPositive(BlocksText.Text) || !IsPositive(WingsText.Text))
            return "Enter positive whole numbers for groups, blocks and wings.";
        if (_lines.Any(line => !double.IsFinite(line.Low) || !double.IsFinite(line.High) ||
            !double.IsFinite(line.SpeedHz) || !double.IsFinite(line.PhaseFrom) ||
            !double.IsFinite(line.PhaseTo) || !double.IsFinite(line.SineOffsetDegrees) || !double.IsFinite(line.Width)))
            return "Enter finite numbers for the effect line values.";
        return null;
    }

    private static bool IsPositive(string text) => int.TryParse(text, out var value) && value > 0;
    private static int Positive(string text) => int.TryParse(text, out var value) ? Math.Max(1, value) : 1;
    private static ConsoleEffectDefinition FromLegacy(EffectTarget target, EffectDefinition definition) => new() { Lines = [new ConsoleEffectLine { Attribute = target switch { EffectTarget.Position => EffectAttribute.Pan, EffectTarget.Colour => EffectAttribute.Red, _ => EffectAttribute.Dimmer }, Form = definition.Waveform, SpeedHz = definition.RateHz, Low = definition.Low, High = definition.High, PhaseFrom = definition.PhaseDegrees, PhaseTo = definition.PhaseDegrees + 360 }] };
}
