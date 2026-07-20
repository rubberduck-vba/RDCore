using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// Represents a statement that pops an offset from the local <em>return stack</em>, then moves the <em>current instruction</em> pointer to that instruction.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
public record class ReturnStatement(Uri SemanticId, Location Location)
    : BoundStatement(SemanticId, Location, $"{Tokens.Return}", []);
