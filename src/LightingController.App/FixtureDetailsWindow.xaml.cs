using System.Windows;
using System.Windows.Threading;
using LightingController.Core;
using LightingController.Fixtures;

namespace LightingController.App;
public partial class FixtureDetailsWindow : Window
{
    private readonly PatchedFixture _patch; private readonly FixtureDefinition _definition; private readonly FixtureMode _mode; private readonly Func<int, byte[]> _frameProvider; private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    public FixtureDetailsWindow(PatchedFixture patch, FixtureDefinition definition, FixtureMode mode, Func<int, byte[]> frameProvider) { _patch = patch; _definition = definition; _mode = mode; _frameProvider = frameProvider; InitializeComponent(); TitleText.Text = $"{patch.Id}  {patch.Name} — U{patch.Universe}.{patch.Address}"; _timer.Tick += (_, _) => Refresh(); Loaded += (_, _) => _timer.Start(); Closed += (_, _) => _timer.Stop(); Refresh(); }
    private void Refresh() { var frame = _frameProvider(_patch.Universe); OutputGrid.ItemsSource = _mode.Channels.OrderBy(item => item.Number).Select(item => new FixtureOutputRow(_patch.Address + item.Number, item.ChannelName, _definition.Channels[item.ChannelName].Preset ?? "", frame[_patch.Address - 1 + item.Number])).ToArray(); }
}
public sealed record FixtureOutputRow(int Address, string Channel, string Preset, byte Value);
