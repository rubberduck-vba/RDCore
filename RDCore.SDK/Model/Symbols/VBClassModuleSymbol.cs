using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
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

    /// <summary>
    /// The interface names named by this class module's own <c>Implements</c> directives
    /// (<strong>MS-VBAL §5.2.4.2</strong>), exactly as written — unresolved, and not yet checked for
    /// validity (self-reference, duplicates, or a name that doesn't resolve to a class at all).
    /// Captured directly off the AST, alongside <see cref="Symbols.Abstract.VBModuleSymbol.Directives"/>,
    /// in <c>WorkspaceSymbolResolver.Compose</c>'s first pass — before any other module's symbol is
    /// known, so resolving these to real <see cref="VBClassModuleSymbol"/> references has to wait for
    /// <see cref="ImplementedInterfaces"/>.
    /// </summary>
    public ImmutableArray<string> ImplementedInterfaceNames { get; init; } = [];

    /// <summary>
    /// <see cref="ImplementedInterfaceNames"/>, resolved to the sibling <see cref="VBClassModuleSymbol"/>
    /// each name refers to — populated by a third pass in <c>WorkspaceSymbolResolver.Compose</c>, once
    /// every module in the composition has its own symbol built. A name that doesn't resolve to a
    /// class in this composition (or that resolves back to this same class) is silently dropped rather
    /// than reported — full MS-VBAL §5.2.4.2/§5.3.1.9 validity checking is not modeled yet.
    /// </summary>
    /// <remarks>
    /// <see cref="Types.Complex.VBClassType.FromClassModule"/> reads this to populate
    /// <see cref="Types.Complex.VBClassType.Supertypes"/> — every consumer of that type gets a correct
    /// <c>Supertypes</c> array for free once this is resolved, with no other code to update.
    /// </remarks>
    public ImmutableArray<VBClassModuleSymbol> ImplementedInterfaces { get; init; } = [];

    private ImmutableArray<VBClassModuleSymbol>? _implicitInterfaces;

    /// <summary>
    /// The interfaces this class module implements without an <c>Implements</c> directive — for every class module,
    /// <see cref="ClassLifecycleInterface"/>. Kept apart from <see cref="ImplementedInterfaces"/>, which is what the
    /// source declares: these are not interfaces a name can refer to, and are not supertypes of the class.
    /// </summary>
    /// <remarks>
    /// A host's own kinds of class module (a form, a document) will add theirs here.
    /// </remarks>
    public ImmutableArray<VBClassModuleSymbol> ImplicitInterfaces
    {
        get => _implicitInterfaces ?? ClassLifecycleInterface.Implicit;
        init => _implicitInterfaces = value;
    }

    /// <summary>
    /// The member of this class that implements <paramref name="interfaceMember"/> of <paramref name="implemented"/>
    /// (<strong>MS-VBAL §5.3.1.9</strong>): the procedure named <c>InterfaceName_MemberName</c>, whatever its access.
    /// </summary>
    /// <param name="implemented">An interface this class implements, explicitly or implicitly.</param>
    /// <param name="interfaceMember">A member of <paramref name="implemented"/>.</param>
    /// <returns>The implementing procedure, or <see langword="null"/> when this class does not implement the member.</returns>
    public VBProcedureMemberSymbol? FindImplementation(VBClassModuleSymbol implemented, VBTypeMemberSymbol interfaceMember)
    {
        var name = $"{implemented.Name}_{interfaceMember.Name}";
        return Members.OfType<VBProcedureMemberSymbol>()
            .FirstOrDefault(member => string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether a live instance of this class is COM Automation-capable (<c>IDispatch</c>) or
    /// <c>IUnknown</c>-only. Defaults to <see cref="VBAutomationKind.Dispatch"/> — true of every
    /// RD-VBA class module today; <see cref="VBAutomationKind.Unknown"/> is groundwork for a future
    /// external/COM reference kind, not constructed anywhere yet.
    /// </summary>
    public VBAutomationKind AutomationKind { get; init; } = VBAutomationKind.Dispatch;
}
