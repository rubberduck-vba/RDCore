using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// An executable statement node that represents a Let-assignment (<strong>MS-VBAL §5.4.3.8</strong>) or
/// a Set-assignment (<strong>MS-VBAL §5.4.3.9</strong>). Both statements share the exact same syntax
/// (<c>[Let] lExpression = expression</c> vs. <c>Set lExpression = expression</c>) — only their static
/// and runtime coercion semantics differ, so one shape suffices; <see cref="Token"/> tells them apart.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Token">Either <c>Let</c> or <c>Set</c> (see <c>Tokens</c>).</param>
/// <param name="IsExplicitLet">
/// Whether the source spelled out the <c>Let</c> keyword. Always <see langword="true"/> when
/// <see cref="Token"/> is <c>Set</c>: MS-VBAL requires that keyword to avoid ambiguity with Let
/// statements, whereas <c>Let</c> itself is optional and most often omitted.
/// </param>
/// <param name="Target">The assignment target (<c>lExpression</c>).</param>
/// <param name="Value">The assigned value.</param>
public record class AssignmentStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, string Token, bool IsExplicitLet, ExpressionNode Target, ExpressionNode Value)
    : StatementNode(Identity, SourceLocation, [Target, Value]);
