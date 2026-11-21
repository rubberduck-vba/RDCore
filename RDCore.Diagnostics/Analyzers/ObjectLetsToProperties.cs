using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;

namespace RDCore.Diagnostics.Analyzers;

// the properties a Let gives an object to (RDC11043, RDC11047): the property the target names (the callee of an index, or the target itself), bound early outside
// the procedure the Let is in, whose Property Let value parameter has a kind the caller accepts; and a value of a class or Object, not an array. A value declared
// Variant is left out: whether it holds an object is known only when it runs.
internal static class ObjectLetsToProperties
{
    public static IEnumerable<ExpressionFactDto> Find(ModuleAnalysisContext context, Func<DeclaredTypeKind, bool> letValueTypeKind)
        => Lets(context, letValueTypeKind).Select(let => let.Property);

    // each such Let, with the facts of the property it names and of the value it gives.
    public static IEnumerable<(AssignmentStatementNode Statement, ExpressionFactDto Property, ExpressionFactDto Value)> Lets(
        ModuleAnalysisContext context, Func<DeclaredTypeKind, bool> letValueTypeKind)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            return [];
        }

        var lets = module.Descendants().OfType<AssignmentStatementNode>()
            .Where(assignment => assignment.Kind is AssignmentKind.ImplicitLet or AssignmentKind.ExplicitLet)
            .ToArray();

        return semantics.Procedures.SelectMany(procedure =>
            {
                var facts = procedure.Expressions.DistinctBy(fact => fact.Node).ToDictionary(fact => fact.Node);
                return lets
                    .Select(let => facts.TryGetValue(PropertyOf(let).Identity, out var property) && facts.TryGetValue(let.Value.Identity, out var value)
                        ? (Statement: let, Property: property, Value: value)
                        : default)
                    .Where(let => let.Property is not null && GivesAnObject(procedure, let.Property, let.Value, letValueTypeKind));
            })
            .GroupBy(let => let.Property.Location.Range.Start)
            .Select(let => let.First())
            .OrderBy(let => let.Property.Location.Range.Start);
    }

    // the property a target names: the callee of an index, or the target itself.
    private static ExpressionNode PropertyOf(AssignmentStatementNode let) => let.Target is IndexExpressionNode { Callee: var called } ? called : let.Target;

    private static bool GivesAnObject(ProcedureSemanticsDto procedure, ExpressionFactDto property, ExpressionFactDto value, Func<DeclaredTypeKind, bool> letValueTypeKind)
        => property is { Error: null, Classification: ExpressionClassification.Property, LetValueTypeKind: { } kind, Binding: { } binding }
            && letValueTypeKind(kind)
            && binding.AbsoluteUri != procedure.Procedure.AbsoluteUri
            && value is { Error: null, TypeKind: DeclaredTypeKind.Class or DeclaredTypeKind.Object, IsArray: false };
}
