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
        try
        {
            var sanitized = PrecompilerNodePattern().Replace(content, match => new string(' ', match.Length));
            var listener = ParseWithFallback(sanitized, errorListener, () => new DeclarationsParseTreeListener(uri, node));

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

        try
        {
            parser.compilationUnit();
        }
        catch (Exception)
        {
            // the conditional-compilation pass is best-effort: default error recovery can fire
            // unbalanced enter/exit events into a stateful listener. A failure here forfeits the
            // precompiler trivia for this module, not the module.
        }
        return [.. listeners.SelectMany(provider => provider.SyntaxNodes)];
    }

    /// <summary>
    /// Two-stage parse: SLL with a bail-on-first-error strategy (fast, and it never runs default error
    /// recovery — which would desynchronize a stateful parse listener), then LL with default recovery
    /// on a <em>fresh</em> listener if SLL bailed.
    /// </summary>
    private static TListener ParseWithFallback<TListener>(string content, ErrorListener errorListener, Func<TListener> listenerFactory)
        where TListener : IParseTreeListener
    {
        try
        {
            return ParseOnce(content, PredictionMode.Sll, new BailErrorStrategy(), errorListener: null, listenerFactory());
        }
        catch (Exception exception) when (exception is ParseCanceledException or RecognitionException)
        {
            return ParseOnce(content, PredictionMode.Ll, new DefaultErrorStrategy(), errorListener, listenerFactory());
        }
    }

    private static TListener ParseOnce<TListener>(string content, PredictionMode mode, IAntlrErrorStrategy errorStrategy, ErrorListener? errorListener, TListener listener)
        where TListener : IParseTreeListener
    {
        var stream = new AntlrInputStream(content);
        var lexer = new VBALexer(stream);
        var tokens = new CommonTokenStream(lexer);
        var parser = new VBAParser(tokens)
        {
            ErrorHandler = errorStrategy,
        };
        parser.Interpreter.PredictionMode = mode;
        parser.RemoveErrorListeners();
        if (errorListener is not null)
        {
            parser.AddErrorListener(errorListener);
        }
        parser.AddParseListener(listener);
        parser.startRule();
        return listener;
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