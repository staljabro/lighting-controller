using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
    private readonly Dictionary<ParameterAddress, CueValue> _inactiveProgrammer = [];
    private readonly List<CuePresetReference> _activePresetReferences = [];
    private readonly HashSet<ParameterAddress> _presetOwnedAddresses = [];
    private readonly Dictionary<ParameterAddress, CueValue> _playbackState = [];
    private IReadOnlyDictionary<ParameterAddress, CueValue>? _fadeFrom;
    private IReadOnlyDictionary<ParameterAddress, CueValue>? _fadeTo;
    private DateTime _fadeStartedUtc;
    private TimeSpan _fadeDuration;
    private DateTime? _pausedAtUtc;
    private readonly FixtureProgrammingEngine _programmingEngine = new();
    private readonly List<int> _selectedFixtures = [];
    private readonly List<FixtureSelection> _selectedParts = [];
    private readonly List<FixtureSelection> _selectionOrder = [];
    private readonly Dictionary<int, FixtureMode> _patchedModes = [];
    private readonly Dictionary<int, FixtureDefinition> _patchedDefinitions = [];
    private readonly DmxNetworkOutput _networkOutput = new();
    private readonly MidiInputService _midiInput = new();
    private readonly MidiOutputService _midiOutput = new();
    private readonly OutputMaster _outputMaster = new();
    private readonly DispatcherTimer _outputTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _midiReconnectTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _midiFeedbackTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private bool _outputSending;
    private bool _storePending;
    private bool _effectRunning;
    private DateTime _effectStartedUtc;
    private EffectTarget _effectTarget = EffectTarget.Intensity;
    private EffectDefinition _effectDefinition = new(EffectWaveform.Sine, 1, 0, 255);
    private ConsoleEffectDefinition _effectProgram = new();
    private readonly Chaser _liveChaser = new();
    private bool _chaserRunning;
    private DateTime _chaserStartedUtc;
    private readonly HashSet<int> _activeEffectSlots = [];
    private readonly HashSet<int> _cueEffectSlots = [];
    private readonly DispatcherTimer _commandOverlayTimer = new() { Interval = TimeSpan.FromSeconds(1.8) };
    private string _activePresetType = "Intensity";
    private bool _keyboardShortcutsEnabled = true;
    private readonly AppSettings _settings = AppSettingsStore.Load();
    private DispatcherTimer? _presetHoldTimer;
    private ColourPresetRow? _heldPreset;
    private bool _renameHoldTriggered;
    private int _playbackIndex = -1;
    private bool _paused;
    private double _defaultCueTimeSeconds = 2;
    private DateTime? _followDueUtc;
    private int? _armedEffectSlot;
    private readonly HashSet<(FixtureSelection Selection, ParameterKind? Kind)> _stompedEffects = [];
    private int _clearPressCount;
    private readonly DispatcherTimer _clearHoldTimer = new() { Interval = TimeSpan.FromSeconds(2.1) };
    private bool _clearHoldTriggered;
    private bool _online = true;
    private bool _panTiltTestRunning;
    private DateTime _panTiltTestStartedUtc;
    private bool _refreshingEncoders;
    private string _activeWorkspace = "Patch";
    private string _activeFeatureGroup = "Dimmer";
    private string? _currentProjectPath;

    public MainWindow()
    {
        InitializeComponent();
        UpdateNavigationHighlight(_activeWorkspace);
        _keyboardShortcutsEnabled = _settings.ShortcutsEnabled;
        Cues = [];
        CueLists = [new CueListRow(1, "Cue List 1")];
        Fixtures = [];
        Library = [];
        VisibleLibrary = [];
        ColourPresets = [];
        VisiblePresets = [];
        SelectionPresets = [];
        ProgrammerFixtures = [];
        Effects = [];
        foreach (var type in new[] { "Intensity", "Position", "Gobo", "Colour", "Beam", "Focus", "Control" })
            for (var slot = 1; slot <= 28; slot++) ColourPresets.Add(new ColourPresetRow(type, slot));
        DataContext = this;
        OutputProtocolCombo.ItemsSource = Enum.GetValues<DmxNetworkProtocol>();
        OutputProtocolCombo.SelectedItem = DmxNetworkProtocol.ArtNet;
        LoadFixtureLibrary();
        ShowPresetType("Intensity");
        CueListSelector.SelectedIndex = 0;
        CueGrid.MouseDoubleClick += EditCue_Click;
        _outputTimer.Tick += OutputTimer_Tick;
        _commandOverlayTimer.Tick += (_, _) => { _commandOverlayTimer.Stop(); CommandOverlay.Visibility = Visibility.Collapsed; };
        _clearHoldTimer.Tick += (_, _) => { _clearHoldTimer.Stop(); _clearHoldTriggered = true; if (_clearPressCount < 3) ExecuteCommand("CLEAR CLEAR CLEAR"); };
        _outputTimer.Start();
        _midiInput.MessageReceived += MidiInput_MessageReceived;
        _midiInput.SysExReceived += MidiInput_SysExReceived;
        _midiReconnectTimer.Tick += (_, _) => ReconnectMidiIfNeeded(); _midiReconnectTimer.Start();
        _midiFeedbackTimer.Tick += (_, _) => { _midiFeedbackTimer.Stop(); UpdateMidiEncoderLcdFeedback(); };
        foreach (var encoder in new[] { DimmerSlider, PanSlider, TiltSlider, RedSlider, GreenSlider, BlueSlider, EffectRateEncoder, EffectLowEncoder, EffectHighEncoder, EffectPhaseEncoder })
            encoder.ValueChanged += (_, _) => RequestMidiEncoderFeedback();
        ConfigureMidiInput();
        Loaded += (_, _) => RequestMidiEncoderFeedback();
        Loaded += MainWindow_Loaded;
        Closed += (_, _) => { _outputTimer.Stop(); _midiReconnectTimer.Stop(); _midiFeedbackTimer.Stop(); _midiInput.Dispose(); _midiOutput.Dispose(); _networkOutput.Dispose(); };
        UpdateCommandModeIndicator();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_settings.LastProjectPath) && File.Exists(_settings.LastProjectPath))
        {
            if (TryLoadProject(_settings.LastProjectPath, false)) return;
        }
        _settings.LastProjectPath = ""; _currentProjectPath = null; Title = "Lighting Controller - Blank Project";
        AppSettingsStore.Save(_settings); SetStatus("Blank project ready");
    }

    public ObservableCollection<CueRow> Cues { get; }
    public ObservableCollection<CueListRow> CueLists { get; }

    public ObservableCollection<FixtureRow> Fixtures { get; }
    public ObservableCollection<FixtureLibraryRow> Library { get; }
    public ObservableCollection<FixtureLibraryRow> VisibleLibrary { get; }
    public ObservableCollection<ColourPresetRow> ColourPresets { get; }
    public ObservableCollection<ColourPresetRow> VisiblePresets { get; }
    public ObservableCollection<SelectionPresetRow> SelectionPresets { get; }
    public ObservableCollection<ProgrammerFixtureTreeRow> ProgrammerFixtures { get; }
    public ObservableCollection<StoredEffectRow> Effects { get; }
    private IEnumerable<FixtureSelection> CurrentSelections => _selectionOrder;

    private void FixtureSelection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string fixtureList, Content: object label })
        {
            return;
        }

        _selectedFixtures.Clear();
        _selectionOrder.Clear();
        foreach (var fixture in fixtureList.Split(','))
        {
            var id = int.Parse(fixture, CultureInfo.InvariantCulture); _selectedFixtures.Add(id); _selectionOrder.Add(new(id));
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
        var cue = new Cue(number, $"Cue {number}") { FadeIn = TimeSpan.FromSeconds(_defaultCueTimeSeconds) };
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
        foreach (var slot in _activeEffectSlots) _cueStack.Cues[index].AddEffectReference(slot);
        Cues[index].UpdateFrom(_cueStack.Cues[index]);
        CueGrid.Items.Refresh();
        SetStatus($"Updated cue {_cueStack.Cues[index].Number}");
        EchoCommand($"UPDATE CUE {_cueStack.Cues[index].Number}");
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
        _followDueUtc = null;
        CueGrid.SelectedIndex = -1;
        SetStatus("Cue stack released");
    }

    private void ClearProgrammer_Click(object sender, RoutedEventArgs e)
    {
        if (_clearHoldTriggered) { _clearHoldTriggered = false; return; }
        ExecuteNextClearLevel();
    }

    private void ClearButton_Down(object sender, MouseButtonEventArgs e) { _clearHoldTriggered = false; _clearHoldTimer.Stop(); _clearHoldTimer.Start(); }
    private void ClearButton_Up(object sender, MouseButtonEventArgs e) => _clearHoldTimer.Stop();
    private void StompButton_Click(object sender, RoutedEventArgs e) => ExecuteCommand("STOMP");

    private void ExecuteNextClearLevel()
    {
        if (_clearPressCount >= 3) return;
        ExecuteCommand(string.Join(' ', Enumerable.Repeat("CLEAR", _clearPressCount + 1)));
    }

    private void ClearSelectionCommand()
    {
        CommandLineText.Clear();
        SelectionPresetList.SelectedIndex = -1; _selectedFixtures.Clear(); _selectedParts.Clear(); _selectionOrder.Clear(); SyncProgrammerFixtureHighlights();
        ProgrammerTitle.Text = "PROGRAMMER - NO FIXTURES SELECTED"; SetStatus("ClearSelection: fixtures deselected; programmer values retained");
    }

    private void ClearActiveCommand()
    {
        CommandLineText.Clear();
        foreach (var item in _programmer) _inactiveProgrammer[item.Key] = item.Value; _programmer.Clear(); _activePresetReferences.Clear(); _presetOwnedAddresses.Clear();
        SetStatus($"ClearActive: {_inactiveProgrammer.Count} programmer value(s) inactive");
    }

    private void ClearAllCommand()
    {
        ClearSelectionCommand(); _programmer.Clear(); _inactiveProgrammer.Clear(); _activePresetReferences.Clear(); _presetOwnedAddresses.Clear(); _activeEffectSlots.Clear(); _cueEffectSlots.Clear(); _stompedEffects.Clear(); _effectRunning = false;
        SetStatus("ClearAll: programmer and programmer effects released"); EffectsView.Items.Refresh();
    }

    private void CueGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CueGrid.SelectedItem is CueRow row)
        {
            if (PendingCommandIs("rename"))
            {
                CommandLineText.Clear(); var index = CueGrid.SelectedIndex; var cue = _cueStack.Cues[index]; var dialog = new PresetNameWindow($"cue {cue.Number}", _selectedCueListIndex + 1, cue.Name, true) { Owner = this };
                if (dialog.ShowDialog() == true) { cue.Name = dialog.PresetName; Cues[index].UpdateFrom(cue); CueGrid.Items.Refresh(); EchoCommand($"RENAME CUE {cue.Number} {cue.Name}"); SetStatus($"Renamed cue {cue.Number}"); }
                return;
            }
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
        if (CueListSelector.SelectedItem is not CueListRow row) return;
        if (PendingCommandIs("rename")) { CommandLineText.Clear(); var dialog = new PresetNameWindow("cue list", row.Number, row.Name, true) { Owner = this }; if (dialog.ShowDialog() == true) { _cueLists[row.Number - 1].Name = dialog.PresetName; CueLists[row.Number - 1] = new(row.Number, dialog.PresetName); EchoCommand($"RENAME CUELIST {row.Number} {dialog.PresetName}"); } return; }
        SelectCueList(row.Number);
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
        _selectionOrder.Clear();
        _selectedFixtures.Add(fixture.Id);
        _selectionOrder.Add(new(fixture.Id));
        ProgrammerTitle.Text = $"PROGRAMMER — {fixture.Name.ToUpperInvariant()}";
        SetStatus($"Selected fixture {fixture.Id}: {fixture.Name}");
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
        if (!IsLoaded || _refreshingEncoders || _selectedFixtures.Count == 0) return;
        AdjustAttribute(EffectAttribute.Dimmer, e.NewValue - e.OldValue);
    }

    private void ColourSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _refreshingEncoders || _selectedFixtures.Count == 0) return;
        var attribute = ReferenceEquals(sender, RedSlider) ? EffectAttribute.Red : ReferenceEquals(sender, GreenSlider) ? EffectAttribute.Green : EffectAttribute.Blue;
        AdjustAttribute(attribute, e.NewValue - e.OldValue);
    }

    private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _refreshingEncoders || _selectedFixtures.Count == 0) return;
        AdjustAttribute(ReferenceEquals(sender, PanSlider) ? EffectAttribute.Pan : EffectAttribute.Tilt, e.NewValue - e.OldValue);
    }

    private void AdjustAttribute(EffectAttribute attribute, double delta)
    {
        if (Math.Abs(delta) < double.Epsilon) return;
        var targets = _programmingEngine.ResolveAttributeTargets(CurrentSelections, attribute);
        foreach (var target in targets)
        {
            var current = _programmer.TryGetValue(target.Address, out var programmed) ? programmed.Value
                : _playbackState.TryGetValue(target.Address, out var playback) ? playback.Value : target.DefaultValue;
            _programmer[target.Address] = new((byte)Math.Clamp(Math.Round(current + delta), 0, 255), target.Kind);
        }
        if (targets.Count == 0) return;
        ReleasePresetOwnership(targets[0].Kind);
        RefreshDynamicEncoders();
        EchoCommand($"{attribute.ToString().ToUpperInvariant()} {delta:+0.##;-0.##}");
        StatusText.Text = $"{attribute.ToString().ToUpperInvariant()} RELATIVE {delta:+0.##;-0.##}";
        _ = SendLiveOutputAsync();
    }

    private void RefreshDynamicEncoders()
    {
        if (!IsLoaded || DimmerSlider is null) return;
        _refreshingEncoders = true;
        try
        {
            RefreshEncoder(DimmerSlider, EffectAttribute.Dimmer);
            RefreshEncoder(PanSlider, EffectAttribute.Pan); RefreshEncoder(TiltSlider, EffectAttribute.Tilt);
            RefreshEncoder(RedSlider, EffectAttribute.Red); RefreshEncoder(GreenSlider, EffectAttribute.Green); RefreshEncoder(BlueSlider, EffectAttribute.Blue);
        }
        finally { _refreshingEncoders = false; RequestMidiEncoderFeedback(); }
    }

    private void RefreshEncoder(RotaryControl encoder, EffectAttribute attribute)
    {
        var targets = _programmingEngine.ResolveAttributeTargets(CurrentSelections, attribute);
        var values = targets.Select(target => _programmer.TryGetValue(target.Address, out var programmed) ? programmed.Value
            : _playbackState.TryGetValue(target.Address, out var playback) ? playback.Value : target.DefaultValue).Distinct().ToArray();
        encoder.IsEnabled = values.Length > 0;
        encoder.IsMixed = values.Length > 1;
        if (values.Length == 1) encoder.Value = values[0];
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
        if (PendingCommandIs("rename")) { CommandLineText.Clear(); OpenPresetRename(preset); return; }
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
        _selectionOrder.RemoveAll(part => part.FixtureId == fixture.Id);
        _patchedModes.Remove(fixture.Id);
        _patchedDefinitions.Remove(fixture.Id);
        _programmingEngine.Remove(fixture.Id);
        RefreshProgrammerFixtureTree();
        SetStatus($"Removed fixture {fixture.Id}: {fixture.Name}");
    }

    private void RenameFixture_Click(object sender, RoutedEventArgs e)
    {
        if (FixtureGrid.SelectedItem is not FixtureRow row) { SetStatus("Select a patched fixture to rename"); return; }
        var patch = _fixturePatch.Find(row.Id); if (patch is null) return;
        var dialog = new PresetNameWindow("fixture", row.Id, row.Name, true) { Owner = this }; if (dialog.ShowDialog() != true) return;
        var renamed = new PatchedFixture(patch.Id, dialog.PresetName, patch.Manufacturer, patch.Model, patch.Mode, patch.Universe, patch.Address, patch.ChannelCount);
        _fixturePatch.Remove(patch.Id); _fixturePatch.Add(renamed);
        if (_patchedDefinitions.TryGetValue(row.Id, out var definition) && _patchedModes.TryGetValue(row.Id, out var mode)) _programmingEngine.Register(renamed, definition, mode);
        var index = Fixtures.IndexOf(row); Fixtures[index] = FixtureRow.From(renamed); RefreshProgrammerFixtureTree();
        FixtureGrid.SelectedIndex = index; EchoCommand($"RENAME FIXTURE {row.Id} {dialog.PresetName}"); SetStatus($"Renamed fixture {row.Id}");
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string workspace } button) return;
        _activeWorkspace = workspace;
        PatchWorkspace.Visibility = workspace == "Patch" ? Visibility.Visible : Visibility.Collapsed;
        ProgrammerWorkspace.Visibility = workspace == "Programmer" ? Visibility.Visible : Visibility.Collapsed;
        CueWorkspace.Visibility = workspace == "Cues" ? Visibility.Visible : Visibility.Collapsed;
        UpdateNavigationHighlight(workspace);
        SetStatus($"{button.Content} workspace selected");
    }

    private void UpdateNavigationHighlight(string selected)
    {
        if (PatchNavigationButton is null) return;
        var inactive = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x29, 0x2D, 0x30));
        var active = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x17, 0x6B, 0x87));
        PatchNavigationButton.Background = selected == "Patch" ? active : inactive;
        ProgrammerNavigationButton.Background = selected == "Programmer" ? active : inactive;
        CuesNavigationButton.Background = selected == "Cues" ? active : inactive;
        SettingsNavigationButton.Background = selected == "Settings" ? active : inactive;
    }

    private void FeatureGroup_Click(object sender, RoutedEventArgs e)
    {
        var group = (sender as Button)?.Tag as string ?? "Dimmer";
        SelectFeatureGroup(group);
    }

    private void SelectFeatureGroup(string group)
    {
        _activeFeatureGroup = group;
        DimmerControls.Visibility = group == "Dimmer" ? Visibility.Visible : Visibility.Collapsed;
        PositionControls.Visibility = group == "Position" ? Visibility.Visible : Visibility.Collapsed;
        ColorControls.Visibility = group == "Color" ? Visibility.Visible : Visibility.Collapsed;
        ControlControls.Visibility = group == "Control" ? Visibility.Visible : Visibility.Collapsed;
        UnsupportedFeatureText.Visibility = group is not ("Dimmer" or "Position" or "Color" or "Control") ? Visibility.Visible : Visibility.Collapsed;
        RequestMidiEncoderFeedback();
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

    private void ProgrammerFixtureButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProgrammerFixtureTreeRow row }) return;
        var whole = new FixtureSelection(row.FixtureId); var selected = _selectionOrder.Contains(whole);
        _selectionOrder.RemoveAll(item => item.FixtureId == row.FixtureId); _selectedParts.RemoveAll(item => item.FixtureId == row.FixtureId);
        if (selected) _selectedFixtures.Remove(row.FixtureId);
        else { if (!_selectedFixtures.Contains(row.FixtureId)) _selectedFixtures.Add(row.FixtureId); _selectionOrder.Add(whole); }
        SetStatus(selected ? $"Deselected fixture {row.FixtureId}" : $"Selected fixture {row.FixtureId}"); e.Handled = true;
    }

    private void ProgrammerPartButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProgrammerFixturePartTreeRow part }) return;
        var selection = new FixtureSelection(part.FixtureId, part.Name); var selected = _selectionOrder.Contains(selection);
        _selectionOrder.RemoveAll(item => item.FixtureId == part.FixtureId && item.PartName is null);
        if (selected) { _selectionOrder.Remove(selection); _selectedParts.Remove(selection); }
        else { _selectionOrder.Add(selection); if (!_selectedParts.Contains(selection)) _selectedParts.Add(selection); }
        if (_selectionOrder.Any(item => item.FixtureId == part.FixtureId)) { if (!_selectedFixtures.Contains(part.FixtureId)) _selectedFixtures.Add(part.FixtureId); }
        else _selectedFixtures.Remove(part.FixtureId);
        SetStatus(selected ? $"Deselected {part.Name}" : $"Selected {part.Name}"); e.Handled = true;
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
        _cueEffectSlots.Clear(); foreach (var slot in cue.EffectSlots) _cueEffectSlots.Add(slot);
        _followDueUtc = cue.TriggerType == CueTriggerType.Follow || cue.Follow is not null ? DateTime.UtcNow + (cue.Follow ?? cue.FadeIn) : null;
        if (_fadeDuration <= TimeSpan.Zero) UpdatePlaybackFade(true);
        SetStatus($"GO cue {cue.Number}: {cue.Name} — {resolved.Count} tracked values");
    }

    private void CopyProgrammerTo(Cue cue)
    {
        foreach (var (address, value) in HardProgrammerValues())
        {
            cue.SetValue(address, value.Value, value.Kind);
        }
        foreach (var slot in _activeEffectSlots) cue.AddEffectReference(slot);
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

    private void SetStatus(string text) { StatusText.Text = text.ToUpperInvariant(); SyncProgrammerFixtureHighlights(); RefreshDynamicEncoders(); }

    private void RefreshProgrammerFixtureTree()
    {
        ProgrammerFixtures.Clear();
        foreach (var fixture in Fixtures)
            ProgrammerFixtures.Add(new(fixture.Id, fixture.Name, _programmingEngine.GetParts(fixture.Id)));
        SyncProgrammerFixtureHighlights();
    }

    private void SyncProgrammerFixtureHighlights()
    {
        if (ProgrammerFixtures is null) return;
        foreach (var fixture in ProgrammerFixtures)
        {
            fixture.IsSelected = _selectionOrder.Contains(new FixtureSelection(fixture.FixtureId));
            foreach (var part in fixture.Parts) part.IsSelected = _selectionOrder.Contains(new FixtureSelection(fixture.FixtureId, part.Name));
            fixture.RefreshHighlight();
        }
    }

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
        if (patchWindow.ShowDialog() != true || patchWindow.Fixtures.Count == 0 || patchWindow.SelectedMode is null) return;
        var validationPatch = new FixturePatch();
        foreach (var existing in _fixturePatch.Fixtures) validationPatch.Add(existing);
        foreach (var fixture in patchWindow.Fixtures) validationPatch.Add(fixture);
        foreach (var fixture in patchWindow.Fixtures)
        {
            _fixturePatch.Add(fixture); _patchedModes[fixture.Id] = patchWindow.SelectedMode; _patchedDefinitions[fixture.Id] = definition;
            _programmingEngine.Register(fixture, definition, patchWindow.SelectedMode); Fixtures.Add(FixtureRow.From(fixture));
        }
        RefreshProgrammerFixtureTree();
        SetStatus($"Patched {patchWindow.Fixtures.Count} fixture(s) from U{patchWindow.Fixtures[0].Universe}.{patchWindow.Fixtures[0].Address}");
    }

    private void SaveShow_Click(object sender, RoutedEventArgs e)
    {
        var path = _currentProjectPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var dialog = new SaveFileDialog { Title = "Save lighting project", Filter = $"Lighting Controller projects (*{ShowFileStore.Extension})|*{ShowFileStore.Extension}", FileName = $"project{ShowFileStore.Extension}" };
            if (dialog.ShowDialog(this) != true) return;
            path = dialog.FileName;
        }
        try { SaveProject(path); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Project save failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void SaveProject(string path)
    {
        var fixtures = _fixturePatch.Fixtures.Where(fixture => _patchedDefinitions.ContainsKey(fixture.Id) && _patchedModes.ContainsKey(fixture.Id))
            .Select(fixture => new ShowFixture(fixture, _patchedDefinitions[fixture.Id], _patchedModes[fixture.Id].Name)).ToArray();
        ShowCue ConvertCue(Cue cue) => new(cue.Number, cue.Name, cue.FadeIn.TotalSeconds,
            cue.Values.Select(item => new ShowValue(item.Key.FixtureId, item.Key.Channel, item.Value.Value, item.Value.Kind)).ToArray(),
            cue.PresetReferences.Select(reference => new ShowPresetReference(reference.Type, reference.Slot, reference.Scope, reference.FixtureIds)).ToArray(),
            cue.Follow?.TotalSeconds, cue.TriggerType, cue.EffectSlots.ToArray());
        var cueLists = _cueLists.Select((stack, index) => new ShowCueList(index + 1, stack.Name, stack.Cues.Select(ConvertCue).ToArray())).ToArray();
        var presets = ColourPresets.Where(preset => preset.Values.Count > 0).Select(preset => new ShowPreset(preset.Slot, preset.Name, preset.Type, preset.Swatch,
            preset.Values.Select(item => new ShowValue(item.Key.FixtureId, item.Key.Channel, item.Value.Value, item.Value.Kind)).ToArray(), preset.Scope)).ToArray();
        ShowFixtureGroup[] groups = [];
        var effects = Effects.Select(item => new ShowEffect(item.Slot, item.Name, item.Target, item.Definition.Waveform, item.Definition.RateHz, item.Definition.Low, item.Definition.High, item.Definition.PhaseDegrees, item.Selections.Select(selection => new ShowFixtureSelection(selection.FixtureId, selection.PartName)).ToArray(), item.Program.Scope, item.Program.Groups, item.Program.Blocks, item.Program.Wings, item.Program.Lines.Select(line => new ShowEffectLine(line.Attribute, line.Form, line.Mode, line.Low, line.High, line.SpeedHz, line.PhaseFrom, line.PhaseTo, line.Width)).ToArray())).ToArray();
        var selections = SelectionPresets.Select(item => new ShowSelectionPreset(item.Slot, item.Name, item.Selections.Select(selection => new ShowFixtureSelection(selection.FixtureId, selection.PartName)).ToArray())).ToArray();
        new ShowFileStore().Save(new ShowFile(1, Path.GetFileNameWithoutExtension(path), fixtures, [], presets, groups, cueLists, effects, selections), path);
        RememberProject(path); SetStatus($"Saved project to {path}");
    }

    private void LoadShow_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Load lighting project", Filter = $"Lighting Controller projects (*{ShowFileStore.Extension})|*{ShowFileStore.Extension}" };
        if (dialog.ShowDialog(this) != true) return;
        TryLoadProject(dialog.FileName, true);
    }

    private bool TryLoadProject(string path, bool showErrors)
    {
        try
        {
            var show = new ShowFileStore().Load(path);
            foreach (var fixture in _fixturePatch.Fixtures.ToArray()) { _fixturePatch.Remove(fixture.Id); _programmingEngine.Remove(fixture.Id); }
            Fixtures.Clear(); _patchedModes.Clear(); _patchedDefinitions.Clear(); _selectedFixtures.Clear(); _selectedParts.Clear(); _selectionOrder.Clear();
            _programmer.Clear(); _inactiveProgrammer.Clear(); _playbackState.Clear(); _activePresetReferences.Clear(); _presetOwnedAddresses.Clear();
            _fadeFrom = null; _fadeTo = null; _playbackIndex = -1; _followDueUtc = null; _paused = false;
            _cueLists.Clear(); CueLists.Clear(); Cues.Clear(); _selectedCueListIndex = 0;
            SelectionPresets.Clear(); Effects.Clear(); _activeEffectSlots.Clear(); _cueEffectSlots.Clear();
            foreach (var item in show.Fixtures)
            {
                var mode = item.Definition.Modes.First(mode => mode.Name == item.ModeName);
                _fixturePatch.Add(item.Patch); _programmingEngine.Register(item.Patch, item.Definition, mode);
                _patchedDefinitions[item.Patch.Id] = item.Definition; _patchedModes[item.Patch.Id] = mode; Fixtures.Add(FixtureRow.From(item.Patch));
            }
            RefreshProgrammerFixtureTree();
            var savedLists = show.CueLists is { Count: > 0 } ? show.CueLists : [new ShowCueList(1, "Cue List 1", show.Cues)];
            foreach (var savedList in savedLists)
            {
                var stack = new CueStack(savedList.Name);
                foreach (var item in savedList.Cues)
                {
                    var cue = new Cue(item.Number, item.Name) { FadeIn = TimeSpan.FromSeconds(item.FadeSeconds), Follow = item.FollowSeconds is null ? null : TimeSpan.FromSeconds(item.FollowSeconds.Value), TriggerType = item.TriggerType };
                    foreach (var value in item.Values) cue.SetValue(value.Address, value.Value, value.Kind);
                    foreach (var reference in item.PresetReferences ?? []) cue.AddPresetReference(new(reference.Type, reference.Slot, reference.Scope, reference.FixtureIds));
                    foreach (var slot in item.EffectSlots ?? []) cue.AddEffectReference(slot);
                    stack.Add(cue);
                }
                _cueLists.Add(stack); CueLists.Add(new(savedList.Number, savedList.Name));
            }
            if (_cueLists.Count == 0) { _cueLists.Add(new("Cue List 1")); CueLists.Add(new(1, "Cue List 1")); }
            foreach (var savedGroup in show.Groups ?? [])
            {
                var selections = savedGroup.Selections?.Select(item => new FixtureSelection(item.FixtureId, item.PartName)).ToArray() ?? savedGroup.FixtureIds.Select(id => new FixtureSelection(id)).ToArray();
                var slot = SelectionPresets.Select(item => item.Slot).DefaultIfEmpty(0).Max() + 1; SelectionPresets.Add(new(slot, savedGroup.Name, selections));
            }
            foreach (var saved in show.Effects ?? [])
            {
                var lines = saved.Lines?.Select(line => new ConsoleEffectLine { Attribute = line.Attribute, Form = line.Form, Mode = line.Mode, Low = line.Low, High = line.High, SpeedHz = line.SpeedHz, PhaseFrom = line.PhaseFrom, PhaseTo = line.PhaseTo, Width = line.Width }).ToList() ?? [new ConsoleEffectLine { Attribute = saved.Target switch { EffectTarget.Position => EffectAttribute.Pan, EffectTarget.Colour => EffectAttribute.Red, _ => EffectAttribute.Dimmer }, Form = saved.Waveform, SpeedHz = saved.RateHz, Low = saved.Low, High = saved.High, PhaseFrom = saved.PhaseDegrees, PhaseTo = saved.PhaseDegrees + 360 }];
                Effects.Add(new StoredEffectRow { Slot = saved.Slot, Name = saved.Name, Program = new ConsoleEffectDefinition { Scope = saved.Scope, Groups = saved.Groups, Blocks = saved.Blocks, Wings = saved.Wings, Lines = lines }, Selections = saved.Selections.Select(item => new FixtureSelection(item.FixtureId, item.PartName)).ToArray() });
            }
            foreach (var saved in show.SelectionPresets ?? []) { var slot = SelectionPresets.Select(item => item.Slot).DefaultIfEmpty(0).Max() + 1; SelectionPresets.Add(new(slot, saved.Name, saved.Selections.Select(item => new FixtureSelection(item.FixtureId, item.PartName)).ToArray())); }
            SelectCueList(1);
            foreach (var preset in ColourPresets) { preset.Values.Clear(); preset.Name = ""; preset.Swatch = "#1B1D1F"; }
            foreach (var saved in show.Presets)
            {
                var preset = ColourPresets.FirstOrDefault(item => item.Slot == saved.Slot && item.Type == saved.Kind); if (preset is null) continue;
                preset.Name = saved.Name; preset.Swatch = saved.Swatch; preset.Scope = saved.Scope; foreach (var value in saved.Values) preset.Values[value.Address] = new(value.Value, value.Kind);
            }
            ShowPresetType(_activePresetType); ColourPresetList.Items.Refresh(); RememberProject(path); SetStatus($"Loaded project {show.Name}"); return true;
        }
        catch (Exception exception)
        {
            if (showErrors) MessageBox.Show(this, exception.Message, "Project load failed", MessageBoxButton.OK, MessageBoxImage.Error);
            else SetStatus($"Last project could not be loaded: {exception.Message}");
            return false;
        }
    }

    private void RememberProject(string path)
    {
        _currentProjectPath = Path.GetFullPath(path); _settings.LastProjectPath = _currentProjectPath; AppSettingsStore.Save(_settings);
        Title = $"Lighting Controller - {Path.GetFileNameWithoutExtension(_currentProjectPath)}";
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
        if (!_online || OutputEnabledCheck?.IsChecked != true) return;
        try { await SendConfiguredOutputsAsync(); }
        catch (Exception exception) { SetStatus($"Output failed: {exception.Message}"); }
    }

    private async void OutputTimer_Tick(object? sender, EventArgs e)
    {
        if (!_paused && _followDueUtc is not null && DateTime.UtcNow >= _followDueUtc)
        {
            _followDueUtc = null;
            if (_playbackIndex < _cueStack.Cues.Count - 1) { _playbackIndex++; ShowPlaybackCue(); }
        }
        if (_outputSending || !_online || OutputEnabledCheck.IsChecked != true) return;
        _outputSending = true;
        try { await SendConfiguredOutputsAsync(); }
        catch (Exception exception) { OutputEnabledCheck.IsChecked = false; SetStatus($"Output stopped: {exception.Message}"); }
        finally { _outputSending = false; }
    }

    private byte[] BuildCurrentFrame()
    {
        var universe = int.TryParse(OutputUniverseText.Text, out var selectedUniverse) ? selectedUniverse : 1;
        return BuildFrame(universe);
    }

    private byte[] BuildFrame(int universe)
    {
        if (!_paused) UpdatePlaybackFade();
        var active = new Dictionary<ParameterAddress, CueValue>(_playbackState);
        foreach (var item in _programmer) active[item.Key] = item.Value;
        if (_effectRunning)
        {
            var elapsed = DateTime.UtcNow - _effectStartedUtc;
            ApplyConsoleEffect(active, _effectProgram, CurrentSelections.ToArray(), elapsed);
        }
        foreach (var effect in Effects.Where(item => _activeEffectSlots.Contains(item.Slot) || _cueEffectSlots.Contains(item.Slot)))
            ApplyConsoleEffect(active, effect.Program, effect.Program.Scope == EffectScope.Template ? CurrentSelections.ToArray() : effect.Selections, DateTime.UtcNow - _effectStartedUtc);
        if (_chaserRunning && _liveChaser.StepAt(DateTime.UtcNow - _chaserStartedUtc) is { } step)
            foreach (var item in step.Values) active[item.Key] = item.Value;
        if (_panTiltTestRunning)
        {
            var elapsed = (DateTime.UtcNow - _panTiltTestStartedUtc).TotalSeconds;
            var pan = (byte)Math.Round((Math.Sin(elapsed * 1.4) + 1) * 127.5);
            var tilt = (byte)Math.Round((Math.Cos(elapsed * 1.1) + 1) * 127.5);
            _programmingEngine.SetPosition(active, CurrentSelections, pan, tilt);
        }
        return _programmingEngine.RenderUniverse(universe, active, _outputMaster);
    }

    private void ApplyEffect(IDictionary<ParameterAddress, CueValue> active, EffectTarget target, EffectDefinition definition, IEnumerable<FixtureSelection> selections, TimeSpan elapsed)
    {
        var value = definition.Sample(elapsed);
        if (target == EffectTarget.Intensity) _programmingEngine.SetIntensity(active, selections, value);
        else if (target == EffectTarget.Position) _programmingEngine.SetPosition(active, selections, value, definition.Sample(elapsed, 90));
        else _programmingEngine.SetColour(active, selections, value, definition.Sample(elapsed, 120), definition.Sample(elapsed, 240));
    }

    private void ApplyConsoleEffect(IDictionary<ParameterAddress, CueValue> active, ConsoleEffectDefinition program, IReadOnlyList<FixtureSelection> selections, TimeSpan elapsed)
    {
        if (selections.Count == 0) return;
        foreach (var line in program.Lines)
            for (var index = 0; index < selections.Count; index++)
                if (!IsEffectStomped(selections[index], AttributeKind(line.Attribute))) _programmingEngine.SetEffectAttribute(active, selections[index], line.Attribute, line.Sample(elapsed, program.PhaseFor(index, selections.Count, line)), line.Mode);
    }

    private bool IsEffectStomped(FixtureSelection selection, ParameterKind kind) => _stompedEffects.Any(item => item.Selection.FixtureId == selection.FixtureId && (item.Selection.PartName is null || item.Selection.PartName == selection.PartName) && (item.Kind is null || item.Kind == kind));
    private static ParameterKind AttributeKind(EffectAttribute attribute) => attribute switch { EffectAttribute.Dimmer => ParameterKind.Intensity, EffectAttribute.Pan or EffectAttribute.Tilt => ParameterKind.Position, EffectAttribute.Red or EffectAttribute.Green or EffectAttribute.Blue => ParameterKind.Colour, _ => ParameterKind.Other };

    private void OnlineToggle_Click(object sender, RoutedEventArgs e)
    {
        _online = !_online;
        OnlineButton.Content = _online ? "ONLINE" : "OFFLINE";
        OnlineButton.Background = _online ? System.Windows.Media.Brushes.SeaGreen : System.Windows.Media.Brushes.DimGray;
        SetStatus(_online ? "Output online" : "Output offline — live data remains available locally");
        EchoCommand(_online ? "ONLINE" : "OFFLINE");
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => OpenSettings(0);

    private void OpenOutputPatch_Click(object sender, RoutedEventArgs e) => OpenSettings(1);

    private void OpenSettings(int selectedTab)
    {
        _midiInput.Close(); _midiOutput.Close();
        UpdateNavigationHighlight("Settings");
        var saved = new SettingsWindow(_settings, selectedTab) { Owner = this }.ShowDialog() == true;
        UpdateNavigationHighlight(_activeWorkspace);
        ConfigureMidiInput();
        if (!saved) return;
        _keyboardShortcutsEnabled = _settings.ShortcutsEnabled;
        UpdateCommandModeIndicator();
        SetStatus("Settings saved");
    }

    private void ConfigureMidiInput()
    {
        try { _midiInput.Open(_settings.MidiInputDevice); }
        catch (Exception exception) { SetStatus($"MIDI unavailable: {exception.Message}"); }
        try { var output = string.IsNullOrWhiteSpace(_settings.MidiOutputDevice) && MidiOutputService.DeviceNames().Contains(_settings.MidiInputDevice) ? _settings.MidiInputDevice : _settings.MidiOutputDevice; _midiOutput.Open(output); }
        catch (Exception exception) { SetStatus($"MIDI feedback unavailable: {exception.Message}"); }
        if (_midiOutput.IsOpen) _midiOutput.SendSysEx([0xf0, 0x00, 0x00, 0x66, 0x14, 0x00, 0xf7]);
        RequestMidiEncoderFeedback();
    }

    private void ReconnectMidiIfNeeded()
    {
        if (_midiInput.IsOpen || string.IsNullOrWhiteSpace(_settings.MidiInputDevice)) return;
        if (MidiInputService.DeviceNames().Contains(_settings.MidiInputDevice)) ConfigureMidiInput();
    }

    private void MidiInput_MessageReceived(byte status, byte data1, byte data2)
    {
        var binding = _settings.MidiBindings.FirstOrDefault(item => item.Status == status && item.Data1 == data1);
        if (binding is null) return;
        if (binding.TargetType is not MidiBindingTarget.Encoder && data2 == 0) return;
        _midiOutput.Send(status, data1, data2);
        Dispatcher.BeginInvoke(() => ExecuteMidiBinding(binding, data2));
    }

    private void MidiInput_SysExReceived(byte[] message)
    {
        if (message.Length < 7 || message[0] != 0xf0 || message[^1] != 0xf7 || message[1] != 0x00 || message[2] != 0x00 || message[3] != 0x66) return;
        var model = message[4]; var command = message[5];
        if (command == 0x01 && message.Length >= 18)
        {
            var serial = message[6..13]; var challenge = message[13..17];
            var response = new byte[]
            {
                (byte)(0x7f & (challenge[0] + (challenge[1] ^ 0x0a) - challenge[3])),
                (byte)(0x7f & ((challenge[2] >> 4) ^ (challenge[0] + challenge[3]))),
                (byte)(0x7f & ((challenge[3] - (challenge[2] << 2)) ^ (challenge[0] | challenge[1]))),
                (byte)(0x7f & (challenge[1] - challenge[2] + (0xf0 ^ (challenge[3] << 4))))
            };
            _midiOutput.SendSysEx([0xf0, 0x00, 0x00, 0x66, model, 0x02, .. serial, .. response, 0xf7]);
        }
        else if (command == 0x03) Dispatcher.BeginInvoke(RequestMidiEncoderFeedback);
    }

    private void ExecuteMidiBinding(MidiCommandBinding binding, byte midiValue)
    {
        var target = binding.EffectiveTarget.Trim();
        if (binding.TargetType == MidiBindingTarget.Command)
        {
            var scaled = binding.OutputMinimum + midiValue / 127d * (binding.OutputMaximum - binding.OutputMinimum);
            ExecuteCommand(target.Replace("{value}", scaled.ToString("0.###", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase));
            return;
        }
        if (binding.TargetType == MidiBindingTarget.Softkey) { ExecuteMidiSoftkey(target); return; }
        var amount = binding.ValueMode == MidiValueMode.McuRelative ? DecodeMcuRelative(midiValue)
            : binding.OutputMinimum + midiValue / 127d * (binding.OutputMaximum - binding.OutputMinimum);
        if (binding.ValueMode == MidiValueMode.McuRelative)
        {
            if (Math.Abs(amount) > double.Epsilon) ApplyMidiEncoderDelta(target, amount);
        }
        else ApplyMidiEncoderAbsolute(target, amount);
    }

    private void ExecuteMidiSoftkey(string target)
    {
        switch (target.Trim().ToUpperInvariant())
        {
            case "GO": Go_Click(this, new RoutedEventArgs()); break;
            case "BACK": Back_Click(this, new RoutedEventArgs()); break;
            case "PAUSE": Pause_Click(this, new RoutedEventArgs()); break;
            case "RELEASE": Release_Click(this, new RoutedEventArgs()); break;
            case "STORE": BeginStore_Click(this, new RoutedEventArgs()); break;
            case "CUE": RecordCue_Click(this, new RoutedEventArgs()); break;
            case "UPDATE": UpdateCue_Click(this, new RoutedEventArgs()); break;
            case "DELETE": DeleteCue_Click(this, new RoutedEventArgs()); break;
            case "CLEAR": ExecuteNextClearLevel(); break;
            case "STOMP": ExecuteCommand("STOMP"); break;
            case "PATCH": Navigation_Click(PatchNavigationButton, new RoutedEventArgs()); break;
            case "PROGRAMMER": Navigation_Click(ProgrammerNavigationButton, new RoutedEventArgs()); break;
            case "CUE LISTS" or "CUELISTS": Navigation_Click(CuesNavigationButton, new RoutedEventArgs()); break;
            case "DIMMER" or "INTENSITY": SelectFeatureGroup("Dimmer"); break;
            case "POSITION": SelectFeatureGroup("Position"); break;
            case "COLOR" or "COLOUR": SelectFeatureGroup("Color"); break;
            case "GOBO": SelectFeatureGroup("Gobo"); break;
            case "BEAM": SelectFeatureGroup("Beam"); break;
            case "FOCUS": SelectFeatureGroup("Focus"); break;
            case "CONTROL": SelectFeatureGroup("Control"); break;
            default: SetStatus($"Unknown MIDI softkey: {target}"); break;
        }
    }

    private static double DecodeMcuRelative(byte value) => value switch { 0 or 64 => 0, <= 63 => value, _ => -(value - 64) };

    private void ApplyMidiEncoderDelta(string target, double delta)
    {
        target = ResolveMidiEncoderTarget(target);
        if (TryGetDynamicAttribute(target, out var attribute)) AdjustAttribute(attribute, delta);
        else if (TryGetRotary(target, out var rotary)) rotary.Value = Math.Clamp(rotary.Value + delta * rotary.Step, rotary.Minimum, rotary.Maximum);
        else SetStatus($"Unknown MIDI encoder: {target}");
    }

    private void ApplyMidiEncoderAbsolute(string target, double value)
    {
        target = ResolveMidiEncoderTarget(target);
        if (TryGetDynamicAttribute(target, out var attribute))
        {
            var targets = _programmingEngine.ResolveAttributeTargets(CurrentSelections, attribute);
            foreach (var item in targets) _programmer[item.Address] = new((byte)Math.Clamp(Math.Round(value), 0, 255), item.Kind);
            if (targets.Count > 0) { ReleasePresetOwnership(targets[0].Kind); RefreshDynamicEncoders(); _ = SendLiveOutputAsync(); }
        }
        else if (TryGetRotary(target, out var rotary)) rotary.Value = Math.Clamp(value, rotary.Minimum, rotary.Maximum);
        else SetStatus($"Unknown MIDI encoder: {target}");
        EchoCommand($"MIDI ENCODER {target.ToUpperInvariant()} {value:0.##}");
    }

    private static bool TryGetDynamicAttribute(string target, out EffectAttribute attribute) => Enum.TryParse(target.Trim() switch
    {
        "INTENSITY" => "Dimmer", "COLOR RED" or "COLOUR RED" => "Red", "COLOR GREEN" or "COLOUR GREEN" => "Green", "COLOR BLUE" or "COLOUR BLUE" => "Blue", _ => target.Trim()
    }, true, out attribute);

    private string ResolveMidiEncoderTarget(string target)
    {
        if (!target.Trim().StartsWith("ENCODER ", StringComparison.OrdinalIgnoreCase) || !int.TryParse(target.Trim()[8..], out var slot)) return target;
        return (_activeFeatureGroup, slot) switch
        {
            ("Dimmer", 1) => "Dimmer", ("Dimmer", 2) => "Effect Rate", ("Dimmer", 3) => "Effect Low", ("Dimmer", 4) => "Effect High",
            ("Position", 1) => "Pan", ("Position", 2) => "Tilt", ("Position", 3) => "Effect Phase",
            ("Color", 1) => "Red", ("Color", 2) => "Green", ("Color", 3) => "Blue", _ => target
        };
    }

    private bool TryGetRotary(string target, out RotaryControl rotary)
    {
        rotary = target.Trim().ToUpperInvariant() switch
        {
            "EFFECT RATE" or "RATE" => EffectRateEncoder, "EFFECT LOW" => EffectLowEncoder, "EFFECT HIGH" => EffectHighEncoder,
            "EFFECT PHASE" or "PHASE" => EffectPhaseEncoder, "GRAND MASTER" or "MASTER" => GrandMasterSlider, _ => null!
        };
        return rotary is not null;
    }

    private void RequestMidiEncoderFeedback()
    {
        if (!IsLoaded) return;
        _midiFeedbackTimer.Stop(); _midiFeedbackTimer.Start();
    }

    private void UpdateMidiEncoderLcdFeedback()
    {
        if (!_midiOutput.IsOpen) return;
        var cells = Enumerable.Repeat("", 8).ToArray();
        for (var slot = 1; slot <= 4; slot++)
        {
            var target = ResolveMidiEncoderTarget($"ENCODER {slot}");
            if (target.StartsWith("ENCODER ", StringComparison.OrdinalIgnoreCase)) continue;
            cells[slot - 1] = MidiLcdText(target);
            cells[slot + 3] = MidiLcdValue(target);
        }
        for (var cell = 1; cell <= 8; cell++) _midiOutput.SendMcuLcdCell(cell, cells[cell - 1]);
    }

    private string MidiLcdValue(string target)
    {
        RotaryControl? rotary = null;
        if (TryGetDynamicAttribute(target, out var attribute)) rotary = attribute switch
        {
            EffectAttribute.Dimmer => DimmerSlider, EffectAttribute.Pan => PanSlider, EffectAttribute.Tilt => TiltSlider,
            EffectAttribute.Red => RedSlider, EffectAttribute.Green => GreenSlider, EffectAttribute.Blue => BlueSlider, _ => null
        };
        else if (TryGetRotary(target, out var other)) rotary = other;
        return rotary is null ? "" : rotary.IsMixed ? "--" : rotary.Value.ToString(rotary.Step < 1 ? "0.0" : "0", CultureInfo.InvariantCulture);
    }

    private static string MidiLcdText(string target) => target.ToUpperInvariant() switch
    {
        "EFFECT RATE" => "RATE", "EFFECT LOW" => "LOW", "EFFECT HIGH" => "HIGH", "EFFECT PHASE" => "PHASE", _ => target.ToUpperInvariant()
    };

    private void UpdateCommandModeIndicator()
    {
        CommandModeText.Text = _keyboardShortcutsEnabled ? "COMMAND [INSERT: ON]" : "COMMAND [INSERT: OFF]";
        CommandModeText.Foreground = _keyboardShortcutsEnabled ? System.Windows.Media.Brushes.Cyan : System.Windows.Media.Brushes.Gray;
    }

    private void OpenDmxMonitor_Click(object sender, RoutedEventArgs e) => new DmxMonitorWindow(BuildFrame, _fixturePatch) { Owner = this }.Show();

    private void OpenFixtureDetails_Click(object sender, RoutedEventArgs e)
    {
        if (FixtureGrid.SelectedItem is not FixtureRow row) { SetStatus("Select a patched fixture to inspect its output"); return; }
        var patch = _fixturePatch.Fixtures.FirstOrDefault(item => item.Id == row.Id);
        if (patch is null || !_patchedDefinitions.TryGetValue(row.Id, out var definition) || !_patchedModes.TryGetValue(row.Id, out var mode)) { SetStatus("Fixture profile details are unavailable"); return; }
        new FixtureDetailsWindow(patch, definition, mode, BuildFrame) { Owner = this }.Show();
    }

    private void OpenTimeline_Click(object sender, RoutedEventArgs e)
    {
        void Refresh() { Cues.Clear(); foreach (var cue in _cueStack.Cues) Cues.Add(CueRow.From(cue)); CueGrid.Items.Refresh(); }
        new CueTimelineWindow(_cueStack, () => _playbackIndex, Refresh) { Owner = this }.Show();
    }

    private void PanTiltTest_Click(object sender, RoutedEventArgs e)
    {
        if (!_selectedFixtures.Any()) { SetStatus("Select moving fixtures before starting pan / tilt test"); return; }
        _panTiltTestRunning = !_panTiltTestRunning;
        _panTiltTestStartedUtc = DateTime.UtcNow;
        SetStatus(_panTiltTestRunning ? "Pan / tilt test running" : "Pan / tilt test stopped");
        EchoCommand(_panTiltTestRunning ? "TEST POSITION START" : "TEST POSITION STOP");
    }

    private void ToggleEffect_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFixtures.Count == 0) { SetStatus("Select fixtures before editing an effect"); return; }
        var editor = new EffectEditorWindow(_effectProgram, _effectRunning) { Owner = this };
        if (editor.ShowDialog() != true) return;
        _effectProgram = editor.Program; _effectTarget = editor.Target; _effectDefinition = editor.Definition; _effectRunning = editor.Running; _effectStartedUtc = DateTime.UtcNow;
        EffectRateEncoder.Value = _effectDefinition.RateHz; EffectLowEncoder.Value = _effectDefinition.Low; EffectHighEncoder.Value = _effectDefinition.High; EffectPhaseEncoder.Value = _effectDefinition.PhaseDegrees;
        if (sender is Button button) button.Background = _effectRunning ? System.Windows.Media.Brushes.DarkGreen : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(92, 40, 109));
        EchoCommand($"EFFECT {_effectTarget.ToString().ToUpperInvariant()} {_effectDefinition.Waveform.ToString().ToUpperInvariant()} RATE {_effectDefinition.RateHz:0.##}");
        SetStatus(_effectRunning ? $"{_effectTarget} effect running" : "Effect stopped");
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
        => StoreSelectionPreset_Click(sender, e);

    private void StoreSelectionPreset_Click(object sender, RoutedEventArgs e)
    {
        var selections = _programmingEngine.ExpandWholeFixturesToParts(CurrentSelections).ToArray(); if (selections.Length == 0) { SetStatus("Select fixtures or parts before storing a fixture group"); return; }
        var slot = SelectionPresets.Select(item => item.Slot).DefaultIfEmpty(0).Max() + 1;
        SelectionPresets.Add(new(slot, $"Group {slot}", selections)); EchoCommand($"STORE GROUP {slot}"); SetStatus($"Stored fixture group {slot}");
    }

    private void SelectionPresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectionPresetList.SelectedItem is not SelectionPresetRow preset) return;
        if (PendingCommandIs("rename")) { CommandLineText.Clear(); RenameSelectionPreset(preset); return; }
        ApplySelectionPreset(preset);
        ProgrammerTitle.Text = $"PROGRAMMER — {preset.Name.ToUpperInvariant()}"; EchoCommand($"GROUP {preset.Slot}"); SetStatus($"Selected {preset.Name}");
    }

    private void SelectionPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SelectionPresetRow preset }) return;
        if (PendingCommandIs("rename")) { CommandLineText.Clear(); RenameSelectionPreset(preset); return; }
        ApplySelectionPreset(preset);
        ProgrammerTitle.Text = $"PROGRAMMER — {preset.Name.ToUpperInvariant()} ({preset.OrderDescription})"; EchoCommand($"GROUP {preset.Slot}"); SetStatus($"Selected {preset.Name} in stored order: {preset.OrderDescription}");
    }

    private void ApplySelectionPreset(SelectionPresetRow preset)
    {
        _selectedFixtures.Clear(); _selectedParts.Clear(); _selectionOrder.Clear();
        foreach (var selection in preset.Selections) { _selectionOrder.Add(selection); if (!_selectedFixtures.Contains(selection.FixtureId)) _selectedFixtures.Add(selection.FixtureId); if (selection.PartName is not null) _selectedParts.Add(selection); }
    }

    private void ToggleFixtureDrawer_Click(object sender, RoutedEventArgs e)
    {
        var show = FixtureDrawerColumn.Width.Value == 0; FixtureDrawerColumn.Width = show ? new GridLength(280) : new GridLength(0); FixtureDrawer.Visibility = show ? Visibility.Visible : Visibility.Collapsed; FixtureDrawerButton.Content = show ? "HIDE FIXTURE DRAWER" : "SHOW FIXTURE DRAWER";
    }

    private void OpenEffectPool_Click(object sender, RoutedEventArgs e)
    {
        StoredEffectRow Current() => new() { Program = CloneEffectProgram(_effectProgram), Selections = CurrentSelections.ToArray() };
        void SetActive(int slot, bool active) { if (active) _activeEffectSlots.Add(slot); else _activeEffectSlots.Remove(slot); _effectStartedUtc = DateTime.UtcNow; EchoCommand($"EFFECT {slot} {(active ? "GO" : "RELEASE")}"); }
        void Select(StoredEffectRow row) { if (row.Program.Scope == EffectScope.Selective) { _selectionOrder.Clear(); _selectedFixtures.Clear(); _selectedParts.Clear(); foreach (var selection in row.Selections) { _selectionOrder.Add(selection); if (!_selectedFixtures.Contains(selection.FixtureId)) _selectedFixtures.Add(selection.FixtureId); if (selection.PartName is not null) _selectedParts.Add(selection); } SetStatus($"Effect {row.Slot}: selection recalled; double-click to run"); } }
        new EffectPoolWindow(Effects, Current, SetActive, Select) { Owner = this }.ShowDialog();
    }

    private void CreateEffect_Click(object sender, RoutedEventArgs e)
    {
        var program = new ConsoleEffectDefinition { Scope = CurrentSelections.Any() ? EffectScope.Selective : EffectScope.Template };
        var editor = new EffectEditorWindow(program, false) { Owner = this }; if (editor.ShowDialog() != true) return;
        var slot = Effects.Select(item => item.Slot).DefaultIfEmpty(0).Max() + 1;
        Effects.Add(new StoredEffectRow { Slot = slot, Name = $"Effect {slot}", Program = editor.Program, Selections = editor.Program.Scope == EffectScope.Selective ? CurrentSelections.ToArray() : [] });
        EchoCommand($"STORE EFFECT {slot}"); SetStatus($"Stored effect {slot}");
    }

    private void EffectPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StoredEffectRow effect }) return;
        if (PendingCommandIs("rename")) { CommandLineText.Clear(); RenameEffect(effect); return; }
        if (effect.Program.Scope == EffectScope.Selective && _armedEffectSlot != effect.Slot)
        {
            _selectionOrder.Clear(); _selectedFixtures.Clear(); _selectedParts.Clear();
            foreach (var selection in effect.Selections) { _selectionOrder.Add(selection); if (!_selectedFixtures.Contains(selection.FixtureId)) _selectedFixtures.Add(selection.FixtureId); if (selection.PartName is not null) _selectedParts.Add(selection); }
            _armedEffectSlot = effect.Slot; SetStatus($"Effect {effect.Slot} selection recalled — tap again to run"); EchoCommand($"EFFECT {effect.Slot} SELECT"); return;
        }
        _activeEffectSlots.Add(effect.Slot); _effectStartedUtc = DateTime.UtcNow; _armedEffectSlot = null; EffectsView?.Items.Refresh(); EchoCommand($"EFFECT {effect.Slot} GO"); SetStatus($"Effect {effect.Slot} running");
    }

    private void EffectPresetButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: StoredEffectRow effect }) return; e.Handled = true;
        var editor = new EffectEditorWindow(effect.Program, _activeEffectSlots.Contains(effect.Slot)) { Owner = this }; if (editor.ShowDialog() != true) return;
        effect.Program = editor.Program; if (editor.Running) _activeEffectSlots.Add(effect.Slot); else _activeEffectSlots.Remove(effect.Slot); EffectsView.Items.Refresh(); EchoCommand($"UPDATE EFFECT {effect.Slot}"); SetStatus($"Updated effect {effect.Slot}");
    }

    private void ReleaseEffects_Click(object sender, RoutedEventArgs e) { _activeEffectSlots.Clear(); _armedEffectSlot = null; EffectsView.Items.Refresh(); EchoCommand("RELEASE EFFECTS"); SetStatus("Effects released"); }

    private static ConsoleEffectDefinition CloneEffectProgram(ConsoleEffectDefinition source) => new() { Scope = source.Scope, Groups = source.Groups, Blocks = source.Blocks, Wings = source.Wings, Lines = source.Lines.Select(line => new ConsoleEffectLine { Attribute = line.Attribute, Form = line.Form, Mode = line.Mode, Low = line.Low, High = line.High, SpeedHz = line.SpeedHz, PhaseFrom = line.PhaseFrom, PhaseTo = line.PhaseTo, Width = line.Width }).ToList() };

    private void CueType_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value }) SetCueType(Enum.Parse<CueTriggerType>(value));
    }

    private void EditCue_Click(object? sender, RoutedEventArgs e)
    {
        if (CueGrid.SelectedIndex < 0) { SetStatus("Select a cue to edit"); return; }
        var cue = _cueStack.Cues[CueGrid.SelectedIndex]; if (new CueEditorWindow(cue) { Owner = this }.ShowDialog() != true) return;
        Cues[CueGrid.SelectedIndex].UpdateFrom(cue); CueGrid.Items.Refresh(); EchoCommand($"UPDATE CUE {cue.Number}"); SetStatus($"Updated cue {cue.Number}");
    }

    private bool PendingCommandIs(string keyword)
    {
        var tokens = ConsoleCommandParser.Parse(CommandLineText.Text); return tokens.Length > 0 && tokens[0] == keyword;
    }

    private void RenameSelectionPreset(SelectionPresetRow preset)
    {
        var dialog = new PresetNameWindow("fixture group", preset.Slot, preset.Name, true) { Owner = this }; if (dialog.ShowDialog() != true) return;
        var index = SelectionPresets.IndexOf(preset); SelectionPresets[index] = new(preset.Slot, dialog.PresetName, preset.Selections); EchoCommand($"RENAME GROUP {preset.Slot} {dialog.PresetName}"); SetStatus($"Renamed group {preset.Slot}");
    }

    private void RenameEffect(StoredEffectRow effect)
    {
        var dialog = new PresetNameWindow("effect", effect.Slot, effect.Name, true) { Owner = this }; if (dialog.ShowDialog() != true) return;
        effect.Name = dialog.PresetName; EffectsView.Items.Refresh(); EchoCommand($"RENAME EFFECT {effect.Slot} {effect.Name}"); SetStatus($"Renamed effect {effect.Slot}");
    }

    private void RenameGroupCommand(int slot, string name)
        => RenameSelectionCommand(slot, name);

    private void RenameSelectionCommand(int slot, string name)
    {
        var row = SelectionPresets.FirstOrDefault(item => item.Slot == slot) ?? throw new InvalidOperationException($"Group {slot} does not exist."); if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("A name is required."); SelectionPresets[SelectionPresets.IndexOf(row)] = new(row.Slot, name.Trim(), row.Selections); SetStatus($"Renamed group {slot}");
    }

    private void RenameEffectCommand(int slot, string name)
    {
        var effect = Effects.FirstOrDefault(item => item.Slot == slot) ?? throw new InvalidOperationException($"Effect {slot} does not exist."); if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("A name is required."); effect.Name = name.Trim(); EffectsView.Items.Refresh(); SetStatus($"Renamed effect {slot}");
    }

    private void RenameCueListCommand(int slot, string name)
    {
        if (slot < 1 || slot > CueLists.Count || string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("A valid cue list and name are required."); _cueLists[slot - 1].Name = name.Trim(); CueLists[slot - 1] = new(slot, name.Trim()); SetStatus($"Renamed cue list {slot}");
    }

    private void RenameCueCommand(string number, string name)
    {
        var index = _cueStack.Cues.ToList().FindIndex(item => item.Number == number); if (index < 0) throw new InvalidOperationException($"Cue {number} does not exist in the selected cue list."); if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("A cue name is required.");
        _cueStack.Cues[index].Name = name.Trim(); Cues[index].UpdateFrom(_cueStack.Cues[index]); CueGrid.Items.Refresh(); SetStatus($"Renamed cue {number}");
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
            UpdateCommandModeIndicator();
            SetStatus($"Keyboard shortcuts {(_keyboardShortcutsEnabled ? "enabled" : "disabled")}");
            e.Handled = true;
            return;
        }
        if (_keyboardShortcutsEnabled && e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Shift && (Keyboard.FocusedElement is not TextBox || Keyboard.FocusedElement == CommandLineText))
        {
            CommandLineText.Focus();
            var renamePrefix = CommandLineText.Text.Length == 0 || char.IsWhiteSpace(CommandLineText.Text[^1]) ? "" : " ";
            CommandLineText.Text += $"{renamePrefix}RENAME "; CommandLineText.CaretIndex = CommandLineText.Text.Length;
            e.Handled = true; return;
        }
        if (_keyboardShortcutsEnabled && e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Shift && (Keyboard.FocusedElement is not TextBox || Keyboard.FocusedElement == CommandLineText))
        {
            AppendCommandWord("SELECT"); e.Handled = true; return;
        }
        if (_keyboardShortcutsEnabled && e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.Shift && (Keyboard.FocusedElement is not TextBox || Keyboard.FocusedElement == CommandLineText))
        {
            AppendCommandWord("TIME"); e.Handled = true; return;
        }
        if (!_keyboardShortcutsEnabled || (Keyboard.FocusedElement is TextBox && Keyboard.FocusedElement != CommandLineText)) return;
        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Q && CommandLineText.Text.TrimEnd().EndsWith("CUE", StringComparison.OrdinalIgnoreCase))
        {
            var text = CommandLineText.Text.TrimEnd(); CommandLineText.Text = text[..^3] + "CUELIST "; CommandLineText.CaretIndex = CommandLineText.Text.Length; e.Handled = true; return;
        }
        var word = _settings.CommandFor(e.Key, Keyboard.Modifiers);
        if (string.Equals(word, "CLEAR", StringComparison.OrdinalIgnoreCase) ||
            (word is null && e.Key == Key.X && Keyboard.Modifiers == ModifierKeys.None))
        {
            ExecuteNextClearLevel(); e.Handled = true; return;
        }
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
        _clearPressCount = GetClearLevel(tokens);
        EchoCommand(input.ToUpperInvariant());
        try
        {
            if (tokens.SequenceEqual(["clear"]) || tokens.SequenceEqual(["clearselection"])) { ClearSelectionCommand(); return; }
            if (tokens.SequenceEqual(["clear", "clear"]) || tokens.SequenceEqual(["clearactive"])) { ClearActiveCommand(); return; }
            if (tokens.SequenceEqual(["clear", "clear", "clear"]) || tokens.SequenceEqual(["clearall"])) { ClearAllCommand(); return; }
            if (tokens.Length >= 1 && tokens[0] is "stomp" or "stom") { ExecuteStomp(tokens.Skip(1).ToArray()); return; }
            if (tokens.SequenceEqual(["go"])) { Go_Click(this, new RoutedEventArgs()); return; }
            if (tokens.SequenceEqual(["back"])) { Back_Click(this, new RoutedEventArgs()); return; }
            if (tokens.SequenceEqual(["pause"])) { Pause_Click(this, new RoutedEventArgs()); EchoCommand("PAUSE"); return; }
            if (tokens.SequenceEqual(["release"])) { Release_Click(this, new RoutedEventArgs()); EchoCommand("RELEASE"); return; }
            if (tokens.SequenceEqual(["delete", "cue"]) || tokens.SequenceEqual(["delete"])) { DeleteCue_Click(this, new RoutedEventArgs()); EchoCommand("DELETE CUE"); return; }
            if (tokens.SequenceEqual(["update"])) { UpdateCue_Click(this, new RoutedEventArgs()); return; }
            if (tokens.Length >= 2 && tokens[0] == "group" && int.TryParse(tokens[1], out var groupSlot)) { RecallGroup(groupSlot); return; }
            if (tokens.Length >= 2 && tokens[0] == "selection" && int.TryParse(tokens[1], out var selectionSlot)) { var preset = SelectionPresets.FirstOrDefault(item => item.Slot == selectionSlot) ?? throw new InvalidOperationException($"Group {selectionSlot} does not exist."); ApplySelectionPreset(preset); SetStatus($"Selected {preset.Name}"); return; }
            if (tokens.Length >= 2 && tokens[0] == "effect" && int.TryParse(tokens[1], out var effectSlot)) { RunEffectFromCommand(effectSlot); return; }
            if (tokens.Length >= 3 && tokens[0] == "release" && tokens[1] == "effect" && int.TryParse(tokens[2], out var releaseEffect)) { _activeEffectSlots.Remove(releaseEffect); EffectsView.Items.Refresh(); SetStatus($"Released effect {releaseEffect}"); return; }
            if (tokens.SequenceEqual(["add", "chaser", "step"])) { AddChaserStep_Click(this, new RoutedEventArgs()); return; }
            if (tokens.SequenceEqual(["run", "chaser"])) { ToggleChaser_Click(this, new RoutedEventArgs()); return; }
            if (tokens.SequenceEqual(["clear", "chaser"])) { ClearChaser_Click(this, new RoutedEventArgs()); return; }
            if (tokens.SequenceEqual(["store", "group"])) { StoreFixtureGroup_Click(this, new RoutedEventArgs()); return; }
            if (tokens.Length >= 4 && (tokens[0] is "store" or "update") && tokens[1] == "preset" && int.TryParse(tokens[3], out var storeSlot)) { StorePresetFromCommand(PresetType(tokens[2]), storeSlot); return; }
            if (tokens.Length >= 3 && tokens[0] == "select" && tokens[1] == "cuelist" && int.TryParse(tokens[2], out var selectedList)) { SelectCueList(selectedList); return; }
            if (tokens.Length >= 5 && tokens[0] == "update" && tokens[1] == "cuelist" && int.TryParse(tokens[2], out var updateList) && tokens[3] == "cue") { UpdateCueFromCommand(updateList, tokens[4]); return; }
            if (tokens.Length >= 3 && tokens[0] == "update" && tokens[1] == "cue") { UpdateCueFromCommand(_selectedCueListIndex + 1, tokens[2]); return; }
            if (tokens.Length >= 2 && tokens[0] == "time" && double.TryParse(tokens[1], out var cueTime)) { SetCueTime(cueTime); return; }
            if (tokens.Length >= 2 && tokens[0] == "follow" && double.TryParse(tokens[1], out var followTime)) { SetCueFollow(followTime); return; }
            if (tokens.SequenceEqual(["follow", "off"])) { SetCueType(CueTriggerType.Go); return; }
            if (tokens.Length >= 2 && tokens[0] == "type") { SetCueType(ParseCueType(tokens[1])); return; }
            if (tokens.Length >= 3 && tokens[0] == "cue" && tokens[1] == "type") { SetCueType(ParseCueType(tokens[2])); return; }
            if (tokens.Length >= 3 && tokens[0] == "preset" && int.TryParse(tokens[2], out var applySlot)) { ApplyPresetFromCommand(PresetType(tokens[1]), applySlot); return; }
            if (tokens.Length >= 2 && tokens[0] == "intensity" && double.TryParse(tokens[1], out var intensity)) { var level = (byte)Math.Clamp(Math.Round(intensity), 0, 255); _programmingEngine.SetIntensity(_programmer, CurrentSelections, level); ReleasePresetOwnership(ParameterKind.Intensity); SetStatus($"Live dimmer: {level}"); _ = SendLiveOutputAsync(); return; }
            if (tokens.Length >= 4 && tokens[0] == "colour" && double.TryParse(tokens[1], out var red) && double.TryParse(tokens[2], out var green) && double.TryParse(tokens[3], out var blue)) { var r = (byte)Math.Clamp(Math.Round(red), 0, 255); var g = (byte)Math.Clamp(Math.Round(green), 0, 255); var b = (byte)Math.Clamp(Math.Round(blue), 0, 255); _programmingEngine.SetColour(_programmer, CurrentSelections, r, g, b); ReleasePresetOwnership(ParameterKind.Colour); SetStatus($"Live colour: R{r} G{g} B{b}"); _ = SendLiveOutputAsync(); return; }
            if (tokens.Length >= 5 && tokens[0] == "rename" && tokens[1] == "preset" && int.TryParse(tokens[3], out var renameSlot)) { RenamePreset(PresetType(tokens[2]), renameSlot, string.Join(' ', input.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(4))); return; }
            if (tokens.Length >= 4 && tokens[0] == "rename" && tokens[1] == "group" && int.TryParse(tokens[2], out var renameGroupSlot)) { RenameGroupCommand(renameGroupSlot, string.Join(' ', input.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(3))); return; }
            if (tokens.Length >= 4 && tokens[0] == "rename" && tokens[1] is "selection" or "fixturepreset" && int.TryParse(tokens[2], out var renameSelectionSlot)) { RenameSelectionCommand(renameSelectionSlot, string.Join(' ', input.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(3))); return; }
            if (tokens.Length >= 4 && tokens[0] == "rename" && tokens[1] == "effect" && int.TryParse(tokens[2], out var renameEffectSlot)) { RenameEffectCommand(renameEffectSlot, string.Join(' ', input.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(3))); return; }
            if (tokens.Length >= 4 && tokens[0] == "rename" && tokens[1] == "cuelist" && int.TryParse(tokens[2], out var renameCueListSlot)) { RenameCueListCommand(renameCueListSlot, string.Join(' ', input.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(3))); return; }
            if (tokens.Length >= 4 && tokens[0] == "rename" && tokens[1] == "cue") { RenameCueCommand(tokens[2], string.Join(' ', input.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(3))); return; }
            if (tokens.Length >= 4 && tokens[0] == "rename" && int.TryParse(tokens[2], out var shortRenameSlot)) { RenamePreset(PresetType(tokens[1]), shortRenameSlot, string.Join(' ', input.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(3))); return; }
            if (tokens.SequenceEqual(["store", "cue"])) { _storePending = true; RecordCue_Click(this, new RoutedEventArgs()); return; }
            SetStatus($"Unknown command: {input}");
        }
        catch (Exception exception) { SetStatus($"Command error: {exception.Message}"); }
    }

    private void ExecuteStomp(string[] arguments)
    {
        var running = Effects.Where(item => _activeEffectSlots.Contains(item.Slot) || _cueEffectSlots.Contains(item.Slot)).ToArray();
        if (running.Length == 0 && !_effectRunning) { SetStatus("Stomp: no running effect applies"); return; }
        ParameterKind? kind = arguments.FirstOrDefault() switch { "intensity" or "dimmer" => ParameterKind.Intensity, "position" => ParameterKind.Position, "colour" or "color" => ParameterKind.Colour, _ => null };
        var targets = CurrentSelections.ToArray();
        if (targets.Length == 0) targets = running.SelectMany(item => item.Program.Scope == EffectScope.Selective ? item.Selections : Array.Empty<FixtureSelection>()).Distinct().ToArray();
        if (targets.Length == 0) { SetStatus("Stomp needs a fixture selection for template/programmer effects"); return; }
        var state = new Dictionary<ParameterAddress, CueValue>(_playbackState); foreach (var item in _programmer) state[item.Key] = item.Value;
        var elapsed = DateTime.UtcNow - _effectStartedUtc;
        if (_effectRunning) ApplyConsoleEffect(state, _effectProgram, targets, elapsed);
        foreach (var effect in running) ApplyConsoleEffect(state, effect.Program, effect.Program.Scope == EffectScope.Template ? targets : effect.Selections, elapsed);
        foreach (var item in state.Where(item => (kind is null || item.Value.Kind == kind) && _programmingEngine.BelongsToSelection(item.Key, targets)).ToArray()) _programmer[item.Key] = item.Value;
        foreach (var selection in targets) _stompedEffects.Add((selection, kind));
        if (arguments.FirstOrDefault() is { } valueText && double.TryParse(valueText, CultureInfo.InvariantCulture, out var value)) _programmingEngine.SetIntensity(_programmer, targets, (byte)Math.Clamp(Math.Round(value), 0, 255));
        SetStatus($"Stomp: suppressed {(kind?.ToString() ?? "all")} effects for {targets.Length} selection item(s)");
    }

    private void RecallGroup(int slot)
    {
        var group = SelectionPresets.FirstOrDefault(item => item.Slot == slot) ?? throw new InvalidOperationException($"Group {slot} does not exist.");
        ApplySelectionPreset(group);
        SetStatus($"Selected group {slot}: {group.Name}");
    }

    private void RunEffectFromCommand(int slot)
    {
        var effect = Effects.FirstOrDefault(item => item.Slot == slot) ?? throw new InvalidOperationException($"Effect {slot} does not exist.");
        if (effect.Program.Scope == EffectScope.Selective) { _selectionOrder.Clear(); _selectedFixtures.Clear(); _selectedParts.Clear(); foreach (var selection in effect.Selections) { _selectionOrder.Add(selection); if (!_selectedFixtures.Contains(selection.FixtureId)) _selectedFixtures.Add(selection.FixtureId); if (selection.PartName is not null) _selectedParts.Add(selection); } }
        _activeEffectSlots.Add(slot); _effectStartedUtc = DateTime.UtcNow; EffectsView.Items.Refresh(); SetStatus($"Effect {slot} running");
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
        foreach (var slot in _activeEffectSlots) stack.Cues[index].AddEffectReference(slot);
        if (cueListNumber == _selectedCueListIndex + 1) { Cues[index].UpdateFrom(stack.Cues[index]); CueGrid.Items.Refresh(); }
        SetStatus($"Updated cue list {cueListNumber} cue {cueNumber}");
    }

    private void SetCueTime(double seconds)
    {
        seconds = Math.Max(0, seconds);
        if (CueGrid.SelectedIndex >= 0) { _cueStack.Cues[CueGrid.SelectedIndex].FadeIn = TimeSpan.FromSeconds(seconds); Cues[CueGrid.SelectedIndex].UpdateFrom(_cueStack.Cues[CueGrid.SelectedIndex]); CueGrid.Items.Refresh(); }
        else _defaultCueTimeSeconds = seconds;
        SetStatus(CueGrid.SelectedIndex >= 0 ? $"Cue time set to {seconds:0.0}s" : $"Default cue time set to {seconds:0.0}s");
    }

    private void SetCueFollow(double seconds)
    {
        if (CueGrid.SelectedIndex < 0) throw new InvalidOperationException("Select a cue before setting follow time.");
        var cue = _cueStack.Cues[CueGrid.SelectedIndex]; cue.Follow = TimeSpan.FromSeconds(Math.Max(0, seconds)); cue.TriggerType = CueTriggerType.Follow;
        Cues[CueGrid.SelectedIndex].UpdateFrom(cue); CueGrid.Items.Refresh(); SetStatus($"Cue {cue.Number} follows after {seconds:0.0}s");
    }

    private void SetCueType(CueTriggerType type)
    {
        if (CueGrid.SelectedIndex < 0) throw new InvalidOperationException("Select a cue before setting its type.");
        var cue = _cueStack.Cues[CueGrid.SelectedIndex]; cue.TriggerType = type;
        if (type != CueTriggerType.Follow) cue.Follow = null;
        else cue.Follow ??= cue.FadeIn;
        Cues[CueGrid.SelectedIndex].UpdateFrom(cue); CueGrid.Items.Refresh();
        SetStatus($"Cue {cue.Number} type set to {type}");
    }

    private static CueTriggerType ParseCueType(string value) => value switch
    {
        "go" or "manual" => CueTriggerType.Go,
        "follow" => CueTriggerType.Follow,
        "wait" => CueTriggerType.Wait,
        _ => throw new InvalidOperationException($"Unknown cue type '{value}'.")
    };

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
        var level = GetClearLevel(ConsoleCommandParser.Parse(command));
        if (level == 0) _clearPressCount = 0;
        CommandHistoryText.Text = $"> {command}    {CommandHistoryText.Text}";
    }

    private static int GetClearLevel(string[] tokens) =>
        tokens.SequenceEqual(["clear"]) || tokens.SequenceEqual(["clearselection"]) ? 1
        : tokens.SequenceEqual(["clear", "clear"]) || tokens.SequenceEqual(["clearactive"]) ? 2
        : tokens.SequenceEqual(["clear", "clear", "clear"]) || tokens.SequenceEqual(["clearall"]) ? 3 : 0;

    private Task SendFrameAsync(byte[] frame)
    {
        var protocol = (DmxNetworkProtocol)(OutputProtocolCombo.SelectedItem ?? DmxNetworkProtocol.ArtNet);
        var universe = int.Parse(OutputUniverseText.Text, CultureInfo.InvariantCulture);
        var wireUniverse = protocol == DmxNetworkProtocol.ArtNet ? universe - 1 : universe;
        return _networkOutput.SendAsync(protocol, wireUniverse, frame, string.IsNullOrWhiteSpace(OutputDestinationText.Text) ? null : OutputDestinationText.Text.Trim());
    }

    private async Task SendConfiguredOutputsAsync()
    {
        var routes = _settings.OutputRoutes.Where(item => item.Enabled).ToArray();
        if (routes.Length == 0) { await SendFrameAsync(BuildCurrentFrame()); return; }
        foreach (var route in routes)
        {
            if (route.Transport == OutputTransport.UsbDmx)
            {
                SetStatus($"USB DMX route U{route.Universe} requires a device adapter");
                continue;
            }
            var protocol = route.Transport == OutputTransport.ArtNet ? DmxNetworkProtocol.ArtNet : DmxNetworkProtocol.Sacn;
            var wireUniverse = protocol == DmxNetworkProtocol.ArtNet ? route.Universe - 1 : route.Universe;
            await _networkOutput.SendAsync(protocol, wireUniverse, BuildFrame(route.Universe), string.IsNullOrWhiteSpace(route.Destination) ? null : route.Destination.Trim());
        }
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

public sealed record SelectionPresetRow(int Slot, string Name, IReadOnlyList<FixtureSelection> Selections)
{
    public string DisplayName => $"{Slot}  {Name}";
    public string OrderDescription => string.Join(" → ", Selections.Select(item => item.PartName is null ? item.FixtureId.ToString() : $"{item.FixtureId}.{item.PartName}"));
}

public sealed class ProgrammerFixtureTreeRow : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;
    public ProgrammerFixtureTreeRow(int fixtureId, string name, IEnumerable<string> parts)
    {
        FixtureId = fixtureId; Name = name; Parts = new(parts.Select(part => new ProgrammerFixturePartTreeRow(fixtureId, part, RefreshHighlight)));
    }
    public int FixtureId { get; }
    public string Name { get; }
    public string DisplayName => $"{FixtureId} - {Name}";
    public ObservableCollection<ProgrammerFixturePartTreeRow> Parts { get; }
    public Visibility PartsVisibility => Visibility.Visible;
    public bool IsExpanded { get => _isExpanded; set { if (_isExpanded == value) return; _isExpanded = value; Changed(); Changed(nameof(IsHighlighted)); } }
    public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; Changed(); Changed(nameof(IsHighlighted)); } }
    public bool IsHighlighted => IsSelected || (!IsExpanded && Parts.Any(part => part.IsSelected));
    public void RefreshHighlight() => Changed(nameof(IsHighlighted));
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class ProgrammerFixturePartTreeRow : INotifyPropertyChanged
{
    private readonly Action _changed;
    private bool _isSelected;
    public ProgrammerFixturePartTreeRow(int fixtureId, string name, Action changed) { FixtureId = fixtureId; Name = name; _changed = changed; }
    public int FixtureId { get; }
    public string Name { get; }
    public string DisplayName => $"{new string('\u2007', FixtureId.ToString(CultureInfo.InvariantCulture).Length)} - {Name}";
    public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); _changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
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

public sealed record FixtureRow(int Id, string FixtureType, string Name, int Universe, int Address)
{
    public static FixtureRow From(PatchedFixture fixture) =>
        new(fixture.Id, fixture.Model, fixture.Name, fixture.Universe, fixture.Address);
}

public sealed class CueRow
{
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public string Fade { get; set; } = "0.0";
    public int ValueCount { get; set; }
    public string Follow { get; set; } = "GO";
    public string Type { get; set; } = "Go";

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
        Type = cue.TriggerType.ToString();
    }
}
