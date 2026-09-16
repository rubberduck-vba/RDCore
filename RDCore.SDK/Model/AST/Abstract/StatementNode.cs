using RDCore.SDK.Model.Source;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace RDCore.SDK.Model.AST.Abstract;

/// <summary>
/// A <see cref="SyntaxNode"/> representing an <em>executable statement</em>.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Inputs">The <em>inputs</em> of the executable statement; expressions evaluated immediately before the call.</param>
[JsonConverter(typeof(SyntaxNodeSubtypeJsonConverter<StatementNode>))]
public abstract record class StatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ImmutableArray<SyntaxNode> Inputs)
    : SyntaxNode(Identity, SourceLocation, [.. Inputs]), IExecutableNode;
