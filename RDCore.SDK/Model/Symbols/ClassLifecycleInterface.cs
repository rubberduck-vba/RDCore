using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// The interface every class module implicitly implements: its lifecycle (<strong>MS-VBAL §5.3.1.10</strong>).
/// </summary>
/// <remarks>
/// It has two members, <c>Initialize</c> and <c>Terminate</c>, and a class handles them the way it handles the member
/// of any interface it implements: with a procedure named <c>InterfaceName_MemberName</c>, which is where
/// <c>Class_Initialize</c> and <c>Class_Terminate</c> come from. The class never writes an <c>Implements Class</c>
/// directive, but the interface is one of its <see cref="VBClassModuleSymbol.ImplementedInterfaces"/> like any other,
/// so whatever lists the interfaces a module implements - an editor building its dropdowns from the module's symbol -
/// finds it there, with its members, and nothing special-cases it.
/// <para>
/// The rule that a class implements every member of an interface it implements (<strong>MS-VBAL §5.3.1.9</strong>)
/// holds for it as for any other, with no exception: each member has an implementation of its own, which is empty
/// (<see cref="SymbolProperties.DefaultImplementation"/>), and a class that writes no handler implements the member
/// with that one. A class that handles neither event, the common case, is therefore in no breach.
/// </para>
/// <para>
/// The runtime does not call the handlers by name: it dispatches the interface's member to the implementation the
/// class has for it (<see cref="VBClassModuleSymbol.FindImplementation"/>), the member's default when there is none,
/// which is the same dispatch an explicit <c>Implements</c> needs. And the interface is not a name workspace code can
/// refer to: nothing declares it to the scope tree, and it is no workspace symbol.
/// </para>
/// </remarks>
public static class ClassLifecycleInterface
{
    /// <summary>The name of the interface, and so the prefix of every handler's name.</summary>
    public const string InterfaceName = "Class";

    /// <summary>The name of the member that is raised once an instance has been created.</summary>
    public const string InitializeName = "Initialize";

    /// <summary>The name of the member that is raised once an instance has lost its last reference.</summary>
    public const string TerminateName = "Terminate";

    private static readonly Uri Root = new("rdcore://language/");

    /// <summary>
    /// The interface itself.
    /// </summary>
    public static VBClassModuleSymbol Interface { get; } = Build();

    /// <summary>
    /// The <c>Initialize</c> member of <see cref="Interface"/>.
    /// </summary>
    public static VBTypeMemberSymbol Initialize => Member(InitializeName);

    /// <summary>
    /// The <c>Terminate</c> member of <see cref="Interface"/>.
    /// </summary>
    public static VBTypeMemberSymbol Terminate => Member(TerminateName);

    private static VBTypeMemberSymbol Member(string name)
        => Interface.Members.Single(member => string.Equals(member.Name, name, StringComparison.Ordinal));

    private static VBClassModuleSymbol Build()
    {
        // the interface does not implement itself: it is what every other class module implements.
        var module = new VBClassModuleSymbol(Root, Root, InterfaceName) { ImplementsLifecycle = false };

        VBTypeMemberSymbol Handler(string name) => (VBTypeMemberSymbol)new VBProcedureMemberSymbol(
            Root, module.Uri, name, ScopeKind.Instance, SymbolKindExt.Procedure, VBVoidType.TypeInfo,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Public)
            .With(SymbolProperties.DefaultImplementation, true);

        ImmutableArray<VBTypeMemberSymbol> members = [Handler(InitializeName), Handler(TerminateName)];
        return module with { Members = members, DefaultInterfaceMembers = members };
    }
}
