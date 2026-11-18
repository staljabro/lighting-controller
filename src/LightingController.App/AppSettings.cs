using System.Text.Json;
using System.Windows.Input;
using System.IO;

namespace LightingController.App;

public sealed class AppSettings
{
    public bool ShortcutsEnabled { get; set; } = true;
    public List<HotkeyBinding> Hotkeys { get; set; } = DefaultHotkeys();
    public List<OutputRoute> OutputRoutes { get; set; } = [new()];
    public string MidiInputDevice { get; set; } = "";
    public string MidiOutputDevice { get; set; } = "";
    public string LastProjectPath { get; set; } = "";
    public List<MidiCommandBinding> MidiBindings { get; set; } = [];

    public static List<HotkeyBinding> DefaultHotkeys() =>
    [
        new("S", "STORE"), new("U", "UPDATE"), new("P", "PRESET"), new("C", "COLOUR"),
        new("I", "INTENSITY"), new("B", "BEAM"), new("F", "FOCUS"), new("O", "GOBO"),
        new("T", "POSITION"), new("R", "RELEASE"), new("G", "GO"), new("Q", "CUE"),
        new("D", "DELETE"), new("X", "CLEAR"), new("M", "STOMP")
    ];

    public string? CommandFor(Key key, ModifierKeys modifiers = ModifierKeys.None) => Hotkeys.FirstOrDefault(item => string.Equals(item.Key, key.ToString(), StringComparison.OrdinalIgnoreCase) && string.Equals(item.Modifiers, modifiers.ToString(), StringComparison.OrdinalIgnoreCase))?.Command;
}

public sealed class HotkeyBinding
{
    public HotkeyBinding() { }
    public HotkeyBinding(string key, string command, string modifiers = "None") { Key = key; Command = command; Modifiers = modifiers; }
    public string Key { get; set; } = "";
    public string Command { get; set; } = "";
    public string Modifiers { get; set; } = "None";
}

public static class AppSettingsStore
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightingController");
    private static readonly string FileName = Path.Combine(Folder, "settings.json");
    public static AppSettings Load()
    {
        try { return File.Exists(FileName) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FileName)) ?? new() : new(); }
        catch { return new(); }
    }
    public static void Save(AppSettings settings) { Directory.CreateDirectory(Folder); File.WriteAllText(FileName, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })); }
}
