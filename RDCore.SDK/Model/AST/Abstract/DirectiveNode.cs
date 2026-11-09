using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Abstract;


/// <summary>
/// A <see cref="SyntaxNode"/> representing a <em>module directive</em>, which is module metadata that is neither typed, nor executable.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Children">The child nodes of the directive.</param>
public abstract record class DirectiveNode(SyntaxNodeId Identity, SourceLocation SourceLocation, ImmutableArray<SyntaxNode> Children) 
    : SyntaxNode(Identity, SourceLocation, Children);

/// <summary>
/// A <em>syntax node</em> representing a <em>line label directive</em>, which is procedure metadata that defines a named symbol for various jump targets.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Name">The label name.</param>
public record class LineLabelNode(SyntaxNodeId Identity, SourceLocation SourceLocation, string Name) 
    : DirectiveNode(Identity, SourceLocation, []);
/// <summary>
/// A <em>line label directive</em> that represents a line number, which is procedure metadata that defines a named symbol for various jump targets.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Number">The label name.</param>
/// <remarks>
/// The string representation of the number is the label name.
/// </remarks>
public record class LineNumberNode(SyntaxNodeId Identity, SourceLocation SourceLocation, int Number)
    : LineLabelNode(Identity, SourceLocation, Number.ToString());