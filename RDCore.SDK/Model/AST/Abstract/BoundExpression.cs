using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Model.AST.Abstract
{
    /// <summary>
    /// A <c>BoundNode</c> that can be statically evaluated to a <c>VBType</c>, and with runtime semantics to a <c>VBTypedValue</c>.
    /// </summary>
    /// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
    /// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
    public abstract record class BoundExpression(Uri SemanticId, Location Location) : BoundNode(SemanticId, Location) { }
}
