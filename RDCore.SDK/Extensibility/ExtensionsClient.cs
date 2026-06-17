using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.LanguageServer.Extensibility;
using RDCore.SDK.Client;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using System.IO.Abstractions;
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
    ILogger<ExtensionsClient> logger) : IExtensionsProvider
{
    private readonly IExtensionManifestValidationService _validation = validation;
    private readonly Dictionary<string, ExtensionInfo> _extensions = [];
    private readonly Dictionary<ExtensionInfo, IRDCoreClientApp> _clients = [];

    private IDirectoryInfo ExtensionsFolder => fileSystem.DirectoryInfo.New(options.Value.Platform.Extensions.Path);

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
                && JsonSerializer.Deserialize<ExtensionInfo>(manifest.OpenRead()) is ExtensionInfo extensionInfo)
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
        AppendValidationFlagMessage(flags, ExtensionValidationFlags.SignatureMismatch, builder, Exceptions.ValidationFlags_SignatureMismatch);
        AppendValidationFlagMessage(flags, ExtensionValidationFlags.InvalidCertificate, builder, Exceptions.ValidationFlags_InvalidCertificate);

        return builder.ToString();
    }

    private static void AppendValidationFlagMessage(ExtensionValidationFlags flags, ExtensionValidationFlags check, StringBuilder builder, string verbose)
    {
        if (flags.HasFlag(check))
        {
            builder.AppendLine($"[{check}:{(int)check}:X2] {verbose}");
        }
    }

    /// <summary>
    /// Asynchronously starts and attempts to connect with the <em>server process</em> for the specified <see cref="ExtensionInfo"/>.
    /// </summary>
    /// <param name="extension">The deserialized <em>manifest</em> of the extension to start.</param>
    /// <returns>An asynchronous <see cref="Task"/> that completes when the specified extension was successfully started and connected, or failed to do so.</returns>
    public async Task StartAsync(ExtensionInfo extension)
    {
        // TODO
    }
}

