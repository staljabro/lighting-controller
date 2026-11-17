using System.Collections.ObjectModel;
using System.Windows;

namespace LightingController.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Cues =
        [
            new("1", "Preset", "2.0", "0.0", "GO"),
            new("2", "Opening look", "3.0", "0.0", "GO"),
            new("3", "Performance", "1.5", "0.5", "GO"),
            new("4", "Blackout", "2.0", "0.0", "GO")
        ];
        DataContext = this;
    }

    public ObservableCollection<CueRow> Cues { get; }
}

public sealed record CueRow(string Number, string Name, string Fade, string Delay, string Follow);
