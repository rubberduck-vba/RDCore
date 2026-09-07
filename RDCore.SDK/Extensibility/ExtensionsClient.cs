using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.SDK.Client;
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
        string signature;
        using (var executableStream = fileSystem.File.OpenRead(executablePath))
        {
            signature = Convert.ToBase64String(SHA512.HashData(executableStream));
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

        return new(name, title, version, publisher, string.Empty, effectiveDescription, signature, capabilities);
    }

    /// <summary>
    /// Scans the <em>extensions folder</em> for subfolders containing an <em>extension manifest</em>.
    /// </summary>
    /// <returns>
    /// Returns all discovered <strong>valid</strong> extensions.
    /// </returns>
    public IEnumerable<ExtensionInfo> Discover()
    {
        var manifestFileName = options.Value.Platform.Extensions.Manifest;
        foreach (var folder in ExtensionsFolder.EnumerateDirectories())
        {
            var title = folder.Name;
            if (folder.GetFiles(manifestFileName).FirstOrDefault() is IFileInfo manifest
                && ReadManifest(manifest) is ExtensionInfo extensionInfo)
            {
                var validation = _validation.Validate(extensionInfo);
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
            else if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("No manifest was found for extension '{title}'.", title);
            }
        }
    }

    // dispose the read stream: a leaked handle blocks a later rewrite (e.g. describe-ext --overwrite).
    private static ExtensionInfo? ReadManifest(IFileInfo manifest)
    {
        using var stream = manifest.OpenRead();
        return JsonSerializer.Deserialize<ExtensionInfo>(stream);
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
        AppendValidationFlagMessage(flags, ExtensionValidationFlags.SignatureMismatch, builder, Exceptions.ValidationFlags_SignatureMismatch);

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
