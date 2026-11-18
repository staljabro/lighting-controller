using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LightingController.App;

public partial class RotaryControl : UserControl
{
    private Point _lastPoint;
    private bool _dragging;

    public RotaryControl() { InitializeComponent(); UpdateDisplay(); }
    public event RoutedPropertyChangedEventHandler<double>? ValueChanged;
    public event EventHandler<RotaryDeltaEventArgs>? DeltaRequested;

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(RotaryControl), new PropertyMetadata("ENCODER", Changed));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(RotaryControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, Changed, Coerce));
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(RotaryControl), new PropertyMetadata(0d, Changed));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(RotaryControl), new PropertyMetadata(255d, Changed));
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(nameof(Step), typeof(double), typeof(RotaryControl), new PropertyMetadata(1d));
    public static readonly DependencyProperty IsMixedProperty = DependencyProperty.Register(nameof(IsMixed), typeof(bool), typeof(RotaryControl), new PropertyMetadata(false, Changed));

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public bool IsMixed { get => (bool)GetValue(IsMixedProperty); set => SetValue(IsMixedProperty, value); }

    private static object Coerce(DependencyObject d, object value) { var c = (RotaryControl)d; return Math.Clamp((double)value, c.Minimum, c.Maximum); }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) { var c = (RotaryControl)d; c.UpdateDisplay(); if (e.Property == ValueProperty) c.ValueChanged?.Invoke(c, new RoutedPropertyChangedEventArgs<double>((double)e.OldValue, (double)e.NewValue)); }
    private void UpdateDisplay() { if (LabelText is null) return; LabelText.Text = Label; ValueText.Text = IsMixed ? "--" : Value.ToString(Step < 1 ? "0.0" : "0"); }
    private void Adjust(double delta)
    {
        var requested = delta * Step;
        if (Math.Abs(requested) < double.Epsilon) return;
        DeltaRequested?.Invoke(this, new RotaryDeltaEventArgs(requested));
        if (IsMixed) ValueChanged?.Invoke(this, new RoutedPropertyChangedEventArgs<double>(Value, Value + requested));
        else Value = Math.Clamp(Value + requested, Minimum, Maximum);
    }
    private void Control_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { _dragging = true; _lastPoint = e.GetPosition(this); CaptureMouse(); }
    private void Control_MouseMove(object sender, MouseEventArgs e) { if (!_dragging) return; var point = e.GetPosition(this); Adjust((_lastPoint.Y - point.Y) + (point.X - _lastPoint.X)); _lastPoint = point; }
    private void Control_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { _dragging = false; ReleaseMouseCapture(); }
    private void Control_MouseWheel(object sender, MouseWheelEventArgs e) => Adjust(e.Delta > 0 ? 1 : -1);
    private void Control_ManipulationDelta(object sender, ManipulationDeltaEventArgs e) => Adjust((-e.DeltaManipulation.Translation.Y + e.DeltaManipulation.Translation.X) / 2);
}

public sealed class RotaryDeltaEventArgs(double delta) : EventArgs
{
    public double Delta { get; } = delta;
}
