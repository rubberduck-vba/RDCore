using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// The static semantics of what a module declares, apart from what its procedures' bodies say: the declarations themselves.
/// </summary>
/// <remarks>
/// A module whose every procedure is valid is not a valid module: a name declared twice, an event that is wrong, an interface that is not
/// implemented are all errors of a declaration, which no body shows. They are found here, once for the module, and a module model holds them
/// next to the models of its procedures (<see cref="ModuleSemanticModel.DeclarationErrors"/>).
/// <list type="bullet">
/// <item>A name is declared once in the scope of a module (<strong>MS-VBAL §5.2</strong>, <see cref="VBCompileErrorId.DuplicateDeclaration"/>). The
/// <c>Get</c>, <c>Let</c> and <c>Set</c> accessors of a property are the one declaration of it.</item>
/// <item>A class module is also checked for what it declares about events (<see cref="ClassModuleEventSemantics"/>) and for what its
/// <c>Implements</c> directives require (<see cref="ImplementsSemantics"/>).</item>
/// </list>
/// </remarks>
public static class DeclarationStaticSemanticsEvaluator
{
    /// <summary>
    /// Checks everything <paramref name="module"/> declares.
    /// </summary>
    /// <param name="module">The module, as the session or the workspace composed it: a class module with its members.</param>
    /// <param name="members">The members declared by the module (<see cref="ISessionSymbols.MembersOf"/>).</param>
    /// <param name="resolver">What finds the classes the module's declarations name, as they are now.</param>
    /// <returns>Every error found, in declaration order within each rule; empty when the declarations are valid.</returns>
    public static ImmutableArray<VBCompileErrorInfo> Evaluate(Symbol module, IReadOnlyList<VBTypeMemberSymbol> members, ISymbolResolver resolver)
    {
        var errors = ImmutableArray.CreateBuilder<VBCompileErrorInfo>();

        CheckDuplicates(members, errors);
        if (module is VBClassModuleSymbol classModule)
        {
            errors.AddRange(ClassModuleEventSemantics.Evaluate(classModule, resolver));
            errors.AddRange(ImplementsSemantics.Evaluate(classModule, resolver));
        }

        return errors.ToImmutable();
    }

    // an event is checked for its own uniqueness by the rule of events, which says what it is that is declared twice.
    private static void CheckDuplicates(IReadOnlyList<VBTypeMemberSymbol> members, ImmutableArray<VBCompileErrorInfo>.Builder errors)
    {
        var seen = new Dictionary<string, VBTypeMemberSymbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members.Where(member => member.ScopeKind is ScopeKind.Module && member is not VBEventMemberSymbol && member.Name.Length > 0))
        {
            if (!seen.TryGetValue(member.Name, out var first))
            {
                seen.Add(member.Name, member);
            }
            else if (!AreAccessorsOfOneProperty(first, member))
            {
                errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.DuplicateDeclaration, new SourceLocation(member.ParentUri, member.SelectionRange),
                    $"'{member.Name}' is declared more than once in this module."));
            }
        }
    }

    private static bool AreAccessorsOfOneProperty(VBTypeMemberSymbol first, VBTypeMemberSymbol second)
        => first.GetType() != second.GetType() && IsAccessor(first) && IsAccessor(second);

    private static bool IsAccessor(VBTypeMemberSymbol member) => member is VBPropertyGetMemberSymbol or VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol;
}
