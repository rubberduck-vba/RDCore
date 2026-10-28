using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using RDCore.SDK.Platform.Protocol;
using System.IO.Abstractions;
using System.Xml.Linq;

namespace RDCore.SDK.Formatting;

/// <summary>
/// Where the legacy Smart Indenter kept its settings: the Windows Registry, which only Windows has.
/// </summary>
public interface ILegacyIndenterRegistry
{
    /// <summary>
    /// Reads the values of the key of the Smart Indenter's settings: <c>HKCU\Software\VB and VBA Program Settings\Office Automation Ltd.\Smart Indenter</c>.
    /// </summary>
    /// <returns>The values by name, a <see cref="int"/> or a <see cref="string"/> each; <see langword="null"/> when there is no such key.</returns>
    IReadOnlyDictionary<string, object>? ReadSmartIndenter();

    /// <summary>
    /// Reads the tab width of the VBA editor: <c>TabWidth</c> in <c>HKCU\Software\Microsoft\VBA\6.0\Common</c>, or in <c>7.0\Common</c> when there is no <c>6.0</c> key.
    /// </summary>
    /// <returns>The tab width; <see langword="null"/> when there is none.</returns>
    int? ReadTabWidth();
}

/// <inheritdoc cref="ILegacyIndenterRegistry"/>
/// <remarks>
/// On a platform that has no registry there is nothing to read.
/// </remarks>
public sealed class WindowsLegacyIndenterRegistry : ILegacyIndenterRegistry
{
    private const string SmartIndenterKey = @"Software\VB and VBA Program Settings\Office Automation Ltd.\Smart Indenter";

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, object>? ReadSmartIndenter()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SmartIndenterKey, writable: false);
            if (key is null)
            {
                return null;
            }

            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is int or string)
                {
                    values[name] = key.GetValue(name)!;
                }
            }

            return values;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public int? ReadTabWidth()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\VBA\6.0\Common", writable: false)
                ?? Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\VBA\7.0\Common", writable: false);
            return key?.GetValue("TabWidth") is int width ? width : null;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}

/// <summary>
/// Reads the indenter settings of Rubberduck where they are, as they are: what a client answers <c>rdcore/rubberduck-indenter-settings</c> with
/// (<see cref="RubberduckIndenterSettingsParams"/>).
/// </summary>
/// <remarks>
/// <para>
/// Two places have them, on Windows: Rubberduck's own configuration, <c>%APPDATA%\Rubberduck\rubberduck.config</c> (XML, the children of its <c>IndenterSettings</c>
/// section), and the registry key of the legacy Smart Indenter (<see cref="ILegacyIndenterRegistry"/>). Rubberduck's own configuration is the more recent of the two,
/// and is the one read when both have settings. On any other platform there is nothing to read.
/// </para>
/// <para>
/// This reads and maps nothing: the names are Rubberduck's, the values are theirs (<see cref="IndenterSettingsImport.From"/> maps them).
/// </para>
/// </remarks>
/// <param name="fileSystem">The file system Rubberduck's configuration is in.</param>
/// <param name="applicationData">The roaming application data folder of the user: <c>%APPDATA%</c>; empty where there is none.</param>
/// <param name="registry">The registry.</param>
/// <param name="isWindows">Whether the platform is Windows, which the sources are of.</param>
public sealed class RubberduckIndenterSettingsReader(IFileSystem fileSystem, string applicationData, ILegacyIndenterRegistry registry, bool isWindows)
{
    /// <summary>The path of Rubberduck's configuration file, under the user's application data.</summary>
    /// <param name="applicationData">The user's application data folder.</param>
    public static string ConfigPathIn(string applicationData) => Path.Combine(applicationData, "Rubberduck", "rubberduck.config");

    /// <summary>The name of the environment variable that names another folder for the user's application data, and has no registry read: a place to try the import in.</summary>
    public const string ApplicationDataVariable = "RDCORE_APPDATA";

    private sealed class NoRegistry : ILegacyIndenterRegistry
    {
        public IReadOnlyDictionary<string, object>? ReadSmartIndenter() => null;

        public int? ReadTabWidth() => null;
    }

    /// <summary>
    /// The reader of this machine: where Rubberduck keeps its settings on Windows, and nothing elsewhere. <see cref="ApplicationDataVariable"/>, when it is set, names the
    /// folder Rubberduck's configuration is read from instead of the user's, and the registry is not read: the real settings of the user are not touched.
    /// </summary>
    public static RubberduckIndenterSettingsReader ForThisMachine()
        => Environment.GetEnvironmentVariable(ApplicationDataVariable) is { Length: > 0 } root
            ? new(new FileSystem(), root, new NoRegistry(), isWindows: true)
            : new(new FileSystem(), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), new WindowsLegacyIndenterRegistry(), OperatingSystem.IsWindows());

    /// <summary>
    /// Reads the settings.
    /// </summary>
    /// <returns>The settings as they are; <see cref="RubberduckIndenterSettingsResult.Nothing"/> when there are none, or the platform is not Windows.</returns>
    public RubberduckIndenterSettingsResult Read() => isWindows ? FromConfig() ?? FromRegistry() ?? RubberduckIndenterSettingsResult.Nothing : RubberduckIndenterSettingsResult.Nothing;

    /// <summary>
    /// The children of the <c>IndenterSettings</c> of a Rubberduck configuration file (<strong>Rubberduck</strong> <c>Configuration.UserSettings.IndenterSettings</c>).
    /// </summary>
    /// <param name="xml">The text of the file.</param>
    /// <returns>The settings, each by its name with its text; <see langword="null"/> when the file is not XML, or has no indenter settings.</returns>
    public static RubberduckIndenterSettingsResult? FromConfigXml(string xml)
    {
        XElement? settings;
        try
        {
            settings = XDocument.Parse(xml).Descendants().FirstOrDefault(element => element.Name.LocalName.Equals("IndenterSettings", StringComparison.OrdinalIgnoreCase));
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        var values = new JObject();
        foreach (var element in settings?.Elements().Where(element => !element.HasElements) ?? [])
        {
            values[element.Name.LocalName] = element.Value;
        }

        return values.Count == 0 ? null : new RubberduckIndenterSettingsResult { Source = RubberduckIndenterSettingsSource.RubberduckConfig, Values = values };
    }

    /// <summary>
    /// The settings of the legacy Smart Indenter, from the values of its registry key.
    /// </summary>
    /// <param name="smartIndenter">The values of the key by name, a <see cref="int"/> or a <see cref="string"/> each.</param>
    /// <param name="tabWidth">The <c>TabWidth</c> of the VBA editor.</param>
    public static RubberduckIndenterSettingsResult FromSmartIndenter(IReadOnlyDictionary<string, object> smartIndenter, int? tabWidth)
        => new()
        {
            Source = RubberduckIndenterSettingsSource.SmartIndenterRegistry,
            Values = new JObject(smartIndenter.Select(value => new JProperty(value.Key, JToken.FromObject(value.Value)))),
            TabWidth = tabWidth,
        };

    private RubberduckIndenterSettingsResult? FromConfig()
    {
        if (applicationData.Length == 0)
        {
            return null;
        }

        var path = ConfigPathIn(applicationData);
        try
        {
            return fileSystem.File.Exists(path) ? FromConfigXml(fileSystem.File.ReadAllText(path)) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private RubberduckIndenterSettingsResult? FromRegistry()
        => registry.ReadSmartIndenter() is { } values ? FromSmartIndenter(values, registry.ReadTabWidth()) : null;
}
