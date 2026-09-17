using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// <strong>MS-VBAL §5.4.3.5</strong> Mid/MidB/Mid$/MidB$ Statement — replaces a span of characters
/// (or bytes, in the <c>MidB</c>/<c>MidB$</c> forms) within <see cref="Target"/>, starting at the
/// 1-based position <see cref="Start"/>, with characters from <see cref="Value"/>.
/// </summary>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="IsByteMode"><c>true</c> for <c>MidB</c>/<c>MidB$</c> (byte-indexed); <c>false</c> for <c>Mid</c>/<c>Mid$</c> (character-indexed).</param>
/// <param name="Target">The <c>bound-variable-expression</c> being modified in place.</param>
/// <param name="Start">The 1-based position within <see cref="Target"/> where replacement begins.</param>
/// <param name="Length">The maximum number of characters/bytes to replace, or <c>null</c> when omitted (replaces through the end of <see cref="Target"/> or <see cref="Value"/>, whichever is shorter).</param>
/// <param name="Value">The expression supplying the replacement characters/bytes.</param>
public record class MidStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, bool IsByteMode, ExpressionNode Target, ExpressionNode Start, ExpressionNode? Length, ExpressionNode Value)
    : StatementNode(Identity, SourceLocation, Length is null ? [Target, Start, Value] : [Target, Start, Length, Value]);
