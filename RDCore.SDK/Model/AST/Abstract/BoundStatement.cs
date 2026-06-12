using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Model.AST.Abstract;

/// <summary>
/// A <c>BoundNode</c> representing an <em>executable statement</em>.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
public abstract record class BoundStatement(Uri SemanticId, Location Location) : BoundNode(SemanticId, Location);
