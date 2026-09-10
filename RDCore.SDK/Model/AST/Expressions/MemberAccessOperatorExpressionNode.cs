using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Expressions;

/// <summary>
/// <strong>MS-VBAL 5.6.x</strong> member access (<c>.</c>) and dictionary access (<c>!</c>). A pure
/// AST node — its static and run-time semantics (owner-type member lookup, the late-bound
/// <c>Variant</c>/<c>Object</c> path, dictionary access via the default member, the
/// design-time vs. immediate-pane error split) live in the semantics layer, not here.
/// </summary>
public record class MemberAccessOperatorExpressionNode(
    SyntaxNodeId Identity,
    SourceLocation Location,
    ImmutableArray<SyntaxNode> Children)
    : VBBinaryOperatorExpressionNode(Tokens.MemberAccess, Identity, Location, Children);