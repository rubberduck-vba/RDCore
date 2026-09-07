using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using RDCore.Parsing.AST;
using RDCore.Parsing.Syntax;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace RDCore.Parsing;

internal interface ISyntaxNodeProvider : IParseTreeListener
{
    ImmutableArray<SyntaxNode> SyntaxNodes { get; }
}

/// <summary>
/// Parses a full module document into a <see cref="ModuleParseResult"/>. Public so the OmniSharp
/// handler container can construct <c>ParseFullDocumentHandler</c>.
/// </summary>
public interface IModuleParser
{
    ModuleParseResult Parse(Uri uri, ModuleType moduleType, string content);
}

internal partial class ModuleParser() : IModuleParser
{
    public ModuleParseResult Parse(Uri uri, ModuleType moduleType, string content)
    {
        var errorListener = new ErrorListener(uri);
        var directiveListener = new PrecompilerDirectiveListener(uri);
        var precompilerTrivia = ParsePrecompilerNodes(content, errorListener, [directiveListener]);

        var node = new ModuleNode(new SyntaxNodeId(uri.AbsolutePath, []), new(uri, SourceRange.Empty), precompilerTrivia, moduleType);
        var listener = new DeclarationsParseTreeListener(uri, node);
        try
        {
            var sanitized = PrecompilerNodePattern().Replace(content, match => new string(' ', match.Length));
            ParseWithFallback(sanitized, errorListener, [listener]);

            var ast = listener.BuildModuleNode();
            return ast.Children.Length > 0 
                ? ModuleParseResult.Success(ast) with { SyntaxErrors = errorListener.Errors, PrecompilerTrivia = precompilerTrivia }
                : ModuleParseResult.Failed(node.SourceLocation, errorListener.Errors.FirstOrDefault()?.Verbose ?? string.Empty);
            ;
        }
        catch (Exception exception)
        {
            var verbose = $"Parsing failed: {exception}";
            return ModuleParseResult.Failed(new(uri, SourceRange.Empty), verbose);
        }
    }

    private static ImmutableArray<SyntaxNode> ParsePrecompilerNodes(string source, ErrorListener errorListener, ISyntaxNodeProvider[] listeners)
    {
        // ignore everything that is NOT a precompiler node,
        // because grammar matches everything as a ccBlock otherwise.
        var sanitized = NoPrecompilerNodePattern().Replace(source, match => new string(' ', match.Length));

        var stream = new AntlrInputStream(sanitized);
        var lexer = new VBALexer(stream);
        var tokens = new CommonTokenStream(lexer);
        var parser = new VBAConditionalCompilationParser(tokens);

        parser.Interpreter.PredictionMode = PredictionMode.Ll;
        parser.AddErrorListener(errorListener);
        foreach (var listener in listeners)
        {
            parser.AddParseListener(listener);
        }
        parser.compilationUnit();
        return [.. listeners.SelectMany(provider => provider.SyntaxNodes)];
    }

    private static void ParseWithFallback(string content, ErrorListener errorListener, IParseTreeListener[] listeners)
    {
        var stream = new AntlrInputStream(content);
        var lexer = new VBALexer(stream);
        var tokens = new CommonTokenStream(lexer);

        try
        {
            ParseFast(tokens, errorListener, listeners);
        }
        catch (InputMismatchException)
        {
            ParseSlow(tokens, errorListener, listeners);
        }
        catch (RecognitionException)
        {
            ParseSlow(tokens, errorListener, listeners);
        }
    }

    private static void ParseFast(CommonTokenStream tokenStream, ErrorListener errorListener, IParseTreeListener[] listeners) 
        => Parse(tokenStream, PredictionMode.Sll, errorListener, listeners);

    private static void ParseSlow(CommonTokenStream tokenStream, ErrorListener errorListener, IParseTreeListener[] listeners) 
        => Parse(tokenStream, PredictionMode.Ll, errorListener, listeners);

    private static void Parse(CommonTokenStream tokenStream, PredictionMode mode, ErrorListener errorListener, IParseTreeListener[] listeners)
    {
        var parser = new VBAParser(tokenStream);
        parser.Interpreter.PredictionMode = mode;
        parser.AddErrorListener(errorListener);
        foreach (var listener in listeners)
        {
            parser.AddParseListener(listener);
        }
        parser.startRule();
    }

    [GeneratedRegex(@"^[ \t]*#.*$", RegexOptions.Multiline)]
    private static partial Regex PrecompilerNodePattern();

    [GeneratedRegex(@"^(?![ \t]*#.*).*$", RegexOptions.Multiline)]
    private static partial Regex NoPrecompilerNodePattern();
}

internal class ErrorListener(Uri uri) : IAntlrErrorListener<IToken>
{
    private readonly Uri _uri = uri;
    private readonly List<VBSyntaxErrorInfo> _errors = [];
    public ImmutableArray<VBSyntaxErrorInfo> Errors => [.. _errors];

    public void SyntaxError([NotNull] IRecognizer recognizer, [Nullable] IToken offendingSymbol, int line, int charPositionInLine, [NotNull] string msg, [Nullable] RecognitionException e)
    {
        var location = new SourceLocation(_uri, new(line, charPositionInLine, line, charPositionInLine));
        _errors.Add(VBSyntaxErrorInfo.For(VBCompileErrorId.SyntaxError, location, msg));
    }
}