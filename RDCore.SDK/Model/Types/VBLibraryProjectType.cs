#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// Defines a <em>library project</em>, as specified in <strong>MS-VBAL 4.1</strong>.
/// </summary>
/// <param name="Name">The semantically valid identifier name of this project or library.</param>
/// <param name="Uri">The root workspace <c>Uri</c> for this project or library.</param>
public sealed record class VBLibraryProjectType(string Name, Uri Uri) : VBProjectType(Name, Uri, ProjectTypeId.LibraryProject) 
{
    private static readonly Lazy<VBLibraryProjectType> _instance = new(() => new("RDCoreLibraryProject1", new Uri("file://rdcore-sdk/project/library/new")), LazyThreadSafetyMode.PublicationOnly);
    public static VBType TypeInfo => _instance.Value;
}
