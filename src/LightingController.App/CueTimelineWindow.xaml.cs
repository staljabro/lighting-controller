using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using LightingController.Core;

namespace LightingController.App;
public partial class CueTimelineWindow : Window
{
    private readonly CueStack _stack; private readonly Func<int> _playbackIndex; private readonly Action _changed; private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private int _selected = -1; private Border? _dragged; private Point _dragStart; private double _originalLeft; private double _originalWidth; private bool _resizing;
    public CueTimelineWindow(CueStack stack, Func<int>? playbackIndex = null, Action? changed = null)
    {
        _stack = stack; _playbackIndex = playbackIndex ?? (() => -1); _changed = changed ?? (() => { }); InitializeComponent(); Title = $"{stack.Name} Timeline";
        Loaded += (_, _) => { Render(); _timer.Start(); }; Closed += (_, _) => _timer.Stop(); _timer.Tick += (_, _) => DrawPlayhead();
    }
    private void Render()
    {
        TimelineCanvas.Children.Clear(); double x = 20;
        for (var index = 0; index < _stack.Cues.Count; index++)
        {
            var cue = _stack.Cues[index]; var width = WidthFor(cue); var border = CreateBlock(cue, index, width); Canvas.SetLeft(border, x); Canvas.SetTop(border, 70); TimelineCanvas.Children.Add(border); x += width + 12;
        }
        TimelineCanvas.Width = Math.Max(1100, x + 20); DrawPlayhead();
    }
    private Border CreateBlock(Cue cue, int index, double width)
    {
        var border = new Border { Tag = index, Width = width, Height = 130, Background = new SolidColorBrush(Color.FromRgb(32, 37, 42)), BorderBrush = cue.TriggerType == CueTriggerType.Follow ? Brushes.Orange : cue.TriggerType == CueTriggerType.Wait ? Brushes.MediumPurple : Brushes.DeepSkyBlue, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(5), Cursor = Cursors.SizeAll };
        var grid = new Grid(); grid.Children.Add(new TextBlock { Text = $"Cue {cue.Number}\n{cue.Name}\nFade {cue.FadeIn.TotalSeconds:0.0}s\n{cue.TriggerType} {(cue.Follow?.TotalSeconds.ToString("0.0") ?? "")}", Margin = new Thickness(10), Foreground = Brushes.White }); grid.Children.Add(new Border { Width = 14, Background = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Right, Cursor = Cursors.SizeWE }); border.Child = grid;
        border.MouseLeftButtonDown += BlockDown; border.MouseMove += BlockMove; border.MouseLeftButtonUp += BlockUp; return border;
    }
    private static double WidthFor(Cue cue) => Math.Max(120, Math.Max(.5, cue.FadeIn.TotalSeconds) * 90);
    private void BlockDown(object sender, MouseButtonEventArgs e)
    {
        _dragged = (Border)sender; _selected = (int)_dragged.Tag; _dragStart = e.GetPosition(TimelineCanvas); _originalLeft = Canvas.GetLeft(_dragged); _originalWidth = _dragged.Width; _resizing = e.GetPosition(_dragged).X >= _dragged.ActualWidth - 20; _dragged.CaptureMouse(); SelectCue(); e.Handled = true;
    }
    private void BlockMove(object sender, MouseEventArgs e)
    {
        if (_dragged is null || e.LeftButton != MouseButtonState.Pressed) return; var point = e.GetPosition(TimelineCanvas); var delta = point.X - _dragStart.X;
        if (_resizing) _dragged.Width = Math.Max(120, _originalWidth + delta); else Canvas.SetLeft(_dragged, Math.Max(0, _originalLeft + delta));
    }
    private void BlockUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragged is null) return; _dragged.ReleaseMouseCapture();
        if (_resizing) _stack.Cues[_selected].FadeIn = TimeSpan.FromSeconds(Math.Max(.1, _dragged.Width / 90));
        else
        {
            var center = Canvas.GetLeft(_dragged) + _dragged.Width / 2; var ordered = TimelineCanvas.Children.OfType<Border>().Where(item => item.Tag is int).OrderBy(item => Canvas.GetLeft(item) + item.Width / 2).ToList(); var newIndex = ordered.IndexOf(_dragged); if (newIndex >= 0 && newIndex != _selected) { _stack.Move(_selected, newIndex); _selected = newIndex; }
        }
        _dragged = null; _changed(); Render(); SelectCue(); e.Handled = true;
    }
    private void SelectCue() { if (_selected < 0 || _selected >= _stack.Cues.Count) return; var cue = _stack.Cues[_selected]; SelectedText.Text = $"Cue {cue.Number} — {cue.Name}"; FadeText.Text = cue.FadeIn.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture); FollowText.Text = cue.Follow?.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) ?? ""; }
    private void ApplyTiming_Click(object sender, RoutedEventArgs e) { if (_selected < 0 || !double.TryParse(FadeText.Text, CultureInfo.InvariantCulture, out var fade)) return; var cue = _stack.Cues[_selected]; cue.FadeIn = TimeSpan.FromSeconds(Math.Max(0, fade)); if (double.TryParse(FollowText.Text, CultureInfo.InvariantCulture, out var follow)) { cue.Follow = TimeSpan.FromSeconds(Math.Max(0, follow)); cue.TriggerType = CueTriggerType.Follow; } _changed(); Render(); SelectCue(); }
    private void EditCue_Click(object sender, RoutedEventArgs e) { if (_selected < 0) return; if (new CueEditorWindow(_stack.Cues[_selected]) { Owner = this }.ShowDialog() == true) { _changed(); Render(); SelectCue(); } }
    private void DrawPlayhead() { foreach (var oldLine in TimelineCanvas.Children.OfType<Line>().ToArray()) TimelineCanvas.Children.Remove(oldLine); var index = _playbackIndex(); if (index < 0 || index >= _stack.Cues.Count) return; var blocks = TimelineCanvas.Children.OfType<Border>().Where(item => item.Tag is int).OrderBy(item => (int)item.Tag).ToArray(); if (index >= blocks.Length) return; var x = Canvas.GetLeft(blocks[index]); var playhead = new Line { X1 = x, X2 = x, Y1 = 30, Y2 = 235, Stroke = Brushes.Red, StrokeThickness = 3, IsHitTestVisible = false }; TimelineCanvas.Children.Add(playhead); }
}
