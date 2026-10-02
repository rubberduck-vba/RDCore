using RDCore.SDK.Model.Source;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// How a run of the text of a listing is shown, which the theme says in colours (<c>syntax</c> of a <c>.theme</c>).
/// </summary>
public enum ReplTextStyle
{
    /// <summary>As the shell shows everything else.</summary>
    Plain,

    /// <summary>A keyword.</summary>
    Keyword,

    /// <summary>A comment.</summary>
    Comment,

    /// <summary>A string literal.</summary>
    String,

    /// <summary>A numeric literal, or a line number.</summary>
    Number,

    /// <summary>A name.</summary>
    Identifier,

    /// <summary>The name of a type.</summary>
    IdentifierClass,

    /// <summary>The name of a constant.</summary>
    IdentifierConst,
}

/// <summary>
/// A run of text of one style.
/// </summary>
/// <param name="Text">The text, which is never more than a line.</param>
/// <param name="Style">How it is shown.</param>
public readonly record struct ReplTextRun(string Text, ReplTextStyle Style);

/// <summary>
/// One semantic token the language server answered with (<strong>LSP 3.17</strong> §Semantic Tokens), decoded: where it is, and what it is by the legend of the
/// platform (<see cref="SemanticTokenLegend"/>).
/// </summary>
/// <param name="Line">The zero-based line.</param>
/// <param name="Character">The zero-based character of the line.</param>
/// <param name="Length">The length of the token.</param>
/// <param name="Type">The index of the token type in <see cref="SemanticTokenLegend.TokenTypes"/>.</param>
/// <param name="Modifiers">The token modifiers, as the bits of <see cref="SemanticTokenLegend.TokenModifiers"/>.</param>
public readonly record struct ReplSemanticToken(int Line, int Character, int Length, int Type, int Modifiers)
{
    /// <summary>
    /// Decodes the relative encoding of an answer: five integers a token, each line and start relative to the token before it.
    /// </summary>
    /// <param name="data">The <c>data</c> of the answer.</param>
    public static IReadOnlyList<ReplSemanticToken> Decode(IReadOnlyList<int> data)
    {
        var tokens = new List<ReplSemanticToken>(data.Count / 5);
        int line = 0, character = 0;
        for (var index = 0; index + 4 < data.Count; index += 5)
        {
            line += data[index];
            character = data[index] == 0 ? character + data[index + 1] : data[index + 1];
            tokens.Add(new ReplSemanticToken(line, character, data[index + 2], data[index + 3], data[index + 4]));
        }

        return tokens;
    }

    /// <summary>How the token is shown.</summary>
    public ReplTextStyle Style => SemanticTokenLegend.TokenTypes.ElementAtOrDefault(Type) switch
    {
        "keyword" => ReplTextStyle.Keyword,
        "comment" => ReplTextStyle.Comment,
        "string" => ReplTextStyle.String,
        "number" => ReplTextStyle.Number,
        "class" => ReplTextStyle.IdentifierClass,
        "variable" when (Modifiers & SemanticTokenLegend.ReadOnly) != 0 => ReplTextStyle.IdentifierConst,
        "variable" => ReplTextStyle.Identifier,
        _ => ReplTextStyle.Plain,
    };
}

/// <summary>
/// Cuts a line of text into the runs its semantic tokens make.
/// </summary>
public static class ReplHighlighter
{
    /// <summary>
    /// The runs of a line of text, which are all of it: the text between tokens is a run of its own, plain.
    /// </summary>
    /// <param name="text">The line.</param>
    /// <param name="tokens">The tokens that are on it, by their character; tokens that are not on it, or are past it, are not taken.</param>
    /// <param name="offset">How many characters the line is further along than the text the tokens are of, which is how far the shell has put it right.</param>
    public static IReadOnlyList<ReplTextRun> Runs(string text, IEnumerable<ReplSemanticToken> tokens, int offset = 0)
    {
        var runs = new List<ReplTextRun>();
        var at = 0;
        foreach (var token in tokens.OrderBy(token => token.Character))
        {
            var start = token.Character + offset;
            var end = start + token.Length;
            if (start < at || end > text.Length)
            {
                continue;
            }

            if (start > at)
            {
                runs.Add(new ReplTextRun(text[at..start], ReplTextStyle.Plain));
            }

            runs.Add(new ReplTextRun(text[start..end], token.Style));
            at = end;
        }

        if (at < text.Length)
        {
            runs.Add(new ReplTextRun(text[at..], ReplTextStyle.Plain));
        }

        return runs;
    }
}
