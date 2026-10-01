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
/// finds it there, with its members, and nothing special-cases it. Two things set it apart. A class need not
/// implement any of its members, which <see cref="SymbolProperties.OptionalImplementation"/> says, so that the rules
/// an <c>Implements</c> directive is held to do not fault a class that handles neither. And it is not a name workspace
/// code can refer to: nothing declares it to the scope tree, and it is no workspace symbol.
/// <para>
/// The runtime does not call the handlers by name: it dispatches the interface's member to whatever the class
/// implements it with (<see cref="VBClassModuleSymbol.FindImplementation"/>), or does nothing when the class does not
/// handle it, which is the same dispatch an explicit <c>Implements</c> needs.
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
        var module = (VBClassModuleSymbol)new VBClassModuleSymbol(Root, Root, InterfaceName) { ImplementsLifecycle = false }
            .With(SymbolProperties.OptionalImplementation, true);

        VBTypeMemberSymbol Handler(string name) => new VBProcedureMemberSymbol(
            Root, module.Uri, name, ScopeKind.Instance, SymbolKindExt.Procedure, VBVoidType.TypeInfo,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);

        ImmutableArray<VBTypeMemberSymbol> members = [Handler(InitializeName), Handler(TerminateName)];
        return module with { Members = members, DefaultInterfaceMembers = members };
    }
}
