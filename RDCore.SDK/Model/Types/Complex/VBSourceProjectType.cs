using RDCore.SDK.Model.Types.Abstract;
using System.Collections.Immutable;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace RDCore.SDK.Model.Types.Complex;

/// <summary>
/// Defines a <em>source project</em>, as specified in <strong>MS-VBAL 4.1</strong>.
/// </summary>
/// <param name="Name">The semantically valid identifier name of this project or library.</param>
/// <param name="Uri">The root workspace <c>Uri</c> for this project or library.</param>
public sealed record class VBSourceProjectType(string Name, Uri Uri) : VBProjectType(Name, Uri, ProjectTypeId.SourceProject)
{
    /// <summary>
    /// An immutable array containing a <c>Uri</c> identifying each library (or project) reference in this project.
    /// </summary>
    public ImmutableArray<Uri> References { get; init; } = [];
}
