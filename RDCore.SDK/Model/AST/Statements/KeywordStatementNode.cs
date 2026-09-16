using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// An executable statement node that represents a fixed-keyword statement with a positional argument
/// list — the file statements (MS-VBAL §5.4.5 File Statements) and similarly-shaped statements
/// (<c>Erase</c>, <c>Name</c>, <c>RaiseEvent</c>) that are not a procedure/function call.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="Token">The statement's keyword (see <c>Tokens</c>), e.g. <c>Close</c>, <c>Erase</c>, <c>Name</c>, <c>RaiseEvent</c>.</param>
/// <param name="Inputs">The statement's arguments, in source order.</param>
public record class KeywordStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, string Token, ImmutableArray<SyntaxNode> Inputs)
    : StatementNode(Identity, SourceLocation, Inputs);
