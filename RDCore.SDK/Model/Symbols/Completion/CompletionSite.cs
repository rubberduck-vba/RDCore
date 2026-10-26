namespace RDCore.SDK.Model.Symbols.Completion;

/// <summary>
/// What is being written where a completion list is asked for, which decides what the list offers.
/// </summary>
public enum CompletionSiteKind
{
    /// <summary>Nothing is offered: the cursor is in a string literal, or in a comment.</summary>
    None,

    /// <summary>A name in the default binding context (<strong>MS-VBAL §5.6.4</strong>): a variable, a procedure, a constant, a module - or a keyword.</summary>
    Name,

    /// <summary>The name of a member, after a <c>.</c> or a <c>!</c>: what the expression before it has (<strong>MS-VBAL §5.6.12</strong>).</summary>
    Member,

    /// <summary>A name in the type binding context, after <c>As</c> (<strong>MS-VBAL §5.6.4</strong>): an intrinsic type, a class, a user-defined type, an Enum.</summary>
    Type,

    /// <summary>The operand of <c>New</c> (<strong>MS-VBAL §5.6.13.3</strong>): a class.</summary>
    NewType,
}

/// <summary>
/// What is being written where a completion list is asked for: read off the text of the line before the cursor, which is what is known for sure of code that is
/// being typed - the rest of it need not parse, and usually does not.
/// </summary>
/// <param name="Kind">What is offered.</param>
/// <param name="Prefix">The part of the name that is typed already, which the list is narrowed to: what a chosen item replaces.</param>
/// <param name="Qualifier">
/// For <see cref="CompletionSiteKind.Member"/>, the expression before the <c>.</c>, as it is written (<c>Foo.Bar(1)</c>); an empty string for a member access
/// with no expression before it, which is a member of the object of the enclosing <c>With</c> block (<strong>MS-VBAL §5.4.2.21</strong>), and one that starts
/// with a <c>.</c> for a member of a member of that object (<c>.Items(1)</c>). For a qualified type
/// name (<c>As VBA.Collection</c>), the qualifier of it. Empty otherwise.
/// </param>
/// <param name="IsStatementStart">Whether the name is the first thing of a statement, where a statement keyword can be written.</param>
/// <param name="Statement">
/// For <see cref="CompletionSiteKind.Name"/> and <see cref="CompletionSiteKind.Type"/>, the statement the name is written in, up to the name: what decides which
/// names and keywords can be written there (<see cref="CompletionStatement"/>). Empty where a statement starts.
/// </param>
public sealed record class CompletionSite(CompletionSiteKind Kind, string Prefix = "", string Qualifier = "", bool IsStatementStart = false, string Statement = "")
{
    /// <summary>The tokens of <see cref="Statement"/>.</summary>
    public IReadOnlyList<string> Tokens => CompletionStatement.Tokenize(Statement);

    /// <summary>What comes next in <see cref="Statement"/>: a statement, an operand, or an operator.</summary>
    public CompletionExpectation Expectation => CompletionStatement.Expectation(Tokens);

    /// <summary>
    /// Reads what is being written at the end of <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The text of the line, up to the cursor.</param>
    public static CompletionSite Read(string text)
    {
        if (IsInLiteralOrComment(text))
        {
            return new(CompletionSiteKind.None);
        }

        var start = text.Length;
        while (start > 0 && IsIdentifierCharacter(text[start - 1]))
        {
            start--;
        }

        var prefix = text[start..];
        if (prefix.Length > 0 && char.IsAsciiDigit(prefix[0]))
        {
            // a number is being typed - a literal, or a line number: there is no name to complete.
            return new(CompletionSiteKind.None);
        }

        if (start > 0 && text[start - 1] is '.' or '!')
        {
            var qualifier = QualifierBefore(text, start - 1);
            if (qualifier is null)
            {
                return new(CompletionSiteKind.None);
            }

            // `As VBA.Coll|` is a qualified type name, and `New VBA.Coll|` the operand of New: the qualifier is a project or a module, and what is offered are types.
            var before = text[..(start - 1 - qualifier.Length)];
            return LastWordOf(before) switch
            {
                var word when word.Equals("As", StringComparison.OrdinalIgnoreCase) && qualifier.Length > 0 => new(CompletionSiteKind.Type, prefix, qualifier),
                var word when word.Equals("New", StringComparison.OrdinalIgnoreCase) && qualifier.Length > 0 => new(CompletionSiteKind.NewType, prefix, qualifier),
                _ => new(CompletionSiteKind.Member, prefix, qualifier),
            };
        }

        var preceding = text[..start];
        var statement = CompletionStatement.StatementOf(preceding);
        return LastWordOf(preceding) switch
        {
            var word when word.Equals("As", StringComparison.OrdinalIgnoreCase) => new(CompletionSiteKind.Type, prefix, Statement: statement),
            var word when word.Equals("New", StringComparison.OrdinalIgnoreCase) => new(CompletionSiteKind.NewType, prefix),
            _ => new(CompletionSiteKind.Name, prefix, IsStatementStart: StartsStatement(preceding), Statement: statement),
        };
    }

    private static bool IsIdentifierCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';

    // a string literal that is not closed yet, or a comment, runs to the end of the line: nothing typed in either is a name.
    internal static bool IsInLiteralOrComment(string text)
    {
        var inString = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                inString = !inString;
            }
            else if (!inString && character == '\'')
            {
                return true;
            }
            else if (!inString && IsRemAt(text, index))
            {
                return true;
            }
        }

        return inString;
    }

    // `Rem` is a comment where a statement starts, and followed by a space or nothing: `Remark = 1` is an assignment.
    private static bool IsRemAt(string text, int index)
        => index + 3 <= text.Length
            && text.AsSpan(index, 3).Equals("Rem", StringComparison.OrdinalIgnoreCase)
            && (index + 3 == text.Length || char.IsWhiteSpace(text[index + 3]))
            && StartsStatement(text[..index]);

    // the expression a member access is written on, back from the `.` at `dot`: names, the argument lists of calls and indexes, and the `.` and `!` between them.
    // An empty string when there is none (a With block's member); null when what is before the dot is not something a member is accessed on (`1.5`).
    internal static string? QualifierBefore(string text, int dot)
    {
        var end = dot;
        var index = dot;
        while (true)
        {
            // the type hint of a name (`Name$.`) is the name's.
            if (index > 0 && text[index - 1] is '$' or '%' or '&' or '#' or '@' or '^' && index > 1 && IsIdentifierCharacter(text[index - 2]))
            {
                index--;
            }

            while (index > 0 && text[index - 1] == ')')
            {
                index = OpeningParenthesisBefore(text, index - 1);
                if (index < 0)
                {
                    return null;
                }
            }

            var nameEnd = index;
            if (index > 0 && text[index - 1] == ']')
            {
                // a bracketed name, `[My Field]`.
                var opening = text.LastIndexOf('[', index - 1);
                if (opening < 0)
                {
                    return null;
                }

                index = opening;
            }
            else
            {
                while (index > 0 && IsIdentifierCharacter(text[index - 1]))
                {
                    index--;
                }
            }

            if (index == nameEnd)
            {
                // nothing is before the dot: the member of a With block's object, or a member of one of its members (`.Items(1).`) - which the qualifier starts
                // with the dot of. Anything else before the dot - an argument list with no name - is nothing a member is accessed on.
                if (index == end)
                {
                    return string.Empty;
                }

                return text[index] is '.' or '!' ? text[index..end] : null;
            }

            if (char.IsAsciiDigit(text[index]))
            {
                return null;
            }

            if (index > 0 && text[index - 1] is '.' or '!')
            {
                index--;
                continue;
            }

            return text[index..end];
        }
    }

    // the index of the `(` that the `)` at `closing` closes, string literals skipped; -1 when there is none on the line.
    private static int OpeningParenthesisBefore(string text, int closing)
    {
        var depth = 0;
        var inString = false;
        for (var index = closing; index >= 0; index--)
        {
            switch (text[index])
            {
                case '"':
                    inString = !inString;
                    break;
                case ')' when !inString:
                    depth++;
                    break;
                case '(' when !inString:
                    if (--depth == 0)
                    {
                        return index;
                    }

                    break;
            }
        }

        return -1;
    }

    private static string LastWordOf(string text)
    {
        var trimmed = text.TrimEnd();
        var start = trimmed.Length;
        while (start > 0 && IsIdentifierCharacter(trimmed[start - 1]))
        {
            start--;
        }

        return trimmed[start..];
    }

    // nothing is before the name on its statement: the start of the line - after a line number or a label - or a `:` that separates statements, or the `Then` or
    // the `Else` of a single-line If.
    private static bool StartsStatement(string text)
    {
        var statement = text;
        var separator = LastSeparatorOf(statement);
        if (separator >= 0)
        {
            statement = statement[(separator + 1)..];
        }

        var trimmed = statement.Trim();
        var digits = 0;
        while (digits < trimmed.Length && char.IsAsciiDigit(trimmed[digits]))
        {
            digits++;
        }

        trimmed = trimmed[digits..].Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        var word = LastWordOf(trimmed);
        return word.Equals("Then", StringComparison.OrdinalIgnoreCase) || word.Equals("Else", StringComparison.OrdinalIgnoreCase);
    }

    // the last `:` of the text that separates statements: not one in a string literal.
    private static int LastSeparatorOf(string text)
    {
        var inString = false;
        var last = -1;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '"')
            {
                inString = !inString;
            }
            else if (!inString && text[index] == ':')
            {
                last = index;
            }
        }

        return last;
    }
}
