using RDCore.SDK.Model.AST.Declarations;
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
/// <item>A name is declared once in the scope of a module (<strong>MS-VBAL §5.2</strong>, <see cref="VBCompileErrorId.DuplicateDeclaration"/>). That is a rule of
/// the declarations as they are written, and not of the symbols they become (<see cref="CheckSyntax"/>).</item>
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

    /// <summary>
    /// Checks that a name is declared once in the scope of a module (<strong>MS-VBAL §5.2</strong>, <see cref="VBCompileErrorId.DuplicateDeclaration"/>), from
    /// what the module's source declares.
    /// </summary>
    /// <remarks>
    /// Read off the declarations of the syntax tree and not the symbols they become, because a symbol is one per identity: a name declared twice as the same
    /// kind of thing (<c>Public Total As Long</c> twice, a <c>Sub</c> of one name twice) is, by then, one symbol, and a rule that read symbols could only
    /// find the names that collide as different kinds. The <c>Get</c>, <c>Let</c> and <c>Set</c> accessors of a property are the one declaration of it; an
    /// event is checked for its own uniqueness by the rule of events, which says what it is that is declared twice; and a name declared in each branch of a
    /// <c>#If</c> block is declared once, whichever branch compiles.
    /// </remarks>
    /// <param name="module">The parsed module.</param>
    /// <param name="blocks">The <c>#If</c> blocks of the module, which say which declarations are alternatives of one another.</param>
    /// <returns>One error for each declaration of a name that is already declared, at the repeated declaration; empty when each is declared once.</returns>
    public static ImmutableArray<VBCompileErrorInfo> CheckSyntax(ModuleNode module, ConditionalCompilationBlocks blocks)
    {
        var errors = ImmutableArray.CreateBuilder<VBCompileErrorInfo>();
        var seen = new Dictionary<string, List<(MemberKind? Kind, SourceLocation Location)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in module.Children)
        {
            var (name, kind) = child switch
            {
                VariableDeclarationNode variable => (variable.Name, default(MemberKind?)),
                ConstantDeclarationNode constant => (constant.Name, default(MemberKind?)),
                ExternalMemberDeclarationNode external => (external.Name, default(MemberKind?)),
                MemberDeclarationNode { MemberKind: MemberKind.Event or MemberKind.UserDefinedTypeField } => (string.Empty, default(MemberKind?)),
                MemberDeclarationNode member => (member.Name, member.MemberKind),
                _ => (string.Empty, default(MemberKind?)),
            };

            if (name.Length == 0)
            {
                continue;
            }

            // the name of a type and the name of a value are bound in different contexts (MS-VBAL §5.6.4): an Enum and a property of the one name are
            // both declared once, and `Property Get PlayerType() As PlayerType` is how code says that.
            var key = (kind is MemberKind.Enum or MemberKind.UserDefinedType ? "type:" : "value:") + name;
            if (!seen.TryGetValue(key, out var declarations))
            {
                seen.Add(key, declarations = []);
            }

            if (declarations.Any(prior => !AreOneDeclaration(prior, (kind, child.SourceLocation), blocks)))
            {
                errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.DuplicateDeclaration, child.SourceLocation, $"'{name}' is declared more than once in this module."));
            }

            declarations.Add((kind, child.SourceLocation));
        }

        return errors.ToImmutable();
    }

    private static bool AreOneDeclaration((MemberKind? Kind, SourceLocation Location) prior, (MemberKind? Kind, SourceLocation Location) next, ConditionalCompilationBlocks blocks)
        => blocks.AreAlternatives(prior.Location.Range.Start, next.Location.Range.Start)
            || (IsAccessor(prior.Kind) && IsAccessor(next.Kind) && prior.Kind != next.Kind);

    private static bool IsAccessor(MemberKind? kind) => kind is MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet;

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
