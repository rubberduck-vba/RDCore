using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a statement that moves the <em>current instruction</em> pointer to a specified label.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="LabelExpression">An expression that resolves to the local label this statement jumps to.</param>
public record class GoToStatement(Uri SemanticId, Location Location, BoundExpression LabelExpression)
    : BoundStatement(SemanticId, Location, $"{Tokens.GoTo}", [LabelExpression]);
