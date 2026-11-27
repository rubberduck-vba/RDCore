using RDCore.SDK.Extensibility.Annotations;

namespace RDCore.LanguageServer;

/// <summary>
/// The annotations the language server knows: those each extension of the platform declares, as its manifest carries them, the core extension's among
/// them.
/// </summary>
internal interface IPlatformAnnotations
{
    /// <summary>
    /// The catalogue of the annotations of every extension added so far, in the order they were added.
    /// </summary>
    AnnotationCatalogue Catalogue { get; }

    /// <summary>
    /// Adds the annotations of an extension, as the platform assembles its extensions.
    /// </summary>
    /// <param name="annotations">The extension's annotations, with their descriptions.</param>
    void Add(AnnotationSet annotations);
}

internal sealed class PlatformAnnotations : IPlatformAnnotations
{
    private readonly Lock _lock = new();
    private readonly List<AnnotationSet> _sets = [];
    private AnnotationCatalogue _catalogue = new([]);

    public AnnotationCatalogue Catalogue => Volatile.Read(ref _catalogue);

    // a request reads the catalogue as it is when it starts: the sets are added as the platform is assembled, while requests may already be answered.
    public void Add(AnnotationSet annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        lock (_lock)
        {
            _sets.Add(annotations);
            Volatile.Write(ref _catalogue, new AnnotationCatalogue([.. _sets]));
        }
    }
}
