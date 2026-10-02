namespace RDCore.SDK.Model.Source;

/// <summary>
/// The legend of the semantic tokens the platform serves (<strong>LSP 3.17</strong> §Semantic Tokens): the token types and token modifiers the integers of a
/// <c>textDocument/semanticTokens</c> answer index into.
/// </summary>
/// <remarks>
/// Every entry is one of the standard token types and modifiers of the protocol, so that a client that knows none of the platform's themes still shows
/// something sensible. The server declares the legend in its capabilities, and a client that decodes an answer reads it from there; this is the one place
/// that says what it is.
/// </remarks>
public static class SemanticTokenLegend
{
    /// <summary>The token types, in the order their index is the one in an answer.</summary>
    public static IReadOnlyList<string> TokenTypes { get; } =
    [
        "keyword",
        "comment",
        "string",
        "number",
        "operator",
        "variable",
        "class",
    ];

    /// <summary>The token modifiers, in the order their bit is the one in an answer.</summary>
    public static IReadOnlyList<string> TokenModifiers { get; } =
    [
        "readonly",
        "declaration",
    ];

    /// <summary>The modifier of a name that is a constant, as a bit.</summary>
    public const int ReadOnly = 1 << 0;

    /// <summary>The modifier of a name where it is declared, as a bit.</summary>
    public const int Declaration = 1 << 1;

    /// <summary>
    /// The index in <see cref="TokenTypes"/> of the type of a token.
    /// </summary>
    /// <param name="kind">What the token is.</param>
    public static int TypeOf(SyntaxTokenKind kind) => kind switch
    {
        SyntaxTokenKind.Keyword => 0,
        SyntaxTokenKind.Comment => 1,
        SyntaxTokenKind.String => 2,
        SyntaxTokenKind.Number => 3,
        SyntaxTokenKind.Operator => 4,
        SyntaxTokenKind.TypeName => 6,
        _ => 5,
    };
}
