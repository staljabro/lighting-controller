using System.Collections.ObjectModel;
using System.Windows;

namespace LightingController.App;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ObservableCollection<HotkeyBinding> _rows;
    private readonly ObservableCollection<OutputRoute> _routes;
    private readonly ObservableCollection<MidiCommandBinding> _midiBindings;
    private readonly MidiInputService _learnInput = new();
    public SettingsWindow(AppSettings settings, int selectedTab = 0)
    {
        _settings = settings; InitializeComponent(); EnabledCheck.IsChecked = settings.ShortcutsEnabled;
        _rows = new(settings.Hotkeys.Select(item => new HotkeyBinding(item.Key, item.Command, item.Modifiers))); HotkeyGrid.ItemsSource = _rows;
        _routes = new(settings.OutputRoutes.Select(item => new OutputRoute { Enabled = item.Enabled, Universe = item.Universe, Transport = item.Transport, Destination = item.Destination, Device = item.Device })); RouteGrid.ItemsSource = _routes;
        TransportColumn.ItemsSource = Enum.GetValues<OutputTransport>();
        _midiBindings = new(settings.MidiBindings.Select(item => new MidiCommandBinding { Status = item.Status, Data1 = item.Data1, Command = item.Command, TargetType = item.TargetType, Target = item.Target, ValueMode = item.ValueMode, OutputMinimum = item.OutputMinimum, OutputMaximum = item.OutputMaximum })); MidiGrid.ItemsSource = _midiBindings;
        MidiTargetTypeColumn.ItemsSource = Enum.GetValues<MidiBindingTarget>(); MidiValueModeColumn.ItemsSource = Enum.GetValues<MidiValueMode>();
        PopulateMidiDevices();
        SettingsTabs.SelectedIndex = selectedTab;
        _learnInput.MessageReceived += LearnMessage; Closed += (_, _) => _learnInput.Dispose();
    }
    private void Reset_Click(object sender, RoutedEventArgs e) { _rows.Clear(); foreach (var item in AppSettings.DefaultHotkeys()) _rows.Add(item); }
    private void AddRoute_Click(object sender, RoutedEventArgs e) => _routes.Add(new OutputRoute { Universe = _routes.Select(item => item.Universe).DefaultIfEmpty(0).Max() + 1 });
    private void RemoveRoute_Click(object sender, RoutedEventArgs e) { if (RouteGrid.SelectedItem is OutputRoute route) _routes.Remove(route); }
    private void AddMidiBinding_Click(object sender, RoutedEventArgs e) => _midiBindings.Add(new MidiCommandBinding());
    private void RemoveMidiBinding_Click(object sender, RoutedEventArgs e) { if (MidiGrid.SelectedItem is MidiCommandBinding binding) _midiBindings.Remove(binding); }
    private void RefreshMidi_Click(object sender, RoutedEventArgs e) => PopulateMidiDevices();
    private void LearnMidi_Click(object sender, RoutedEventArgs e)
    {
        if (MidiGrid.SelectedItem is not MidiCommandBinding) { AddMidiBinding_Click(sender, e); MidiGrid.SelectedItem = _midiBindings[^1]; }
        try { _learnInput.Open(MidiDeviceCombo.SelectedItem as string); LearnButton.Content = "MOVE A CONTROL…"; }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "MIDI learn", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void LearnMessage(byte status, byte data1, byte data2) => Dispatcher.BeginInvoke(() => { if (MidiGrid.SelectedItem is MidiCommandBinding binding) { binding.Status = status; binding.Data1 = data1; MidiGrid.Items.Refresh(); LearnButton.Content = "MIDI LEARN"; _learnInput.Close(); } });
    private void PopulateMidiDevices() { var selected = MidiDeviceCombo.SelectedItem as string ?? _settings.MidiInputDevice; var output = MidiOutputDeviceCombo.SelectedItem as string ?? _settings.MidiOutputDevice; MidiDeviceCombo.ItemsSource = new[] { "" }.Concat(MidiInputService.DeviceNames()); MidiOutputDeviceCombo.ItemsSource = new[] { "" }.Concat(MidiOutputService.DeviceNames()); MidiDeviceCombo.SelectedItem = selected; MidiOutputDeviceCombo.SelectedItem = output; }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HotkeyGrid.CommitEdit(); RouteGrid.CommitEdit(); MidiGrid.CommitEdit();
        var duplicateKey = _rows.GroupBy(item => $"{item.Modifiers}:{item.Key}", StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicateKey is not null) { MessageBox.Show(this, $"Hotkey conflict: {duplicateKey.Key}", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var duplicateMidi = _midiBindings.GroupBy(item => (item.Status, item.Data1)).FirstOrDefault(group => group.Count() > 1);
        if (duplicateMidi is not null) { MessageBox.Show(this, $"MIDI conflict: status {duplicateMidi.Key.Status}, data {duplicateMidi.Key.Data1}", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (_routes.Any(item => item.Universe is < 1 or > 63999)) { MessageBox.Show(this, "Output universes must be between 1 and 63999.", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        _settings.ShortcutsEnabled = EnabledCheck.IsChecked == true;
        _settings.Hotkeys = _rows.Where(item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Command)).ToList();
        _settings.OutputRoutes = _routes.Where(item => item.Universe is >= 1 and <= 63999).ToList();
        _settings.MidiInputDevice = MidiDeviceCombo.SelectedItem as string ?? "";
        _settings.MidiOutputDevice = MidiOutputDeviceCombo.SelectedItem as string ?? "";
        foreach (var item in _midiBindings.Where(item => string.IsNullOrWhiteSpace(item.Target))) item.Target = item.Command;
        _settings.MidiBindings = _midiBindings.Where(item => !string.IsNullOrWhiteSpace(item.Target)).ToList();
        AppSettingsStore.Save(_settings); DialogResult = true;
    }
}
