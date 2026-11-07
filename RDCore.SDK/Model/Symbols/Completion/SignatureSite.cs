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

    /// <summary>
    /// The arguments of the call up to the cursor, as they are written: after the opening parenthesis of a call in parentheses, after the name and the space
    /// of a call statement. The last of them is the one the cursor is in.
    /// </summary>
    public string ArgumentsBefore { get; init; } = string.Empty;

    /// <summary>
    /// Whether the arguments are in parentheses, which close them; a call statement's arguments go on to the end of the statement.
    /// </summary>
    public bool IsParenthesized { get; init; }

    /// <summary>
    /// The names of the named arguments written before the one the cursor is in, as they are written (<c>Title</c> of <c>Title:="Hi"</c>).
    /// </summary>
    public IReadOnlyList<string> NamesBefore => [.. PrecedingArguments.Select(NameOf).OfType<string>()];

    /// <summary>
    /// How many of the arguments written before the one the cursor is in are positional, an omitted one counted (<c>Foo 1, , |</c> has two): each is mapped to
    /// the parameter at its position (<strong>MS-VBAL §5.3.1.11</strong>).
    /// </summary>
    public int PositionalBefore => PrecedingArguments.Count(argument => NameOf(argument) is null);

    /// <summary>
    /// Whether the cursor is where an argument starts, with nothing of it written but <paramref name="word"/>: after the opening parenthesis, after a comma,
    /// after the name of a call statement. Not after the <c>:=</c> of a named argument, nor inside an expression.
    /// </summary>
    /// <param name="word">What is typed of the name at the cursor.</param>
    public bool IsWhereAnArgumentStarts(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var current = TopLevelPartsOf(ArgumentsBefore)[^1];
        return current.EndsWith(word, StringComparison.Ordinal) && current[..^word.Length].Trim().Length == 0;
    }

    /// <summary>
    /// Whether the cursor is where the value of an argument starts, with nothing of it written but <paramref name="word"/>: where an argument starts
    /// (<see cref="IsWhereAnArgumentStarts"/>), or after the <c>:=</c> of a named argument (<c>Weigh Amounts:=|</c>).
    /// </summary>
    /// <param name="word">What is written of the value at the cursor.</param>
    public bool IsWhereAnArgumentsValueStarts(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var current = TopLevelPartsOf(ArgumentsBefore)[^1];
        if (NamedArgument().Match(current) is { Success: true } named)
        {
            current = current[named.Length..];
        }

        return current.EndsWith(word, StringComparison.Ordinal) && current[..^word.Length].Trim().Length == 0;
    }

    /// <summary>
    /// The names of the named arguments written after the one the cursor is in, up to the end of the argument list: its closing parenthesis, or the end of the
    /// call statement (a <c>:</c> that separates statements, a comment, the end of the logical line, <see cref="LogicalLine"/>).
    /// </summary>
    /// <param name="after">The text of the logical line from the cursor on.</param>
    public IReadOnlyList<string> NamesAfter(string after) => [.. ArgumentsAfter(after).Select(NameOf).OfType<string>()];

    /// <summary>
    /// Whether a positional argument is written after the one the cursor is in, an omitted one counted (<c>Foo 1, |, , 2</c>): it is mapped to the parameter at
    /// its position, and no named argument comes before it (<strong>MS-VBAL §5.6.13.1</strong>).
    /// </summary>
    /// <param name="after">The text of the logical line from the cursor on.</param>
    public bool IsFollowedByAPositionalArgument(string after) => ArgumentsAfter(after).Any(argument => NameOf(argument) is null);

    // the arguments written after the one the cursor is in, up to the end of the argument list.
    private List<string> ArgumentsAfter(string after)
    {
        ArgumentNullException.ThrowIfNull(after);
        var end = after.Length;
        var depth = 0;
        var inString = false;
        for (var index = 0; index < after.Length && end == after.Length; index++)
        {
            var character = after[index];
            if (character == '"')
            {
                inString = !inString;
            }
            else if (!inString)
            {
                switch (character)
                {
                    case '(':
                        depth++;
                        break;
                    case ')' when depth == 0:
                        end = index;
                        break;
                    case ')':
                        depth--;
                        break;
                    case '\'' when !IsParenthesized:
                    case ':' when !IsParenthesized && depth == 0 && (index + 1 >= after.Length || after[index + 1] != '='):
                        end = index;
                        break;
                }
            }
        }

        // the first part is the rest of the argument the cursor is in.
        return [.. TopLevelPartsOf(after[..end]).Skip(1)];
    }

    // the arguments before the one the cursor is in.
    private IReadOnlyList<string> PrecedingArguments => TopLevelPartsOf(ArgumentsBefore) is var parts ? parts.GetRange(0, parts.Count - 1) : [];

    // the text cut at each comma that is not in a string literal nor in parentheses.
    private static List<string> TopLevelPartsOf(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var inString = false;
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '"':
                    inString = !inString;
                    break;
                case '(' when !inString:
                    depth++;
                    break;
                case ')' when !inString:
                    depth--;
                    break;
                case ',' when !inString && depth == 0:
                    parts.Add(text[start..index]);
                    start = index + 1;
                    break;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

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
                case ':' when open.Count == 0 && (index + 1 >= text.Length || text[index + 1] != '='):
                    statementStart = index + 1;
                    break;
            }
        }

        // the innermost argument list that is a call's.
        for (var index = open.Count - 1; index >= 0; index--)
        {
            var list = open[index];
            if (CompletionSite.QualifierBefore(text, list.Opening) is { Length: > 0 } callee && !char.IsAsciiDigit(callee[0]))
            {
                return new SignatureSite(callee, list.Commas, NameOf(text[list.ArgumentStart..])) { ArgumentsBefore = text[(list.Opening + 1)..], IsParenthesized = true };
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

        // the arguments are counted from the call's name, not from the start of the line: `If a Then Send "x", 1 Else Send |` is at Send's first.
        var arguments = TopLevelPartsOf(call.Groups["arguments"].Value);
        return new SignatureSite(call.Groups["callee"].Value, arguments.Count - 1, NameOf(arguments[^1])) { ArgumentsBefore = call.Groups["arguments"].Value };
    }

    /// <summary>
    /// Whether the text up to the cursor ends where a value of the argument starts: after the opening parenthesis, a comma or a <c>:=</c>, after the name a call
    /// statement starts with, or after an operator that combines the flags of an <c>Enum</c> - <c>+</c>, <c>Or</c>, <c>And</c>, <c>Xor</c>, <c>Not</c>
    /// (<c>MsgBox "a", vbYesNo + |</c>).
    /// </summary>
    /// <remarks>
    /// A space anywhere else in an argument - after an operand, <c>Foo a |</c> - starts no value: a list of what the parameter is declared as has nothing to
    /// offer there.
    /// </remarks>
    /// <param name="text">The text of the line up to the cursor, which this site was read off.</param>
    public bool IsWhereAValueStarts(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var before = text.TrimEnd();
        if (before.Length == 0)
        {
            return false;
        }

        if (before[^1] is ',' or '(' or '+' || before.EndsWith(":=", StringComparison.Ordinal))
        {
            return true;
        }

        if (FlagOperator().IsMatch(before))
        {
            return true;
        }

        // the name a call statement starts with, and a space: its first argument starts there.
        return ArgumentIndex == 0 && before.EndsWith(Callee, StringComparison.OrdinalIgnoreCase)
            && (before.Length == Callee.Length || !IsNameCharacter(before[^(Callee.Length + 1)]));
    }

    private static bool IsNameCharacter(char character) => char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '!';

    [GeneratedRegex(@"(?:^|[\s(,])(?:Or|And|Xor|Not)$", RegexOptions.IgnoreCase)]
    private static partial Regex FlagOperator();

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
