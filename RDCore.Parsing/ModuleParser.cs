using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RDCore.Parsing.AST;
using RDCore.Parsing.Syntax;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Server.Configuration;
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

/// <param name="serverOptions">
/// Supplies <see cref="SdkServerOptions.WireErrorDetail"/> — how a source path in a caught
/// exception's text is anonymized before it can reach the wire. Optional; the default suits tests.
/// </param>
/// <param name="logger">
/// Records exceptions the resilience guards swallow, so a genuine listener bug is still visible in the
/// parse-server log even though the module degrades gracefully. Defaults to a no-op for tests.
/// </param>
internal partial class ModuleParser(
    IOptions<SdkServerOptions>? serverOptions = null,
    ILogger<ModuleParser>? logger = null) : IModuleParser
{
    private readonly ILogger<ModuleParser> _logger = logger ?? NullLogger<ModuleParser>.Instance;
    private readonly SourcePathScrubMode _scrub = serverOptions?.Value.WireErrorDetail ?? SourcePathScrubMode.RepoRelative;

    public ModuleParseResult Parse(Uri uri, ModuleType moduleType, string content)
    {
        var errorListener = new ErrorListener(uri);
        var precompilerTrivia = ImmutableArray<SyntaxNode>.Empty;
        DeclarationsParseTreeListener? declarations = null;

        try
        {
            content = NormalizeSource(content);
            if (string.IsNullOrWhiteSpace(content))
            {
                // an empty module is valid VBA, not a parse failure.
                return ModuleParseResult.Success(EmptyModule(uri, moduleType));
            }

            precompilerTrivia = ParsePrecompilerNodes(content, errorListener, [new PrecompilerDirectiveListener(uri, errorListener)]);
            var node = new ModuleNode(new SyntaxNodeId(uri.AbsolutePath, []), new(uri, SourceRange.Empty), precompilerTrivia, moduleType);

            var sanitized = PrecompilerNodePattern().Replace(content, match => new string(' ', match.Length));
            var listener = ParseWithFallback(sanitized, errorListener, () => declarations = new DeclarationsParseTreeListener(uri, node, errorListener));
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
            // the declaration pass must never throw: an exception past both parse attempts on a fresh
            // listener is much more likely a listener bug than malformed input, so it is logged even
            // though the module degrades. The result still carries the located errors, the precompiler
            // trivia, AND whatever the listener had already built (salvaged below) — a module with
            // three good members and one half-typed line still contributes those three symbols.
            _logger.LogWarning(exception, "❌ Parse of {uri} degraded after an unhandled exception in the declaration pass.", uri);

            ModuleNode? salvaged = null;
            try
            {
                salvaged = declarations?.BuildModuleNode();
            }
            catch (Exception salvageFailure)
            {
                _logger.LogDebug(salvageFailure, "The partial tree could not be salvaged for {uri}.", uri);
            }

            return new ModuleParseResult
            {
                SyntaxTree = salvaged,
                PrecompilerTrivia = precompilerTrivia,
                SyntaxErrors = errorListener.Errors.IsEmpty
                    ? [VBSyntaxErrorInfo.For(VBCompileErrorId.SyntaxError, new(uri, SourceRange.Empty),
                        SourcePathAnonymizer.Scrub(exception.ToString(), _scrub))]
                    : errorListener.Errors,
            };
        }
    }

    private static ModuleNode EmptyModule(Uri uri, ModuleType moduleType)
        => new(new SyntaxNodeId(uri.AbsolutePath, []), new(uri, SourceRange.Empty), [], moduleType);

    /// <summary>
    /// ANTLR's input stream and the precompiler-line regexes (<see cref="RegexOptions.Multiline"/>)
    /// recognize only <c>\n</c> as a line boundary, and a leading byte-order mark would land in
    /// column 0 of the first token. Fold CR, CRLF, U+2028 and U+2029 to <c>\n</c> and drop one leading
    /// BOM so line numbers, columns, and <c>#</c>-directive detection are right no matter how the
    /// client saved the file.
    /// </summary>
    private static string NormalizeSource(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        // strip one leading byte-order mark (U+FEFF); ANTLR would otherwise place it at column 0.
        if (content[0] == '\uFEFF')
        {
            content = content[1..];
        }

        return content
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace('\u2028', '\n')
            .Replace('\u2029', '\n');
    }

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

        try
        {
            // parse to a tree, THEN walk it. AddParseListener fires Exit before Enter<Op> on a
            // left-recursive ccExpression (`#If VBA7 And Win64`), which desyncs the builder-stack
            // listener and makes the binary-operator node constructor throw — losing every directive
            // in the module. A tree walk fires Enter before any child of the recursive alternative.
            var tree = parser.compilationUnit();
            foreach (var listener in listeners)
            {
                ParseTreeWalker.Default.Walk(listener, tree);
            }
            return [.. listeners.SelectMany(provider => provider.SyntaxNodes)];
        }
        catch (Exception exception)
        {
            // still best-effort: default error recovery can fire unbalanced enter/exit events into a
            // stateful listener. A failure here forfeits the precompiler trivia for this module, not
            // the module.
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

/// <summary>
/// Collects both ANTLR grammar-mismatch errors and the token-semantic errors a declaration listener
/// raises (e.g. a numeric literal that overflows its type). One collection so
/// <see cref="ModuleParseResult.SyntaxErrors"/> carries every syntax error of either origin.
/// </summary>
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

    /// <summary>
    /// Records a token-semantic syntax error at <paramref name="location"/>. Idempotent per
    /// (location, id) — the two-stage parse runs a fresh listener per attempt, which would otherwise
    /// double-report a literal both passes reach.
    /// </summary>
    public void Report(SourceLocation location, VBCompileErrorId id, string verbose)
    {
        if (_errors.Any(existing => existing.ErrorId == (int)id && existing.Location.Equals(location)))
        {
            return;
        }
        _errors.Add(VBSyntaxErrorInfo.For(id, location, verbose));
    }
}
