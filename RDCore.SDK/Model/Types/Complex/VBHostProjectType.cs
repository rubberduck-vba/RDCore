using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Model.Types.Complex;

/// <summary>
/// Defines a <em>host project</em>, as specified in <strong>MS-VBAL 4.1</strong>.
/// </summary>
/// <param name="Name">The semantically valid identifier name of this project or library.</param>
/// <param name="Uri">The root workspace <c>Uri</c> for this project or library.</param>
public sealed record class VBHostProjectType(string Name, Uri Uri) : VBProjectType(Name, Uri, ProjectTypeId.HostProject) { }
