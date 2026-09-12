using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using System.Text.Json.Serialization;

namespace RDCore.SDK.Model.AST.Directives;

/// <summary>
/// Represents an <c>Implements</c> directive.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="Location">The <c>Location</c> of the directive.</param>
public record class ImplementsDirectiveNode(SyntaxNodeId Identity, SourceLocation Location, SyntaxNode? NameExpression = null)
    : DirectiveNode(Identity, Location, NameExpression is null ? [] : [NameExpression])
{
    /// <summary>
    /// The name expression resolving the implemented interface, or <c>null</c> for a half-typed
    /// <c>Implements</c> with no name yet.
    /// </summary>
    [JsonIgnore]
    public SyntaxNode? NameExpression => Children.Length == 1 ? Children[0] : null;
}
