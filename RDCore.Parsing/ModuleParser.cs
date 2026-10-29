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
    /// <param name="token">
    /// Cancels a parse that is no longer wanted (its text has changed since, or its caller gave up): the parse stops at the next rule it enters, and throws
    /// <see cref="OperationCanceledException"/> rather than answer.
    /// </param>
    ModuleParseResult Parse(Uri uri, string content, SourcePosition anchorOffset = default, CancellationToken token = default);
}

/// <param name="serverOptions">Supplies <see cref="SdkServerOptions.WireErrorDetail"/>; optional, the default suits tests.</param>
/// <param name="logger">Records the exceptions the resilience guards swallow; optional, no-op by default.</param>
public partial class ModuleParser(
    IOptions<SdkServerOptions>? serverOptions = null,
    ILogger<ModuleParser>? logger = null) : IModuleParser
{
    private readonly ILogger<ModuleParser> _logger = logger ?? NullLogger<ModuleParser>.Instance;
    private readonly SourcePathScrubMode _scrub = serverOptions?.Value.WireErrorDetail ?? SourcePathScrubMode.RepoRelative;

    public ModuleParseResult Parse(Uri uri, string content, SourcePosition anchorOffset = default, CancellationToken token = default)
        => Parse(uri, content, anchorOffset, [], token);

    // rootLineage: the lineage of the module node, which every node's identity extends - a part of the module parsed on its own (a header alternative) has one
    // of its own, so that no identity of its nodes is one of the module's.
    private ModuleParseResult Parse(Uri uri, string content, SourcePosition anchorOffset, ImmutableArray<int> rootLineage, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
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

            // a module with no directive has nothing for the directive pass to find, which reads the whole module (a third of a parse, measured on a
            // 2,400-line module): it is not run, and the main pass reads the module as it is.
            var hasDirectives = PrecompilerNodePattern().IsMatch(content);
            IReadOnlyList<DirectiveStructureScanner.Finding> directiveFindings = [];
            var sanitized = content;
            if (hasDirectives)
            {
                precompilerTrivia = ParsePrecompilerNodes(content, errorListener, [new PrecompilerDirectiveListener(uri, errorListener)], anchorOffset, token);

                // the directive pass reads a directive it cannot place as text: what is wrong with the directives is found on their own lines. The main pass
                // reads the module with each directive's lines blank.
                var directives = PrecompilerNodePattern().Matches(content).Select(match => (match.Index, match.Length)).ToList();
                directiveFindings = DirectiveStructureScanner.Scan(content, directives);
                sanitized = WithoutDirectives(content, directives);
            }

            var node = new ModuleNode(new SyntaxNodeId(uri.AbsolutePath, rootLineage), new(uri, new SourceRange(anchorOffset, anchorOffset)), precompilerTrivia);

            // a procedure header written once in each branch of a #If block is one procedure: the main pass reads one of its headers, and the branches of the
            // others are parsed on their own, as its alternatives (SplitHeaderScanner).
            var alternatives = hasDirectives && ConditionalBlockPattern().IsMatch(content) ? SplitHeaderScanner.Find(content) : [];
            if (alternatives.Count > 0)
            {
                sanitized = WithoutBranches(sanitized, alternatives);
            }

            var listener = ParseWithFallback(sanitized, errorListener, () => declarations = new DeclarationsParseTreeListener(uri, node, errorListener), anchorOffset, token);
            var ast = listener.BuildModuleNode();
            var trivia = listener.Trivia;
            if (alternatives.Count > 0)
            {
                ast = WithHeaderAlternatives(uri, content, ast, alternatives, anchorOffset, rootLineage, errorListener, ref trivia, token);
            }

            // a module the grammar refuses may have blocks that do not match, which MS-VBA says in words of its own. Both branches of a #If block are in
            // the source, which a module that has one would have read as one block too many.
            if (errorListener.HasGrammarErrors && !ConditionalBlockPattern().IsMatch(content))
            {
                errorListener.ReplaceGrammarErrorsWith(BlockStructureScanner.Scan(uri, sanitized, anchorOffset));
            }

            // what is wrong with a directive is no error of the grammar's, which a block's error could replace: it is reported once the grammar's are settled.
            foreach (var finding in directiveFindings)
            {
                var range = new SourceRange(finding.Range.Start.AnchoredAt(anchorOffset), finding.Range.End.AnchoredAt(anchorOffset));
                errorListener.Report(new SourceLocation(uri, range), finding.ErrorId, finding.Verbose);
            }

            ReportTooManyLineContinuations(content, uri, anchorOffset, errorListener);

            // a partial tree is still useful to the symbol pass — IsSuccess is governed by whether
            // any syntax error was recorded, not by the child count (a valid module can declare nothing).
            return ModuleParseResult.Success(ast) with
            {
                SyntaxErrors = errorListener.Errors,
                PrecompilerTrivia = precompilerTrivia,
                Trivia = trivia,
            };
        }
        // a parse that was cancelled is no failure to degrade from: it is not answered (ANTLR's own ParseCanceledException is an OperationCanceledException too,
        // which is why the token says so, not the exception's type).
        catch (Exception exception) when (!token.IsCancellationRequested)
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
                Trivia = declarations?.Trivia ?? [],
                SyntaxErrors = errorListener.Errors.IsEmpty
                    ? [VBSyntaxErrorInfo.For(VBCompileErrorId.SyntaxError, new(uri, new SourceRange(anchorOffset, anchorOffset)),
                        SourcePathAnonymizer.Scrub(exception.ToString(), _scrub))]
                    : errorListener.Errors,
            };
        }
    }

    // the source with the lines of the alternatives' branches blank, as a directive is: every position stays where it is.
    private static string WithoutBranches(string sanitized, IReadOnlyList<SplitHeaderScanner.Alternative> alternatives)
    {
        var lines = sanitized.Split('\n');
        foreach (var alternative in alternatives)
        {
            for (var line = alternative.StartLine; line < alternative.EndLine && line < lines.Length; line++)
            {
                lines[line] = new string(' ', lines[line].Length);
            }
        }

        return string.Join('\n', lines);
    }

    // each alternative's branch parsed on its own, closed by the statement its header opens, where it stands in the module. The header - the procedure the
    // branch leaves open - is an alternative of the procedure the main pass read in the branch it read (MemberDeclarationNode.HeaderAlternatives), and what
    // follows the header in the branch is of that procedure's body, in source order. Whatever else the branch holds before its header (a Declare, a variable,
    // a whole procedure) is the module's, where it is written, as a declaration written in a branch of any #If block is. What the branch's parse says is
    // wrong is the module's.
    private ModuleNode WithHeaderAlternatives(Uri uri, string content, ModuleNode ast, IReadOnlyList<SplitHeaderScanner.Alternative> alternatives,
        SourcePosition anchorOffset, ImmutableArray<int> rootLineage, ErrorListener errorListener, ref ImmutableArray<SyntaxNode> trivia, CancellationToken token)
    {
        var lines = content.Split('\n');
        var children = ast.Children.ToList();
        var ofBranches = new HashSet<SyntaxNodeId>();
        for (var index = 0; index < alternatives.Count; index++)
        {
            var alternative = alternatives[index];
            var branch = string.Join('\n', lines[alternative.StartLine..Math.Min(alternative.EndLine, lines.Length)]);
            var start = new SourcePosition(alternative.StartLine, 0).AnchoredAt(anchorOffset);
            var parsed = Parse(uri, $"{branch}\n{alternative.Closer}\n", start, rootLineage.Add(-1 - index), token);
            errorListener.AddRange(parsed.SyntaxErrors);
            trivia = trivia.AddRange(parsed.Trivia);

            var nodes = parsed.SyntaxTree?.Children ?? [];
            var header = nodes.LastOrDefault(IsProcedure) as MemberDeclarationNode;
            foreach (var node in nodes.Where(node => !ReferenceEquals(node, header)))
            {
                ofBranches.Add(node.Identity);
                var at = children.FindIndex(child => child.SourceLocation.Range.Start.CompareTo(node.SourceLocation.Range.Start) > 0);
                children.Insert(at < 0 ? children.Count : at, node);
            }

            if (header is null)
            {
                continue;
            }

            // the procedure the block opens is the one the main pass read in the branch it read: the last that starts in it, the main branch too holding
            // whatever it holds before its header.
            var mainStart = new SourcePosition(alternative.MainStartLine, 0).AnchoredAt(anchorOffset);
            var mainEnd = new SourcePosition(alternative.MainEndLine, 0).AnchoredAt(anchorOffset);
            var found = children.FindLastIndex(child => IsProcedure(child) && !ofBranches.Contains(child.Identity)
                && child.SourceLocation.Range.Start.CompareTo(mainStart) >= 0 && child.SourceLocation.Range.Start.CompareTo(mainEnd) < 0);
            // the main pass did not read the procedure, which only a syntax error of its own keeps it from doing: that error is the module's already.
            if (found < 0)
            {
                continue;
            }

            var procedure = (MemberDeclarationNode)children[found];
            var headerChildren = header.HeaderChildren.ToImmutableArray();
            var ownHeader = procedure.HeaderChildren.ToImmutableArray();
            children[found] = procedure with
            {
                HeaderAlternatives = [.. procedure.HeaderAlternatives, header with { Children = headerChildren, HeaderAlternatives = [] }, .. header.HeaderAlternatives],
                Children = [.. ownHeader, .. procedure.Children.Skip(ownHeader.Length).Concat(header.Children.Skip(headerChildren.Length))
                    .OrderBy(child => child.SourceLocation.Range.Start)],
            };
        }

        return ast with { Children = [.. children] };
    }

    private static bool IsProcedure(SyntaxNode node) => node is MemberDeclarationNode and not ExternalMemberDeclarationNode;

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

    private ImmutableArray<SyntaxNode> ParsePrecompilerNodes(string source, ErrorListener errorListener, ISyntaxNodeProvider[] listeners, SourcePosition anchorOffset,
        CancellationToken token)
    {
        // ignore everything that is NOT a precompiler node,
        // because grammar matches everything as a ccBlock otherwise.
        var sanitized = Blanked(source).ToCharArray();
        foreach (Match directive in PrecompilerNodePattern().Matches(source))
        {
            source.CopyTo(directive.Index, sanitized, directive.Index, directive.Length);
        }

        try
        {
            // parse to a tree, then walk it: AddParseListener fires Exit before Enter<Op> on a
            // left-recursive ccExpression (`#If A And B`), desyncing the builder stack. A walk doesn't.
            var tree = ParseDirectives(new string(sanitized), errorListener, token);
            AnchorTree(tree, anchorOffset);
            foreach (var listener in listeners)
            {
                ParseTreeWalker.Default.Walk(listener, tree);
            }
            return [.. listeners.SelectMany(provider => provider.SyntaxNodes)];
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            // best-effort: a failure here forfeits this module's precompiler trivia, not the module.
            _logger.LogDebug(exception, "Precompiler-trivia pass failed; trivia forfeited for this module.");
            return [];
        }
    }

    // the directive pass in two stages, as the main pass is: SLL with a bail-on-first-error strategy, then LL with default recovery and the error listener
    // when SLL fails. A block's body is a non-greedy loop of lines (ccBlock), and whether a line ends the loop is a decision LL resolves with the full
    // context of every enclosing block: on a module of many #If blocks that is quadratic in their length - 7 s for the 1,200 lines of a JSON parser - where
    // SLL takes milliseconds, and gives the tree LL gives whenever it succeeds.
    private static VBAConditionalCompilationParser.CompilationUnitContext ParseDirectives(string sanitized, ErrorListener errorListener, CancellationToken token)
    {
        try
        {
            return DirectiveParser(sanitized, PredictionMode.Sll, new BailErrorStrategy(), errorListener: null, token).compilationUnit();
        }
        catch (Exception exception) when ((exception is ParseCanceledException or RecognitionException) && !token.IsCancellationRequested)
        {
            return DirectiveParser(sanitized, PredictionMode.Ll, new DefaultErrorStrategy(), errorListener, token).compilationUnit();
        }
    }

    private static VBAConditionalCompilationParser DirectiveParser(string sanitized, PredictionMode mode, IAntlrErrorStrategy errorStrategy, ErrorListener? errorListener,
        CancellationToken token)
    {
        var parser = new VBAConditionalCompilationParser(new CommonTokenStream(new VBALexer(new AntlrInputStream(sanitized))))
        {
            ErrorHandler = errorStrategy,
        };
        parser.Interpreter.PredictionMode = mode;
        parser.RemoveErrorListeners();
        if (errorListener is not null)
        {
            parser.AddErrorListener(errorListener);
        }

        if (token.CanBeCanceled)
        {
            parser.AddParseListener(new CancellationListener(token));
        }

        return parser;
    }

    /// <summary>
    /// Two-stage parse: SLL with a bail-on-first-error strategy (fast, and it never runs default error
    /// recovery — which would desynchronize a stateful parse listener), then LL with default recovery
    /// on a <em>fresh</em> listener if SLL failed.
    /// </summary>
    private TListener ParseWithFallback<TListener>(string content, ErrorListener errorListener, Func<TListener> listenerFactory, SourcePosition anchorOffset,
        CancellationToken token)
        where TListener : IParseTreeListener
    {
        try
        {
            return ParseOnce(content, PredictionMode.Sll, new BailErrorStrategy(), errorListener: null, listenerFactory(), anchorOffset, token);
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
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
            return ParseOnce(content, PredictionMode.Ll, new DefaultErrorStrategy(), errorListener, listenerFactory(), anchorOffset, token);
        }
    }

    private static TListener ParseOnce<TListener>(string content, PredictionMode mode, IAntlrErrorStrategy errorStrategy, ErrorListener? errorListener, TListener listener,
        SourcePosition anchorOffset, CancellationToken token)
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
        if (token.CanBeCanceled)
        {
            parser.AddParseListener(new CancellationListener(token));
        }

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

    // the module with each directive's lines blank, as a directive is no code of it: every position stays where it is.
    private static string WithoutDirectives(string content, List<(int Index, int Length)> directives)
    {
        var text = content.ToCharArray();
        foreach (var (index, length) in directives)
        {
            for (var i = index; i < index + length; i++)
            {
                if (text[i] != '\n')
                {
                    text[i] = ' ';
                }
            }
        }

        return new string(text);
    }

    // the text with every character but the line breaks a space, so that what is left of it is where it was.
    private static string Blanked(string text)
        => string.Create(text.Length, text, (blanked, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                blanked[i] = source[i] == '\n' ? '\n' : ' ';
            }
        });

    // a directive is a logical line that starts with '#': the physical lines a line-continuation joins to it are its own (MS-VBAL 3.2.2, 3.4), and a '#' that
    // starts the continuation of another line is not one (`d = _` / `#1/2/2000#`).
    [GeneratedRegex(@"(?<![ \t]_[ \t]*\n)^[ \t]*#(?:[^\n]*[ \t]_[ \t]*\n)*[^\n]*", RegexOptions.Multiline)]
    private static partial Regex PrecompilerNodePattern();

    // a #If directive, whose branches are both in the source the grammar reads.
    [GeneratedRegex(@"^[ \t]*#If\b", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex ConditionalBlockPattern();

    // MS-VBA joins at most 25 physical lines into one logical line: 24 line-continuations in a row, and the 25th is a syntax error, "Too many line continuations"
    // (MS Learn; recorded in Excel: oracle\size-limits B01-B03). MS-VBAL leaves the length of a line to the implementation (3.2.1).
    private const int MaxLineContinuations = 24;

    // reports the line-continuation after the 24th of a logical line, once for that line.
    private static void ReportTooManyLineContinuations(string content, Uri uri, SourcePosition anchorOffset, ErrorListener errors)
    {
        var run = 0;
        var line = 0;
        foreach (var physical in content.AsSpan().EnumerateLines())
        {
            // a line-continuation is an underscore at the end of the line, after a space or a tab.
            var trimmed = physical.TrimEnd(" \t");
            var continuation = trimmed.Length >= 2 && trimmed[^1] == '_' && trimmed[^2] is ' ' or '\t';
            run = continuation ? run + 1 : 0;
            if (run == MaxLineContinuations + 1)
            {
                var at = trimmed.Length - 1;
                var start = new SourcePosition(line, at).AnchoredAt(anchorOffset);
                var end = new SourcePosition(line, at + 1).AnchoredAt(anchorOffset);
                errors.Report(new SourceLocation(uri, new(start, end)), VBCompileErrorId.SyntaxError, SDK.Exceptions.VBCompileError_TooManyLineContinuations_Verbose);
            }

            line++;
        }
    }
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

// a parse that is no longer wanted stops at the next rule it enters: registered live, as StackDepthGuardListener is, on the directive pass and on the main
// pass alike (it implements the plain IParseTreeListener, as AnchorOffsetListener does).
internal sealed class CancellationListener(CancellationToken token) : IParseTreeListener
{
    public void EnterEveryRule(ParserRuleContext context) => token.ThrowIfCancellationRequested();

    public void ExitEveryRule(ParserRuleContext context) { }

    public void VisitTerminal(ITerminalNode node) { }

    public void VisitErrorNode(IErrorNode node) { }
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
        // in the larger document rather than one local to the fragment - through AnchoredAt rather than a
        // plain addition, so an error past the fragment's first line is not pushed to the right by the
        // anchor's own column.
        var position = new SourcePosition(line - 1, charPositionInLine).AnchoredAt(anchorOffset);
        var location = new SourceLocation(_uri, new(position, position));
        _errors.Add(VBSyntaxErrorInfo.For(VBCompileErrorId.SyntaxError, location, msg));
    }

    /// <summary>
    /// Replaces what the grammar says of a module whose blocks do not match with what MS-VBA says of them (<see cref="BlockStructureScanner"/>): the
    /// grammar's errors from the first block error on are its consequences, and the ones before it are kept.
    /// </summary>
    public void ReplaceGrammarErrorsWith(ImmutableArray<VBSyntaxErrorInfo> blockErrors)
    {
        if (blockErrors.IsEmpty)
        {
            return;
        }

        var first = blockErrors.Min(error => error.Location.Range.Start);
        _errors.RemoveAll(error => error.ErrorId == (int)VBCompileErrorId.SyntaxError && error.Location.Range.Start.CompareTo(first) >= 0);
        _errors.AddRange(blockErrors);
    }

    // whether the grammar refused something of the module.
    public bool HasGrammarErrors => _errors.Any(error => error.ErrorId == (int)VBCompileErrorId.SyntaxError);

    // the errors of a part of the module parsed on its own, already located in the module (a header alternative).
    public void AddRange(IEnumerable<VBSyntaxErrorInfo> errors) => _errors.AddRange(errors);

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
