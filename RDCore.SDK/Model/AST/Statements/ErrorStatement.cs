using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a (legacy) statement that raises a run-time error.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="NumberExpression">The number/code of the run-time error to raise.</param>
public record class ErrorStatement(Uri SemanticId, Location Location, BoundExpression NumberExpression)
    : BoundStatement(SemanticId, Location, $"{Tokens.Error}", [NumberExpression]);
