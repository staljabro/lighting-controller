using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using LightingController.Core;

namespace LightingController.App;

public partial class DmxMonitorWindow : Window
{
    private readonly Func<int, byte[]> _frameProvider; private readonly FixturePatch _patch; private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    public ObservableCollection<DmxMonitorRow> Rows { get; } = [];
    public DmxMonitorWindow(Func<int, byte[]> frameProvider, FixturePatch patch) { _frameProvider = frameProvider; _patch = patch; InitializeComponent(); ChannelGrid.ItemsSource = Rows; _timer.Tick += (_, _) => RefreshRows(); Loaded += (_, _) => _timer.Start(); Closed += (_, _) => _timer.Stop(); RefreshRows(); }
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRows();
    private void RefreshRows() { if (!int.TryParse(UniverseText.Text, out var universe)) return; var frame = _frameProvider(universe); Rows.Clear(); for (var address = 1; address <= 512; address++) { var fixture = _patch.Fixtures.FirstOrDefault(item => item.Universe == universe && address >= item.Address && address <= item.LastAddress); Rows.Add(new(address, frame[address - 1], fixture?.Name ?? "", fixture is null ? "" : $"Channel {address - fixture.Address + 1}")); } }
}
public sealed record DmxMonitorRow(int Address, byte Value, string Fixture, string Channel);
