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
    /// <summary>
    /// Parses <paramref name="content"/> as a module.
    /// </summary>
    /// <param name="anchorOffset">
    /// Where <paramref name="content"/> sits within a larger document. Every reported location (AST node
    /// ranges and syntax errors alike) is offset by this position, so a non-zero anchor is how a fragment
    /// of a larger document reports positions in that document's coordinates rather than its own.
    /// </param>
    ModuleParseResult Parse(Uri uri, string content, SourcePosition anchorOffset = default);
}

/// <param name="serverOptions">Supplies <see cref="SdkServerOptions.WireErrorDetail"/>; optional, the default suits tests.</param>
/// <param name="logger">Records the exceptions the resilience guards swallow; optional, no-op by default.</param>
internal partial class ModuleParser(
    IOptions<SdkServerOptions>? serverOptions = null,
    ILogger<ModuleParser>? logger = null) : IModuleParser
{
    private readonly ILogger<ModuleParser> _logger = logger ?? NullLogger<ModuleParser>.Instance;
    private readonly SourcePathScrubMode _scrub = serverOptions?.Value.WireErrorDetail ?? SourcePathScrubMode.RepoRelative;

    public ModuleParseResult Parse(Uri uri, string content, SourcePosition anchorOffset = default)
    {
        var errorListener = new ErrorListener(uri, anchorOffset);
        var precompilerTrivia = ImmutableArray<SyntaxNode>.Empty;
        DeclarationsParseTreeListener? declarations = null;

        try
        {
            content = NormalizeSource(content);
            if (string.IsNullOrWhiteSpace(content))
            {
                // an empty module is valid VBA, not a parse failure.
                return ModuleParseResult.Success(EmptyModule(uri, anchorOffset));
            }

            precompilerTrivia = ParsePrecompilerNodes(content, errorListener, [new PrecompilerDirectiveListener(uri, errorListener)], anchorOffset);
            var node = new ModuleNode(new SyntaxNodeId(uri.AbsolutePath, []), new(uri, new SourceRange(anchorOffset, anchorOffset)), precompilerTrivia);

            var sanitized = PrecompilerNodePattern().Replace(content, match => new string(' ', match.Length));
            var listener = ParseWithFallback(sanitized, errorListener, () => declarations = new DeclarationsParseTreeListener(uri, node, errorListener), anchorOffset);
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
            // an exception past both parse attempts on a fresh listener is more likely a listener bug
            // than bad input, so log it — the module still degrades to located errors + trivia +
            // whatever the listener had already built (salvaged below).
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
                    ? [VBSyntaxErrorInfo.For(VBCompileErrorId.SyntaxError, new(uri, new SourceRange(anchorOffset, anchorOffset)),
                        SourcePathAnonymizer.Scrub(exception.ToString(), _scrub))]
                    : errorListener.Errors,
            };
        }
    }

    private static ModuleNode EmptyModule(Uri uri, SourcePosition anchorOffset)
        => new(new SyntaxNodeId(uri.AbsolutePath, []), new(uri, new SourceRange(anchorOffset, anchorOffset)), []);

    // ANTLR's input stream and the precompiler-line regexes (RegexOptions.Multiline) only treat \n as
    // a line boundary, and a leading BOM would land in column 0 of the first token. Fold every line
    // ending to \n and drop one leading BOM so locations and #-directive detection hold regardless of
    // how the client saved the file.
    private static string NormalizeSource(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

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

    private ImmutableArray<SyntaxNode> ParsePrecompilerNodes(string source, ErrorListener errorListener, ISyntaxNodeProvider[] listeners, SourcePosition anchorOffset)
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
            // parse to a tree, then walk it: AddParseListener fires Exit before Enter<Op> on a
            // left-recursive ccExpression (`#If A And B`), desyncing the builder stack. A walk doesn't.
            var tree = parser.compilationUnit();
            AnchorTree(tree, anchorOffset);
            foreach (var listener in listeners)
            {
                ParseTreeWalker.Default.Walk(listener, tree);
            }
            return [.. listeners.SelectMany(provider => provider.SyntaxNodes)];
        }
        catch (Exception exception)
        {
            // best-effort: a failure here forfeits this module's precompiler trivia, not the module.
            _logger.LogDebug(exception, "Precompiler-trivia pass failed; trivia forfeited for this module.");
            return [];
        }
    }

    /// <summary>
    /// Two-stage parse: SLL with a bail-on-first-error strategy (fast, and it never runs default error
    /// recovery — which would desynchronize a stateful parse listener), then LL with default recovery
    /// on a <em>fresh</em> listener if SLL failed.
    /// </summary>
    private TListener ParseWithFallback<TListener>(string content, ErrorListener errorListener, Func<TListener> listenerFactory, SourcePosition anchorOffset)
        where TListener : IParseTreeListener
    {
        try
        {
            return ParseOnce(content, PredictionMode.Sll, new BailErrorStrategy(), errorListener: null, listenerFactory(), anchorOffset);
        }
        catch (Exception exception)
        {
            // any SLL failure falls through to LL: a plain ParseCanceledException/RecognitionException,
            // but also a listener exception the bail strategy triggered on a half-matched rule. SLL
            // carries no error listener, so nothing located is lost. LL runs default recovery + the
            // error listener on a fresh listener and produces the located errors.
            if (exception is not (ParseCanceledException or RecognitionException))
            {
                // a routine SLL bail needs no trace; anything else does, even though LL will retry.
                _logger.LogDebug(exception, "SLL parse pass raised {type}; retrying on LL.", exception.GetType().Name);
            }
            return ParseOnce(content, PredictionMode.Ll, new DefaultErrorStrategy(), errorListener, listenerFactory(), anchorOffset);
        }
    }

    private static TListener ParseOnce<TListener>(string content, PredictionMode mode, IAntlrErrorStrategy errorStrategy, ErrorListener? errorListener, TListener listener, SourcePosition anchorOffset)
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
        // Pathological nesting recurses through `expression` to an uncatchable stack overflow. Walking
        // the finished tree afterward is protected by DeclarationsParseTreeListener's own EnterEveryRule
        // guard, but that only runs once startRule() has already returned — for input deep enough to
        // overflow the parser's OWN recursive descent, that point is never reached at all, so a
        // dedicated guard must run live, interleaved with the parse itself.
        parser.AddParseListener(new StackDepthGuardListener());
        var tree = parser.startRule();
        AnchorTree(tree, anchorOffset);
        ParseTreeWalker.Default.Walk(listener, tree);
        return listener;
    }

    // every VBABaseParserRuleContext in a freshly-built tree is constructed anchored at L0C0 (ANTLR's
    // generated grammar code only ever calls the zero-anchoring constructors — see
    // VBABaseParserRuleContext). Re-anchoring the whole tree here, before anything reads SourceRange
    // from it, is what lets a fragment report positions in the larger document it was extracted from.
    private static void AnchorTree(IParseTree tree, SourcePosition anchorOffset)
    {
        if (anchorOffset == SourcePosition.Zero)
        {
            return;
        }
        ParseTreeWalker.Default.Walk(new AnchorOffsetListener(anchorOffset), tree);
    }

    [GeneratedRegex(@"^[ \t]*#.*$", RegexOptions.Multiline)]
    private static partial Regex PrecompilerNodePattern();

    [GeneratedRegex(@"^(?![ \t]*#.*).*$", RegexOptions.Multiline)]
    private static partial Regex NoPrecompilerNodePattern();
}

// Registered via AddParseListener (fires live, interleaved with the parser's own recursive descent) —
// not walked later like DeclarationsParseTreeListener. Deliberately has no AST-building side effects
// of its own: its only job is to fail fast, during parsing itself, before pathological nesting can
// exhaust the stack past the point of no return. See ParseOnce's remarks for why a walk-time-only
// guard can't substitute for this.
internal sealed class StackDepthGuardListener : VBAParserBaseListener
{
    public override void EnterEveryRule([NotNull] ParserRuleContext context)
        => System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
}

// grammar-agnostic (implements the plain IParseTreeListener, not a specific grammar's generated base):
// EnterEveryRule fires for every rule context regardless of which parser produced the tree, so the same
// listener anchors both the precompiler tree (VBAConditionalCompilationParser) and the main declarations
// tree (VBAParser).
internal sealed class AnchorOffsetListener(SourcePosition anchorOffset) : IParseTreeListener
{
    public void EnterEveryRule(ParserRuleContext context)
    {
        if (context is VBABaseParserRuleContext anchored)
        {
            anchored.AnchorAt(anchorOffset);
        }
    }

    public void ExitEveryRule(ParserRuleContext context) { }
    public void VisitTerminal(ITerminalNode node) { }
    public void VisitErrorNode(IErrorNode node) { }
}

// collects both ANTLR grammar-mismatch errors and the token-semantic errors a declaration listener
// raises (e.g. a numeric literal overflow), so ModuleParseResult.SyntaxErrors carries either origin.
internal class ErrorListener(Uri uri, SourcePosition anchorOffset) : IAntlrErrorListener<IToken>
{
    private readonly Uri _uri = uri;
    private readonly List<VBSyntaxErrorInfo> _errors = [];
    public ImmutableArray<VBSyntaxErrorInfo> Errors => [.. _errors];

    public void SyntaxError([NotNull] IRecognizer recognizer, [Nullable] IToken offendingSymbol, int line, int charPositionInLine, [NotNull] string msg, [Nullable] RecognitionException e)
    {
        // ANTLR's line is 1-based; SourcePosition is documented zero-based (charPositionInLine already
        // is), matching the same -1 conversion VBABaseParserRuleContext applies to node locations. Also
        // anchored, same as node locations, so a fragment's syntax errors land at an absolute position
        // in the larger document rather than one local to the fragment.
        var position = anchorOffset + new SourcePosition(line - 1, charPositionInLine);
        var location = new SourceLocation(_uri, new(position, position));
        _errors.Add(VBSyntaxErrorInfo.For(VBCompileErrorId.SyntaxError, location, msg));
    }

    // records a token-semantic syntax error; idempotent per (location, id) so the two-stage parse
    // does not double-report a literal both passes reach.
    public void Report(SourceLocation location, VBCompileErrorId id, string verbose)
    {
        if (_errors.Any(existing => existing.ErrorId == (int)id && existing.Location.Equals(location)))
        {
            return;
        }
        _errors.Add(VBSyntaxErrorInfo.For(id, location, verbose));
    }
}
