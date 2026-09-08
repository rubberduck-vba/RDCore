using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RDCore.Parsing.AST;
using RDCore.Parsing.Syntax;
using RDCore.SDK.ConsoleIO;
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

/// <param name="wireErrorDetail">
/// How a build-machine source path in a caught exception's text is anonymized before it can reach the
/// wire. Supplied from configuration in the parse server; the default suits tests.
/// </param>
/// <param name="logger">
/// Records exceptions the resilience guards swallow, so a genuine listener bug is still visible in the
/// parse-server log even though the module degrades gracefully. Defaults to a no-op for tests.
/// </param>
internal partial class ModuleParser(
    SourcePathScrubMode wireErrorDetail = SourcePathScrubMode.RepoRelative,
    ILogger<ModuleParser>? logger = null) : IModuleParser
{
    private readonly ILogger<ModuleParser> _logger = logger ?? NullLogger<ModuleParser>.Instance;

    public ModuleParseResult Parse(Uri uri, ModuleType moduleType, string content)
    {
        var errorListener = new ErrorListener(uri);
        var precompilerTrivia = ImmutableArray<SyntaxNode>.Empty;

        try
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                // an empty module is valid VBA, not a parse failure.
                return ModuleParseResult.Success(EmptyModule(uri, moduleType));
            }

            precompilerTrivia = ParsePrecompilerNodes(content, errorListener, [new PrecompilerDirectiveListener(uri)]);
            var node = new ModuleNode(new SyntaxNodeId(uri.AbsolutePath, []), new(uri, SourceRange.Empty), precompilerTrivia, moduleType);

            var sanitized = PrecompilerNodePattern().Replace(content, match => new string(' ', match.Length));
            var listener = ParseWithFallback(sanitized, errorListener, () => new DeclarationsParseTreeListener(uri, node));
            var ast = listener.BuildModuleNode();

            // a partial tree is still useful to the symbol pass — IsSuccess is governed by whether
            // any syntax error was recorded, not by the child count (a valid module can declare nothing).
            return ModuleParseResult.Success(ast) with
            {
                SyntaxErrors = errorListener.Errors,
                PrecompilerTrivia = precompilerTrivia,
            };
        }
        catch (Exception exception)
        {
            // the declaration pass must never throw: an exception escaping both parse attempts degrades
            // to a failed result that still carries whatever the LL pass located, plus the precompiler
            // trivia. Build-machine paths in the exception text are anonymized before they can leave.
            // an exception here is past LL recovery on a fresh listener, so it is much more likely a
            // listener bug than malformed input — log it even though the module degrades.
            _logger.LogWarning(exception, "❌ Parse of {uri} degraded after an unhandled exception in the declaration pass.", uri);
            return errorListener.Errors.IsEmpty
                ? ModuleParseResult.Failed(new(uri, SourceRange.Empty), exception, wireErrorDetail) with { PrecompilerTrivia = precompilerTrivia }
                : new ModuleParseResult { SyntaxErrors = errorListener.Errors, PrecompilerTrivia = precompilerTrivia };
        }
    }

    private static ModuleNode EmptyModule(Uri uri, ModuleType moduleType)
        => new(new SyntaxNodeId(uri.AbsolutePath, []), new(uri, SourceRange.Empty), [], moduleType);

    private ImmutableArray<SyntaxNode> ParsePrecompilerNodes(string source, ErrorListener errorListener, ISyntaxNodeProvider[] listeners)
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
            // SyntaxNodes unwinds the listener's builder stack, so it can throw too — keep it inside
            // the guard.
            return [.. listeners.SelectMany(provider => provider.SyntaxNodes)];
        }
        catch (Exception exception)
        {
            // the conditional-compilation pass is best-effort: default error recovery can fire
            // unbalanced enter/exit events into a stateful listener. A failure here forfeits the
            // precompiler trivia for this module, not the module.
            _logger.LogDebug(exception, "Precompiler-trivia pass failed; trivia forfeited for this module.");
            return [];
        }
    }

    /// <summary>
    /// Two-stage parse: SLL with a bail-on-first-error strategy (fast, and it never runs default error
    /// recovery — which would desynchronize a stateful parse listener), then LL with default recovery
    /// on a <em>fresh</em> listener if SLL failed.
    /// </summary>
    private TListener ParseWithFallback<TListener>(string content, ErrorListener errorListener, Func<TListener> listenerFactory)
        where TListener : IParseTreeListener
    {
        try
        {
            return ParseOnce(content, PredictionMode.Sll, new BailErrorStrategy(), errorListener: null, listenerFactory());
        }
        catch (Exception exception)
        {
            // any SLL failure falls through to LL: a plain ParseCanceledException/RecognitionException,
            // but also a listener exception the bail strategy triggered on a half-matched rule. SLL
            // carries no error listener, so nothing located is lost. LL runs default recovery + the
            // error listener on a fresh listener and produces the located errors.
            if (exception is not (ParseCanceledException or RecognitionException))
            {
                // an expected bail is routine (SLL trips on valid constructs like `foo!bar`); anything
                // else from the SLL pass is worth a trace even though LL will retry.
                _logger.LogDebug(exception, "SLL parse pass raised {type}; retrying on LL.", exception.GetType().Name);
            }
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