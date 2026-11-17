using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LightingController.Core;
using LightingController.Fixtures;
using LightingController.Output;
using LightingController.Persistence;
using Microsoft.Win32;
using System.Windows.Threading;

namespace LightingController.App;

public partial class MainWindow : Window
{
    private readonly List<CueStack> _cueLists = [new CueStack("Cue List 1")];
    private int _selectedCueListIndex;
    private CueStack _cueStack => _cueLists[_selectedCueListIndex];
    private readonly FixturePatch _fixturePatch = new();
    private readonly Dictionary<ParameterAddress, CueValue> _programmer = [];
    private readonly List<CuePresetReference> _activePresetReferences = [];
    private readonly HashSet<ParameterAddress> _presetOwnedAddresses = [];
    private readonly Dictionary<ParameterAddress, CueValue> _playbackState = [];
    private IReadOnlyDictionary<ParameterAddress, CueValue>? _fadeFrom;
    private IReadOnlyDictionary<ParameterAddress, CueValue>? _fadeTo;
    private DateTime _fadeStartedUtc;
    private TimeSpan _fadeDuration;
    private DateTime? _pausedAtUtc;
    private readonly FixtureProgrammingEngine _programmingEngine = new();
    private readonly HashSet<int> _selectedFixtures = [];
    private readonly List<FixtureSelection> _selectedParts = [];
    private readonly Dictionary<int, FixtureMode> _patchedModes = [];
    private readonly Dictionary<int, FixtureDefinition> _patchedDefinitions = [];
    private readonly DmxNetworkOutput _networkOutput = new();
    private readonly OutputMaster _outputMaster = new();
    private readonly DispatcherTimer _outputTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private bool _outputSending;
    private bool _storePending;
    private bool _effectRunning;
    private DateTime _effectStartedUtc;
    private readonly Chaser _liveChaser = new();
    private bool _chaserRunning;
    private DateTime _chaserStartedUtc;
    private readonly List<FixtureSelectionGroup> _fixtureGroups = [];
    private string _activePresetType = "Intensity";
    private bool _keyboardShortcutsEnabled = true;
    private DispatcherTimer? _presetHoldTimer;
    private ColourPresetRow? _heldPreset;
    private bool _renameHoldTriggered;
    private int _playbackIndex = -1;
    private bool _paused;

    public MainWindow()
    {
        InitializeComponent();
        Cues = [];
        CueLists = [new CueListRow(1, "Cue List 1")];
        Fixtures = [];
        Library = [];
        VisibleLibrary = [];
        ColourPresets = [];
        VisiblePresets = [];
        foreach (var type in new[] { "Intensity", "Position", "Gobo", "Colour", "Beam", "Focus", "Control" })
            for (var slot = 1; slot <= 28; slot++) ColourPresets.Add(new ColourPresetRow(type, slot));
        DataContext = this;
        OutputProtocolCombo.ItemsSource = Enum.GetValues<DmxNetworkProtocol>();
        OutputProtocolCombo.SelectedItem = DmxNetworkProtocol.ArtNet;
        LoadFixtureLibrary();
        ShowPresetType("Intensity");
        CueListSelector.SelectedIndex = 0;
        _outputTimer.Tick += OutputTimer_Tick;
        _outputTimer.Start();
        Closed += (_, _) => { _outputTimer.Stop(); _networkOutput.Dispose(); };
    }

    public ObservableCollection<CueRow> Cues { get; }
    public ObservableCollection<CueListRow> CueLists { get; }

    public ObservableCollection<FixtureRow> Fixtures { get; }
    public ObservableCollection<FixtureLibraryRow> Library { get; }
    public ObservableCollection<FixtureLibraryRow> VisibleLibrary { get; }
    public ObservableCollection<ColourPresetRow> ColourPresets { get; }
    public ObservableCollection<ColourPresetRow> VisiblePresets { get; }
    private IEnumerable<FixtureSelection> CurrentSelections => _selectedParts.Count > 0 ? _selectedParts : _selectedFixtures.Select(id => new FixtureSelection(id));

    private void FixtureSelection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string fixtureList, Content: object label })
        {
            return;
        }

        _selectedFixtures.Clear();
        foreach (var fixture in fixtureList.Split(','))
        {
            _selectedFixtures.Add(int.Parse(fixture, CultureInfo.InvariantCulture));
        }

        ProgrammerTitle.Text = $"PROGRAMMER — {label}";
        SetStatus($"Selected {_selectedFixtures.Count} fixture(s)");
    }

    private void ProgrammerValue_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFixtures.Count == 0)
        {
            SetStatus("Select fixtures before setting attributes");
            return;
        }

        if (sender is not Button { Tag: string command })
        {
            return;
        }

        var parts = command.Split(':');
        var kind = Enum.Parse<ParameterKind>(parts[0]);
        var value = byte.Parse(parts[1], CultureInfo.InvariantCulture);
        if (kind == ParameterKind.Intensity)
            _programmingEngine.SetIntensity(_programmer, CurrentSelections, value);
        else if (kind == ParameterKind.Colour)
        {
            var colour = value switch
            {
                0 => ((byte)255, (byte)255, (byte)255),
                32 => ((byte)255, (byte)0, (byte)0),
                96 => ((byte)0, (byte)255, (byte)0),
                160 => ((byte)0, (byte)0, (byte)255),
                _ => ((byte)255, (byte)0, (byte)255)
            };
            _programmingEngine.SetColour(_programmer, CurrentSelections, colour.Item1, colour.Item2, colour.Item3);
        }
        else if (kind == ParameterKind.Position)
            _programmingEngine.SetPosition(_programmer, CurrentSelections, value, value);

        ReleasePresetOwnership(kind);

        SetStatus($"Programmer: {kind} = {value} ({_programmer.Count} values)");
        _ = SendLiveOutputAsync();
    }

    private void PositionPad_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_selectedFixtures.Count == 0)
        {
            SetStatus("Select moving fixtures before using pan/tilt");
            return;
        }

        var point = e.GetPosition(PositionPad);
        var width = Math.Max(1, PositionPad.ActualWidth);
        var height = Math.Max(1, PositionPad.ActualHeight);
        var pan = (byte)Math.Clamp(Math.Round(point.X / width * 255), 0, 255);
        var tilt = (byte)Math.Clamp(Math.Round(point.Y / height * 255), 0, 255);

        Canvas.SetLeft(PositionMarker, Math.Clamp(point.X - 12, 0, width - 24));
        Canvas.SetTop(PositionMarker, Math.Clamp(point.Y - 12, 0, height - 24));
        _programmingEngine.SetPosition(_programmer, CurrentSelections, pan, tilt);
        ReleasePresetOwnership(ParameterKind.Position);

        SetStatus($"Pan {pan} / Tilt {tilt}");
        _ = SendLiveOutputAsync();
    }

    private void RecordCue_Click(object sender, RoutedEventArgs e)
    {
        if (!_storePending) { SetStatus("Press STORE, then choose CUE or a preset slot"); return; }
        if (_programmer.Count == 0)
        {
            SetStatus("Programmer is empty — set fixture attributes first");
            return;
        }

        var number = (_cueStack.Cues.Count + 1).ToString(CultureInfo.InvariantCulture);
        var cue = new Cue(number, $"Cue {number}") { FadeIn = TimeSpan.FromSeconds(2) };
        CopyProgrammerTo(cue);
        foreach (var reference in _activePresetReferences) cue.AddPresetReference(reference);
        _cueStack.Add(cue);
        Cues.Add(CueRow.From(cue));
        CueGrid.SelectedIndex = Cues.Count - 1;
        SetStatus($"Recorded cue {number} with {_programmer.Count} explicit values");
        EchoCommand($"STORE CUE {number}");
        CompleteStore();
    }

    private void UpdateCue_Click(object sender, RoutedEventArgs e)
    {
        var index = CueGrid.SelectedIndex;
        if (index < 0 || _programmer.Count == 0)
        {
            SetStatus("Select a cue and set programmer values before updating");
            return;
        }

        _cueStack.Update(index, HardProgrammerValues());
        foreach (var reference in _activePresetReferences) _cueStack.Cues[index].AddPresetReference(reference);
        Cues[index].UpdateFrom(_cueStack.Cues[index]);
        CueGrid.Items.Refresh();
        SetStatus($"Updated cue {_cueStack.Cues[index].Number}");
        EchoCommand($"UPDATE CUE {_cueStack.Cues[index].Number}");
    }

    private void EditCue_Click(object sender, RoutedEventArgs e)
    {
        var index = CueGrid.SelectedIndex;
        if (index < 0 || CueGrid.SelectedItem is not CueRow row)
        {
            SetStatus("Select a cue to apply edited fields");
            return;
        }

        CueGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var edited = _cueStack.Cues[index].Copy();
        edited.Number = string.IsNullOrWhiteSpace(row.Number) ? edited.Number : row.Number.Trim();
        edited.Name = row.Name;
        if (double.TryParse(row.Fade, CultureInfo.InvariantCulture, out var seconds))
        {
            edited.FadeIn = TimeSpan.FromSeconds(Math.Max(0, seconds));
        }

        _cueStack.Replace(index, edited);
        row.UpdateFrom(edited);
        CueGrid.Items.Refresh();
        SetStatus($"Applied edits to cue {edited.Number}");
    }

    private void DeleteCue_Click(object sender, RoutedEventArgs e)
    {
        var index = CueGrid.SelectedIndex;
        if (index < 0)
        {
            SetStatus("Select a cue to delete");
            return;
        }

        var number = _cueStack.Cues[index].Number;
        _cueStack.RemoveAt(index);
        Cues.RemoveAt(index);
        _playbackIndex = Math.Min(_playbackIndex, Cues.Count - 1);
        SetStatus($"Deleted cue {number}");
    }

    private void Go_Click(object sender, RoutedEventArgs e)
    {
        if (_cueStack.Cues.Count == 0)
        {
            SetStatus("No cues recorded");
            return;
        }

        _paused = false;
        _playbackIndex = Math.Min(_playbackIndex + 1, _cueStack.Cues.Count - 1);
        EchoCommand("GO");
        ShowPlaybackCue();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_cueStack.Cues.Count == 0)
        {
            SetStatus("No cues recorded");
            return;
        }

        _playbackIndex = Math.Max(0, _playbackIndex - 1);
        ShowPlaybackCue();
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (!_paused) { UpdatePlaybackFade(); _pausedAtUtc = DateTime.UtcNow; }
        else if (_pausedAtUtc is not null) { _fadeStartedUtc += DateTime.UtcNow - _pausedAtUtc.Value; _pausedAtUtc = null; }
        _paused = !_paused;
        SetStatus(_paused ? "Playback paused" : "Playback resumed");
    }

    private void Release_Click(object sender, RoutedEventArgs e)
    {
        _playbackIndex = -1;
        _paused = false;
        _playbackState.Clear();
        _fadeFrom = null; _fadeTo = null; _pausedAtUtc = null;
        CueGrid.SelectedIndex = -1;
        SetStatus("Cue stack released");
    }

    private void ClearProgrammer_Click(object sender, RoutedEventArgs e)
    {
        _programmer.Clear();
        _activePresetReferences.Clear();
        _presetOwnedAddresses.Clear();
        SetStatus("Programmer cleared");
    }

    private void CueGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CueGrid.SelectedItem is CueRow row)
        {
            SetStatus($"Selected cue {row.Number}: {row.Name}");
        }
    }

    private void AddCueList_Click(object sender, RoutedEventArgs e)
    {
        var number = _cueLists.Count + 1;
        _cueLists.Add(new CueStack($"Cue List {number}")); CueLists.Add(new(number, $"Cue List {number}"));
        SelectCueList(number); EchoCommand($"STORE CUELIST {number}");
    }

    private void CueListSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CueListSelector.SelectedItem is CueListRow row) SelectCueList(row.Number);
    }

    private void SelectCueList(int number)
    {
        if (number < 1 || number > _cueLists.Count) throw new InvalidOperationException($"Cue list {number} does not exist.");
        _selectedCueListIndex = number - 1; _playbackIndex = -1; _playbackState.Clear(); Cues.Clear();
        foreach (var cue in _cueStack.Cues) Cues.Add(CueRow.From(cue));
        if (CueListSelector.SelectedIndex != _selectedCueListIndex) CueListSelector.SelectedIndex = _selectedCueListIndex;
        CueListTitle.Text = $"CUE LIST {number} — {_cueStack.Name.ToUpperInvariant()} (TRACKING)";
        SetStatus($"Selected cue list {number}");
    }

    private void ImportFixture_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add fixture profile to library",
            Filter = $"Lighting Controller fixture profiles (*{FixtureProfileStore.Extension})|*{FixtureProfileStore.Extension}"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var definition = new FixtureProfileStore().Load(dialog.FileName);
            var libraryPath = GetFixtureLibraryPath();
            Directory.CreateDirectory(libraryPath);
            var destination = Path.Combine(libraryPath, Path.GetFileName(dialog.FileName));
            File.Copy(dialog.FileName, destination, true);
            AddLibraryFixture(definition);
            ApplyLibraryFilter();
            SetStatus($"Added {definition.Manufacturer} {definition.Model} to fixture library");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or System.Xml.XmlException or InvalidOperationException or ArgumentException)
        {
            MessageBox.Show(this, exception.Message, "Fixture profile failed", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Fixture profile failed");
        }
    }

    private void OpenFixtureEditor_Click(object sender, RoutedEventArgs e)
    {
        var editor = new FixtureEditorWindow { Owner = this };
        editor.ShowDialog();
        if (editor.SavedFixture is not null) { AddLibraryFixture(editor.SavedFixture); ApplyLibraryFilter(); }
        SetStatus("Fixture editor closed");
    }

    private void EditFixtureProfile_Click(object sender, RoutedEventArgs e)
    {
        if (FixtureLibraryList.SelectedItem is not FixtureLibraryRow selected) { SetStatus("Select a fixture profile to edit"); return; }
        var editor = new FixtureEditorWindow(selected.Definition) { Owner = this };
        editor.ShowDialog();
        if (editor.SavedFixture is null) return;
        Library.Remove(selected); VisibleLibrary.Remove(selected); AddLibraryFixture(editor.SavedFixture); ApplyLibraryFilter();
        SetStatus($"Updated {editor.SavedFixture.Manufacturer} {editor.SavedFixture.Model}");
    }

    private void FixtureGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FixtureGrid.SelectedItem is not FixtureRow fixture)
        {
            return;
        }

        _selectedFixtures.Clear();
        _selectedParts.Clear();
        _selectedFixtures.Add(fixture.Id);
        FixturePartList.ItemsSource = _programmingEngine.GetParts(fixture.Id).Select(part => new FixturePartRow(fixture.Id, part)).ToArray();
        ProgrammerTitle.Text = $"PROGRAMMER — {fixture.Name.ToUpperInvariant()}";
        SetStatus($"Selected fixture {fixture.Id}: {fixture.Name}");
    }

    private void FixturePartList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedParts.Clear();
        foreach (FixturePartRow part in FixturePartList.SelectedItems) _selectedParts.Add(new(part.FixtureId, part.PartName));
        if (_selectedParts.Count > 0) SetStatus($"Selected {_selectedParts.Count} fixture part(s)");
    }

    private void ApplyRgb_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadRgb(out var red, out var green, out var blue)) return;
        _programmingEngine.SetColour(_programmer, CurrentSelections, red, green, blue);
        ReleasePresetOwnership(ParameterKind.Colour);
        SetStatus($"Programmer colour: R{red} G{green} B{blue}");
    }

    private void DimmerSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _selectedFixtures.Count == 0) return;
        var value = (byte)Math.Round(e.NewValue);
        _programmingEngine.SetIntensity(_programmer, CurrentSelections, value);
        ReleasePresetOwnership(ParameterKind.Intensity);
        SetStatus($"Live dimmer: {value}");
        EchoCommand($"INTENSITY {value}");
        _ = SendLiveOutputAsync();
    }

    private void ColourSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _selectedFixtures.Count == 0 || RedSlider is null || GreenSlider is null || BlueSlider is null) return;
        var red = (byte)Math.Round(RedSlider.Value); var green = (byte)Math.Round(GreenSlider.Value); var blue = (byte)Math.Round(BlueSlider.Value);
        _programmingEngine.SetColour(_programmer, CurrentSelections, red, green, blue);
        ReleasePresetOwnership(ParameterKind.Colour);
        SetStatus($"Live colour: R{red} G{green} B{blue}");
        EchoCommand($"COLOUR {red} {green} {blue}");
        _ = SendLiveOutputAsync();
    }

    private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _selectedFixtures.Count == 0 || PanSlider is null || TiltSlider is null) return;
        var pan = (byte)Math.Round(PanSlider.Value); var tilt = (byte)Math.Round(TiltSlider.Value);
        _programmingEngine.SetPosition(_programmer, CurrentSelections, pan, tilt);
        ReleasePresetOwnership(ParameterKind.Position);
        SetStatus($"Live position: Pan {pan} / Tilt {tilt}");
        EchoCommand($"POSITION {pan} {tilt}");
        _ = SendLiveOutputAsync();
    }

    private void BeginStore_Click(object sender, RoutedEventArgs e)
    {
        _storePending = !_storePending;
        StoreButton.Content = _storePending ? "STORE: SELECT TARGET" : "STORE";
        EchoCommand(_storePending ? "STORE" : "ESCAPE");
        SetStatus(_storePending ? "Store active — click a preset slot or CUE" : "Store cancelled");
    }

    private void ColourPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ColourPresetRow preset }) return;
        if (_storePending)
        {
            var values = CurrentPresetValues(preset.Type);
            if (values.Count == 0) { SetStatus($"Program {preset.Type.ToLowerInvariant()} values before storing this preset"); return; }
            foreach (var item in values) preset.Values[item.Key] = item.Value;
            preset.Name = $"{preset.Type} {preset.Slot}";
            if (preset.Type == "Colour" && TryReadRgb(out var red, out var green, out var blue)) preset.Swatch = $"#{red:X2}{green:X2}{blue:X2}";
            ShowPresetType(preset.Type); ColourPresetList.Items.Refresh();
            SetStatus($"Stored fixture-specific values in {preset.Name}");
            EchoCommand($"STORE PRESET {preset.Type.ToUpperInvariant()} {preset.Slot}");
            CompleteStore();
            return;
        }
        if (preset.Values.Count == 0) { SetStatus($"Preset slot {preset.Slot} is empty"); return; }
        foreach (var item in _programmingEngine.ResolvePresetValues(preset.Values, preset.Scope, CurrentSelections)) { _programmer[item.Key] = item.Value; _presetOwnedAddresses.Add(item.Key); }
        _activePresetReferences.RemoveAll(item => item.Type == preset.Type);
        _activePresetReferences.Add(new(preset.Type, preset.Slot, preset.Scope, _selectedFixtures.ToArray()));
        EchoCommand($"PRESET {preset.Type.ToUpperInvariant()} {preset.Slot}");
        SetStatus($"Applied {preset.Name}");
    }

    private void CompleteStore() { _storePending = false; StoreButton.Content = "STORE"; }

    private Dictionary<ParameterAddress, CueValue> CurrentColourValues() => _programmer
        .Where(item => item.Value.Kind == ParameterKind.Colour && _programmingEngine.BelongsToSelection(item.Key, CurrentSelections))
        .ToDictionary(item => item.Key, item => item.Value);

    private Dictionary<ParameterAddress, CueValue> CurrentPresetValues(string type)
    {
        var kind = type switch { "Intensity" => ParameterKind.Intensity, "Position" => ParameterKind.Position, "Colour" => ParameterKind.Colour, "Beam" or "Focus" or "Gobo" => ParameterKind.Beam, _ => ParameterKind.Other };
        return _programmer.Where(item => item.Value.Kind == kind && _programmingEngine.BelongsToSelection(item.Key, CurrentSelections)).ToDictionary(item => item.Key, item => item.Value);
    }

    private bool TryReadRgb(out byte red, out byte green, out byte blue)
    {
        red = (byte)Math.Round(RedSlider.Value); green = (byte)Math.Round(GreenSlider.Value); blue = (byte)Math.Round(BlueSlider.Value); return true;
    }

    private void RemoveFixture_Click(object sender, RoutedEventArgs e)
    {
        if (FixtureGrid.SelectedItem is not FixtureRow fixture)
        {
            SetStatus("Select a patched fixture to remove");
            return;
        }

        _fixturePatch.Remove(fixture.Id);
        Fixtures.Remove(fixture);
        _selectedFixtures.Remove(fixture.Id);
        _selectedParts.RemoveAll(part => part.FixtureId == fixture.Id);
        FixturePartList.ItemsSource = null;
        _patchedModes.Remove(fixture.Id);
        _patchedDefinitions.Remove(fixture.Id);
        _programmingEngine.Remove(fixture.Id);
        SetStatus($"Removed fixture {fixture.Id}: {fixture.Name}");
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string workspace } button) return;
        PatchWorkspace.Visibility = workspace == "Patch" ? Visibility.Visible : Visibility.Collapsed;
        ProgrammerWorkspace.Visibility = workspace == "Programmer" ? Visibility.Visible : Visibility.Collapsed;
        CueWorkspace.Visibility = workspace == "Cues" ? Visibility.Visible : Visibility.Collapsed;
        SetStatus($"{button.Content} workspace selected");
    }

    private void FeatureGroup_Click(object sender, RoutedEventArgs e)
    {
        var group = (sender as Button)?.Tag as string ?? "Dimmer";
        DimmerControls.Visibility = group == "Dimmer" ? Visibility.Visible : Visibility.Collapsed;
        PositionControls.Visibility = group == "Position" ? Visibility.Visible : Visibility.Collapsed;
        ColorControls.Visibility = group == "Color" ? Visibility.Visible : Visibility.Collapsed;
        ControlControls.Visibility = group == "Control" ? Visibility.Visible : Visibility.Collapsed;
        UnsupportedFeatureText.Visibility = group is not ("Dimmer" or "Position" or "Color" or "Control") ? Visibility.Visible : Visibility.Collapsed;
        SetStatus($"{group} feature group selected");
        ShowPresetType(group switch { "Dimmer" => "Intensity", "Color" => "Colour", _ => group });
        EchoCommand(group switch { "Dimmer" => "INTENSITY", "Color" => "COLOUR", _ => group.ToUpperInvariant() });
    }

    private void ShowPresetType(string type)
    {
        _activePresetType = type;
        var latestStored = ColourPresets.Where(item => item.Type == type && item.Values.Count > 0).Select(item => item.Slot).DefaultIfEmpty(0).Max();
        var lastVisible = Math.Min(28, latestStored + 1);
        VisiblePresets.Clear();
        foreach (var preset in ColourPresets.Where(item => item.Type == type && item.Slot <= lastVisible)) VisiblePresets.Add(preset);
        if (PresetPoolTitle is not null) PresetPoolTitle.Text = $"{type.ToUpperInvariant()} PRESET POOL";
    }

    private void ProgrammerFixtureList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProgrammerFixtureList.SelectedItem is not FixtureRow fixture) return;
        _selectedFixtures.Clear(); _selectedParts.Clear(); _selectedFixtures.Add(fixture.Id);
        FixturePartList.ItemsSource = _programmingEngine.GetParts(fixture.Id).Select(part => new FixturePartRow(fixture.Id, part)).ToArray();
        ProgrammerTitle.Text = $"PROGRAMMER — {fixture.Name.ToUpperInvariant()}";
        SetStatus($"Selected fixture {fixture.Id}: {fixture.Name}");
    }

    private void ShowPlaybackCue()
    {
        CueGrid.SelectedIndex = _playbackIndex;
        CueGrid.ScrollIntoView(CueGrid.SelectedItem);
        var cue = _cueStack.Cues[_playbackIndex];
        var resolved = _cueStack.Resolve(_playbackIndex, ResolvePresetReference);
        UpdatePlaybackFade();
        _fadeFrom = new Dictionary<ParameterAddress, CueValue>(_playbackState);
        _fadeTo = resolved;
        _fadeStartedUtc = DateTime.UtcNow;
        _fadeDuration = cue.FadeIn;
        if (_fadeDuration <= TimeSpan.Zero) UpdatePlaybackFade(true);
        SetStatus($"GO cue {cue.Number}: {cue.Name} — {resolved.Count} tracked values");
    }

    private void CopyProgrammerTo(Cue cue)
    {
        foreach (var (address, value) in HardProgrammerValues())
        {
            cue.SetValue(address, value.Value, value.Kind);
        }
    }

    private IEnumerable<KeyValuePair<ParameterAddress, CueValue>> HardProgrammerValues() => _programmer.Where(item => !_presetOwnedAddresses.Contains(item.Key));

    private void ReleasePresetOwnership(ParameterKind kind)
    {
        foreach (var address in _programmer.Where(item => item.Value.Kind == kind && _programmingEngine.BelongsToSelection(item.Key, CurrentSelections)).Select(item => item.Key).ToArray()) _presetOwnedAddresses.Remove(address);
        var type = kind switch { ParameterKind.Intensity => "Intensity", ParameterKind.Position => "Position", ParameterKind.Colour => "Colour", _ => "" };
        _activePresetReferences.RemoveAll(reference => reference.Type == type);
    }

    private int NextAvailableAddress(int universe)
    {
        var lastAddress = _fixturePatch.Fixtures
            .Where(fixture => fixture.Universe == universe)
            .Select(fixture => fixture.LastAddress)
            .DefaultIfEmpty(0)
            .Max();
        return Math.Min(512, lastAddress + 1);
    }

    private void SetStatus(string text) => StatusText.Text = text.ToUpperInvariant();

    private void LoadFixtureLibrary()
    {
        var path = GetFixtureLibraryPath();
        Directory.CreateDirectory(path);
        foreach (var file in Directory.EnumerateFiles(path, $"*{FixtureProfileStore.Extension}"))
        {
            try { AddLibraryFixture(new FixtureProfileStore().Load(file)); }
            catch (Exception) { }
        }
        foreach (var fixture in BuiltInFixtureLibrary.Create()) AddLibraryFixture(fixture);
        ApplyLibraryFilter();
    }

    private static string GetFixtureLibraryPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightingController", "Fixtures");

    private void AddLibraryFixture(FixtureDefinition fixture)
    {
        if (Library.Any(item => item.Definition.Manufacturer == fixture.Manufacturer && item.Definition.Model == fixture.Model)) return;
        Library.Add(new FixtureLibraryRow(fixture));
    }

    private void ApplyLibraryFilter()
    {
        var query = LibrarySearchText?.Text?.Trim() ?? "";
        VisibleLibrary.Clear();
        foreach (var item in Library.Where(item => query.Length == 0 || item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Details.Contains(query, StringComparison.OrdinalIgnoreCase)))
            VisibleLibrary.Add(item);
    }

    private void LibrarySearchText_TextChanged(object sender, TextChangedEventArgs e) => ApplyLibraryFilter();

    private void FixtureLibraryList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && FixtureLibraryList.SelectedItem is FixtureLibraryRow row)
            DragDrop.DoDragDrop(FixtureLibraryList, row, DragDropEffects.Copy);
    }

    private void FixtureGrid_DragOver(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(typeof(FixtureLibraryRow)) ? DragDropEffects.Copy : DragDropEffects.None;

    private void FixtureGrid_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(FixtureLibraryRow)) is FixtureLibraryRow row) PatchFixture(row.Definition);
    }

    private void PatchFixture(FixtureDefinition definition)
    {
        var suggestedId = _fixturePatch.Fixtures.Count == 0 ? 1 : _fixturePatch.Fixtures.Max(fixture => fixture.Id) + 1;
        var patchWindow = new PatchFixtureWindow(definition, suggestedId, NextAvailableAddress(1)) { Owner = this };
        if (patchWindow.ShowDialog() != true || patchWindow.Fixture is null || patchWindow.SelectedMode is null) return;
        _fixturePatch.Add(patchWindow.Fixture);
        _patchedModes[patchWindow.Fixture.Id] = patchWindow.SelectedMode;
        _patchedDefinitions[patchWindow.Fixture.Id] = definition;
        _programmingEngine.Register(patchWindow.Fixture, definition, patchWindow.SelectedMode);
        Fixtures.Add(FixtureRow.From(patchWindow.Fixture));
        SetStatus($"Patched {patchWindow.Fixture.Name} at U{patchWindow.Fixture.Universe}.{patchWindow.Fixture.Address}");
    }

    private void SaveShow_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Save lighting show", Filter = $"Lighting Controller shows (*{ShowFileStore.Extension})|*{ShowFileStore.Extension}", FileName = $"show{ShowFileStore.Extension}" };
        if (dialog.ShowDialog(this) != true) return;
        var fixtures = _fixturePatch.Fixtures.Where(fixture => _patchedDefinitions.ContainsKey(fixture.Id) && _patchedModes.ContainsKey(fixture.Id))
            .Select(fixture => new ShowFixture(fixture, _patchedDefinitions[fixture.Id], _patchedModes[fixture.Id].Name)).ToArray();
        ShowCue ConvertCue(Cue cue) => new(cue.Number, cue.Name, cue.FadeIn.TotalSeconds,
            cue.Values.Select(item => new ShowValue(item.Key.FixtureId, item.Key.Channel, item.Value.Value, item.Value.Kind)).ToArray(),
            cue.PresetReferences.Select(reference => new ShowPresetReference(reference.Type, reference.Slot, reference.Scope, reference.FixtureIds)).ToArray());
        var cueLists = _cueLists.Select((stack, index) => new ShowCueList(index + 1, stack.Name, stack.Cues.Select(ConvertCue).ToArray())).ToArray();
        var presets = ColourPresets.Where(preset => preset.Values.Count > 0).Select(preset => new ShowPreset(preset.Slot, preset.Name, preset.Type, preset.Swatch,
            preset.Values.Select(item => new ShowValue(item.Key.FixtureId, item.Key.Channel, item.Value.Value, item.Value.Kind)).ToArray(), preset.Scope)).ToArray();
        new ShowFileStore().Save(new ShowFile(1, Path.GetFileNameWithoutExtension(dialog.FileName), fixtures, [], presets, [], cueLists), dialog.FileName);
        SetStatus($"Saved show to {dialog.FileName}");
    }

    private void LoadShow_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Load lighting show", Filter = $"Lighting Controller shows (*{ShowFileStore.Extension})|*{ShowFileStore.Extension}" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var show = new ShowFileStore().Load(dialog.FileName);
            foreach (var fixture in _fixturePatch.Fixtures.ToArray()) { _fixturePatch.Remove(fixture.Id); _programmingEngine.Remove(fixture.Id); }
            Fixtures.Clear(); _patchedModes.Clear(); _patchedDefinitions.Clear(); _selectedFixtures.Clear(); _selectedParts.Clear();
            _cueLists.Clear(); CueLists.Clear(); Cues.Clear(); _selectedCueListIndex = 0;
            foreach (var item in show.Fixtures)
            {
                var mode = item.Definition.Modes.First(mode => mode.Name == item.ModeName);
                _fixturePatch.Add(item.Patch); _programmingEngine.Register(item.Patch, item.Definition, mode);
                _patchedDefinitions[item.Patch.Id] = item.Definition; _patchedModes[item.Patch.Id] = mode; Fixtures.Add(FixtureRow.From(item.Patch));
            }
            var savedLists = show.CueLists is { Count: > 0 } ? show.CueLists : [new ShowCueList(1, "Cue List 1", show.Cues)];
            foreach (var savedList in savedLists)
            {
                var stack = new CueStack(savedList.Name);
                foreach (var item in savedList.Cues)
                {
                    var cue = new Cue(item.Number, item.Name) { FadeIn = TimeSpan.FromSeconds(item.FadeSeconds) };
                    foreach (var value in item.Values) cue.SetValue(value.Address, value.Value, value.Kind);
                    foreach (var reference in item.PresetReferences ?? []) cue.AddPresetReference(new(reference.Type, reference.Slot, reference.Scope, reference.FixtureIds));
                    stack.Add(cue);
                }
                _cueLists.Add(stack); CueLists.Add(new(savedList.Number, savedList.Name));
            }
            if (_cueLists.Count == 0) { _cueLists.Add(new("Cue List 1")); CueLists.Add(new(1, "Cue List 1")); }
            SelectCueList(1);
            foreach (var preset in ColourPresets) { preset.Values.Clear(); preset.Name = ""; preset.Swatch = "#1B1D1F"; }
            foreach (var saved in show.Presets)
            {
                var preset = ColourPresets.FirstOrDefault(item => item.Slot == saved.Slot && item.Type == saved.Kind); if (preset is null) continue;
                preset.Name = saved.Name; preset.Swatch = saved.Swatch; preset.Scope = saved.Scope; foreach (var value in saved.Values) preset.Values[value.Address] = new(value.Value, value.Kind);
            }
            ShowPresetType(_activePresetType); ColourPresetList.Items.Refresh(); SetStatus($"Loaded show {show.Name}");
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Show load failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void OutputProtocolCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OutputDestinationText is null) return;
        OutputDestinationText.Text = OutputProtocolCombo.SelectedItem is DmxNetworkProtocol.Sacn ? "" : "127.0.0.1";
        OutputDestinationText.ToolTip = OutputProtocolCombo.SelectedItem is DmxNetworkProtocol.Sacn ? "Leave blank for universe multicast." : "Art-Net node IP address.";
    }

    private async void SendOutputTest_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var frame = new byte[512];
            for (var index = 0; index < frame.Length; index++) frame[index] = (byte)(index % 2 == 0 ? 255 : 0);
            await SendFrameAsync(frame);
            SetStatus("Sent network DMX test frame");
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Output failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async Task SendLiveOutputAsync()
    {
        if (OutputEnabledCheck?.IsChecked != true) return;
        try { await SendFrameAsync(BuildCurrentFrame()); }
        catch (Exception exception) { SetStatus($"Output failed: {exception.Message}"); }
    }

    private async void OutputTimer_Tick(object? sender, EventArgs e)
    {
        if (_outputSending || OutputEnabledCheck.IsChecked != true) return;
        _outputSending = true;
        try { await SendFrameAsync(BuildCurrentFrame()); }
        catch (Exception exception) { OutputEnabledCheck.IsChecked = false; SetStatus($"Output stopped: {exception.Message}"); }
        finally { _outputSending = false; }
    }

    private byte[] BuildCurrentFrame()
    {
        if (!_paused) UpdatePlaybackFade();
        var universe = int.TryParse(OutputUniverseText.Text, out var selectedUniverse) ? selectedUniverse : 1;
        var active = new Dictionary<ParameterAddress, CueValue>(_playbackState);
        foreach (var item in _programmer) active[item.Key] = item.Value;
        if (_effectRunning)
        {
            var effect = new EffectDefinition(EffectWaveform.Sine, EffectRateEncoder.Value, (byte)EffectLowEncoder.Value, (byte)EffectHighEncoder.Value, EffectPhaseEncoder.Value);
            _programmingEngine.SetIntensity(active, CurrentSelections, effect.Sample(DateTime.UtcNow - _effectStartedUtc));
        }
        if (_chaserRunning && _liveChaser.StepAt(DateTime.UtcNow - _chaserStartedUtc) is { } step)
            foreach (var item in step.Values) active[item.Key] = item.Value;
        return _programmingEngine.RenderUniverse(universe, active, _outputMaster);
    }

    private void ToggleEffect_Click(object sender, RoutedEventArgs e)
    {
        _effectRunning = !_effectRunning;
        _effectStartedUtc = DateTime.UtcNow;
        if (sender is Button button) button.Background = _effectRunning ? System.Windows.Media.Brushes.DarkGreen : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(92, 40, 109));
        SetStatus(_effectRunning ? "Intensity effect running" : "Intensity effect stopped");
    }

    private void AddChaserStep_Click(object sender, RoutedEventArgs e)
    {
        if (_programmer.Count == 0) { SetStatus("Program fixtures before adding a chaser step"); return; }
        _liveChaser.Add(new ChaserStep(new Dictionary<ParameterAddress, CueValue>(_programmer), TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(750)));
        SetStatus($"Added chaser step {_liveChaser.Steps.Count}");
    }

    private void ToggleChaser_Click(object sender, RoutedEventArgs e)
    {
        if (_liveChaser.Steps.Count == 0) { SetStatus("Add at least one chaser step"); return; }
        _chaserRunning = !_chaserRunning; _chaserStartedUtc = DateTime.UtcNow;
        RunChaserButton.Background = _chaserRunning ? System.Windows.Media.Brushes.DarkGreen : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(92, 40, 109));
        SetStatus(_chaserRunning ? "Chaser running" : "Chaser stopped");
    }

    private void ClearChaser_Click(object sender, RoutedEventArgs e)
    {
        _chaserRunning = false;
        _liveChaser.Clear();
        SetStatus("Chaser cleared");
    }

    private void StoreFixtureGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFixtures.Count == 0) { SetStatus("Select fixtures before storing a group"); return; }
        var group = new FixtureSelectionGroup($"Group {_fixtureGroups.Count + 1}", _selectedFixtures);
        _fixtureGroups.Add(group);
        SetStatus($"Stored {group.Name} with {group.FixtureIds.Count} fixture(s)");
    }

    private void UpdatePlaybackFade(bool forceComplete = false)
    {
        if (_fadeFrom is null || _fadeTo is null) return;
        var progress = forceComplete || _fadeDuration <= TimeSpan.Zero ? 1d : (DateTime.UtcNow - _fadeStartedUtc).TotalMilliseconds / _fadeDuration.TotalMilliseconds;
        var state = FadeCalculator.Interpolate(_fadeFrom, _fadeTo, progress);
        _playbackState.Clear();
        foreach (var item in state) _playbackState[item.Key] = item.Value;
        if (progress >= 1) { _fadeFrom = null; _fadeTo = null; }
    }

    private void GrandMasterSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _outputMaster.Level = (byte)Math.Round(e.NewValue);
        if (IsLoaded) _ = SendLiveOutputAsync();
    }

    private void Blackout_Click(object sender, RoutedEventArgs e)
    {
        _outputMaster.Blackout = !_outputMaster.Blackout;
        BlackoutButton.Background = _outputMaster.Blackout ? System.Windows.Media.Brushes.Red : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(41, 45, 48));
        SetStatus(_outputMaster.Blackout ? "Blackout enabled" : "Blackout released");
        EchoCommand(_outputMaster.Blackout ? "BLACKOUT ON" : "BLACKOUT OFF");
        _ = SendLiveOutputAsync();
    }

    private void CommandLineText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var command = CommandLineText.Text.Trim(); CommandLineText.Clear(); e.Handled = true;
        if (command.Length > 0) ExecuteCommand(command);
    }

    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (Keyboard.FocusedElement == CommandLineText || Keyboard.FocusedElement is TextBox || !_keyboardShortcutsEnabled) return;
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Insert)
        {
            _keyboardShortcutsEnabled = !_keyboardShortcutsEnabled;
            CommandModeText.Text = _keyboardShortcutsEnabled ? "COMMAND [INSERT: ON]" : "COMMAND [INSERT: OFF]";
            CommandModeText.Foreground = _keyboardShortcutsEnabled ? System.Windows.Media.Brushes.Cyan : System.Windows.Media.Brushes.Gray;
            SetStatus($"Keyboard shortcuts {(_keyboardShortcutsEnabled ? "enabled" : "disabled")}");
            e.Handled = true;
            return;
        }
        if (_keyboardShortcutsEnabled && e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Shift && (Keyboard.FocusedElement is not TextBox || Keyboard.FocusedElement == CommandLineText))
        {
            CommandLineText.Focus();
            var renamePrefix = CommandLineText.Text.Length == 0 || char.IsWhiteSpace(CommandLineText.Text[^1]) ? "" : " ";
            CommandLineText.Text += $"{renamePrefix}RENAME PRESET "; CommandLineText.CaretIndex = CommandLineText.Text.Length;
            e.Handled = true; return;
        }
        if (_keyboardShortcutsEnabled && e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Shift && (Keyboard.FocusedElement is not TextBox || Keyboard.FocusedElement == CommandLineText))
        {
            AppendCommandWord("SELECT"); e.Handled = true; return;
        }
        if (!_keyboardShortcutsEnabled || Keyboard.Modifiers != ModifierKeys.None || (Keyboard.FocusedElement is TextBox && Keyboard.FocusedElement != CommandLineText)) return;
        if (e.Key == Key.Q && CommandLineText.Text.TrimEnd().EndsWith("CUE", StringComparison.OrdinalIgnoreCase))
        {
            var text = CommandLineText.Text.TrimEnd(); CommandLineText.Text = text[..^3] + "CUELIST "; CommandLineText.CaretIndex = CommandLineText.Text.Length; e.Handled = true; return;
        }
        var word = e.Key switch
        {
            Key.S => "STORE", Key.U => "UPDATE", Key.P => "PRESET", Key.C => "COLOUR", Key.I => "INTENSITY",
            Key.B => "BEAM", Key.F => "FOCUS", Key.O => "GOBO", Key.T => "POSITION", Key.R => "RELEASE",
            Key.G => "GO", Key.Q => "CUE", Key.D => "DELETE", Key.X => "CLEAR", _ => null
        };
        if (word is null) return;
        CommandLineText.Focus();
        var prefix = CommandLineText.Text.Length == 0 || char.IsWhiteSpace(CommandLineText.Text[^1]) ? "" : " ";
        CommandLineText.Text += $"{prefix}{word} ";
        CommandLineText.CaretIndex = CommandLineText.Text.Length;
        e.Handled = true;
    }

    private void AppendCommandWord(string word)
    {
        CommandLineText.Focus(); var prefix = CommandLineText.Text.Length == 0 || char.IsWhiteSpace(CommandLineText.Text[^1]) ? "" : " ";
        CommandLineText.Text += $"{prefix}{word} "; CommandLineText.CaretIndex = CommandLineText.Text.Length;
    }

    private void ExecuteCommand(string input)
    {
        var tokens = ConsoleCommandParser.Parse(input);
        EchoCommand(input.ToUpperInvariant());
        try
        {
            if (tokens.Length >= 4 && (tokens[0] is "store" or "update") && tokens[1] == "preset" && int.TryParse(tokens[3], out var storeSlot)) { StorePresetFromCommand(PresetType(tokens[2]), storeSlot); return; }
            if (tokens.Length >= 3 && tokens[0] == "select" && tokens[1] == "cuelist" && int.TryParse(tokens[2], out var selectedList)) { SelectCueList(selectedList); return; }
            if (tokens.Length >= 5 && tokens[0] == "update" && tokens[1] == "cuelist" && int.TryParse(tokens[2], out var updateList) && tokens[3] == "cue") { UpdateCueFromCommand(updateList, tokens[4]); return; }
            if (tokens.Length >= 3 && tokens[0] == "update" && tokens[1] == "cue") { UpdateCueFromCommand(_selectedCueListIndex + 1, tokens[2]); return; }
            if (tokens.Length >= 3 && tokens[0] == "preset" && int.TryParse(tokens[2], out var applySlot)) { ApplyPresetFromCommand(PresetType(tokens[1]), applySlot); return; }
            if (tokens.Length >= 2 && tokens[0] == "intensity" && double.TryParse(tokens[1], out var intensity)) { DimmerSlider.Value = Math.Clamp(intensity, 0, 255); return; }
            if (tokens.Length >= 4 && tokens[0] == "colour" && double.TryParse(tokens[1], out var red) && double.TryParse(tokens[2], out var green) && double.TryParse(tokens[3], out var blue)) { RedSlider.Value = Math.Clamp(red, 0, 255); GreenSlider.Value = Math.Clamp(green, 0, 255); BlueSlider.Value = Math.Clamp(blue, 0, 255); return; }
            if (tokens.Length >= 5 && tokens[0] == "rename" && tokens[1] == "preset" && int.TryParse(tokens[3], out var renameSlot)) { RenamePreset(PresetType(tokens[2]), renameSlot, string.Join(' ', input.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(4))); return; }
            if (tokens.SequenceEqual(["store", "cue"])) { _storePending = true; RecordCue_Click(this, new RoutedEventArgs()); return; }
            SetStatus($"Unknown command: {input}");
        }
        catch (Exception exception) { SetStatus($"Command error: {exception.Message}"); }
    }

    private void StorePresetFromCommand(string type, int slot)
    {
        var preset = ColourPresets.FirstOrDefault(item => item.Type == type && item.Slot == slot) ?? throw new InvalidOperationException("Preset slot does not exist.");
        var values = CurrentPresetValues(type); if (values.Count == 0) throw new InvalidOperationException($"No {type.ToLowerInvariant()} values are in the programmer.");
        foreach (var item in values) preset.Values[item.Key] = item.Value;
        preset.Name = $"{type} {slot}";
        if (type == "Colour" && TryReadRgb(out var red, out var green, out var blue)) preset.Swatch = $"#{red:X2}{green:X2}{blue:X2}";
        ShowPresetType(type); ColourPresetList.Items.Refresh(); SetStatus($"Stored preset {type} {slot}");
    }

    private void UpdateCueFromCommand(int cueListNumber, string cueNumber)
    {
        if (cueListNumber < 1 || cueListNumber > _cueLists.Count) throw new InvalidOperationException($"Cue list {cueListNumber} does not exist.");
        var stack = _cueLists[cueListNumber - 1]; var index = stack.Cues.ToList().FindIndex(cue => cue.Number == cueNumber);
        if (index < 0) throw new InvalidOperationException($"Cue {cueNumber} does not exist in cue list {cueListNumber}.");
        stack.Update(index, HardProgrammerValues()); foreach (var reference in _activePresetReferences) stack.Cues[index].AddPresetReference(reference);
        if (cueListNumber == _selectedCueListIndex + 1) { Cues[index].UpdateFrom(stack.Cues[index]); CueGrid.Items.Refresh(); }
        SetStatus($"Updated cue list {cueListNumber} cue {cueNumber}");
    }

    private void ApplyPresetFromCommand(string type, int slot)
    {
        var preset = ColourPresets.FirstOrDefault(item => item.Type == type && item.Slot == slot && item.Values.Count > 0) ?? throw new InvalidOperationException("Preset is empty.");
        foreach (var item in _programmingEngine.ResolvePresetValues(preset.Values, preset.Scope, CurrentSelections)) { _programmer[item.Key] = item.Value; _presetOwnedAddresses.Add(item.Key); }
        _activePresetReferences.RemoveAll(item => item.Type == type);
        _activePresetReferences.Add(new(type, slot, preset.Scope, _selectedFixtures.ToArray()));
        ShowPresetType(type); SetStatus($"Applied preset {type} {slot}");
    }

    private IReadOnlyDictionary<ParameterAddress, CueValue> ResolvePresetReference(CuePresetReference reference)
    {
        var preset = ColourPresets.FirstOrDefault(item => item.Type == reference.Type && item.Slot == reference.Slot);
        if (preset is null) return new Dictionary<ParameterAddress, CueValue>();
        var selections = reference.FixtureIds.Select(id => new FixtureSelection(id));
        return _programmingEngine.ResolvePresetValues(preset.Values, reference.Scope, selections);
    }

    private void PresetButton_RightDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: ColourPresetRow preset } || preset.Values.Count == 0) return;
        _heldPreset = preset; _renameHoldTriggered = false;
        _presetHoldTimer?.Stop(); _presetHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _presetHoldTimer.Tick += (_, _) => { _presetHoldTimer.Stop(); _renameHoldTriggered = true; OpenPresetRename(preset); };
        _presetHoldTimer.Start(); e.Handled = true;
    }

    private void PresetButton_RightUp(object sender, MouseButtonEventArgs e)
    {
        _presetHoldTimer?.Stop(); e.Handled = true;
        if (_renameHoldTriggered || _heldPreset is null) return;
        var menu = new ContextMenu();
        foreach (var scope in Enum.GetValues<PresetScope>())
        {
            var item = new MenuItem { Header = scope switch { PresetScope.FixtureType => "Fixture type", _ => scope.ToString() }, IsCheckable = true, IsChecked = _heldPreset.Scope == scope, Tag = scope };
            item.Click += (_, _) => { _heldPreset.Scope = (PresetScope)item.Tag; ColourPresetList.Items.Refresh(); EchoCommand($"PRESET {_heldPreset.Type.ToUpperInvariant()} {_heldPreset.Slot} SCOPE {_heldPreset.Scope.ToString().ToUpperInvariant()}"); };
            menu.Items.Add(item);
        }
        menu.PlacementTarget = sender as Button; menu.IsOpen = true;
    }

    private void OpenPresetRename(ColourPresetRow preset)
    {
        var dialog = new PresetNameWindow(preset.Type, preset.Slot, preset.Name) { Owner = this };
        if (dialog.ShowDialog() == true) RenamePreset(preset.Type, preset.Slot, dialog.PresetName);
    }

    private void RenamePreset(string type, int slot, string name)
    {
        var preset = ColourPresets.FirstOrDefault(item => item.Type == type && item.Slot == slot && item.Values.Count > 0) ?? throw new InvalidOperationException("Preset is empty.");
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Preset name cannot be empty.");
        preset.Name = name.Trim(); ShowPresetType(type); ColourPresetList.Items.Refresh();
        EchoCommand($"RENAME PRESET {type.ToUpperInvariant()} {slot} {preset.Name}"); SetStatus($"Renamed preset {type} {slot} to {preset.Name}");
    }

    private static string PresetType(string token) => token switch { "color" or "colour" => "Colour", "dimmer" or "intensity" => "Intensity", "position" => "Position", "gobo" => "Gobo", "beam" => "Beam", "focus" => "Focus", "control" => "Control", _ => throw new InvalidOperationException($"Unknown preset type '{token}'.") };

    private void EchoCommand(string command)
    {
        if (CommandHistoryText is null) return;
        CommandHistoryText.Text = $"> {command}    {CommandHistoryText.Text}";
    }

    private Task SendFrameAsync(byte[] frame)
    {
        var protocol = (DmxNetworkProtocol)(OutputProtocolCombo.SelectedItem ?? DmxNetworkProtocol.ArtNet);
        var universe = int.Parse(OutputUniverseText.Text, CultureInfo.InvariantCulture);
        var wireUniverse = protocol == DmxNetworkProtocol.ArtNet ? universe - 1 : universe;
        return _networkOutput.SendAsync(protocol, wireUniverse, frame, string.IsNullOrWhiteSpace(OutputDestinationText.Text) ? null : OutputDestinationText.Text.Trim());
    }
}

public sealed record FixtureLibraryRow(FixtureDefinition Definition)
{
    public string DisplayName => $"{Definition.Manufacturer} {Definition.Model}";
    public string Details => $"{Definition.Type} • {string.Join(", ", Definition.Modes.Select(mode => $"{mode.Name} ({mode.Channels.Count}ch)"))}";
}

public sealed record FixturePartRow(int FixtureId, string PartName)
{
    public string DisplayName => $"{FixtureId}.{PartName}";
}

public sealed record CueListRow(int Number, string Name)
{
    public string DisplayName => $"{Number}  {Name}";
}

public sealed class ColourPresetRow(string type, int slot)
{
    public string Type { get; } = type;
    public int Slot { get; } = slot;
    public string Name { get; set; } = "";
    public string Swatch { get; set; } = "#1B1D1F";
    public PresetScope Scope { get; set; } = type == "Position" ? PresetScope.Individual : PresetScope.Global;
    public string ScopeShort => Scope switch { PresetScope.Global => "G", PresetScope.FixtureType => "T", _ => "I" };
    public string BorderColour => Values.Count == 0 ? "#454A4E" : "#00D7D7";
    public Dictionary<ParameterAddress, CueValue> Values { get; } = [];
}

public sealed record FixtureRow(int Id, string Name, int Universe, int Address)
{
    public static FixtureRow From(PatchedFixture fixture) =>
        new(fixture.Id, fixture.Name, fixture.Universe, fixture.Address);
}

public sealed class CueRow
{
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public string Fade { get; set; } = "0.0";
    public int ValueCount { get; set; }
    public string Follow { get; set; } = "GO";

    public static CueRow From(Cue cue)
    {
        var row = new CueRow();
        row.UpdateFrom(cue);
        return row;
    }

    public void UpdateFrom(Cue cue)
    {
        Number = cue.Number;
        Name = cue.Name;
        Fade = cue.FadeIn.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        ValueCount = cue.Values.Count + cue.PresetReferences.Count;
        Follow = cue.Follow?.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) ?? "GO";
    }
}
