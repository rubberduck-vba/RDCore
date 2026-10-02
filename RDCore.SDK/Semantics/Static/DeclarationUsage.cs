using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Semantics.Flags;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// Counts how the declarations of a module are used by its code, from the expression facts of its procedures.
/// </summary>
/// <remarks>
/// A declaration is used by an expression that is bound to it (<see cref="ExpressionFact.Binding"/>): written to when the expression is flagged
/// <see cref="ValueExpressionSemanticFlags.AssignmentTarget"/>, read otherwise. Only the module's own code is counted, so the count of a declaration another
/// module can refer to is not the count of its uses.
/// </remarks>
public static class DeclarationUsage
{
    /// <summary>
    /// Counts the uses of <paramref name="declared"/> by <paramref name="procedures"/>.
    /// </summary>
    /// <param name="declared">
    /// The symbols the module declares, of which the variables, constants, parameters, procedures, properties and events are the declarations counted: its members,
    /// and the parameters and locals of its procedures. Whatever else is among them is not a declaration of one of those kinds, and is left out.
    /// </param>
    /// <param name="procedures">The models of the procedures of the module.</param>
    /// <returns>A fact for each declaration, in the order they are given.</returns>
    public static ImmutableArray<DeclarationFact> Of(IEnumerable<Symbol> declared, IEnumerable<ProcedureSemanticModel> procedures)
    {
        var reads = new Dictionary<SemanticId, int>();
        var writes = new Dictionary<SemanticId, int>();
        foreach (var fact in procedures.SelectMany(procedure => procedure.Expressions.Values).Where(fact => fact.Binding is not null))
        {
            var counts = fact.Flags.HasFlag(ValueExpressionSemanticFlags.AssignmentTarget) ? writes : reads;
            counts[fact.Binding!.Value] = counts.GetValueOrDefault(fact.Binding.Value) + 1;
        }

        // the accessors of a property are one declaration, though each has the identity of its own that an expression can be bound to: it is the first one
        // that stands for it, and what is counted is what refers to any of them.
        var candidates = declared
            .Select(symbol => (Symbol: symbol, Kind: KindOf(symbol)))
            .Where(candidate => candidate.Kind is not null)
            .DistinctBy(candidate => candidate.Symbol.SemanticId)
            .ToList();
        var accessorsByProperty = candidates
            .Where(candidate => candidate.Kind is DeclarationKind.Property)
            .ToLookup(candidate => (candidate.Symbol.ParentUri.AbsoluteUri, candidate.Symbol.Name.ToUpperInvariant()));

        return [.. candidates
            .Where(candidate => candidate.Kind is not DeclarationKind.Property
                || ReferenceEquals(accessorsByProperty[(candidate.Symbol.ParentUri.AbsoluteUri, candidate.Symbol.Name.ToUpperInvariant())].First().Symbol, candidate.Symbol))
            .Select(candidate =>
            {
                var identities = candidate.Kind is DeclarationKind.Property
                    ? accessorsByProperty[(candidate.Symbol.ParentUri.AbsoluteUri, candidate.Symbol.Name.ToUpperInvariant())].Select(accessor => accessor.Symbol.SemanticId).ToList()
                    : [candidate.Symbol.SemanticId];
                return new DeclarationFact(
                    candidate.Symbol.SemanticId, candidate.Symbol.Name, candidate.Kind!.Value, AccessOf(candidate.Symbol), IsImplicit(candidate.Symbol),
                    LocationOf(candidate.Symbol), identities.Sum(id => reads.GetValueOrDefault(id)), identities.Sum(id => writes.GetValueOrDefault(id)));
            })];
    }

    /// <summary>
    /// Everything a module declares that <see cref="Of"/> counts: its members, and the parameters and locals of each.
    /// </summary>
    /// <param name="members">The members declared by the module.</param>
    public static IEnumerable<Symbol> DeclaredBy(IEnumerable<VBTypeMemberSymbol> members)
        => members.SelectMany(member => new Symbol[] { member }
            .Concat(DeclarationStaticSemanticsEvaluator.ParametersOf(member))
            .Concat(DeclarationStaticSemanticsEvaluator.LocalsOf(member)));

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
