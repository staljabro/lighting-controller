using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using LightingController.Fixtures;
using Microsoft.Win32;

namespace LightingController.App;

public partial class FixtureEditorWindow : Window, INotifyPropertyChanged
{
    private ObservableCollection<CapabilityEditorRow> _selectedCapabilities = [];
    private readonly FixtureDefinition? _originalFixture;

    public FixtureEditorWindow(FixtureDefinition? existing = null)
    {
        _originalFixture = existing;
        InitializeComponent();
        DataContext = this;
        TypeCombo.ItemsSource = FixtureTypes;
        TypeCombo.SelectedItem = "Moving Head";
        GroupColumn.ItemsSource = ChannelGroups;
        PresetColumn.ItemsSource = ChannelPresets;
        if (existing is null) AddInitialChannels(); else LoadFixture(existing);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ChannelEditorRow> Channels { get; } = [];

    public ObservableCollection<ModeEditorRow> Modes { get; } = [];
    public ObservableCollection<PartEditorRow> Parts { get; } = [];

    public ObservableCollection<CapabilityEditorRow> SelectedCapabilities
    {
        get => _selectedCapabilities;
        private set
        {
            _selectedCapabilities = value;
            OnPropertyChanged();
        }
    }

    private static string[] FixtureTypes =>
        ["Moving Head", "Color Changer", "Dimmer", "Scanner", "LED Bar", "Strobe", "Laser", "Other"];

    private static string[] ChannelGroups =>
        ["Intensity", "Colour", "Gobo", "Speed", "Pan", "Tilt", "Shutter", "Prism", "Beam", "Effect", "Maintenance", "Nothing"];

    private static string[] ChannelPresets =>
    [
        "", "IntensityMasterDimmer", "IntensityDimmer", "IntensityRed", "IntensityGreen", "IntensityBlue",
        "IntensityWhite", "IntensityAmber", "IntensityUV", "PositionPan", "PositionPanFine", "PositionTilt",
        "PositionTiltFine", "SpeedPanTiltSlowFast", "ColorMacro", "ColorWheel", "GoboWheel", "GoboIndex",
        "ShutterStrobeSlowFast", "ShutterIrisMinToMax", "BeamFocusNearFar", "BeamZoomSmallBig",
        "PrismRotationSlowFast", "NoFunction"
    ];

    private void AddInitialChannels()
    {
        Channels.Add(new ChannelEditorRow { Name = "Dimmer", Group = "Intensity", Preset = "IntensityDimmer" });
        ReindexChannels();
        ChannelGrid.SelectedIndex = 0;
    }

    private void AddChannel_Click(object sender, RoutedEventArgs e)
    {
        Channels.Add(new ChannelEditorRow { Name = $"Channel {Channels.Count + 1}", Group = "Effect" });
        ReindexChannels();
        ChannelGrid.SelectedIndex = Channels.Count - 1;
        EditorStatus.Text = "Channel added. Choose its group, preset, default value and capabilities.";
    }

    private void AddRgbCell_Click(object sender, RoutedEventArgs e)
    {
        var cellNumber = Parts.Count + 1;
        var name = $"Cell {cellNumber}";
        var channelNames = new[] { $"{name} Red", $"{name} Green", $"{name} Blue" };
        Channels.Add(new() { Name = channelNames[0], Group = "Colour", Preset = "IntensityRed" });
        Channels.Add(new() { Name = channelNames[1], Group = "Colour", Preset = "IntensityGreen" });
        Channels.Add(new() { Name = channelNames[2], Group = "Colour", Preset = "IntensityBlue" });
        Channels.Add(new() { Name = $"{name} Virtual Dimmer", Group = "Intensity", Preset = "IntensityMasterDimmer", IsVirtual = true, Targets = string.Join(", ", channelNames) });
        Parts.Add(new() { Name = name, Channels = string.Join(", ", channelNames) });
        ReindexChannels();
        EditorStatus.Text = $"Added {name}. Recreate the fixture mode to include its physical channels.";
    }

    private void AddPart_Click(object sender, RoutedEventArgs e) => Parts.Add(new() { Name = $"Part {Parts.Count + 1}" });
    private void RemovePart_Click(object sender, RoutedEventArgs e) { if (PartGrid.SelectedItem is PartEditorRow row) Parts.Remove(row); }

    private void AddVirtualDimmer_Click(object sender, RoutedEventArgs e)
    {
        var targets = Channels
            .Where(channel => !channel.IsVirtual && IsColourIntensity(channel.Preset))
            .Select(channel => channel.Name)
            .ToArray();
        Channels.Add(new ChannelEditorRow
        {
            Name = "Virtual Dimmer",
            Group = "Intensity",
            Preset = "IntensityMasterDimmer",
            IsVirtual = true,
            Targets = string.Join(", ", targets)
        });
        ReindexChannels();
        ChannelGrid.SelectedIndex = Channels.Count - 1;
        EditorStatus.Text = targets.Length > 0
            ? "Virtual dimmer added. It controls the listed physical colour channels without using a DMX address."
            : "Virtual dimmer added. Enter its target physical channel names, separated by commas.";
    }

    private void RemoveChannel_Click(object sender, RoutedEventArgs e)
    {
        if (ChannelGrid.SelectedItem is ChannelEditorRow row)
        {
            Channels.Remove(row);
            ReindexChannels();
            EditorStatus.Text = "Channel removed. Recreate affected modes before saving.";
        }
    }

    private void MoveChannelUp_Click(object sender, RoutedEventArgs e) => MoveSelectedChannel(-1);

    private void MoveChannelDown_Click(object sender, RoutedEventArgs e) => MoveSelectedChannel(1);

    private void MoveSelectedChannel(int offset)
    {
        var index = ChannelGrid.SelectedIndex;
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Channels.Count)
            return;
        Channels.Move(index, target);
        ReindexChannels();
        ChannelGrid.SelectedIndex = target;
    }

    private void ChannelGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        SelectedCapabilities = ChannelGrid.SelectedItem is ChannelEditorRow row ? row.Capabilities : [];
    }

    private void AddCapability_Click(object sender, RoutedEventArgs e)
    {
        if (ChannelGrid.SelectedItem is not ChannelEditorRow channel)
        {
            EditorStatus.Text = "Select a channel before adding a capability.";
            return;
        }

        var minimum = channel.Capabilities.Count == 0 ? 0 : Math.Min(255, channel.Capabilities.Max(item => item.Maximum) + 1);
        channel.Capabilities.Add(new CapabilityEditorRow { Minimum = minimum, Maximum = 255, Name = "New capability" });
        CapabilityGrid.SelectedIndex = channel.Capabilities.Count - 1;
    }

    private void RemoveCapability_Click(object sender, RoutedEventArgs e)
    {
        if (ChannelGrid.SelectedItem is ChannelEditorRow channel && CapabilityGrid.SelectedItem is CapabilityEditorRow capability)
            channel.Capabilities.Remove(capability);
    }

    private void CreateMode_Click(object sender, RoutedEventArgs e)
    {
        var physicalChannels = Channels.Where(channel => !channel.IsVirtual).ToArray();
        if (physicalChannels.Length == 0)
        {
            EditorStatus.Text = "Add at least one channel before creating a mode.";
            return;
        }

        var name = string.IsNullOrWhiteSpace(ModeNameText.Text) ? $"{physicalChannels.Length} channel" : ModeNameText.Text.Trim();
        var existing = Modes.FirstOrDefault(mode => string.Equals(mode.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            Modes.Remove(existing);
        Modes.Add(new ModeEditorRow(name, physicalChannels.Select(channel => channel.Name).ToArray()));
        EditorStatus.Text = $"Mode '{name}' maps {physicalChannels.Length} physical channels. Virtual channels use no DMX address.";
    }

    private void RemoveMode_Click(object sender, RoutedEventArgs e)
    {
        if (ModeGrid.SelectedItem is ModeEditorRow mode)
            Modes.Remove(mode);
    }

    private void SaveFixture_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ChannelGrid.CommitEdit();
            CapabilityGrid.CommitEdit();
            var fixture = BuildFixture();
            var libraryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightingController", "Fixtures");
            var fileName = $"{SafeFileName(fixture.Manufacturer)}-{SafeFileName(fixture.Model)}{FixtureProfileStore.Extension}";
            var path = Path.Combine(libraryPath, fileName);
            if (_originalFixture is not null)
            {
                var originalName = $"{SafeFileName(_originalFixture.Manufacturer)}-{SafeFileName(_originalFixture.Model)}{FixtureProfileStore.Extension}";
                var originalPath = Path.Combine(libraryPath, originalName);
                if (!string.Equals(originalPath, path, StringComparison.OrdinalIgnoreCase) && File.Exists(originalPath)) File.Delete(originalPath);
            }
            new FixtureProfileStore().Save(fixture, path);
            SavedFixture = fixture;
            EditorStatus.Text = $"Saved {fixture.Manufacturer} {fixture.Model} to the fixture library.";
            MessageBox.Show(this, "Fixture saved successfully.", "Fixture Editor", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException)
        {
            EditorStatus.Text = exception.Message;
            MessageBox.Show(this, exception.Message, "Fixture validation failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public FixtureDefinition? SavedFixture { get; private set; }

    private FixtureDefinition BuildFixture()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ManufacturerText.Text);
        ArgumentException.ThrowIfNullOrWhiteSpace(ModelText.Text);
        if (TypeCombo.SelectedItem is not string type)
            throw new InvalidDataException("Select a fixture type.");
        if (Channels.Count == 0 || Modes.Count == 0)
            throw new InvalidDataException("Create at least one channel and one mode before saving.");

        var channels = Channels.Where(channel => !channel.IsVirtual).Select(channel => channel.ToFixtureChannel())
            .ToDictionary(channel => channel.Name, StringComparer.Ordinal);
        var virtualChannels = Channels.Where(channel => channel.IsVirtual)
            .Select(channel => channel.ToVirtualFixtureChannel()).ToArray();
        var modes = Modes.Select(mode => new FixtureMode(
            mode.Name,
            mode.ChannelNames.Select((channel, index) => new FixtureModeChannel(index, channel)).ToArray())).ToArray();
        var parts = Parts.Select(part => part.ToFixturePart()).ToArray();
        return new FixtureDefinition(ManufacturerText.Text.Trim(), ModelText.Text.Trim(), type, channels, modes, virtualChannels, parts);
    }

    private void LoadFixture(FixtureDefinition fixture)
    {
        ManufacturerText.Text = fixture.Manufacturer; ModelText.Text = fixture.Model; TypeCombo.SelectedItem = fixture.Type;
        foreach (var channel in fixture.Channels.Values)
        {
            var row = new ChannelEditorRow { Name = channel.Name, Group = channel.Group ?? "Effect", Preset = channel.Preset ?? "", GroupByte = channel.GroupByte, DefaultValue = channel.DefaultValue };
            foreach (var capability in channel.CapabilityRanges) row.Capabilities.Add(new() { Minimum = capability.Minimum, Maximum = capability.Maximum, Name = capability.Name, Preset = capability.Preset ?? "" });
            Channels.Add(row);
        }
        foreach (var channel in fixture.VirtualControlChannels) Channels.Add(new() { Name = channel.Name, Group = channel.Group ?? "Intensity", Preset = channel.Preset ?? "", IsVirtual = true, Targets = string.Join(", ", channel.TargetChannels) });
        foreach (var mode in fixture.Modes) Modes.Add(new(mode.Name, mode.Channels.OrderBy(channel => channel.Number).Select(channel => channel.ChannelName).ToArray()));
        foreach (var part in fixture.FixtureParts) Parts.Add(new() { Name = part.Name, Channels = string.Join(", ", part.ChannelNames) });
        ReindexChannels(); ChannelGrid.SelectedIndex = Channels.Count > 0 ? 0 : -1;
    }

    private void ReindexChannels()
    {
        var address = 1;
        foreach (var channel in Channels)
            channel.Number = channel.IsVirtual ? 0 : address++;
        ChannelGrid.Items.Refresh();
    }

    private static bool IsColourIntensity(string preset) => preset is
        "IntensityRed" or "IntensityGreen" or "IntensityBlue" or "IntensityWhite" or
        "IntensityAmber" or "IntensityUV";

    private static string SafeFileName(string text)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(text.Trim().Select(character => invalid.Contains(character) || char.IsWhiteSpace(character) ? '-' : character));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ChannelEditorRow
{
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public string Group { get; set; } = "Effect";
    public string Preset { get; set; } = "";
    public int GroupByte { get; set; }
    public int DefaultValue { get; set; }
    public bool IsVirtual { get; set; }
    public string Targets { get; set; } = "";
    public ObservableCollection<CapabilityEditorRow> Capabilities { get; } = [];

    public FixtureChannel ToFixtureChannel()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new InvalidDataException("Every channel must have a name.");
        if (DefaultValue is < 0 or > 255 || GroupByte is < 0 or > 1)
            throw new InvalidDataException($"Channel '{Name}' has an invalid default or control byte.");
        var capabilities = Capabilities.Select(capability => capability.ToFixtureCapability()).ToArray();
        return new FixtureChannel(Name.Trim(), string.IsNullOrWhiteSpace(Preset) ? null : Preset, Group, GroupByte, (byte)DefaultValue, capabilities);
    }

    public VirtualFixtureChannel ToVirtualFixtureChannel()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new InvalidDataException("Every virtual channel must have a name.");
        var targets = Targets.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (targets.Length == 0)
            throw new InvalidDataException($"Virtual channel '{Name}' must target at least one physical channel.");
        return new VirtualFixtureChannel(Name.Trim(), string.IsNullOrWhiteSpace(Preset) ? null : Preset, Group, targets);
    }
}

public sealed class CapabilityEditorRow
{
    public int Minimum { get; set; }
    public int Maximum { get; set; } = 255;
    public string Name { get; set; } = "";
    public string Preset { get; set; } = "";

    public FixtureCapability ToFixtureCapability()
    {
        if (Minimum is < 0 or > 255 || Maximum is < 0 or > 255)
            throw new InvalidDataException("Capability values must be between 0 and 255.");
        if (string.IsNullOrWhiteSpace(Name))
            throw new InvalidDataException("Every capability must have a name.");
        return new FixtureCapability((byte)Minimum, (byte)Maximum, Name.Trim(), string.IsNullOrWhiteSpace(Preset) ? null : Preset);
    }
}

public sealed record ModeEditorRow(string Name, IReadOnlyList<string> ChannelNames)
{
    public int ChannelCount => ChannelNames.Count;
    public string MappingSummary => string.Join(" → ", ChannelNames);
}

public sealed class PartEditorRow
{
    public string Name { get; set; } = "";
    public string Channels { get; set; } = "";
    public FixturePart ToFixturePart() => new(Name.Trim(), Channels.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
}
