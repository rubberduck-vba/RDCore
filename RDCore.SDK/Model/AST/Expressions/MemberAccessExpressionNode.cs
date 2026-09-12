using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using System.Text.Json.Serialization;

namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// <strong>MS-VBAL 5.6</strong> <c>member-access-expression</c> (<c>owner.member</c>) and its
/// <c>with-expression</c> sibling (<c>.member</c>, <see cref="Owner"/> omitted). Both are part of the
/// <c>l-expression</c> grammar family alongside <see cref="SimpleNameExpressionNode"/> — a pure AST
/// node; its static and run-time semantics (owner-type member lookup, the late-bound
/// <c>Variant</c>/<c>Object</c> path, the design-time vs. immediate-pane error split) live in the
/// semantics layer, not here.
/// </summary>
/// <remarks>
/// <see cref="Owner"/> and <see cref="Member"/> are views over <see cref="SyntaxNode.Children"/>, not
/// separately stored — <c>Children</c> is the single source of truth a <c>with</c> expression can't
/// desync them from. Not yet represented: <em>dictionary access</em> (<c>!</c>) — MS-VBAL's
/// <c>dictionary-access-expression</c> is a distinct <c>l-expression</c> alternative, not a flag on
/// this one; it needs its own node once a parser builds one.
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> (holds the document <c>Uri</c> and a <c>Range</c>) of the bound expression.</param>
/// <param name="Owner">
/// The expression the member is accessed on, or <c>null</c> for a <c>with-expression</c> — a
/// leading-dot reference (<c>.member</c>) whose owner is implicit, legal only inside a <c>With</c>
/// block.
/// </param>
/// <param name="Member">The identifier naming the member being accessed.</param>
public sealed record class MemberAccessExpressionNode(SyntaxNodeId Identity, SourceLocation Location, ExpressionNode? Owner, SimpleNameExpressionNode Member)
    : ExpressionNode(Identity, Location, Owner is null ? [Member] : [Owner, Member])
{
    /// <summary>
    /// The expression the member is accessed on, or <c>null</c> for a <c>with-expression</c> — a
    /// leading-dot reference (<c>.member</c>) whose owner is implicit, legal only inside a <c>With</c>
    /// block. A view over <see cref="SyntaxNode.Children"/>, not separately stored.
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
