using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// An executable statement node that represents a procedure or function call (MS-VBAL §5.4.2.1 Call
/// Statement) — either the explicit <c>Call &lt;lExpression&gt;</c> form or an implicit bare-call
/// statement.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Callee">
/// The call target. A parenthesized call (<c>Call Foo(1, 2)</c>, or bare <c>Foo(1, 2)</c>) carries its
/// argument list as part of this expression's own tree (an <c>IndexExpressionNode</c>) — for that
/// shape, <see cref="Arguments"/> is empty.
/// </param>
/// <param name="Arguments">
/// The statement's own argument list — only ever non-empty for the bare, unparenthesized form
/// (<c>Foo 1, 2</c>), which MS-VBAL grants no parenthesized <c>lExpression</c> equivalent; <c>Call</c>
/// always requires the parenthesized shape instead.
/// </param>
/// <param name="IsExplicitCall">Whether the statement used the <c>Call</c> keyword.</param>
public record class CallStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ExpressionNode Callee, ImmutableArray<ExpressionNode> Arguments, bool IsExplicitCall)
    : StatementNode(Identity, SourceLocation, [Callee, .. Arguments]);
