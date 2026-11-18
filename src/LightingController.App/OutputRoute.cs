namespace LightingController.App;

public enum OutputTransport { ArtNet, Sacn, UsbDmx }
public enum MidiBindingTarget { Command, Softkey, Encoder }
public enum MidiValueMode { Button, Absolute, McuRelative }

public sealed class OutputRoute
{
    public bool Enabled { get; set; } = true;
    public int Universe { get; set; } = 1;
    public OutputTransport Transport { get; set; } = OutputTransport.ArtNet;
    public string Destination { get; set; } = "127.0.0.1";
    public string Device { get; set; } = "";
}

public sealed class MidiCommandBinding
{
    public int Status { get; set; } = 144;
    public int Data1 { get; set; }
    public string Command { get; set; } = "GO";
    public MidiBindingTarget TargetType { get; set; } = MidiBindingTarget.Command;
    public string Target { get; set; } = "";
    public MidiValueMode ValueMode { get; set; } = MidiValueMode.Button;
    public double OutputMinimum { get; set; }
    public double OutputMaximum { get; set; } = 255;
    public string EffectiveTarget => string.IsNullOrWhiteSpace(Target) ? Command : Target;
}
