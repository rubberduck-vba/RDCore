using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a statement that resumes error handling, optionally at a specified label.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="LabelExpression">An expression that resolves to a <em>label</em> denoting the error-handling subroutine.</param>
/// <remarks>
/// This statement is only legal with an active error state.
/// </remarks>
public record class ResumeStatement(Uri SemanticId, Location Location, BoundExpression? LabelExpression)
    : BoundStatement(SemanticId, Location, $"{Tokens.Resume}{(LabelExpression is null ? string.Empty : $"-Label")}", LabelExpression is null ? [] : [LabelExpression]);
