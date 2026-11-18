using System.Windows;

namespace LightingController.App;

public partial class PresetNameWindow : Window
{
    public PresetNameWindow(string type, int slot, string currentName)
        : this($"{type} preset", slot, currentName, true) { }

    public PresetNameWindow(string objectLabel, int slot, string currentName, bool genericObject)
    {
        InitializeComponent(); PromptText.Text = $"Rename {objectLabel} {slot}"; NameText.Text = currentName; NameText.SelectAll(); Loaded += (_, _) => NameText.Focus();
    }
    public string PresetName => NameText.Text.Trim();
    private void Rename_Click(object sender, RoutedEventArgs e) { if (string.IsNullOrWhiteSpace(PresetName)) return; DialogResult = true; }
}
