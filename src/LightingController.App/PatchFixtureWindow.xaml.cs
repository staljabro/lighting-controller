using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LightingController.Core;
using LightingController.Fixtures;

namespace LightingController.App;

public partial class PatchFixtureWindow : Window
{
    private readonly FixtureDefinition _definition;

    public PatchFixtureWindow(FixtureDefinition definition, int suggestedId, int suggestedAddress)
    {
        _definition = definition;
        InitializeComponent();
        FixtureTitle.Text = $"{definition.Manufacturer} {definition.Model}";
        FixtureIdText.Text = suggestedId.ToString(CultureInfo.InvariantCulture);
        FixtureNameText.Text = $"{definition.Model} {suggestedId}";
        AddressText.Text = suggestedAddress.ToString(CultureInfo.InvariantCulture);
        ModeCombo.ItemsSource = definition.Modes;
        ModeCombo.SelectedIndex = definition.Modes.Count > 0 ? 0 : -1;
        if (definition.Modes.Count == 0)
        {
            ValidationText.Text = "This profile has no fixture modes and cannot be patched.";
        }
    }

    public PatchedFixture? Fixture { get; private set; }
    public FixtureMode? SelectedMode { get; private set; }

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModeCombo.SelectedItem is FixtureMode mode)
        {
            FootprintText.Text = $"{mode.Channels.Count} channels";
        }
    }

    private void AddFixture_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ModeCombo.SelectedItem is not FixtureMode mode)
            {
                throw new InvalidOperationException("Select a fixture mode.");
            }

            Fixture = new PatchedFixture(
                int.Parse(FixtureIdText.Text, CultureInfo.InvariantCulture),
                FixtureNameText.Text,
                _definition.Manufacturer,
                _definition.Model,
                mode.Name,
                int.Parse(UniverseText.Text, CultureInfo.InvariantCulture),
                int.Parse(AddressText.Text, CultureInfo.InvariantCulture),
                mode.Channels.Count);
            SelectedMode = mode;
            DialogResult = true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or InvalidOperationException)
        {
            ValidationText.Text = exception.Message;
        }
    }
}
