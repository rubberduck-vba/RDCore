using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// The three spellings an <see cref="AssignmentStatementNode"/> can have.
/// </summary>
public enum AssignmentKind
{
    /// <summary>A Let-assignment (<strong>MS-VBAL §5.4.3.8</strong>) with the optional <c>Let</c> keyword omitted.</summary>
    ImplicitLet,
    /// <summary>A Let-assignment (<strong>MS-VBAL §5.4.3.8</strong>) with the optional <c>Let</c> keyword spelled out.</summary>
    ExplicitLet,
    /// <summary>A Set-assignment (<strong>MS-VBAL §5.4.3.9</strong>); its <c>Set</c> keyword is never optional.</summary>
    Set,
}

/// <summary>
/// An executable statement node that represents a Let-assignment (<strong>MS-VBAL §5.4.3.8</strong>) or
/// a Set-assignment (<strong>MS-VBAL §5.4.3.9</strong>). Both statements share the exact same syntax
/// (<c>[Let] lExpression = expression</c> vs. <c>Set lExpression = expression</c>) — only their static
/// and runtime coercion semantics differ, so one shape suffices; <see cref="Kind"/> tells them apart.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Kind">Which of the three spellings this assignment used.</param>
/// <param name="Target">The assignment target (<c>lExpression</c>).</param>
/// <param name="Value">The assigned value.</param>
public record class AssignmentStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, AssignmentKind Kind, ExpressionNode Target, ExpressionNode Value)
    : StatementNode(Identity, SourceLocation, [Target, Value]);
