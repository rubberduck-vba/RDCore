using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// An unbound symbol representing a <em>class module</em>.
/// </summary>
public record class VBClassModuleSymbol : VBModuleSymbol
{
    /// <summary>
    /// Creates a new <em>class module</em> of the specified kind.
    /// </summary>
    /// <param name="workspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    /// <param name="name">The name of the symbol.</param>
    public VBClassModuleSymbol(Uri workspaceRoot, Uri parentUri, string name)
        : base(workspaceRoot, parentUri, name, Abstract.ScopeKind.Instance, SymbolKindExt.Class) { }

    /// <summary>
    /// This class's default interface (MS-VBAL's own term) — the members reachable through a
    /// member-access expression typed against this class, whether through <c>Me</c> or any other
    /// variable declared as this class. A pure function of <see cref="VBModuleSymbol.Members"/>
    /// (<see cref="Types.Complex.VBClassType.FromClassModule"/> computes it), fixed the moment the
    /// class's own member list is known — populated exactly once, alongside <c>Members</c>, by
    /// <c>WorkspaceSymbolResolver.Compose</c>'s second pass. Consumers (<c>New</c>, <c>As</c>-type
    /// clauses, <c>Me</c>) read this directly rather than recomputing it at resolution time.
    /// </summary>
    public ImmutableArray<VBTypeMemberSymbol> DefaultInterfaceMembers { get; init; } = [];
}
