namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// A project or library reference as the runtime sees it — its source-visible name and its priority
/// among the workspace's references. This is the runtime-facing view of a <c>.rdproj</c>
/// <c>RDCoreReference</c>: only the identity a resolver needs, not the on-disk location.
/// </summary>
/// <remarks>
/// <see cref="Priority"/> is the reference's rank in the ordered list the <c>.rdproj</c> declares
/// (<strong>RD-VBAL §2.3.1.2</strong>). Rank <c>0</c> is the lowest priority — always the <c>VBA</c>
/// standard library — and every later reference shadows the earlier ones when a global-scope name
/// resolves to more than one of them. The order is preserved exactly as the language server provides
/// it; the runtime does not re-sort it.
/// </remarks>
/// <param name="Name">The identifier name workspace source uses to qualify this reference's members (<c>VBA</c>, <c>Excel</c>, …).</param>
/// <param name="Priority">The reference's rank in the workspace's ordered reference list — <c>0</c> is the lowest priority.</param>
public sealed record class ProjectReference(string Name, int Priority)
{
    /// <summary>
    /// The COM class id identifying the referenced library in a host application registry, when the
    /// <c>.rdproj</c> supplies one.
    /// </summary>
    public Guid? Guid { get; init; }

    /// <summary>The referenced library's major version, when known.</summary>
    public int? MajorVersion { get; init; }

    /// <summary>The referenced library's minor version, when known.</summary>
    public int? MinorVersion { get; init; }
}
