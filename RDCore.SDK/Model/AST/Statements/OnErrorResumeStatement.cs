using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a statement that disables error handling.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
public record class OnErrorResumeStatement(Uri SemanticId, Location Location)
    : BoundStatement(SemanticId, Location, $"{Tokens.On}{Tokens.Error}{Tokens.Resume}{Tokens.Next}", []);
