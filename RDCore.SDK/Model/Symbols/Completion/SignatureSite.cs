using System.Text.RegularExpressions;

namespace RDCore.SDK.Model.Symbols.Completion;

/// <summary>
/// The call being written where a signature help is asked for (<strong>LSP 3.17</strong> §Signature Help): what is called, and which of its arguments the cursor
/// is in - read off the text of the line before the cursor, as what is being written is for a completion list (<see cref="CompletionSite"/>).
/// </summary>
/// <remarks>
/// <para>
/// The call is the innermost one whose argument list is open at the cursor: <c>Left(x, |</c>, <c>o.Item(|</c>, the <c>Mid(</c> of <c>Debug.Print Left(Mid(s, 2|</c>.
/// An argument list that is open after no name - the parentheses of an expression, <c>(a + |</c> - is no call's, and the one around it is looked for. With no
/// argument list open, the call is the statement's own when it is a call statement without parentheses (<strong>MS-VBAL §5.4.2.1</strong>): <c>MsgBox "a", |</c>.
/// </para>
/// <para>
/// Which argument the cursor is in is counted by the commas of the argument list before it, those of string literals and of argument lists inside it left out; a
/// named argument (<c>Prompt:=</c>) says the name of the parameter it is given to. A <c>:</c> that is not the start of a <c>:=</c> separates statements.
/// </para>
/// </remarks>
/// <param name="Callee">What is called, as it is written: <c>Left</c>, <c>o.Item</c>, <c>.Add</c>, <c>w.Grow(2).Parts.Add</c>.</param>
/// <param name="ArgumentIndex">The position of the argument the cursor is in, from 0.</param>
/// <param name="ArgumentName">The name the argument is written with, for a named argument; <see langword="null"/> otherwise.</param>
public sealed partial record class SignatureSite(string Callee, int ArgumentIndex, string? ArgumentName = null)
{
    /// <summary>
    /// Whether what is called is a member of something - of the expression of <see cref="Qualifier"/>, or of the object of a <c>With</c> block when that is empty.
    /// </summary>
    public bool IsMemberAccess => SeparatorIndex >= 0;

    /// <summary>
    /// For a <see cref="IsMemberAccess"/>, what the member is of, as it is written: <c>o</c> of <c>o.Item</c>, <c>.Items</c> of <c>.Items.Add</c>, empty for the
    /// <c>.Add</c> of a <c>With</c> block's object. Empty for a call of a name.
    /// </summary>
    public string Qualifier => SeparatorIndex < 0 ? string.Empty : Callee[..SeparatorIndex];

    /// <summary>The name of what is called: <c>Item</c> of <c>o.Item</c>.</summary>
    public string Name => Callee[(SeparatorIndex + 1)..];

    // the last `.` or `!` of the callee that is not in an argument list of it.
    private int SeparatorIndex
    {
        get
        {
            var depth = 0;
            for (var index = Callee.Length - 1; index >= 0; index--)
            {
                switch (Callee[index])
                {
                    case ')':
                        depth++;
                        break;
                    case '(':
                        depth--;
                        break;
                    case '.' or '!' when depth == 0:
                        return index;
                }
            }

            return -1;
        }
    }

    /// <summary>
    /// Reads the call being written at the end of <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The text of the line, up to the cursor.</param>
    /// <returns>The call; <see langword="null"/> when the cursor is in none - or in a string literal, or in a comment.</returns>
    public static SignatureSite? Read(string text)
    {
        if (CompletionSite.IsInLiteralOrComment(text))
        {
            return null;
        }

        var open = new List<ArgumentList>();
        var statementStart = 0;
        var commas = 0;
        var argumentStart = -1;
        var inString = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            switch (character)
            {
                case '(':
                    open.Add(new ArgumentList(index));
                    break;
                case ')' when open.Count > 0:
                    open.RemoveAt(open.Count - 1);
                    break;
                case ',' when open.Count > 0:
                    open[^1].Commas++;
                    open[^1].ArgumentStart = index + 1;
                    break;
                case ',':
                    commas++;
                    argumentStart = index + 1;
                    break;
                case ':' when open.Count == 0 && (index + 1 >= text.Length || text[index + 1] != '='):
                    (statementStart, commas, argumentStart) = (index + 1, 0, -1);
                    break;
            }
        }

        // the innermost argument list that is a call's.
        for (var index = open.Count - 1; index >= 0; index--)
        {
            var list = open[index];
            if (CompletionSite.QualifierBefore(text, list.Opening) is { Length: > 0 } callee && !char.IsAsciiDigit(callee[0]))
            {
                return new SignatureSite(callee, list.Commas, NameOf(text[list.ArgumentStart..]));
            }
        }

        if (open.Count > 0)
        {
            return null;
        }

        // a call statement without parentheses: the name it starts with, a space, and its arguments - which an assignment is not.
        var statement = StatementOf(text[statementStart..]);
        if (CallStatement().Match(statement) is not { Success: true } call || call.Groups["arguments"].Value.TrimStart().StartsWith('='))
        {
            return null;
        }

        var arguments = argumentStart < 0 ? call.Groups["arguments"].Value : text[argumentStart..];
        return new SignatureSite(call.Groups["callee"].Value, commas, NameOf(arguments));
    }

    // what of a statement the call is: after its line number, and after the Then or the Else of a single-line If.
    private static string StatementOf(string text)
    {
        var statement = LineNumber().Replace(text, string.Empty);
        var clause = ClauseKeyword().Matches(statement).LastOrDefault();
        return clause is null ? statement : statement[(clause.Index + clause.Length)..];
    }

    private static string? NameOf(string argument) => NamedArgument().Match(argument) is { Success: true } named ? named.Groups["name"].Value : null;

    [GeneratedRegex(@"^\s*\d*\s*")]
    private static partial Regex LineNumber();

    // a Then or an Else outside string literals: every `"` before it is closed.
    [GeneratedRegex(@"\b(Then|Else)\s+(?=(?:[^""]*""[^""]*"")*[^""]*$)", RegexOptions.IgnoreCase)]
    private static partial Regex ClauseKeyword();

    [GeneratedRegex(@"^\s*(?<callee>\.?[A-Za-z_][A-Za-z0-9_]*[$%&#@^]?(?:[.!][A-Za-z_][A-Za-z0-9_]*[$%&#@^]?)*)\s+(?<arguments>.*)$", RegexOptions.Singleline)]
    private static partial Regex CallStatement();

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:=")]
    private static partial Regex NamedArgument();

    private sealed class ArgumentList(int opening)
    {
        public int Opening { get; } = opening;

        public int Commas { get; set; }

        public int ArgumentStart { get; set; } = opening + 1;
    }
}
