using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.SDK.Model.AST.Statements;

/// <summary>
/// <strong>MS-VBAL §5.4.3.5</strong> Mid/MidB/Mid$/MidB$ Statement — replaces a span of characters
/// (or bytes, in the <c>MidB</c>/<c>MidB$</c> forms) within <see cref="Target"/>, starting at the
/// 1-based position <see cref="Start"/>, with characters from <see cref="Value"/>.
/// </summary>
/// <remarks>
/// <see cref="IsByteMode"/> and <see cref="IsStringInput"/> are independent: all four spellings
/// (<c>Mid</c>, <c>Mid$</c>, <c>MidB</c>, <c>MidB$</c>) are distinct combinations of the two. The
/// runtime replacement mechanics MS-VBAL §5.4.3.5 spells out only ever split on byte vs. character
/// indexing (<c>Mid</c>/<c>Mid$</c> together vs. <c>MidB</c>/<c>MidB$</c> together) — but the trailing
/// <c>$</c> still has to survive into the AST, because it mirrors the same intrinsic-family split as
/// the <c>Mid</c>/<c>Mid$</c> *function* overloads (<c>Function Mid(String As Variant, ...) As
/// Variant</c> vs. the String-only <c>$</c> form): <see cref="Target"/>/<see cref="Value"/> coerce
/// against <c>VBVariant</c> without the suffix and against <c>VBString</c> with it. Dropping the
/// suffix here would silently collapse two different static-semantics signatures into one.
/// </remarks>
/// <param name="Identity">A unique identifier for this specific syntax node.</param>
/// <param name="SourceLocation">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
/// <param name="IsByteMode"><c>true</c> for <c>MidB</c>/<c>MidB$</c> (byte-indexed); <c>false</c> for <c>Mid</c>/<c>Mid$</c> (character-indexed).</param>
/// <param name="IsStringInput"><c>true</c> when the mode specifier carries the <c>$</c> suffix (<c>Mid$</c>/<c>MidB$</c>).</param>
/// <param name="Target">The <c>bound-variable-expression</c> being modified in place.</param>
/// <param name="Start">The 1-based position within <see cref="Target"/> where replacement begins.</param>
/// <param name="Length">The maximum number of characters/bytes to replace, or <c>null</c> when omitted (replaces through the end of <see cref="Target"/> or <see cref="Value"/>, whichever is shorter).</param>
/// <param name="Value">The expression supplying the replacement characters/bytes.</param>
public record class MidStatementNode(SyntaxNodeId Identity, SourceLocation SourceLocation, bool IsByteMode, bool IsStringInput, ExpressionNode Target, ExpressionNode Start, ExpressionNode? Length, ExpressionNode Value)
    : StatementNode(Identity, SourceLocation, Length is null ? [Target, Start, Value] : [Target, Start, Length, Value]);
