namespace RDCore.SDK.Model.Source;

/// <summary>
/// What a token of source text is, as far as its spelling and its place in a line say: the classification a listing is highlighted by.
/// </summary>
/// <remarks>
/// A syntax token kind is lexical. It never says what a name <em>means</em>: that a word is an identifier is known from the text alone, while that it is
/// the name of a constant is a fact of the module it is in, which a consumer adds (<see cref="SemanticTokenLegend"/>).
/// </remarks>
public enum SyntaxTokenKind
{
    /// <summary>A reserved word or a keyword of the language, as it is used as one (<c>Dim</c>, <c>If</c>, <c>Print</c>).</summary>
    Keyword,

    /// <summary>A comment: from the <c>'</c> or the <c>Rem</c> that starts it to the end of the logical line.</summary>
    Comment,

    /// <summary>A string literal, with its quotes.</summary>
    String,

    /// <summary>A numeric literal, a date literal, or the line number that labels a line (<strong>MS-VBAL §3.3.2</strong>).</summary>
    Number,

    /// <summary>An operator or a delimiter (<c>+</c>, <c>=</c>, <c>(</c>).</summary>
    Operator,

    /// <summary>A name: of a variable, a procedure, a type, a member. Which of them is not known from the spelling.</summary>
    Identifier,

    /// <summary>
    /// The name of a type, where its place says it is one: after <c>As</c>, <c>As New</c> or <c>Implements</c>.
    /// </summary>
    TypeName,
}

/// <summary>
/// One token of source text, located by line and character.
/// </summary>
/// <remarks>
/// Zero-based, with the characters counted in UTF-16 code units, which is how LSP 3.17 counts them (§Position): a token is what a client can lay a style on
/// as it is. A token never spans lines.
/// </remarks>
public sealed class SyntaxToken
{
    /// <summary>The zero-based line of the token.</summary>
    public int Line { get; set; }

    /// <summary>The zero-based character of the token in its line, in UTF-16 code units.</summary>
    public int Character { get; set; }

    /// <summary>The length of the token, in UTF-16 code units.</summary>
    public int Length { get; set; }

    /// <summary>What the token is.</summary>
    public SyntaxTokenKind Kind { get; set; }
}
