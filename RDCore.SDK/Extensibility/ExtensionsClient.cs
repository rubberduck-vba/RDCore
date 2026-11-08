using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.SDK.Client;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Platform;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using System.IO.Abstractions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RDCore.SDK.Extensibility;

/// <summary>
/// An <em>extensions provider</em> service that manages the extensions of a <see cref="RDCoreExtensionServerApp"/>.
/// </summary>
/// <param name="options">The <em>extensions</em> configuration settings.</param>
/// <param name="validation">A service that validates an <em>extension manifest</em>.</param>
/// <param name="fileSystem">Abstracts the <em>file system</em>.</param>
/// <param name="environment">The platform environment the extensions are found in.</param>
/// <param name="logger">The service's logger.</param>
public class ExtensionsClient(
    IOptions<SdkAppOptions> options,
    IExtensionManifestValidationService validation,
    IFileSystem fileSystem,
    IPlatformEnvironment environment,
    ILogger<ExtensionsClient> logger) : IExtensionsProvider
{
    private readonly IExtensionManifestValidationService _validation = validation;
    private readonly Dictionary<string, ExtensionInfo> _extensions = [];
    private readonly Dictionary<ExtensionInfo, IRDCoreClientApp> _clients = [];

    private IDirectoryInfo ExtensionsFolder
        => fileSystem.DirectoryInfo.New(environment.Resolve(options.Value.Platform.Extensions.Path));

    /// <summary>
    /// Enables the specified <see cref="ExtensionInfo"/> if the manifest and associated executable pass validation.
    /// </summary>
    /// <param name="extension">The deserialized <em>manifest</em> of the extension to enable.</param>
    /// <returns><c>true</c> if the specified extension passed validation and was enabled, <c>false</c> otherwise.</returns>
    public bool Allow(ExtensionInfo extension)
    {
        if (_extensions.TryGetValue(extension.Title, out var found)
            && !options.Value.Platform.Extensions.Allowed.Contains(found.Title))
        {
            var validation = _validation.Validate(found);
            if (validation == ExtensionValidationFlags.NoFlags)
            {
                options.Value.Platform.Extensions.Allowed.Add(found.Title);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Disables the specified <see cref="ExtensionInfo"/>.
    /// </summary>
    /// <param name="extension">The deserialized <em>manifest</em> of the extension to disable.</param>
    /// <returns><c>true</c> if the specified extension could be disabled, <c>false</c> otherwise.</returns>
    public bool Block(ExtensionInfo extension)
    {
        if (_extensions.TryGetValue(extension.Title, out var found)
            && !options.Value.Platform.Extensions.Blocked.Any(e => e.Title == found.Title))
        {
            options.Value.Platform.Extensions.Blocked.Add(new() { Title = found.Title, Flags = ExtensionValidationFlags.Blocked });
            return true;
        }
        return false;
    }

    /// <summary>
    /// Creates a serializable <see cref="ExtensionInfo"/> model for the specified extension executable <c>name</c>, with the specified <c>description</c>.
    /// </summary>
    /// <param name="name">The name of the executable extension.</param>
    /// <param name="description">A short description of the extension.</param>
    /// <remarks>
    /// The implementation validates that it is executing this method inside the target extension folder.
    /// </remarks>
    /// <returns><c>null</c> if the specified extension cannot be described.</returns>
    public ExtensionInfo? Describe(string name, string description)
    {
        // guard: this must run from inside <ExtensionsRoot>/<ExtensionFolder>/ so the folder name is
        // the extension title. Compare the resolved extensions root, not a relative path fragment.
        var currentDirectory = fileSystem.DirectoryInfo.New(fileSystem.Directory.GetCurrentDirectory());
        var extensionsRoot = environment.Resolve(options.Value.Platform.Extensions.Path);
        if (!string.Equals(currentDirectory.Parent?.FullName, extensionsRoot, StringComparison.InvariantCultureIgnoreCase))
        {
            return null;
        }

        // resolve the named executable against the current directory: Assembly.LoadFrom and the hash
        // both need a real path, and the caller passes a bare filename.
        var executablePath = fileSystem.Path.GetFullPath(name);
        string fileHash;
        using (var executableStream = fileSystem.File.OpenRead(executablePath))
        {
            fileHash = Convert.ToBase64String(SHA512.HashData(executableStream));
        }

        // reflect the managed assembly (the companion .dll next to the launch .exe) for its name,
        // version, publisher metadata, and advertised platform capabilities.
        var assembly = Assembly.LoadFrom(fileSystem.Path.ChangeExtension(executablePath, ".dll"));
        var assemblyName = assembly.GetName();

        var version = assemblyName.Version ?? new Version(0, 0, 0);
        var title = currentDirectory.Name;
        var publisher = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty;
        var effectiveDescription = string.IsNullOrWhiteSpace(description)
            ? assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? string.Empty
            : description;

        var capabilities = ProvidedCorePlatformCapabilities.Reflect(assembly)
            .Select(capability => new PlatformExtensionServerCapability(capability))
            .ToArray();

        // the SDK the extension was built against is the one its assembly references: read from its metadata, nothing of the extension runs.
        var sdkName = typeof(ExtensionInfo).Assembly.GetName().Name;
        var sdkVersion = assembly.GetReferencedAssemblies()
            .FirstOrDefault(reference => string.Equals(reference.Name, sdkName, StringComparison.Ordinal))?
            .Version;

        return new ExtensionInfo
        {
            Name = name,
            Title = title,
            Version = version,
            Publisher = publisher,
            PublisherWebUrl = string.Empty,
            Description = effectiveDescription,
            FileHash = fileHash,
            Capabilities = capabilities,
            DiagnosticPrefix = assembly.GetCustomAttribute<DiagnosticPrefixAttribute>()?.Prefix,
            SdkVersion = sdkVersion,
        }.WithSettingsOf(assembly);
    }

    /// <summary>
    /// Scans the <em>extensions folder</em> for subfolders containing an <em>extension manifest</em>.
    /// </summary>
    /// <returns>
    /// Returns all discovered <strong>valid</strong> extensions.
    /// </returns>
    /// <remarks>
    /// The folders are read in the order of their names. One extension is found of a diagnostic prefix (<see cref="ExtensionInfo.DiagnosticPrefix"/>): one that
    /// declares a prefix an extension found before it declares, one of the language's own (<c>VBC</c>, <c>VBR</c>, <c>VBA</c>), or a prefix that is not three
    /// letters <c>A</c> to <c>Z</c>, is not.
    /// </remarks>
    public IEnumerable<ExtensionInfo> Discover()
    {
        var manifestFileName = options.Value.Platform.Extensions.Manifest;

        // a platform that has no folder of extensions has none: a process that is not run from within a platform (a verb run from a build folder) is not an error.
        if (!ExtensionsFolder.Exists)
        {
            yield break;
        }

        // the language's own codes are the platform's, and no extension reports under them.
        var prefixes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RDCoreDiagnosticCode.CompilePrefix] = nameof(RDCore),
            [RDCoreDiagnosticCode.RuntimePrefix] = nameof(RDCore),
            [RDCoreDiagnosticCode.ApplicationPrefix] = nameof(RDCore),
        };

        // the order of the folders decides which of two extensions of a prefix is found: it is the same on every file system.
        foreach (var folder in ExtensionsFolder.EnumerateDirectories().OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase))
        {
            var title = folder.Name;
            var manifest = folder.GetFiles(manifestFileName).FirstOrDefault();
            if (manifest is not null && ReadManifest(manifest, title) is ExtensionInfo extensionInfo)
            {
                var validation = _validation.Validate(extensionInfo);
                if (validation == ExtensionValidationFlags.NoFlags && !ClaimPrefix(prefixes, extensionInfo))
                {
                    continue;
                }

                if (validation == ExtensionValidationFlags.NoFlags)
                {
                    _extensions[title] = extensionInfo;
                    yield return extensionInfo;
                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        var message = TraceMessages.ValidExtensionFound.Replace("{$NAME}", extensionInfo.Name);
                        logger.LogInformation("{message}", message);
                    }
                }
                else if(logger.IsEnabled(LogLevel.Warning))
                {
                    var message = Exceptions.InvalidExtension_Message;
                    var verbose = string.Empty;
                    if (options.Value.Server.Verbose)
                    {
                        verbose += GetVerboseValidationFlags(validation);
                    }
                    logger.LogWarning("{message}{verbose}", message, verbose);
                }
            }
            else if (manifest is null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("No manifest was found for extension '{title}'.", title);
            }
        }
    }

    // whether the extension's diagnostic prefix is its own: none, or one no extension found before it declares.
    private bool ClaimPrefix(Dictionary<string, string> prefixes, ExtensionInfo extension)
    {
        if (extension.DiagnosticPrefix is not { } prefix)
        {
            return true;
        }

        if (!RDCoreDiagnosticCode.IsPrefix(prefix))
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("🚫 Extension '{title}' declares the diagnostic prefix '{prefix}', which is not three letters A to Z: it is not started.", extension.Title, prefix);
            }
            return false;
        }

        if (prefixes.TryGetValue(prefix, out var owner))
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("🚫 Extension '{title}' declares the diagnostic prefix '{prefix}', which '{owner}' already declares: it is not started.", extension.Title, prefix, owner);
            }
            return false;
        }

        prefixes[prefix] = extension.Title;
        return true;
    }

    // dispose the read stream: a leaked handle blocks a later rewrite (e.g. describe-ext --overwrite).
    // a manifest that is not valid JSON, or lacks a required property (one written before a property was added), describes no extension: it is not started.
    private ExtensionInfo? ReadManifest(IFileInfo manifest, string title)
    {
        using var stream = manifest.OpenRead();
        try
        {
            return JsonSerializer.Deserialize<ExtensionInfo>(stream);
        }
        catch (JsonException exception)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("The manifest of extension '{title}' could not be read: {error}", title, exception.Message);
            }
            return null;
        }
    }

    private static string GetVerboseValidationFlags(ExtensionValidationFlags flags)
    {
        if (flags == ExtensionValidationFlags.NoFlags)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine(Exceptions.InvalidExtension_Verbose);

        AppendValidationFlagMessage(flags, ExtensionValidationFlags.Blocked, builder, Exceptions.ValidationFlags_Blocked);
        AppendValidationFlagMessage(flags, ExtensionValidationFlags.NotAllowed, builder, Exceptions.ValidationFlags_NotAllowed);
        AppendValidationFlagMessage(flags, ExtensionValidationFlags.LocationMismatch, builder, Exceptions.ValidationFlags_LocationMismatch);
        AppendValidationFlagMessage(flags, ExtensionValidationFlags.FileNotFound, builder, Exceptions.ValidationFlags_FileNotFound);
        AppendValidationFlagMessage(flags, ExtensionValidationFlags.FileHashMismatch, builder, Exceptions.ValidationFlags_FileHashMismatch);

        return builder.ToString();
    }

    private static void AppendValidationFlagMessage(ExtensionValidationFlags flags, ExtensionValidationFlags check, StringBuilder builder, string verbose)
    {
        if (flags.HasFlag(check))
        {
            builder.AppendLine($"[{check}:{(int)check}:X2] {verbose}");
        }
    }
}
