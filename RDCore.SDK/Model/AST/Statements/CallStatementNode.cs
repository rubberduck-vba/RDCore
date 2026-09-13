using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// An executable statement node that represents a procedure or function call (MS-VBAL §5.4.4 Call
/// Statement) — either the explicit <c>Call &lt;lExpression&gt;</c> form or an implicit bare-call
/// statement.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Callee">The call target; any argument list is part of this expression's own tree (e.g. an index/call <c>lExpression</c>).</param>
/// <param name="IsExplicitCall">Whether the statement used the <c>Call</c> keyword.</param>
public record class CallStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ExpressionNode Callee, bool IsExplicitCall)
    : StatementNode(Identity, SourceLocation, [Callee]);
