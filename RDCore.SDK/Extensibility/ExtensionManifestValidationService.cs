using Microsoft.Extensions.Options;
using RDCore.SDK.Platform;
using RDCore.SDK.Server.Configuration;
using System.IO.Abstractions;
using System.Security.Cryptography;

namespace RDCore.SDK.Extensibility;

/// <summary>
/// The <em>validation flags</em> issued by the <em>extension validation</em> service.
/// </summary>
[Flags]
public enum ExtensionValidationFlags
{
    /// <summary>
    /// The extension is valid.
    /// </summary>
    NoFlags = 0,
    /// <summary>
    /// The <c>Title</c> specified in the extension manifest is currently explicitly blocked.
    /// </summary>
    /// <remarks>
    /// Application configuration explicitly lists <c>Blocked</c> extensions.
    /// </remarks>
    Blocked = 1 << 0,
    /// <summary>
    /// The <c>Title</c> specified in the extension manifest is not currently enabled.
    /// </summary>
    /// <remarks>
    /// Application configuration must explicitly list <c>Allowed</c> extensions.
    /// </remarks>
    NotAllowed = 1 << 1,
    /// <summary>
    /// The <c>Title</c> specified in the extension manifest mismatches its folder location.
    /// </summary>
    /// <remarks>
    /// Folder location <strong>ensures uniqueness of extension titles</strong> available to a given server host.
    /// </remarks>
    LocationMismatch = 1 << 2,
    /// <summary>
    /// The <c>Name</c> specified in the extension manifest does not point to an existing file in this location.
    /// </summary>
    /// <remarks>
    /// The server executable may have been moved, renamed, or deleted.
    /// </remarks>
    FileNotFound = 1 << 3,
    /// <summary>
    /// The <c>Signature</c> specified in the extension manifest does not match the signature of the discovered binary executable.
    /// </summary>
    /// <remarks>
    /// The server executable may have been tampered with.
    /// </remarks>
    SignatureMismatch = 1 << 4,

}

/// <summary>
/// A service that provides platform extension capabilities.
/// </summary>
public interface IExtensionCapabilityProvider
{
    /// <summary>
    /// Asynchronously validates the availability of the specified <em>capability</em> type.
    /// </summary>
    /// <typeparam name="TCapability">The <em>capability</em> type requested.</typeparam>
    /// <returns>An asynchronous <c>Task</c> that yields a <c>bool</c> that is <c>true</c> if the requested capability is available from thie provider.</returns>
    Task<bool> IsAvailableAsync<TCapability>();
    /// <summary>
    /// Asynchronously requests metadata associated with the specified <em>capability</em> type.
    /// </summary>
    /// <typeparam name="TCapability">The <em>capability</em> type requested.</typeparam>
    /// <typeparam name="T">The type of capability metadata to get from this provider.</typeparam>
    /// <returns>The metadata associated with the requested <em>capability</em> type.</returns>
    Task<T> GetAsync<TCapability,T>() where T : class, new();
}

/// <summary>
/// A service that validates an <em>extension manifest.</em>.
/// </summary>
public interface IExtensionManifestValidationService
{
    /// <summary>
    /// Validates the specified <see cref="ExtensionInfo"/>.
    /// </summary>
    /// <param name="manifest">The deserialized <em>extension manifest</em> metadata.</param>
    /// <returns>A <see cref="ExtensionValidationFlags"/> value that encodes any <em>validation flags</em> (use <c>HasFlags</c>), or <see cref="ExtensionValidationFlags.NoFlags"/> if there are no issues.</returns>
    ExtensionValidationFlags Validate(ExtensionInfo manifest);
}

/// <summary>
/// A service that validates an <em>extension manifest.</em>.
/// </summary>
/// <param name="options">The <em>extensions</em> configuration settings.</param>
/// <param name="fileSystem">Abstracts the <em>file system</em>.</param>
/// <param name="environment">Resolves the extensions folder from the platform root rather than the process working directory.</param>
public class ExtensionManifestValidationService(IOptions<SdkAppOptions> options, IFileSystem fileSystem, IPlatformEnvironment environment) : IExtensionManifestValidationService
{
    // resolve against the platform root: the language server runs with its own subfolder as the
    // working directory, so the raw relative "Extensions" would never match the real location.
    private IDirectoryInfo ExtensionsFolder => fileSystem.DirectoryInfo.New(environment.Resolve(options.Value.Platform.Extensions.Path));

    /// <summary>
    /// Validates the specified <see cref="ExtensionInfo"/>.
    /// </summary>
    /// <param name="manifest">The deserialized <em>extension manifest</em> metadata.</param>
    /// <returns>A <see cref="ExtensionValidationFlags"/> value that encodes any <em>validation flags</em> (use <c>HasFlags</c>), or <see cref="ExtensionValidationFlags.NoFlags"/> if there are no issues.</returns>
    public ExtensionValidationFlags Validate(ExtensionInfo manifest)
    {
        var flags = FlagNotAllowed(manifest) | FlagBlocked(manifest);
        if (flags == ExtensionValidationFlags.NoFlags)
        {// configuration allows the extension to run.

            flags |= FlagLocationMismatch(manifest) | FlagFileNotFound(manifest);
            if (flags == ExtensionValidationFlags.NoFlags)
            {// the extension exists in the expected location.

                flags |= FlagSignatureMismatch(manifest);
                // the extension executable matches the file hash specified in the manifest.
            }
        }

        return flags;
    }

    private ExtensionValidationFlags FlagNotAllowed(ExtensionInfo manifest)
        => options.Value.Platform.Extensions.Allowed.Contains(manifest.Title)
            ? ExtensionValidationFlags.NoFlags
            : ExtensionValidationFlags.NotAllowed;

    private ExtensionValidationFlags FlagBlocked(ExtensionInfo manifest)
        => !options.Value.Platform.Extensions.Blocked.Any(e => e.Title == manifest.Title)
            ? ExtensionValidationFlags.NoFlags
            : ExtensionValidationFlags.Blocked;

    private ExtensionValidationFlags FlagLocationMismatch(ExtensionInfo manifest)
        => ExtensionsFolder.GetDirectories(manifest.Title, new EnumerationOptions() { MatchCasing = MatchCasing.CaseInsensitive, RecurseSubdirectories = false })
            .SingleOrDefault() != default
                ? ExtensionValidationFlags.NoFlags
                : ExtensionValidationFlags.LocationMismatch;

    private ExtensionValidationFlags FlagFileNotFound(ExtensionInfo manifest)
        => ExtensionsFolder.GetDirectories(manifest.Title, SearchOption.TopDirectoryOnly).Single()
            .GetFiles(manifest.Name, SearchOption.TopDirectoryOnly)
            .SingleOrDefault() != default
                ? ExtensionValidationFlags.NoFlags
                : ExtensionValidationFlags.FileNotFound;

    private ExtensionValidationFlags FlagSignatureMismatch(ExtensionInfo manifest)
        => options.Value.Server.UnsafeDevMode || ExtensionsFolder.GetDirectories(manifest.Title, SearchOption.TopDirectoryOnly).Single()
            .GetFiles(manifest.Name, SearchOption.TopDirectoryOnly)
            .SingleOrDefault(file => GetSignature(file) == manifest.Signature) != default
                ? ExtensionValidationFlags.NoFlags
                : ExtensionValidationFlags.SignatureMismatch;

    // base64(SHA512) — must match how ExtensionsClient.Describe writes the manifest signature.
    private static string GetSignature(IFileInfo file)
    {
        using var stream = file.OpenRead();
        return Convert.ToBase64String(SHA512.HashData(stream));
    }
}
