namespace LightingController.Core;

public static class ConsoleCommandParser
{
    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["s"] = "store", ["u"] = "update", ["c"] = "colour", ["p"] = "preset",
        ["i"] = "intensity", ["pos"] = "position", ["g"] = "gobo", ["b"] = "beam",
        ["f"] = "focus", ["ctl"] = "control", ["n"] = "rename"
    };

    public static string[] Parse(string input) => input
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(token => Aliases.GetValueOrDefault(token, token).ToLowerInvariant())
        .ToArray();
}
