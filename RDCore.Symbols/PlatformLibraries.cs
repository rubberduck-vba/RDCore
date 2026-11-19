using RDCore.Forms.Libraries;
using RDCore.SDK.Runtime.Libraries;

namespace RDCore.Symbols;

/// <summary>
/// The descriptions of the libraries a project can reference: those the packages the platform ships with describe, ahead of the platform's
/// <c>Symbols</c> folder. RDForms describes <c>VB</c>, VB6.OLB's export with RDForms' additions, so the folder has no <c>VB.json</c>.
/// </summary>
public static class PlatformLibraries
{
    /// <summary>
    /// The platform's libraries: RDForms' ahead of <paramref name="symbols"/>.
    /// </summary>
    /// <param name="symbols">The descriptions in the platform's <c>Symbols</c> folder.</param>
    public static ILibrarySource With(ILibrarySource symbols) => new CompositeLibrarySource(RDFormsLibraries.Instance, symbols);
}
