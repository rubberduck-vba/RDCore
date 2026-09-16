using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using System.Text.Json.Serialization;

namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// <strong>MS-VBAL 5.6.14</strong> <c>dictionary-access-expression</c> (<c>owner!member</c>) and its
/// <c>with-expression</c> sibling (<c>!member</c>, <see cref="Owner"/> omitted). A pure AST node — the
/// default-member lookup the <c>!</c> token implies is a semantics-layer concern, not this node's.
/// </summary>
/// <remarks>
/// <see cref="Owner"/> and <see cref="Member"/> are views over <see cref="SyntaxNode.Children"/>, not
/// separately stored — <c>Children</c> is the single source of truth a <c>with</c> expression can't
/// desync them from. Shape mirrors <see cref="MemberAccessExpressionNode"/> exactly.
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> (holds the document <c>Uri</c> and a <c>Range</c>) of the bound expression.</param>
/// <param name="Owner">
/// The expression the member is accessed on, or <c>null</c> for a <c>with-expression</c> — a
/// leading-<c>!</c> reference (<c>!member</c>) whose owner is implicit, legal only inside a
/// <c>With</c> block.
/// </param>
/// <param name="Member">The identifier naming the member being accessed.</param>
public sealed record class DictionaryAccessExpressionNode(SyntaxNodeId Identity, SourceLocation Location, ExpressionNode? Owner, SimpleNameExpressionNode Member)
    : ExpressionNode(Identity, Location, Owner is null ? [Member] : [Owner, Member])
{
    /// <summary>
    /// The expression the member is accessed on, or <c>null</c> for a <c>with-expression</c>. A view
    /// over <see cref="SyntaxNode.Children"/>, not separately stored.
    /// </summary>
    [JsonIgnore]
    public ExpressionNode? Owner => Children.Length == 2 ? (ExpressionNode)Children[0] : null;

    /// <summary>
    /// The identifier naming the member being accessed. A view over <see cref="SyntaxNode.Children"/>,
    /// not separately stored.
    /// </summary>
    [JsonIgnore]
    public SimpleNameExpressionNode Member => (SimpleNameExpressionNode)Children[^1];
}
