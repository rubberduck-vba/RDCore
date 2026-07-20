using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a <c>Do...Loop</c> construct.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Body">The executable statements in the body of the loop.</param>
/// <remarks>
/// If the <c>Body</c> contains no <c>Exit</c> statement (conditional or not), the loop is deterministically infinite.
/// </remarks>
public record DoLoopStatement(Uri SemanticId, Location Location, StatementBlock Body)
    : BoundStatement(SemanticId, Location, $"{Tokens.Do}-{Tokens.Loop}", []);

