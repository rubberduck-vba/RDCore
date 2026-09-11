using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Declarations;

/// <summary>
/// An AST root node, representing an entire module.
/// </summary>
/// <remarks>
/// A module's <em>kind</em> — standard vs. class vs. document/form — is not a syntactic fact this
/// node carries: the parser is never told it and does not derive it (the grammar drops the
/// <c>VERSION</c>/<c>BEGIN…END</c> header rather than keeping it as trivia). It is a property of the
/// <c>Attribute VB_*</c> block a caller reads off the parsed tree, or of the raw source
/// (<c>RDCore.SDK.Workspace.ModuleHeader.IsClassModule</c>), on whichever side of the pipe needs it.
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The source location of this module; the <c>SourceRange</c> is invalid.</param>
/// <param name="Children">An immutable array containing all AST nodes of this module.</param>
public record class ModuleNode(SyntaxNodeId Identity, SourceLocation Location, ImmutableArray<SyntaxNode> Children)
    : SyntaxNode(Identity, Location, Children);
