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
        FixtureTypeText.Text = definition.Model;
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
    public IReadOnlyList<PatchedFixture> Fixtures { get; private set; } = [];
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

            var firstId = int.Parse(FixtureIdText.Text, CultureInfo.InvariantCulture);
            var firstAddress = int.Parse(AddressText.Text, CultureInfo.InvariantCulture);
            var quantity = int.Parse(QuantityText.Text, CultureInfo.InvariantCulture);
            var idOffset = int.Parse(IdOffsetText.Text, CultureInfo.InvariantCulture);
            var requestedAddressOffset = int.Parse(AddressOffsetText.Text, CultureInfo.InvariantCulture);
            if (quantity < 1 || quantity > 999) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be between 1 and 999.");
            if (idOffset < 1) throw new ArgumentOutOfRangeException(nameof(idOffset), "Fixture ID offset must be positive.");
            var addressOffset = requestedAddressOffset == 0 ? mode.Channels.Count : requestedAddressOffset;
            var fixtures = new List<PatchedFixture>();
            for (var index = 0; index < quantity; index++)
                fixtures.Add(new PatchedFixture(firstId + index * idOffset, quantity == 1 ? FixtureNameText.Text : $"{FixtureNameText.Text} {index + 1}", _definition.Manufacturer, _definition.Model, mode.Name,
                    int.Parse(UniverseText.Text, CultureInfo.InvariantCulture), firstAddress + index * addressOffset, mode.Channels.Count));
            Fixtures = fixtures; Fixture = fixtures[0];
            SelectedMode = mode;
            DialogResult = true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or InvalidOperationException)
        {
            ValidationText.Text = exception.Message;
        }
    }
}
