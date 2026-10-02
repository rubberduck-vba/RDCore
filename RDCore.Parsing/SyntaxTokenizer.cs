using Antlr4.Runtime;
using RDCore.Parsing.Syntax;
using RDCore.SDK.Model.Source;

namespace RDCore.Parsing;

/// <summary>
/// Classifies the tokens of a text by what their spelling and their place in a line say they are.
/// </summary>
/// <remarks>
/// Public so the OmniSharp handler container can construct <c>ParseTokensHandler</c>.
/// </remarks>
public interface ISyntaxTokenizer
{
    /// <summary>
    /// The tokens of <paramref name="text"/>, in the order they are in it.
    /// </summary>
    /// <param name="text">The source text, as its client holds it.</param>
    /// <returns>The tokens, never the whitespace between them. Text that is not valid is tokenized all the same: it is a listing before it is a program.</returns>
    IReadOnlyList<SyntaxToken> Tokenize(string text);
}

internal sealed class SyntaxTokenizer : ISyntaxTokenizer
{
    // where a token is in what names a type: after As, As New or Implements, and in a name with dots.
    private enum TypeNameState
    {
        None,
        Expected,
        Named,
    }

    public IReadOnlyList<SyntaxToken> Tokenize(string text)
    {
        var lexer = new VBALexer(new AntlrInputStream(text));
        lexer.RemoveErrorListeners();

        var tokens = new List<SyntaxToken>();
        SyntaxToken? comment = null;
        IToken? previous = null;
        var startsStatement = true;
        var typeName = TypeNameState.None;

        foreach (var token in lexer.GetAllTokens())
        {
            var type = token.Type;
            if (type == VBALexer.NEWLINE)
            {
                comment = null;
                previous = null;
                startsStatement = true;
                typeName = TypeNameState.None;
                continue;
            }

            if (comment is not null)
            {
                Extend(tokens, ref comment, token);
                continue;
            }

            if (type is VBALexer.WS or VBALexer.LINE_CONTINUATION)
            {
                continue;
            }

            if (type == VBALexer.SINGLEQUOTE || (type == VBALexer.REM && startsStatement))
            {
                comment = Add(tokens, token, SyntaxTokenKind.Comment);
                continue;
            }

            var kind = Classify(token, previous);
            var isWord = kind is SyntaxTokenKind.Keyword or SyntaxTokenKind.Identifier;
            switch (typeName)
            {
                case TypeNameState.Expected when type == VBALexer.NEW:
                    break;
                case TypeNameState.Expected when isWord:
                    kind = SyntaxTokenKind.TypeName;
                    typeName = TypeNameState.Named;
                    break;
                case TypeNameState.Named when type == VBALexer.DOT:
                    typeName = TypeNameState.Expected;
                    break;
                default:
                    typeName = type is VBALexer.AS or VBALexer.IMPLEMENTS ? TypeNameState.Expected : TypeNameState.None;
                    break;
            }

            // a line number labels the statement that follows it: a statement starts after it, as it does after a colon.
            startsStatement = type == VBALexer.COLON || (startsStatement && type == VBALexer.INTEGERLITERAL);
            Add(tokens, token, kind);
            previous = token;
        }

        return tokens;
    }

    private static SyntaxToken Add(List<SyntaxToken> tokens, IToken token, SyntaxTokenKind kind)
    {
        var added = new SyntaxToken { Line = token.Line - 1, Character = token.Column, Length = Width(token), Kind = kind };
        tokens.Add(added);
        return added;
    }

    // a comment is as long as the line it is on, and goes on in the next one when the line is continued.
    private static void Extend(List<SyntaxToken> tokens, ref SyntaxToken? comment, IToken token)
    {
        if (token.Type == VBALexer.LINE_CONTINUATION)
        {
            comment!.Length = token.Column - comment.Character + Width(token);
            return;
        }

        if (token.Line - 1 != comment!.Line)
        {
            comment = Add(tokens, token, SyntaxTokenKind.Comment);
            return;
        }

        comment.Length = token.Column - comment.Character + Width(token);
    }

    // a token that spans lines is a line continuation: it is measured to the end of its first line, which is as far as a token goes.
    private static int Width(IToken token)
    {
        var text = token.Text;
        var newLine = text.IndexOfAny(['\r', '\n']);
        return newLine < 0 ? text.Length : newLine;
    }

    private static SyntaxTokenKind Classify(IToken token, IToken? previous)
    {
        switch (token.Type)
        {
            case VBALexer.IDENTIFIER:
                return SyntaxTokenKind.Identifier;
            case VBALexer.STRINGLITERAL:
            case VBALexer.GUIDLITERAL:
                return SyntaxTokenKind.String;
            case VBALexer.INTEGERLITERAL:
            case VBALexer.FLOATLITERAL:
            case VBALexer.HEXLITERAL:
            case VBALexer.OCTLITERAL:
            case VBALexer.DATELITERAL:
            case VBALexer.BARE_HEX_LITERAL:
                return SyntaxTokenKind.Number;
        }

        var text = token.Text;
        if (text.Length > 0 && (char.IsLetter(text[0]) || text[0] == '_'))
        {
            // a word of the language that comes after a member access is the name of a member: x.Name, d!Name.
            return previous is { Type: VBALexer.DOT or VBALexer.EXCLAMATIONPOINT }
                ? SyntaxTokenKind.Identifier
                : SyntaxTokenKind.Keyword;
        }

        return SyntaxTokenKind.Operator;
    }
}
