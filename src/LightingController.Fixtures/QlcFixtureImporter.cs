using System.Xml.Linq;

namespace LightingController.Fixtures;

public sealed class QlcFixtureImporter
{
    public FixtureDefinition Import(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Import(stream);
    }

    public FixtureDefinition Import(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var document = XDocument.Load(stream, LoadOptions.SetLineInfo);
        var root = document.Root ?? throw new InvalidDataException("The QLC fixture file has no root element.");
        if (root.Name.LocalName != "FixtureDefinition")
        {
            throw new InvalidDataException("The file is not a QLC+ fixture definition.");
        }

        var manufacturer = RequiredElement(root, "Manufacturer");
        var model = RequiredElement(root, "Model");
        var type = RequiredElement(root, "Type");

        var channels = root.Elements()
            .Where(element => element.Name.LocalName == "Channel")
            .Select(element => new FixtureChannel(
                RequiredAttribute(element, "Name"),
                OptionalAttribute(element, "Preset")))
            .ToDictionary(channel => channel.Name, StringComparer.Ordinal);

        var modes = root.Elements()
            .Where(element => element.Name.LocalName == "Mode")
            .Select(mode => new FixtureMode(
                RequiredAttribute(mode, "Name"),
                mode.Elements()
                    .Where(element => element.Name.LocalName == "Channel")
                    .Select(element => new FixtureModeChannel(
                        int.Parse(RequiredAttribute(element, "Number"), System.Globalization.CultureInfo.InvariantCulture),
                        element.Value.Trim()))
                    .OrderBy(channel => channel.Number)
                    .ToArray()))
            .ToArray();

        foreach (var mode in modes)
        {
            foreach (var channel in mode.Channels)
            {
                if (!channels.ContainsKey(channel.ChannelName))
                {
                    throw new InvalidDataException(
                        $"Mode '{mode.Name}' refers to unknown channel '{channel.ChannelName}'.");
                }
            }
        }

        return new FixtureDefinition(manufacturer, model, type, channels, modes);
    }

    private static string RequiredElement(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value.Trim()
        ?? throw new InvalidDataException($"Required element '{localName}' is missing.");

    private static string RequiredAttribute(XElement element, string localName) =>
        OptionalAttribute(element, localName)
        ?? throw new InvalidDataException($"Required attribute '{localName}' is missing.");

    private static string? OptionalAttribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == localName)?.Value.Trim();
}
