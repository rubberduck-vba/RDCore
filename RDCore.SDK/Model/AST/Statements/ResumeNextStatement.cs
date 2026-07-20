using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a statement that resumes error handling at the instruction following the instruction that tripped the last <c>On Error</c> jump.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <remarks>
/// This statement is only legal with an active error state.
/// </remarks>
public record class ResumeNextStatement(Uri SemanticId, Location Location)
    : BoundStatement(SemanticId, Location, $"{Tokens.Resume}-{Tokens.Next}", []);
