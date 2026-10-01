using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// What is done to a public variable of an interface, which is implemented by property declarations
/// (<strong>MS-VBAL §5.3.1.9</strong>).
/// </summary>
public enum ImplementationAccess
{
    /// <summary>The variable is read, which invokes its <c>Property Get</c>.</summary>
    Get,

    /// <summary>The variable is assigned, which invokes its <c>Property Let</c>.</summary>
    Let,

    /// <summary>The variable is <c>Set</c>-assigned, which invokes its <c>Property Set</c>.</summary>
    Set,
}

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
    /// Where each of <see cref="ImplementedInterfaceNames"/> is written in the source: the range of its <c>Implements</c> directive,
    /// one for each name and in the same order. Empty for a symbol that was not read from source, whose directives have no
    /// location; what is reported of one is then reported at the module.
    /// </summary>
    public ImmutableArray<SourceRange> ImplementedInterfaceRanges { get; init; } = [];

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
    /// <para>
    /// What is assigned is what the source declares. What is read also has what the language implements for every
    /// class module, first: <see cref="ClassLifecycleInterface"/>, whose members are <c>Initialize</c> and
    /// <c>Terminate</c>. It is an interface of the module like any other, which is why whatever builds a list of the
    /// interfaces a module implements — an editor's dropdown among them — finds it there. Its members have an
    /// implementation of their own (<see cref="SymbolProperties.DefaultImplementation"/>), so a module that writes no
    /// handler still implements every one of them, as <strong>MS-VBAL §5.3.1.9</strong> requires. It is still not a
    /// name workspace code can refer to.
    /// </para>
    /// </remarks>
    public ImmutableArray<VBClassModuleSymbol> ImplementedInterfaces
    {
        get => ImplementsLifecycle && !_declaredInterfaces.Any(IsLifecycleInterface)
            ? [ClassLifecycleInterface.Interface, .. _declaredInterfaces]
            : _declaredInterfaces;
        init => _declaredInterfaces = value;
    }

    private ImmutableArray<VBClassModuleSymbol> _declaredInterfaces = [];

    // a Uri's fragment is where a symbol's identity lives, and Uri equality ignores it.
    private static bool IsLifecycleInterface(VBClassModuleSymbol candidate)
        => candidate.Uri.AbsoluteUri == ClassLifecycleInterface.Interface.Uri.AbsoluteUri;

    /// <summary>
    /// Whether the language implements <see cref="ClassLifecycleInterface"/> for this module, which it does for every
    /// class module but that interface itself.
    /// </summary>
    public bool ImplementsLifecycle { get; init; } = true;

    /// <summary>
    /// The member of this class that implements <paramref name="interfaceMember"/> of <paramref name="implemented"/>
    /// (<strong>MS-VBAL §5.3.1.9</strong>): the declaration named <c>InterfaceName_MemberName</c>, whatever its access, of
    /// the kind the member calls for.
    /// </summary>
    /// <remarks>
    /// A subroutine is implemented by a subroutine, a function by a function, and each property accessor by the same
    /// accessor. A public variable is implemented by property declarations; the one this finds is its
    /// <c>Property Get</c>, which is what reading the variable through the interface invokes.
    /// </remarks>
    /// <param name="implemented">An interface this class implements, explicitly or implicitly.</param>
    /// <param name="interfaceMember">A member of <paramref name="implemented"/>.</param>
    /// <returns>The implementing declaration, or <see langword="null"/> when this class does not implement the member.</returns>
    public VBTypeMemberSymbol? FindImplementation(VBClassModuleSymbol implemented, VBTypeMemberSymbol interfaceMember)
        => FindImplementation(implemented, interfaceMember, ImplementationAccess.Get);

    /// <summary>
    /// <inheritdoc cref="FindImplementation(VBClassModuleSymbol, VBTypeMemberSymbol)" path="/summary"/>
    /// </summary>
    /// <remarks>
    /// A public variable of the interface is implemented by property declarations, and which of them is
    /// <paramref name="access"/>: reading the variable invokes the <c>Property Get</c>, assigning it the <c>Property Let</c>,
    /// and <c>Set</c>-assigning it the <c>Property Set</c>. Any other member is implemented by one declaration, and
    /// <paramref name="access"/> is not asked of it.
    /// </remarks>
    /// <param name="implemented">An interface this class implements, explicitly or implicitly.</param>
    /// <param name="interfaceMember">A member of <paramref name="implemented"/>.</param>
    /// <param name="access">What is done to <paramref name="interfaceMember"/>, when it is a variable.</param>
    public VBTypeMemberSymbol? FindImplementation(
        VBClassModuleSymbol implemented, VBTypeMemberSymbol interfaceMember, ImplementationAccess access)
    {
        var name = $"{implemented.Name}_{interfaceMember.Name}";
        return Members.FirstOrDefault(member => string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase)
            && ImplementsKindOf(interfaceMember, member, access));
    }

    // Property Get, Let and Set derive from the function's and the subroutine's symbols, and are not the kinds of
    // declaration they derive from.
    private static bool ImplementsKindOf(VBTypeMemberSymbol interfaceMember, VBTypeMemberSymbol candidate, ImplementationAccess access) => interfaceMember switch
    {
        VBPropertyGetMemberSymbol => candidate is VBPropertyGetMemberSymbol,
        VBPropertyLetMemberSymbol => candidate is VBPropertyLetMemberSymbol,
        VBPropertySetMemberSymbol => candidate is VBPropertySetMemberSymbol,
        VBFunctionMemberSymbol => candidate is VBFunctionMemberSymbol,
        VBProcedureMemberSymbol => candidate is VBProcedureMemberSymbol and not (VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol),
        _ => access switch
        {
            ImplementationAccess.Let => candidate is VBPropertyLetMemberSymbol,
            ImplementationAccess.Set => candidate is VBPropertySetMemberSymbol,
            _ => candidate is VBPropertyGetMemberSymbol,
        },
    };

    /// <summary>
    /// Whether this class implements <paramref name="interfaceMember"/> of <paramref name="implemented"/>
    /// (<strong>MS-VBAL §5.3.1.9</strong>): it has the procedure for it (<see cref="FindImplementation"/>), or the
    /// member has an implementation of its own (<see cref="SymbolProperties.DefaultImplementation"/>). What a check that
    /// an <c>Implements</c> directive is complete asks, of each member of the interface.
    /// </summary>
    /// <param name="implemented">An interface this class implements, explicitly or implicitly.</param>
    /// <param name="interfaceMember">A member of <paramref name="implemented"/>.</param>
    public bool IsImplemented(VBClassModuleSymbol implemented, VBTypeMemberSymbol interfaceMember)
        => FindImplementation(implemented, interfaceMember) is not null || interfaceMember.GetProperty(SymbolProperties.DefaultImplementation);

    /// <summary>
    /// The events this class declares (<strong>MS-VBAL §5.2.4.3</strong>).
    /// </summary>
    public IEnumerable<VBEventMemberSymbol> Events => Members.OfType<VBEventMemberSymbol>();

    /// <summary>
    /// The event of this class named <paramref name="name"/>, or <see langword="null"/> when it declares none.
    /// </summary>
    /// <param name="name">The event name, compared without regard to case.</param>
    public VBEventMemberSymbol? FindEvent(string name)
        => Events.FirstOrDefault(declared => string.Equals(declared.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The WithEvents variables this class declares (<strong>MS-VBAL §5.2.3.1.2</strong>).
    /// </summary>
    public IEnumerable<VBTypeMemberSymbol> WithEventsVariables
        => Members.Where(member => member.GetProperty(SymbolProperties.WithEvents));

    /// <summary>
    /// The procedure of this class that handles <paramref name="handled"/> for <paramref name="variable"/>
    /// (<strong>MS-VBAL §5.3.1.8</strong>): the one named <c>VariableName_EventName</c>.
    /// </summary>
    /// <remarks>
    /// Whether it is a valid handler - a subroutine, with a parameter list compatible with the event's - is not
    /// decided here: this is only the procedure that is named like one.
    /// </remarks>
    /// <param name="variable">A WithEvents variable of this class.</param>
    /// <param name="handled">An event of the class that is the variable's declared type.</param>
    /// <returns>The handler, or <see langword="null"/> when this class does not handle the event.</returns>
    public VBProcedureMemberSymbol? FindEventHandler(VBTypeMemberSymbol variable, VBEventMemberSymbol handled)
    {
        var name = $"{variable.Name}_{handled.Name}";
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
