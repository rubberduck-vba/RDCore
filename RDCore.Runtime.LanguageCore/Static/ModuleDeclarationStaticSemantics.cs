using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Semantics;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using System.Collections.Immutable;

namespace RDCore.Runtime.LanguageCore.Static;

// what the names in the expressions of a module's declarations refer to: the value of a constant and of an enumeration member, the bounds of an array and of
// an array field of a record. IsFullyAnalyzed: there is a fact for every name in them.
public sealed record ModuleDeclarationFacts(ImmutableDictionary<SyntaxNodeId, ExpressionFact> Expressions, bool IsFullyAnalyzed)
{
    public static ModuleDeclarationFacts None { get; } = new(ImmutableDictionary<SyntaxNodeId, ExpressionFact>.Empty, IsFullyAnalyzed: true);
}

// MS-VBAL §5.2.3.1, §5.2.3.3, §5.2.3.4, §5.2.3.5: the expressions of the declarations of a module are constant expressions of the module, evaluated for their facts
// only. What is wrong with one is the declaration's to report (the constant's own evaluation, DeclaredSizeStaticSemantics). The code of a procedure, and its
// header, are the procedure's (StatementStaticSemanticsEvaluator.Analyze): a Declare is one, and the defaults of its parameters are its header's.
public static class ModuleDeclarationStaticSemantics
{
    public static ModuleDeclarationFacts Analyze(IEnumerable<SyntaxNode> declarations, StaticEvaluationContext context, StaticSemanticsOptions options = default)
    {
        var live = declarations.Where(declaration => !IsProcedure(declaration) && !options.IsDead(declaration.SourceLocation.Range)).ToList();
        var facts = new ExpressionFactCollector();
        // an expression with an error is evaluated up to it: the names after it have no fact.
        var hasError = false;
        foreach (var expression in live.SelectMany(declaration => ExpressionsOf(declaration, options)))
        {
            hasError |= ExpressionStaticSemanticsEvaluator.Evaluate(context with { Facts = facts }, expression).IsError;
        }

        return new(facts.ToImmutable(), !hasError && ExpressionCoverage.IsCovered(new StatementBlock([.. live]), options, facts));
    }

    private static bool IsProcedure(SyntaxNode declaration)
        => declaration is MemberDeclarationNode { MemberKind: not (MemberKind.Enum or MemberKind.UserDefinedType) };

    // an enumeration's members and a record's fields are declarations of their own.
    private static IEnumerable<ExpressionNode> ExpressionsOf(SyntaxNode declaration, StaticSemanticsOptions options) => declaration switch
    {
        MemberDeclarationNode => declaration.Children
            .Where(child => !options.IsDead(child.SourceLocation.Range))
            .SelectMany(child => child is ConstantDeclarationNode or MemberDeclarationNode
                ? StatementStaticSemanticsEvaluator.DeclarationExpressions(child)
                : []),
        ConstantDeclarationNode or VariableDeclarationNode => StatementStaticSemanticsEvaluator.DeclarationExpressions(declaration),
        _ => [],
    };
}
