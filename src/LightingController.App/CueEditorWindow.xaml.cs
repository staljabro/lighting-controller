using System.Globalization;
using System.Windows;
using LightingController.Core;

namespace LightingController.App;
public partial class CueEditorWindow : Window
{
    private readonly Cue _cue;
    public CueEditorWindow(Cue cue) { _cue = cue; InitializeComponent(); TypeCombo.ItemsSource = Enum.GetValues<CueTriggerType>(); NumberText.Text = cue.Number; NameText.Text = cue.Name; TypeCombo.SelectedItem = cue.TriggerType; FadeText.Text = cue.FadeIn.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture); FollowText.Text = cue.Follow?.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) ?? ""; NotesText.Text = cue.Notes; }
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NumberText.Text) || !double.TryParse(FadeText.Text, CultureInfo.InvariantCulture, out var fade) || fade < 0) { MessageBox.Show(this, "Enter a cue number and a valid fade time."); return; }
        double? follow = null; double parsed = 0; if (!string.IsNullOrWhiteSpace(FollowText.Text) && (!double.TryParse(FollowText.Text, CultureInfo.InvariantCulture, out parsed) || parsed < 0)) { MessageBox.Show(this, "Follow time must be blank or a positive number."); return; } else if (!string.IsNullOrWhiteSpace(FollowText.Text)) follow = parsed;
        _cue.Number = NumberText.Text.Trim(); _cue.Name = NameText.Text.Trim(); _cue.TriggerType = (CueTriggerType)(TypeCombo.SelectedItem ?? CueTriggerType.Go); _cue.FadeIn = TimeSpan.FromSeconds(fade); _cue.Follow = _cue.TriggerType == CueTriggerType.Follow ? TimeSpan.FromSeconds(follow ?? fade) : null; _cue.Notes = NotesText.Text; DialogResult = true;
    }
}
