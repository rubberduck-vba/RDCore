using System.Text;

namespace RDCore.SDK.Model.Symbols.Completion;

/// <summary>
/// What comes next in a statement that is being written, which decides whether the names of the language are offered or its operators.
/// </summary>
public enum CompletionExpectation
{
    /// <summary>A statement starts: a statement keyword, or the name a call or an assignment starts with.</summary>
    Statement,

    /// <summary>An operand: a name, a literal, or a keyword that starts an expression (<c>Not</c>, <c>New</c>, <c>Nothing</c>).</summary>
    Operand,

    /// <summary>
    /// What follows an operand that is complete: an operator (<c>And</c>, <c>Mod</c>, <c>Like</c>), or the keyword that goes on with the statement (<c>Then</c>,
    /// <c>To</c>). A name cannot be written here, and is not offered.
    /// </summary>
    Operator,
}

/// <summary>
/// The statement a name is being written in, as tokens: what a completion list needs to know of the code before the name - which statement it is, and where in it.
/// </summary>
/// <remarks>
/// The tokens are a lexer's, and not the parser's: the statement is not finished, and need not parse. A word is a name or a keyword, with the type hint it has; a
/// string literal is <c>""</c>, whatever is in it; a number is its digits; any other character is a token of its own, but for the two-character operators
/// <c>:=</c>, <c>&lt;=</c>, <c>&gt;=</c> and <c>&lt;&gt;</c>.
/// </remarks>
public static class CompletionStatement
{
    // the keywords after which an operand comes (MS-VBAL §5.6.9): the operators that are words, and the words of statements that are followed by an expression.
    private static readonly HashSet<string> OperandAfter = new(StringComparer.OrdinalIgnoreCase)
    {
        "And", "Or", "Xor", "Eqv", "Imp", "Mod", "Like", "Is", "Not", "To", "Step", "Then", "Else", "Case", "Print", "Call", "New", "TypeOf", "AddressOf", "ByVal",
        "If", "ElseIf", "While", "Until", "Select", "Set", "Let", "Return", "In", "Each", "For", "Erase", "ReDim", "Preserve", "GoTo", "GoSub", "Resume", "LSet", "RSet",
    };

    /// <summary>
    /// The tokens of <paramref name="statement"/>.
    /// </summary>
    /// <param name="statement">The statement, up to where the name is written.</param>
    public static IReadOnlyList<string> Tokenize(string statement) => [.. Lex(statement).Select(token => token.Text)];

    // the tokens, and where each ends in the text.
    private static List<(string Text, int End)> Lex(string statement)
    {
        var tokens = new List<(string Text, int End)>();
        var index = 0;
        while (index < statement.Length)
        {
            var character = statement[index];
            if (char.IsWhiteSpace(character))
            {
                index++;
            }
            else if (character == '"')
            {
                var end = statement.IndexOf('"', index + 1);
                while (end >= 0 && end + 1 < statement.Length && statement[end + 1] == '"')
                {
                    end = statement.IndexOf('"', end + 2);
                }

                index = end < 0 ? statement.Length : end + 1;
                tokens.Add(("\"\"", index));
            }
            else if (char.IsLetter(character) || character == '_' || character == '[')
            {
                var start = index;
                if (character == '[')
                {
                    var closing = statement.IndexOf(']', index);
                    index = closing < 0 ? statement.Length : closing + 1;
                }
                else
                {
                    while (index < statement.Length && (char.IsLetterOrDigit(statement[index]) || statement[index] == '_'))
                    {
                        index++;
                    }
                }

                // a type hint is the name's, but for `&` and `!`, which are operators when a space follows them.
                if (index < statement.Length && statement[index] is '$' or '%' or '#' or '@' or '^')
                {
                    index++;
                }

                tokens.Add((statement[start..index], index));
            }
            else if (char.IsAsciiDigit(character))
            {
                var start = index;
                while (index < statement.Length && (char.IsAsciiLetterOrDigit(statement[index]) || statement[index] is '.' or '#'))
                {
                    index++;
                }

                tokens.Add((statement[start..index], index));
            }
            else
            {
                var pair = index + 1 < statement.Length ? statement.Substring(index, 2) : string.Empty;
                if (pair is ":=" or "<=" or ">=" or "<>")
                {
                    index += 2;
                    tokens.Add((pair, index));
                }
                else
                {
                    index++;
                    tokens.Add((character.ToString(), index));
                }
            }
        }

        return tokens;
    }

    /// <summary>
    /// What comes next after <paramref name="tokens"/>.
    /// </summary>
    /// <param name="tokens">The tokens of the statement before the name (<see cref="Tokenize"/>).</param>
    public static CompletionExpectation Expectation(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return CompletionExpectation.Statement;
        }

        var last = tokens[^1];
        if (IsWord(last))
        {
            // a statement that is a name and a space is a call with its arguments to come (MS-VBAL §5.4.2.1): `MsgBox |`.
            return OperandAfter.Contains(last) || tokens.Count == 1 ? CompletionExpectation.Operand : CompletionExpectation.Operator;
        }

        return last is ")" or "\"\"" || char.IsAsciiDigit(last[0]) ? CompletionExpectation.Operator : CompletionExpectation.Operand;
    }

    /// <summary>Whether a token is a word: a name, or a keyword.</summary>
    /// <param name="token">The token.</param>
    public static bool IsWord(string token) => token.Length > 0 && (char.IsLetter(token[0]) || token[0] is '_' or '[');

    /// <summary>Whether a token is the keyword <paramref name="keyword"/>, in any case.</summary>
    /// <param name="token">The token.</param>
    /// <param name="keyword">The keyword.</param>
    public static bool Is(this string token, string keyword) => string.Equals(token, keyword, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The statement the end of <paramref name="text"/> is in: after the last <c>:</c> that separates statements, the <c>Then</c> or the <c>Else</c> of a single-line
    /// <c>If</c>, and the line number or the label the line starts with.
    /// </summary>
    /// <param name="text">The text of the line, up to where the name is written.</param>
    public static string StatementOf(string text)
    {
        var statement = new StringBuilder();
        var inString = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                inString = !inString;
            }
            else if (!inString && character == ':' && (index + 1 >= text.Length || text[index + 1] != '='))
            {
                statement.Clear();
                continue;
            }

            statement.Append(character);
        }

        var result = statement.ToString().TrimStart();
        var digits = 0;
        while (digits < result.Length && char.IsAsciiDigit(result[digits]))
        {
            digits++;
        }

        if (digits > 0 && (digits == result.Length || char.IsWhiteSpace(result[digits])))
        {
            result = result[digits..].TrimStart();
        }

        // the statement after the Then or the Else of a single-line If is a statement of its own.
        var after = Lex(result).LastOrDefault(token => token.Text.Is("Then") || token.Text.Is("Else"));
        return after.Text is null ? result : result[after.End..].TrimStart();
    }
}
