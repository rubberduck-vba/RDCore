using Microsoft.Extensions.Options;
using RDCore.SDK.Server.Configuration;
using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;

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
    /// <summary>
    /// The certificate associated with the extension is invalid.
    /// </summary>
    /// <remarks>
    /// The server executable <em>and</em> the extension manifest may have been tampered with.
    /// </remarks>
    InvalidCertificate = 1 << 5,
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
public class ExtensionManifestValidationService(IOptions<ExtensionsOptions> options, IFileSystem fileSystem) : IExtensionManifestValidationService
{
    private IDirectoryInfo ExtensionsFolder => fileSystem.DirectoryInfo.New(options.Value.Path);

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
                if (flags == ExtensionValidationFlags.NoFlags)
                {// the extension executable matches the file hash specified in the manifest.

                    flags |= FlagCertificateMismatch(manifest); // FIXME no-op.
                    // if we made it here, this extension is certified.
                }
            }
        }

        return flags;
    }

    private ExtensionValidationFlags FlagNotAllowed(ExtensionInfo manifest)
        => options.Value.Allowed.Contains(manifest.Title)
            ? ExtensionValidationFlags.NoFlags
            : ExtensionValidationFlags.NotAllowed;

    private ExtensionValidationFlags FlagBlocked(ExtensionInfo manifest)
        => !options.Value.Blocked.Any(e => e.Title == manifest.Title)
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
        => ExtensionsFolder.GetDirectories(manifest.Title, SearchOption.TopDirectoryOnly).Single()
            .GetFiles(manifest.Name, SearchOption.TopDirectoryOnly)
            .SingleOrDefault(file => GetSignature(file) == manifest.Signature) != default
                ? ExtensionValidationFlags.NoFlags
                : ExtensionValidationFlags.SignatureMismatch;

    private static ExtensionValidationFlags FlagCertificateMismatch(ExtensionInfo manifest)
        => ExtensionValidationFlags.NoFlags; // TODO (+remove static as needed)

    private static string GetSignature(IFileInfo file)
        => Encoding.UTF8.GetString(GetFileHash(file, SHA512.Create()));

    private static byte[] GetFileHash(IFileInfo file, HashAlgorithm algorithm)
    {
        using var stream = file.OpenRead();
        return algorithm.ComputeHash(stream);
    }
}
