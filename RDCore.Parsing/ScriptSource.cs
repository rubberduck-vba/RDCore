using Antlr4.Runtime;
using RDCore.Parsing.Syntax;
using RDCore.SDK.Model.Source;

namespace RDCore.Parsing;

/// <summary>
/// The source of a script (<see cref="SDK.Server.Configuration.SdkWorkspaceOptions.AllowsModuleLevelStatements"/>) in three texts, each the length and
/// the lines of the source: what it declares outside its procedures, its procedures, and its code, which is the statements outside its procedures that a
/// module that is not a script refuses (MS-VBAL §5.1). Each text has the characters of the other two blank and every line ending kept, so that a position
/// in any of them is the position in the source, and the grammar reads each as it reads a module, or a procedure's body.
/// </summary>
/// <remarks>
/// A statement ends at the end of its line, or at a colon outside a single-line <c>If</c>. A comment, the blanks between statements and a separator are the
/// text of the statement before them on their line, and the declarations' when there is none; inside a procedure, from its header to its end, all is the
/// procedure's. A declaration is written with a modifier (<c>Public</c>, <c>Private</c>...) or starts with what only a declaration starts with
/// (<c>Dim</c>, <c>Const</c>, <c>Declare</c>, <c>Option</c>...); a module's header, its attributes and the members of a <c>Type</c> or an <c>Enum</c> are the
/// declarations' too.
/// </remarks>
internal static class ScriptSource
{
    /// <summary>
    /// The three texts of a script, and where its code is: from the first statement of it to the end of the last; <see langword="null"/> for a script that
    /// has none.
    /// </summary>
    public sealed record class Parts(string Declarations, string Procedures, string Code, SourceRange? CodeRange);

    private enum Part
    {
        Declarations,
        Procedures,
        Code,
    }

    public static Parts Split(string source)
    {
        var lexer = new VBALexer(new AntlrInputStream(source));
        lexer.RemoveErrorListeners();
        var parts = new Part?[source.Length];
        var reader = new Reader(parts, source.Length);
        var statement = new List<IToken>();
        var inComment = false;
        var inSingleLineIf = false;
        foreach (var token in lexer.GetAllTokens())
        {
            switch (token.Type)
            {
                case VBALexer.NEWLINE:
                    reader.Read(statement);
                    inComment = false;
                    inSingleLineIf = false;
                    continue;
                case VBALexer.WS or VBALexer.LINE_CONTINUATION:
                    continue;
            }

            if (inComment || token.Type == VBALexer.SINGLEQUOTE || token.Type == VBALexer.REM && statement.Count == 0)
            {
                inComment = true;
                continue;
            }

            if (token.Type == VBALexer.COLON && !inSingleLineIf)
            {
                reader.Read(statement);
                continue;
            }

            inSingleLineIf |= token.Type == VBALexer.THEN && statement.Count > 0 && statement[0].Type == VBALexer.IF;
            statement.Add(token);
        }

        reader.Read(statement);
        reader.End();

        // what no statement is (a comment, a blank, a colon) is the part of the statement before it on its line.
        Part? before = null;
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] is '\r' or '\n')
            {
                before = null;
            }
            else if (parts[index] is { } part)
            {
                before = part;
            }
            else
            {
                parts[index] = before ?? Part.Declarations;
            }
        }

        return new Parts(TextOf(source, parts, Part.Declarations), TextOf(source, parts, Part.Procedures), TextOf(source, parts, Part.Code), reader.CodeRange);
    }

    private static string TextOf(string source, Part?[] parts, Part part)
        => string.Create(source.Length, (source, parts, part), static (text, state) =>
        {
            for (var index = 0; index < text.Length; index++)
            {
                var character = state.source[index];
                text[index] = character is '\r' or '\n' || state.parts[index] == state.part ? character : ' ';
            }
        });

    // reads the statements in order: which part each is, and where each procedure is, from its header to its end.
    private sealed class Reader(Part?[] parts, int length)
    {
        private int? _procedure;
        private bool _inHeader;
        private bool _inTypeOrEnum;
        private IToken? _firstCode;
        private IToken? _lastCode;

        public SourceRange? CodeRange => _firstCode is { } first && _lastCode is { } last
            ? new SourceRange(new SourcePosition(first.Line - 1, first.Column), EndOf(last))
            : null;

        public void Read(List<IToken> statement)
        {
            if (statement.Count == 0)
            {
                return;
            }

            var part = PartOf(statement);
            Mark(statement[0].StartIndex, statement[^1].StopIndex, part);
            if (part == Part.Code)
            {
                _firstCode ??= statement[0];
                _lastCode = statement[^1];
            }

            statement.Clear();
        }

        // a procedure the source does not end is the procedure's to the end of the source, as a parse of a module reads it.
        public void End()
        {
            if (_procedure is { } start)
            {
                Mark(start, length - 1, Part.Procedures);
            }
        }

        private Part PartOf(List<IToken> statement)
        {
            var modified = 0;
            while (modified < statement.Count - 1
                   && statement[modified].Type is VBALexer.PUBLIC or VBALexer.PRIVATE or VBALexer.GLOBAL or VBALexer.FRIEND or VBALexer.STATIC)
            {
                modified++;
            }

            var first = statement[modified];
            if (_procedure is { } start)
            {
                if (first.Type is VBALexer.END_SUB or VBALexer.END_FUNCTION or VBALexer.END_PROPERTY)
                {
                    Mark(start, statement[^1].StopIndex, Part.Procedures);
                    _procedure = null;
                }

                return Part.Procedures;
            }

            if (first.Type is VBALexer.SUB or VBALexer.FUNCTION or VBALexer.PROPERTY_GET or VBALexer.PROPERTY_LET or VBALexer.PROPERTY_SET
                || first.Type == VBALexer.IDENTIFIER && string.Equals(first.Text, "Property", StringComparison.OrdinalIgnoreCase) && modified > 0)
            {
                _procedure = statement[0].StartIndex;
                return Part.Procedures;
            }

            if (_inHeader)
            {
                _inHeader = !(first.Type == VBALexer.END && statement.Count == 1);
                return Part.Declarations;
            }

            if (_inTypeOrEnum)
            {
                _inTypeOrEnum = first.Type is not (VBALexer.END_TYPE or VBALexer.END_ENUM);
                return Part.Declarations;
            }

            _inHeader = first.Type == VBALexer.BEGIN;
            _inTypeOrEnum = first.Type is VBALexer.TYPE or VBALexer.ENUM;
            return modified > 0 || IsDeclaration(first.Type) ? Part.Declarations : Part.Code;
        }

        private void Mark(int start, int stop, Part part)
        {
            for (var index = start; index <= stop && index < parts.Length; index++)
            {
                parts[index] = part;
            }
        }

        private static SourcePosition EndOf(IToken token)
        {
            var text = token.Text ?? string.Empty;
            var lastLine = text.LastIndexOf('\n');
            var lines = text.Count(character => character == '\n');
            return lastLine < 0
                ? new SourcePosition(token.Line - 1, token.Column + text.Length)
                : new SourcePosition(token.Line - 1 + lines, text.Length - lastLine - 1);
        }

        private static bool IsDeclaration(int token) => token is VBALexer.DIM or VBALexer.CONST or VBALexer.DECLARE or VBALexer.TYPE or VBALexer.ENUM
            or VBALexer.EVENT or VBALexer.IMPLEMENTS or VBALexer.OPTION_BASE or VBALexer.OPTION_EXPLICIT or VBALexer.OPTION_COMPARE
            or VBALexer.OPTION_PRIVATE_MODULE or VBALexer.DEFBOOL or VBALexer.DEFBYTE or VBALexer.DEFDATE or VBALexer.DEFDBL or VBALexer.DEFCUR
            or VBALexer.DEFINT or VBALexer.DEFLNG or VBALexer.DEFLNGLNG or VBALexer.DEFLNGPTR or VBALexer.DEFOBJ or VBALexer.DEFSNG or VBALexer.DEFSTR
            or VBALexer.DEFVAR or VBALexer.VERSION or VBALexer.ATTRIBUTE or VBALexer.BEGIN;
    }
}
