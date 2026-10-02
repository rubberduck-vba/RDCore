using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
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
/// <item>A declared type is a name that resolves to a type (<see cref="VBCompileErrorId.UserDefinedTypeNotDefined"/>): of a variable, a constant, a
/// parameter, a function's or a property's result, and a local. Whether it does can only be told once everything the declaration can see is defined,
/// which is why this rule is one a caller asks for (<see cref="DeclarationRules.DeclaredTypes"/>).</item>
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
    /// <param name="rules">Which of the rules that depend on what else is defined are checked.</param>
    /// <returns>Every error found, in declaration order within each rule; empty when the declarations are valid.</returns>
    public static ImmutableArray<VBCompileErrorInfo> Evaluate(
        Symbol module, IReadOnlyList<VBTypeMemberSymbol> members, ISymbolResolver resolver, DeclarationRules rules = DeclarationRules.Default)
    {
        var errors = ImmutableArray.CreateBuilder<VBCompileErrorInfo>();

        CheckDuplicates(members, errors);
        if (rules.HasFlag(DeclarationRules.DeclaredTypes))
        {
            CheckDeclaredTypes(module, members, resolver, errors);
        }

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

    // MS-VBAL §5.6.4: a declared type is bound in the type binding context. A name that did not resolve where the declaration was read, which may have been
    // before what it names was defined, is resolved again as seen from the module: an error is a name that still does not.
    private static void CheckDeclaredTypes(
        Symbol module, IReadOnlyList<VBTypeMemberSymbol> members, ISymbolResolver resolver, ImmutableArray<VBCompileErrorInfo>.Builder errors)
    {
        foreach (var member in members)
        {
            CheckDeclaredType(module, member, member.SelectionRange, member.ParentUri, resolver, errors);

            foreach (var parameter in ParametersOf(member))
            {
                CheckDeclaredType(module, parameter, parameter.SelectionRange, member.ParentUri, resolver, errors);
            }

            foreach (var local in LocalsOf(member))
            {
                CheckDeclaredType(module, local, local.SelectionRange, member.ParentUri, resolver, errors);
            }
        }
    }

    internal static ImmutableArray<VBParameterSymbol> ParametersOf(VBTypeMemberSymbol member) => member switch
    {
        VBReturningMemberSymbol returning => returning.Parameters,
        VBProcedureMemberSymbol procedure => procedure.Parameters,
        VBEventMemberSymbol declaredEvent => declaredEvent.Parameters,
        _ => [],
    };

    internal static ImmutableArray<BoundTypedSymbol> LocalsOf(VBTypeMemberSymbol member) => member switch
    {
        VBReturningMemberSymbol returning => returning.Locals,
        VBProcedureMemberSymbol procedure => procedure.Locals,
        _ => [],
    };

    private static void CheckDeclaredType(
        Symbol module, Symbol declared, SourceRange range, Uri document, ISymbolResolver resolver, ImmutableArray<VBCompileErrorInfo>.Builder errors)
    {
        if (declared is not ITypedSymbol { ResolvedType: var type } || Unresolved(type) is not { } name || Resolves(name, module, resolver))
        {
            return;
        }

        errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.UserDefinedTypeNotDefined, new SourceLocation(document, range),
            $"The declared type '{name}' could not be resolved."));
    }

    // the name a declared type was written with, when it did not resolve; for an array, its element's.
    private static string? Unresolved(VBType type) => type switch
    {
        VBUnresolvedType unresolved => unresolved.DeclaredName,
        VBArrayType array => Unresolved(array.ItemType),
        _ => null,
    };

    private static bool Resolves(string name, Symbol module, ISymbolResolver resolver)
    {
        var split = name.LastIndexOf('.');
        var (qualifier, typeName) = split < 0 ? ((string?)null, name) : (name[..split], name[(split + 1)..]);

        return qualifier is null && IntrinsicVBTypes.TryResolve(typeName, out _)
            || VBProjectSymbol.ResolveQualifiedType(resolver, qualifier, typeName, module.Uri).IsResolved;
    }
}

/// <summary>
/// The rules of <see cref="DeclarationStaticSemanticsEvaluator"/> that depend on what else is defined.
/// </summary>
[Flags]
public enum DeclarationRules
{
    /// <summary>
    /// The rules that depend on nothing but the module.
    /// </summary>
    Default = 0,

    /// <summary>
    /// A declared type is a name that resolves to a type. Only to be asked once everything the module can see is defined: a module that is defined
    /// before the one it names has a name that does not resolve yet.
    /// </summary>
    DeclaredTypes = 1,
}
