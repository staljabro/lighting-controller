namespace LightingController.Core;

public static class ConsoleCommandParser
{
    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["s"] = "store", ["u"] = "update", ["c"] = "colour", ["p"] = "preset",
        ["i"] = "intensity", ["pos"] = "position", ["g"] = "gobo", ["b"] = "beam",
        ["f"] = "focus", ["ctl"] = "control", ["n"] = "rename", ["x"] = "clear"
    };

    public static string[] Parse(string input) => input
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .SelectMany(ExpandToken)
        .ToArray();

    public static string RemoveLastStroke(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var trimmed = input.TrimEnd();
        var separator = trimmed.LastIndexOf(' ');
        return separator < 0 ? string.Empty : trimmed[..(separator + 1)];
    }

    private static IEnumerable<string> ExpandToken(string token)
    {
        if (token.Length is >= 1 and <= 3 && token.All(character => character is 'x' or 'X'))
            return Enumerable.Repeat("clear", token.Length);
        return [Aliases.GetValueOrDefault(token, token).ToLowerInvariant()];
    }
}
