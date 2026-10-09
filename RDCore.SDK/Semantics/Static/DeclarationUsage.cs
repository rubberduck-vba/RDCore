using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Semantics.Flags;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// Counts how the declarations of a module are used by its code, from the expression facts of its procedures, where the count is the whole truth.
/// </summary>
/// <remarks>
/// A fact is stated only when it is true, and a count of the references to a declaration is true only when every reference to it is among the ones counted. That
/// takes two things, and a declaration that lacks either has no count (<see cref="DeclarationFact.References"/> is <see langword="null"/>):
/// <list type="bullet">
/// <item>Nothing outside the code analyzed can refer to it: a local, a parameter, a <c>Private</c> variable or constant. A <c>Public</c> or <c>Friend</c>
/// declaration can be referred to from another module; a procedure, a property or an event is also called by convention (an event handler, a member that implements
/// an interface) or by name at run time, which no expression says.</item>
/// <item>The code that could refer to it was analyzed completely (<see cref="ProcedureSemanticModel.IsFullyAnalyzed"/>): a procedure that has an error, or that
/// the pass does not look into entirely, may refer to anything. For a local or a parameter that is its procedure; for a variable or a constant of the module,
/// every procedure of it.</item>
/// </list>
/// What is counted is each expression bound to the declaration: written to when it is flagged <see cref="ValueExpressionSemanticFlags.AssignmentTarget"/>,
/// passed as an argument that may be taken by reference when it is flagged <see cref="ValueExpressionSemanticFlags.PassedAsArgument"/>, read otherwise.
/// </remarks>
public static class DeclarationUsage
{
    /// <summary>
    /// Counts the references to <paramref name="declared"/> by <paramref name="procedures"/>.
    /// </summary>
    /// <param name="declared">
    /// The symbols the module declares, of which the variables, constants, parameters, procedures, properties and events are the declarations stated: its members,
    /// and the parameters and locals of its procedures. Whatever else is among them is not a declaration of one of those kinds, and is left out.
    /// </param>
    /// <param name="procedures">The models of the procedures of the module.</param>
    /// <param name="roles">What the members of a class module are there for (<see cref="MemberRoles"/>), or <see langword="null"/> when the module is not a class module.</param>
    /// <returns>A fact for each declaration, in the order they are given.</returns>
    public static ImmutableArray<DeclarationFact> Of(
        IEnumerable<Symbol> declared, IEnumerable<ProcedureSemanticModel> procedures, IReadOnlyDictionary<SemanticId, DeclarationRole>? roles = null)
    {
        var models = procedures.ToList();
        var references = new Dictionary<SemanticId, DeclarationReferences>();
        foreach (var fact in models.SelectMany(procedure => procedure.Expressions.Values).Where(fact => fact.Binding is not null))
        {
            var current = references.GetValueOrDefault(fact.Binding!.Value);
            references[fact.Binding.Value] = fact.Flags switch
            {
                var flags when flags.HasFlag(ValueExpressionSemanticFlags.AssignmentTarget) => current with { Writes = current.Writes + 1 },
                var flags when flags.HasFlag(ValueExpressionSemanticFlags.PassedAsArgument) => current with { PassedAsArguments = current.PassedAsArguments + 1 },
                _ => current with { Reads = current.Reads + 1 },
            };
        }

        var moduleIsFullyAnalyzed = models.All(procedure => procedure.IsFullyAnalyzed);

        // the accessors of a property are one declaration, though each has the identity of its own that an expression can be bound to: it is the first one
        // that stands for it.
        var candidates = declared
            .Select(symbol => (Symbol: symbol, Kind: KindOf(symbol)))
            .Where(candidate => candidate.Kind is not null)
            .DistinctBy(candidate => candidate.Symbol.SemanticId)
            .ToList();
        var seenProperties = new HashSet<(string Module, string Name)>();

        var facts = ImmutableArray.CreateBuilder<DeclarationFact>();
        foreach (var (symbol, kind) in candidates)
        {
            if (kind is DeclarationKind.Property && !seenProperties.Add((symbol.ParentUri.AbsoluteUri, symbol.Name.ToUpperInvariant())))
            {
                continue;
            }

            DeclarationReferences? counted = null;
            if (IsAccessibleOnlyFromItsOwnCode(symbol, kind!.Value) && CodeThatCanReferToIt(symbol, models, moduleIsFullyAnalyzed))
            {
                counted = references.GetValueOrDefault(symbol.SemanticId);
            }

            facts.Add(new DeclarationFact(
                symbol.SemanticId, symbol.Name, kind!.Value, AccessOf(symbol), IsImplicit(symbol), LocationOf(symbol), counted)
            {
                Role = roles is not null && roles.TryGetValue(symbol.SemanticId, out var role) ? role : DeclarationRole.None,
            });
        }

        return facts.ToImmutable();
    }

    /// <summary>
    /// Everything a module declares that <see cref="Of"/> states: its members, and the parameters and locals of each.
    /// </summary>
    /// <param name="members">The members declared by the module.</param>
    public static IEnumerable<Symbol> DeclaredBy(IEnumerable<VBTypeMemberSymbol> members)
        => members.SelectMany(member => new Symbol[] { member }
            .Concat(DeclarationStaticSemanticsEvaluator.ParametersOf(member))
            .Concat(DeclarationStaticSemanticsEvaluator.LocalsOf(member)));

    // a local, a parameter, and a variable or a constant of the module that is not Public or Friend: nothing outside the module's code can refer to them, and
    // what refers to one by name is an expression. A procedure, a property or an event can be called with no expression that names it.
    private static bool IsAccessibleOnlyFromItsOwnCode(Symbol symbol, DeclarationKind kind) => kind switch
    {
        DeclarationKind.Parameter => true,
        DeclarationKind.Variable when symbol is VBLocalVariableSymbol => true,
        // a variable that is declared with Dim or with no modifier at all is Private.
        DeclarationKind.Variable => symbol is AccessibleTypedSymbol { AccessModifier: AccessModifier.Private or AccessModifier.Implicit },
        // a constant is referred to where its value is substituted, which includes the bounds of an array and the value of another constant, of an enumeration
        // member and of an optional parameter: expressions of the declarations of a module, which the pass does not evaluate as it does those of its procedures.
        // 🚧 TODO count the references of a constant once the expressions of declarations are evaluated.
        _ => false,
    };

    // the code that could refer to a local or a parameter is the code of its procedure; to anything else that is confined to the module, any procedure of it.
    private static bool CodeThatCanReferToIt(Symbol symbol, List<ProcedureSemanticModel> models, bool moduleIsFullyAnalyzed)
        => symbol is VBLocalVariableSymbol or VBLocalConstantSymbol
            ? models.Any(procedure => procedure.IsFullyAnalyzed && procedure.Procedure.Uri.AbsoluteUri == symbol.ParentUri.AbsoluteUri)
            : moduleIsFullyAnalyzed;

    private static DeclarationKind? KindOf(Symbol symbol) => symbol switch
    {
        VBParameterSymbol => DeclarationKind.Parameter,
        VBLocalVariableSymbol or VBModuleFieldVariableMemberSymbol => DeclarationKind.Variable,
        VBLocalConstantSymbol or VBConstantMemberSymbol => DeclarationKind.Constant,
        VBPropertyGetMemberSymbol or VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol => DeclarationKind.Property,
        VBFunctionMemberSymbol or VBProcedureMemberSymbol => DeclarationKind.Procedure,
        VBEventMemberSymbol => DeclarationKind.Event,
        _ => null,
    };

    private static AccessModifier AccessOf(Symbol symbol)
        => symbol is AccessibleTypedSymbol accessible ? accessible.AccessModifier : AccessModifier.Private;

    private static bool IsImplicit(Symbol symbol) => symbol is VBLocalVariableSymbol { DeclaredBy: var kind } && kind.IsImplicit();

    private static SourceLocation LocationOf(Symbol symbol)
        => new(symbol.ParentUri, symbol switch
        {
            AccessibleTypedSymbol accessible => accessible.SelectionRange,
            BoundTypedSymbol bound => bound.SelectionRange,
            _ => SourceRange.Empty,
        });
}
