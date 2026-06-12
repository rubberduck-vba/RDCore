using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Model.AST.Abstract;


/// <summary>
/// A <c>BoundNode</c> representing a <em>module directive</em>, which is module metadata that is neither typed, nor executable.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of this node.</param>
public abstract record class BoundDirective(Uri SemanticId, Location Location) : BoundNode(SemanticId, Location);
