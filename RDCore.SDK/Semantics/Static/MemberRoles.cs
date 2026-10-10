using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// Tells which members of a class module are there to implement a member of an interface it <c>Implements</c>, or to handle an event of one of its
/// <c>WithEvents</c> variables (<strong>MS-VBAL §5.3.1.8</strong>, <strong>§5.3.1.9</strong>).
/// </summary>
/// <remarks>
/// A member is one or the other by its name alone: <c>InterfaceName_MemberName</c> for the member of an interface that the module implements, and
/// <c>VariableName_EventName</c> for the event of a variable it declares <c>WithEvents</c> - the same rules <see cref="ImplementsSemantics"/> and
/// <see cref="ClassModuleEventSemantics"/> check the members by. A name that only begins like one, because the interface has no such member or the variable's class
/// has no such event, is the name of a member like any other. An interface or a class that does not resolve gives no member a role: a role is only stated when it is
/// known.
/// </remarks>
public static class MemberRoles
{
    /// <summary>
    /// The role of each member of <paramref name="module"/> that has one.
    /// </summary>
    /// <param name="module">The class module, with its members.</param>
    /// <param name="resolver">What finds the interface classes and the classes of the <c>WithEvents</c> variables, as they are now.</param>
    /// <returns>The role of each procedure and property accessor that implements an interface member or handles an event; a member that has none is not among them.</returns>
    public static IReadOnlyDictionary<SemanticId, DeclarationRole> Of(VBClassModuleSymbol module, ISymbolResolver resolver)
    {
        var roles = new Dictionary<SemanticId, DeclarationRole>();

        foreach (var name in module.ImplementedInterfaceNames)
        {
            // a Uri's fragment is where a symbol's identity lives, and Uri equality ignores it.
            if (resolver.ResolveType(name, ScopeKind.Global, StaticSymbol.GlobalUri).Symbol is not VBClassModuleSymbol implemented
                || implemented.Uri.AbsoluteUri == module.Uri.AbsoluteUri)
            {
                continue;
            }

            foreach (var member in ImplementsSemantics.InterfaceMembersOf(implemented))
            {
                Mark(module, $"{implemented.Name}_{member.Name}", DeclarationRole.InterfaceImplementation, roles);
            }
        }

        foreach (var variable in module.WithEventsVariables)
        {
            if (variable.ResolvedType is not VBClassType declared)
            {
                continue;
            }

            var source = VBProjectSymbol.ResolveClass(resolver, declared.Symbol) ?? declared.Symbol;
            if (source.Uri.AbsoluteUri == module.Uri.AbsoluteUri)
            {
                continue;
            }

            foreach (var handled in source.Events)
            {
                Mark(module, $"{variable.Name}_{handled.Name}", DeclarationRole.EventHandler, roles);
            }
        }

        return roles;
    }

    // the accessors of a property are procedures that derive from the subroutine's and the function's symbols.
    private static void Mark(VBClassModuleSymbol module, string name, DeclarationRole role, Dictionary<SemanticId, DeclarationRole> roles)
    {
        foreach (var candidate in module.Members.Where(member =>
            member is VBProcedureMemberSymbol or VBFunctionMemberSymbol && string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            roles.TryAdd(candidate.SemanticId, role);
        }
    }
}
