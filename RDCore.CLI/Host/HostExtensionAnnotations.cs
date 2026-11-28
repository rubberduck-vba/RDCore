using Microsoft.Extensions.Logging;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Extensibility.Annotations;

namespace RDCore.CLI.Host;

/// <summary>
/// The annotations the platform's extensions declare, as the host knows them: what says which members an extension calls from outside the project.
/// </summary>
internal interface IHostExtensionAnnotations
{
    /// <summary>
    /// Whether the annotation written under <paramref name="name"/>, its own or an alias, is one an extension declares as an entry point
    /// (<see cref="AnnotationDeclaration.IsEntryPoint"/>).
    /// </summary>
    /// <param name="name">The name written after <c>'@</c>.</param>
    bool IsEntryPoint(string name);
}

// the host finds the extensions the language server finds: the same folder, under the platform's settings, which every component of the platform reads. It
// finds them once, the first time it is asked; a platform whose extensions cannot be read has none, as the language server's has.
internal sealed class HostExtensionAnnotations(IExtensionsProvider extensions, ILogger<HostExtensionAnnotations> logger) : IHostExtensionAnnotations
{
    private readonly Lazy<AnnotationCatalogue> _catalogue = new(() => Discovered(extensions, logger));

    public bool IsEntryPoint(string name) => _catalogue.Value.Find(name) is { IsEntryPoint: true };

    private static AnnotationCatalogue Discovered(IExtensionsProvider extensions, ILogger logger)
    {
        try
        {
            return new AnnotationCatalogue([.. extensions.Discover().Select(extension => extension.AnnotationSet)]);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning("The annotations of the platform's extensions could not be read: {Message}", exception.Message);
            return new AnnotationCatalogue([]);
        }
    }
}
