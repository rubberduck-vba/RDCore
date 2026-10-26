using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Server.Configuration;

/// <summary>
/// How the live settings travel between a client and a server (<strong>LSP 3.17</strong> §Configuration Request): the configuration section <c>rdcore</c>,
/// whose keys are those under <see cref="SettingsSchema.Root"/> - <c>rdcore.Server.Verbose</c> is <c>Configuration:Server:Verbose</c>.
/// </summary>
/// <remarks>
/// A server asks for the section with <c>workspace/configuration</c> when it is initialized, and again whenever its client says the configuration changed
/// (<c>workspace/didChangeConfiguration</c>, whose own settings are not read: the server asks). A client answers with what its component hands down
/// (<see cref="ILiveSettings.Current"/>): a JSON object of sections, a list of texts as an array.
/// </remarks>
public static class LiveSettingsProtocol
{
    /// <summary>The configuration section of the platform's settings.</summary>
    public const string Section = "rdcore";

    /// <summary>
    /// What a client answers a <c>workspace/configuration</c> request with: for each item, the section it names of <paramref name="settings"/> - all of them for
    /// the item that names no section, or <see cref="Section"/> - or <see langword="null"/> for a section that is no setting's.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="settings">What the client's component hands down, by the keys under <see cref="SettingsSchema.Root"/>, separated by <c>:</c>.</param>
    public static Container<JToken> Answer(ConfigurationParams request, IReadOnlyDictionary<string, string> settings)
    {
        var tree = Tree(settings);
        return new Container<JToken>(request.Items.Select(item => SectionOf(tree, item.Section)));
    }

    /// <summary>
    /// What a client said the settings are, as a server's configuration has them once it asked: the keys of <see cref="Section"/>, under
    /// <see cref="SettingsSchema.Root"/>.
    /// </summary>
    /// <param name="configuration">The configuration the client's answers are in (OmniSharp's <c>ILanguageServerConfiguration</c>).</param>
    public static Dictionary<string, string> Read(IConfiguration configuration)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in configuration.GetSection(Section).AsEnumerable(makePathsRelative: true))
        {
            if (value is not null)
            {
                settings[key] = value;
            }
        }

        return settings;
    }

    private static JToken SectionOf(JObject tree, string? section)
    {
        if (string.IsNullOrEmpty(section))
        {
            return new JObject { [Section] = tree };
        }

        var parts = section.Split('.');
        if (!string.Equals(parts[0], Section, StringComparison.OrdinalIgnoreCase))
        {
            return JValue.CreateNull();
        }

        JToken? node = tree;
        foreach (var part in parts[1..])
        {
            node = node is JObject parent ? parent.Properties().FirstOrDefault(property => string.Equals(property.Name, part, StringComparison.OrdinalIgnoreCase))?.Value : null;
        }

        return node ?? JValue.CreateNull();
    }

    // the sections of the keys, nested; a section whose keys are all numbers is a list.
    private static JObject Tree(IReadOnlyDictionary<string, string> settings)
    {
        var tree = new JObject();
        foreach (var (key, value) in settings.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var parts = key.Split(':');
            var section = tree;
            foreach (var part in parts[..^1])
            {
                if (section[part] is not JObject child)
                {
                    child = new JObject();
                    section[part] = child;
                }

                section = child;
            }

            section[parts[^1]] = value;
        }

        return (JObject)Lists(tree);
    }

    private static JToken Lists(JToken node)
    {
        if (node is not JObject section)
        {
            return node;
        }

        var properties = section.Properties().ToList();
        if (properties.Count > 0 && properties.All(property => int.TryParse(property.Name, out _)))
        {
            return new JArray(properties.OrderBy(property => int.Parse(property.Name)).Select(property => Lists(property.Value)));
        }

        var nested = new JObject();
        foreach (var property in properties)
        {
            nested[property.Name] = Lists(property.Value);
        }

        return nested;
    }
}
