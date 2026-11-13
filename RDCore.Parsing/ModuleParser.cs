using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RDCore.Parsing.AST;
using RDCore.Parsing.Syntax;
using RDCore.SDK.Logging;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform;
using RDCore.SDK.Server.Configuration;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using RDCore.Runtime.LanguageCore.Errors;

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
    /// <see cref="OperationCanceledException"/> rather than return a result. A request for it is answered as cancelled, not with a failed parse.
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

    /// <summary>
    /// How many rules the parse may be inside at once, past which it stops and the module is nested too deeply to be read: 1 to
    /// <see cref="StackDepthGuardListener.MaxRuleDepth"/>, which is the default.
    /// </summary>
    /// <remarks>
    /// It is set as the parser is made, and is no parameter of a constructor: the parse server's container (OmniSharp's) makes a service from any of its
    /// constructors, the internal ones too, and gives a number it is asked for a value of its own, whatever the parameter's default - a ceiling no parse reads to.
    /// </remarks>
    internal int RuleDepthCeiling
    {
        get;
        init => field = value is > 0 and <= StackDepthGuardListener.MaxRuleDepth
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"A parse reads 1 to {StackDepthGuardListener.MaxRuleDepth} rules deep.");
    } = StackDepthGuardListener.MaxRuleDepth;

    // on a thread whose stack has room for the deepest expression RD-VBA reads (LargeStackThread): the grammar's descent and the build of the tree recurse once
    // or more for each level of an expression.
    public ModuleParseResult Parse(Uri uri, string content, SourcePosition anchorOffset = default, CancellationToken token = default)
    {
        // the looks are counted on the thread that parses, which is not the caller's when the parse moves to a large stack: they are told to the caller's.
        var looks = 0L;
        var result = LargeStackThread.Run(() =>
        {
            t_directiveLooks = 0;
            var parsed = Parse(uri, content, anchorOffset, [], Statistics, token);
            looks = t_directiveLooks;
            return parsed;
        });

        t_directiveLooks = looks;
        return result;
    }

    // rootLineage: the lineage of the module node, which every node's identity extends - a part of the module parsed on its own (a header alternative) has one
    // of its own, so that no identity of its nodes is one of the module's. statistics: where how the module was read is counted; none for such a part, which
    // is no module of its own.
    private ModuleParseResult Parse(Uri uri, string content, SourcePosition anchorOffset, ImmutableArray<int> rootLineage, SegmentationStatistics? statistics,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var errorListener = new ErrorListener(uri, anchorOffset);
        var precompilerTrivia = ImmutableArray<SyntaxNode>.Empty;
        DeclarationsParseTreeListener? declarations = null;
        (ModuleNode Tree, ImmutableArray<SyntaxNode> Trivia)? read = null;

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
                precompilerTrivia = ParsePrecompilerNodes(content, [new PrecompilerDirectiveListener(uri, errorListener)], anchorOffset, token);

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

            // the module read with the grammar's fast prediction, as a whole; where that fails, each of its segments on its own where it can be, so that a part
            // the fast prediction can't read is read slowly alone, and not the whole module with it; and the whole module read slowly otherwise. A header
            // written once in each branch of a #If block has its branches blank in the text the segments are found in, as in the text the main pass reads,
            // and its alternatives are read the same way after either.
            ModuleNode ast;
            ImmutableArray<SyntaxNode> trivia;
            if (!ForceSegments && TryParseFast(sanitized, errorListener, errors => declarations = new DeclarationsParseTreeListener(uri, node, errors) { Anchor = anchorOffset },
                anchorOffset, token) is { } fast)
            {
                (ast, trivia) = (fast.BuildModuleNode(), fast.Trivia);
                statistics?.Count(SegmentationStatistics.Path.Whole);
            }
            else if (Segmented && Segments(sanitized, statistics) is { } segments && ParseSegments(uri, node, sanitized, segments, anchorOffset, errorListener, statistics, token) is { } stitched)
            {
                (ast, trivia) = (stitched.Module, stitched.Trivia);
                errorListener.AddRange(stitched.Errors);
                statistics?.Count(SegmentationStatistics.Path.Stitched);
            }
            else
            {
                var slow = ParseSlowly(sanitized, errorListener, declarations = new DeclarationsParseTreeListener(uri, node, errorListener) { Anchor = anchorOffset },
                    anchorOffset, token);
                (ast, trivia) = (slow.BuildModuleNode(), slow.Trivia);
                if (!Segmented)
                {
                    statistics?.Count(SegmentationStatistics.Path.NotSegmented);
                }
            }

            // what the module was read as is what a failure past this point salvages: a stitched module has no listener of its own, and the one the fast
            // attempt left behind is a failed one's.
            read = (ast, trivia);
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
            ast = PruneExpressionsNestedTooDeeply(ast, content, anchorOffset, errorListener);

            // a directive's expression (#If, #ElseIf, #Const) is of the directive pass's own tree, and has the same limit.
            precompilerTrivia = [.. precompilerTrivia.Select(directive => PruneExpressionsNestedTooDeeply(directive, content, anchorOffset, errorListener))];

            // a partial tree is still useful to the symbol pass — IsSuccess is governed by whether
            // any syntax error was recorded, not by the child count (a valid module can declare nothing).
            return ModuleParseResult.Success(ast) with
            {
                SyntaxErrors = errorListener.Errors,
                PrecompilerTrivia = precompilerTrivia,
                Trivia = trivia,
            };
        }
        // a parse that was cancelled is no failure to degrade from: it throws that it was cancelled (ANTLR's own ParseCanceledException is an OperationCanceledException too,
        // which is why the token says so, not the exception's type).
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            // an exception past both parse attempts on a fresh listener is more likely a listener bug
            // than bad input, so log it — the module still degrades to located errors + trivia +
            // whatever the listener had already built (salvaged below).
            // A module nested deeper than the parse reads (StackDepthGuardListener) is bad input: its exception's trace is a frame for each level, tens of
            // thousands of them, and is neither logged nor given to a client - the error says what is wrong with the module.
            var outOfStack = IsOutOfStack(exception);
            if (outOfStack)
            {
                _logger.LogWarning("❌ Parse of {uri} stopped: the module is nested deeper than the parse reads.", uri);
            }
            else
            {
                _logger.LogWarning(exception, "❌ Parse of {uri} degraded after an unhandled exception in the declaration pass.", uri);
            }

            var salvaged = read?.Tree;
            try
            {
                salvaged ??= declarations?.BuildModuleNode();
            }
            catch (Exception salvageFailure)
            {
                _logger.LogDebug(salvageFailure, "The partial tree could not be salvaged for {uri}.", uri);
            }

            // what is salvaged is held to the limit as a module that was read is: the tree read before the prune failed is as deep as the module, and a stage
            // after the parser that walks it runs out of stack.
            salvaged = PrunedOrNone(uri, salvaged, content, anchorOffset, errorListener);

            // and so are the directives, which the directive pass read before the parse failed (#If, #ElseIf, #Const), each of them on its own.
            var directives = precompilerTrivia.Select(directive => PrunedOrNone(uri, directive, content, anchorOffset, errorListener)).OfType<SyntaxNode>().ToImmutableArray();

            // the errors the slow parse found before it stopped are the module's, and so is that it stopped: what is after it was not read.
            var stopped = VBSyntaxErrorInfo.For(VBCompileErrorId.SyntaxError, new(uri, new SourceRange(anchorOffset, anchorOffset)),
                outOfStack ? Exceptions.VBCompileError_ModuleNestedTooDeeplyToRead_Verbose : SourcePathAnonymizer.Scrub(exception.ToString(), _scrub));
            return new ModuleParseResult
            {
                SyntaxTree = salvaged,
                PrecompilerTrivia = directives,
                Trivia = read?.Trivia ?? declarations?.Trivia ?? [],
                SyntaxErrors = errorListener.Errors.IsEmpty ? [stopped] : outOfStack ? errorListener.Errors.Add(stopped) : errorListener.Errors,
            };
        }
    }

    // whether the parse went deeper than it reads (StackDepthGuardListener.MaxRuleDepth) or ran out of its stack, however the exception that says so was wrapped on
    // its way out of the grammar's descent.
    private static bool IsOutOfStack(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is InsufficientExecutionStackException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a module is parsed a segment at a time (<see cref="ModuleSegmentScanner"/>) where it can be. The tree, its trivia and its errors are those a
    /// parse of the whole module gives; only the time differs.
    /// </summary>
    internal bool Segmented { get; init; } = true;

    /// <summary>
    /// Whether a module is parsed a segment at a time without its fast parse as a whole being tried first: what the equivalence oracle parses under, so that
    /// every module it is given takes the segmented path where it can.
    /// </summary>
    internal bool ForceSegments { get; init; }

    /// <summary>
    /// How the modules this parser read were read: whole, a segment at a time, or whole after a segmented parse gave up, by why. Each module that has code
    /// is counted once; the branches of a split header, parsed on their own, are not modules.
    /// </summary>
    internal SegmentationStatistics Statistics { get; } = new();

    // the segments of the module, or none - counted by why - when it must be parsed whole.
    private static IReadOnlyList<ModuleSegmentScanner.Segment>? Segments(string sanitized, SegmentationStatistics? statistics)
    {
        var segments = ModuleSegmentScanner.Scan(sanitized, out var giveUp);
        if (segments is { Count: > 1 })
        {
            return segments;
        }

        statistics?.Count(giveUp ?? SegmentationStatistics.Path.OneSegment);
        return null;
    }

    // the module's segments parsed each on its own, in order, each anchored where it starts and numbering its nodes where the one before it left off, and
    // stitched: the tree, the trivia and the errors a parse of the whole module gives. A member's syntax errors bound to its statements are kept with it
    // (KeepsItsErrors), and only the member is read slowly. None when a segment has another syntax error - what the grammar says is wrong, and what the
    // block scanner says instead, is a whole module's to say - or when its parse fails: the module is then parsed whole, as a module
    // whose parse fails is, its partial tree salvaged. A segment nested deeper than the parse reads is not: the module is not read again whole, as a segment's
    // parse has read it to that depth slowly already, and it stops where a parse of the whole module would. What that parse found wrong before it stopped is
    // the module's (errorListener), as a slow parse of the whole module would have found it.
    private (ModuleNode Module, ImmutableArray<SyntaxNode> Trivia, ImmutableArray<VBSyntaxErrorInfo> Errors)? ParseSegments(Uri uri, ModuleNode node, string sanitized,
        IReadOnlyList<ModuleSegmentScanner.Segment> segments, SourcePosition anchorOffset, ErrorListener errorListener, SegmentationStatistics? statistics,
        CancellationToken token)
    {
        try
        {
            return ParseEachSegment(uri, node, sanitized, segments, anchorOffset, errorListener, statistics, token);
        }
        catch (Exception exception) when (!token.IsCancellationRequested && !IsOutOfStack(exception))
        {
            _logger.LogDebug(exception, "A segment of {uri} failed to parse; parsing it whole.", uri);
            statistics?.Count(SegmentationStatistics.Path.SegmentFailed);
            return null;
        }
    }

    private (ModuleNode Module, ImmutableArray<SyntaxNode> Trivia, ImmutableArray<VBSyntaxErrorInfo> Errors)? ParseEachSegment(Uri uri, ModuleNode node, string sanitized,
        IReadOnlyList<ModuleSegmentScanner.Segment> segments, SourcePosition anchorOffset, ErrorListener errorListener, SegmentationStatistics? statistics,
        CancellationToken token)
    {
        var lines = sanitized.Split('\n');
        var children = ImmutableArray.CreateBuilder<SyntaxNode>();
        var trivia = ImmutableArray.CreateBuilder<SyntaxNode>();
        var errors = ImmutableArray.CreateBuilder<VBSyntaxErrorInfo>();
        var next = 0;
        foreach (var segment in segments)
        {
            // the segment's lines, each with the line ending it had in the module.
            var text = string.Join('\n', lines[segment.StartLine..segment.EndLine]) + (segment.EndLine < lines.Length ? "\n" : string.Empty);
            var start = new SourcePosition(segment.StartLine, 0).AnchoredAt(anchorOffset);
            var segmentErrors = new ErrorListener(uri, start);
            var first = next;
            DeclarationsParseTreeListener listener;
            try
            {
                listener = ParseWithFallback(text, segmentErrors, errors => new DeclarationsParseTreeListener(uri, node, errors) { NextModuleChildId = first, Anchor = start },
                    start, token);
            }
            catch (Exception exception) when (IsOutOfStack(exception))
            {
                errorListener.AddRange(segmentErrors.Errors);
                throw;
            }

            var read = listener.BuildModuleNode();
            if (segmentErrors.HasGrammarErrors && !KeepsItsErrors(segmentErrors, read))
            {
                statistics?.Count(SegmentationStatistics.Path.SegmentHadErrors);
                return null;
            }

            next = listener.NextModuleChildId;
            children.AddRange(read.Children);
            trivia.AddRange(listener.Trivia);
            errors.AddRange(segmentErrors.Errors);
        }

        return (node with { Children = children.ToImmutable() }, trivia.ToImmutable(), errors.ToImmutable());
    }

    // whether a segment's syntax errors are what a parse of the whole module reports there, and the segment what it reads: each error is in a member, from
    // its header to its end, and none is that of a block its parse found not closed (StatementStart). A parse of the whole module reads a block that is not
    // closed past the member's end; and it reads what is wrong outside the members - in the declarations section, or between them - by what follows it.
    private static bool KeepsItsErrors(ErrorListener errors, ModuleNode segment)
    {
        var members = segment.Children.OfType<MemberDeclarationNode>().Select(member => member.SourceLocation.Range).ToList();
        return errors.Errors.All(error => error.StatementStart is null
            && members.Any(member => member.Start.CompareTo(error.Location.Range.Start) <= 0 && error.Location.Range.Start.CompareTo(member.End) <= 0));
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
            var parsed = Parse(uri, $"{branch}\n{alternative.Closer}\n", start, rootLineage.Add(-1 - index), statistics: null, token);
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

    private ImmutableArray<SyntaxNode> ParsePrecompilerNodes(string source, ISyntaxNodeProvider[] listeners, SourcePosition anchorOffset, CancellationToken token)
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
            var tree = ParseDirectives(new string(sanitized), token);
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

    // the directive pass in SLL, first with a bail-on-first-error strategy, then with default recovery when that fails. A directive's line is a physicalLine
    // too, and ccBlock's non-greedy loop and the order of its alternatives choose the directive: SLL makes that choice where the alternatives conflict, and
    // gives the tree LL gives whenever it succeeds. LL reads on to the end of the module to prove each conflict an ambiguity first - 7 s for the 1,200 lines
    // of a JSON parser, and a stack overflow at a few hundred #If blocks - so a module whose directives do not fit the grammar is recovered from in SLL too,
    // for its trivia at their best. What is wrong with a directive is the directive scanner's to report, not this pass's. A cancelled parse is told by the
    // token, never by the exception: SLL's bail is itself an OperationCanceledException.
    private static VBAConditionalCompilationParser.CompilationUnitContext ParseDirectives(string sanitized, CancellationToken token)
    {
        var bailing = DirectiveParser(sanitized, new BailErrorStrategy(), token);
        try
        {
            return bailing.compilationUnit();
        }
        catch (Exception exception) when ((exception is ParseCanceledException or RecognitionException) && !token.IsCancellationRequested)
        {
            var recovering = DirectiveParser(sanitized, new DefaultErrorStrategy(), token);
            try
            {
                return recovering.compilationUnit();
            }
            finally
            {
                t_directiveLooks += ((LookCountingTokenStream)recovering.InputStream).Looks;
            }
        }
        finally
        {
            t_directiveLooks += ((LookCountingTokenStream)bailing.InputStream).Looks;
        }
    }

    [ThreadStatic]
    private static long t_directiveLooks;

    /// <summary>
    /// The work the directive passes of this thread's last <see cref="Parse(Uri, string, SourcePosition, CancellationToken)"/> did, the module's and its
    /// header alternatives': each token their parses looked at, as many times as each looked at it, in prediction and in matching. None for a module with
    /// no directive. What a guard of the pass's work counts, where a clock would count the machine. It isn't a function of the input alone: a decision the
    /// shared DFA cache hasn't seen yet adds the looks of its full prediction, once.
    /// </summary>
    internal static long DirectiveLooksOfLastParse => t_directiveLooks;

    // a token stream that counts the looks the parser takes at its tokens.
    private sealed class LookCountingTokenStream(ITokenSource source) : CommonTokenStream(source)
    {
        public long Looks { get; private set; }

        public override int La(int i)
        {
            Looks++;
            return base.La(i);
        }

        public override IToken Lt(int k)
        {
            Looks++;
            return base.Lt(k);
        }
    }

    private static VBAConditionalCompilationParser DirectiveParser(string sanitized, IAntlrErrorStrategy errorStrategy, CancellationToken token)
    {
        var parser = new VBAConditionalCompilationParser(new LookCountingTokenStream(new VBALexer(new AntlrInputStream(sanitized))))
        {
            ErrorHandler = errorStrategy,
        };
        parser.Interpreter.PredictionMode = PredictionMode.Sll;
        parser.RemoveErrorListeners();
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
    private TListener ParseWithFallback<TListener>(string content, ErrorListener errorListener, Func<ErrorListener, TListener> listenerFactory, SourcePosition anchorOffset,
        CancellationToken token)
        where TListener : class, IParseTreeListener
        => TryParseFast(content, errorListener, listenerFactory, anchorOffset, token) ?? ParseSlowly(content, errorListener, listenerFactory(errorListener), anchorOffset, token);

    // SLL with a bail-on-first-error strategy; none when it fails. Any SLL failure falls through to LL: a plain ParseCanceledException/RecognitionException, but
    // also a listener exception the bail strategy triggered on a half-matched rule. SLL carries no error listener, so nothing located is lost. What the
    // listener reports goes to errors of its own, which are errorListener's only when the parse succeeds: a walk that bails half-way through has reported
    // some, which the parse that follows reports again.
    private TListener? TryParseFast<TListener>(string content, ErrorListener errorListener, Func<ErrorListener, TListener> listenerFactory, SourcePosition anchorOffset,
        CancellationToken token)
        where TListener : class, IParseTreeListener
    {
        try
        {
            var reported = errorListener.Fresh();
            var listener = ParseOnce(content, PredictionMode.Sll, new BailErrorStrategy(), errorListener: null, reported, listenerFactory(reported), anchorOffset, token);
            errorListener.AddRange(reported.Errors);
            return listener;
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            if (IsOutOfStack(exception))
            {
                // its trace is a frame for each level the parse read, megabytes of them: the message says what stopped it.
                _logger.LogDebug("SLL parse pass stopped: the module is nested deeper than the parse reads; retrying on LL.");
            }
            else if (exception is not (ParseCanceledException or RecognitionException))
            {
                // a routine SLL bail needs no trace; anything else does, even though LL will retry.
                _logger.LogDebug(exception, "SLL parse pass raised {type}; retrying on LL.", exception.GetType().Name);
            }

            return null;
        }
    }

    // LL with recovery and the error listener, on a fresh listener: what produces the located errors. The recovery is ANTLR's, bound to the statement the error is in.
    private TListener ParseSlowly<TListener>(string content, ErrorListener errorListener, TListener listener, SourcePosition anchorOffset, CancellationToken token)
        where TListener : IParseTreeListener
        => ParseOnce(content, PredictionMode.Ll, new StatementBoundErrorStrategy(), errorListener, errorListener, listener, anchorOffset, token);

    // errorListener hears the grammar's errors, and none when it is null; findings is where what the parse finds of the tokens around its rules is reported.
    private TListener ParseOnce<TListener>(string content, PredictionMode mode, IAntlrErrorStrategy errorStrategy, ErrorListener? errorListener, ErrorListener findings,
        TListener listener, SourcePosition anchorOffset, CancellationToken token)
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
        parser.AddParseListener(new StackDepthGuardListener(RuleDepthCeiling));
        if (token.CanBeCanceled)
        {
            parser.AddParseListener(new CancellationListener(token));
        }
        var outputLists = new OutputListCollector();
        parser.AddParseListener(outputLists);
        var declarationNames = new ReservedDeclarationNameCollector();
        parser.AddParseListener(declarationNames);

        // the recovery reads a block whose header has an operator right before one of its words as the block it is, without the operator.
        var takenOff = errorStrategy is StatementBoundErrorStrategy && StatementBoundErrorStrategy.TakeOffOperatorsBeforeAWordOfAHeader(tokens) is { Count: > 0 } operators
            ? new OperatorsTakenOffReporter(parser, operators)
            : null;
        if (takenOff is not null)
        {
            parser.AddParseListener(takenOff);
        }

        var tree = parser.startRule();
        takenOff?.ReportTheRest();
        AnchorTree(tree, anchorOffset);
        ReportOutputItemsNotSeparated(outputLists.Lists, tokens, findings);
        ReportReservedDeclarationNames(declarationNames, findings);
        if (parser.NumberOfSyntaxErrors > 0)
        {
            MarkDanglingOperators(tree, tokens);
        }
        ParseTreeWalker.Default.Walk(listener, tree);
        return listener;
    }

    internal static readonly HashSet<int> BinaryOperators =
    [
        VBAParser.PLUS, VBAParser.MINUS, VBAParser.MULT, VBAParser.DIV, VBAParser.INTDIV, VBAParser.MOD, VBAParser.POW, VBAParser.AMPERSAND,
        VBAParser.EQ, VBAParser.NEQ, VBAParser.LT, VBAParser.GT, VBAParser.LEQ, VBAParser.GEQ, VBAParser.LIKE, VBAParser.IS,
        VBAParser.AND, VBAParser.OR, VBAParser.XOR, VBAParser.EQV, VBAParser.IMP,
    ];

    // what ends a statement: a line, a colon, a comment, the end of the module.
    private static readonly HashSet<int> EndsOfStatement = [VBAParser.NEWLINE, VBAParser.COLON, VBAParser.SINGLEQUOTE, VBAParser.REM, TokenConstants.Eof];

    /// <summary>
    /// Marks each expression that a binary operator the parse could not read is written right after, at the end of its statement (<c>a = 1 -</c>), with that
    /// operator: the listener keeps the two as an unbuilt expression, and not the expression alone as if it were all the statement said.
    /// </summary>
    /// <remarks>
    /// The operation has no right operand, and is a syntax error, reported as a zero-width range at the operator: before a <c>+</c>, <c>-</c>, <c>*</c>,
    /// <c>=</c>, <c>Like</c>, <c>Is</c>, <c>Mod</c>, <c>And</c>, <c>Or</c>, <c>Xor</c>, <c>Eqv</c> or <c>Imp</c>, after a <c>/</c>, <c>\</c>, <c>^</c>,
    /// <c>&amp;</c>, <c>&lt;&gt;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>&lt;=</c> or <c>&gt;=</c>. Where the operator can also begin something that follows an
    /// expression (a unary <c>+</c> or <c>-</c>, a name such as <c>Mod</c>, the next item of a list to print), the parse ends the expression before it, and
    /// the operator is a token no rule read; where it cannot, the parse reads the operation, and the listener keeps it unbuilt when it exits it. Valid code
    /// has no token that no rule read.
    /// </remarks>
    private static void MarkDanglingOperators(IParseTree tree, CommonTokenStream tokens)
    {
        // each token no rule read, and the outermost expression that ends at each token: an expression is met before the ones it holds.
        var unread = new List<IToken>();
        var expressions = new Dictionary<int, VBAParser.ExpressionContext>();
        var pending = new Stack<IParseTree>([tree]);
        while (pending.TryPop(out var node))
        {
            switch (node)
            {
                case IErrorNode error:
                    unread.Add(error.Symbol);
                    break;
                case VBAParser.ExpressionContext { Stop: { } stop } expression:
                    expressions.TryAdd(stop.TokenIndex, expression);
                    break;
            }

            for (var i = node.ChildCount - 1; i >= 0; i--)
            {
                pending.Push(node.GetChild(i));
            }
        }

        foreach (var token in unread.Where(token => BinaryOperators.Contains(token.Type)))
        {
            if (EndsOfStatement.Contains(tokens.Get(SignificantIndex(tokens, token.TokenIndex, +1)).Type)
                && expressions.TryGetValue(SignificantIndex(tokens, token.TokenIndex, -1), out var operand))
            {
                operand.DanglingOperator = (token, tokens.GetText(operand.Start, token));

                // what ends with the operand - the assignment, the item of a list to print, the statement - holds the operation it is kept as.
                for (var parent = operand.Parent as VBABaseParserRuleContext; parent is { Stop: { } stop } && stop.TokenIndex < token.TokenIndex;
                    parent = parent.Parent as VBABaseParserRuleContext)
                {
                    parent.ExtendThrough(token);
                }
            }
        }
    }

    // the index of the token before or after the one at index that is no blank: a whitespace or a line continuation.
    private static int SignificantIndex(CommonTokenStream tokens, int index, int step)
    {
        do
        {
            index += step;
        }
        while (index > 0 && index < tokens.Size - 1 && tokens.Get(index).Type is VBAParser.WS or VBAParser.LINE_CONTINUATION);
        return index;
    }

    // the tokens an output clause can begin with, but the whitespace a parenthesized expression may begin with.
    private static readonly IntervalSet OutputClauseStart = OutputClauseStartTokens();

    private static IntervalSet OutputClauseStartTokens()
    {
        var start = new IntervalSet(VBAParser._ATN.NextTokens(VBAParser._ATN.ruleToStartState[VBAParser.RULE_outputClause]));
        start.Remove(VBALexer.WS);
        start.Remove(VBALexer.LINE_CONTINUATION);
        return start;
    }

    /// <summary>
    /// Reports each output item written right after another, with nothing between them: <c>Debug.Print 1(2)</c>, <c>Print #1, "a"x</c>.
    /// </summary>
    /// <remarks>
    /// <strong>MS-VBAL §5.4.5.8.1</strong> lets an output item follow another with nothing between them (<c>output-list = *output-item</c>). RD-VBA requires a
    /// space, a <c>;</c> or a <c>,</c> between two output clauses (<strong>RD-VBAL §5.4.5.8.1</strong>), as the VBE writes them. The grammar reads no clause
    /// there, so the token right after an output list that begins one is the clause written right after the list's last. The grammar's error at it, if there
    /// is one, is its consequence: in a <c>Print #</c> statement, the token is not the end of the statement. After the output list of <c>Debug.Print</c> and
    /// of an object's <c>Print</c>, which is an l-expression, the grammar reads a <c>(</c> as the index of the expression, and reports nothing.
    /// </remarks>
    private static void ReportOutputItemsNotSeparated(IEnumerable<VBAParser.OutputListContext> lists, ITokenStream tokens, ErrorListener findings)
    {
        // a list the grammar stopped in is checked as well: what stopped it is often the clause written right after another (`Spc(1)x`), which is the error
        // to say. Where it stopped on anything else, no clause begins after it.
        foreach (var list in lists)
        {
            if (list.Stop is not { } last || last.TokenIndex < list.Start.TokenIndex || last.TokenIndex + 1 >= tokens.Size)
            {
                continue;
            }

            var next = tokens.Get(last.TokenIndex + 1);
            if (OutputClauseStart.Contains(next.Type))
            {
                findings.ReplaceGrammarErrorAt(next, VBCompileErrorId.OutputItemsNotSeparated, Exceptions.VBCompileError_OutputItemsNotSeparated_Verbose);
            }
        }
    }

    /// <summary>
    /// Reports each declaration named with a reserved identifier as a syntax error at its name: <c>Sub S(ByVal Mod As Long)</c>, <c>Dim Len As Long</c>.
    /// </summary>
    /// <remarks>
    /// A parameter, a variable, a constant, a procedure, an <c>Enum</c> and its members, a user-defined type, an external procedure and an event are named
    /// with an <c>IDENTIFIER</c>, which is no reserved identifier (<strong>MS-VBAL §5.3.1.5</strong>, <strong>§3.3.5.2</strong>); a member of a user-defined
    /// type can be named with one (<strong>MS-VBAL §5.2.3.3</strong>). The grammar still reads one there, so the declaration is read whole: the body of a procedure, its end and the procedures after it
    /// parse as they would with a valid name.
    /// </remarks>
    private static void ReportReservedDeclarationNames(ReservedDeclarationNameCollector names, ErrorListener findings)
    {
        foreach (var name in names.Parameters)
        {
            findings.ReplaceGrammarErrorAt(name, VBCompileErrorId.SyntaxError, string.Format(Exceptions.VBCompileError_ReservedParameterName_Verbose, name.Text));
        }

        foreach (var name in names.Names)
        {
            findings.ReplaceGrammarErrorAt(name, VBCompileErrorId.SyntaxError, string.Format(Exceptions.VBCompileError_ReservedDeclarationName_Verbose, name.Text));
        }
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
                errors.Report(new SourceLocation(uri, new(start, end)), VBCompileErrorId.SyntaxError, Exceptions.VBCompileError_TooManyLineContinuations_Verbose);
            }

            line++;
        }
    }

    // RD-VBA reads an expression nested in at most 2,500 others: MS-VBA runs a chain of 2,500 terms (2,499 additions), 2,000 nested parentheses and 1,000
    // nested calls, and crashes the host past the chain, with no error (recorded in Excel: oracle\s65). MS-VBAL leaves the depth of an expression to the
    // implementation. RD-VBAL: a deeper expression is a syntax error, kept as text and not analyzed.
    internal const int MaxExpressionNesting = 2500;

    // reports the first expression nested in more than MaxExpressionNesting others, once for the outermost expression it is in, and prunes each: the tree - the
    // module, or a directive of its own parse - comes back with every expression nested that deep an error node of its source text (UnbuiltExpressionTriviaNode),
    // so that no stage after the parser is handed an expression deeper than the limit. The tree is walked with a stack of its own, as deep as the expression may be.
    private static TNode PruneExpressionsNestedTooDeeply<TNode>(TNode module, string content, SourcePosition anchorOffset, ErrorListener errors) where TNode : SyntaxNode
    {
        var pending = new Stack<(SyntaxNode Node, int Depth, int Nesting, ExpressionNode? Outermost)>();
        var path = new List<SyntaxNode>();
        var reported = new HashSet<ExpressionNode>(ReferenceEqualityComparer.Instance);
        var pruned = new Dictionary<SyntaxNode, SyntaxNode>(ReferenceEqualityComparer.Instance);
        var holders = new Dictionary<SyntaxNode, int>(ReferenceEqualityComparer.Instance);
        int[]? lineStarts = null;

        pending.Push((module, 0, 0, null));
        while (pending.TryPop(out var entry))
        {
            var (node, depth, nesting, outermost) = entry;
            path.RemoveRange(depth, path.Count - depth);
            path.Add(node);

            var expression = AsALevelOfNesting(node);
            if (expression is not null)
            {
                outermost ??= expression;
                if (nesting > MaxExpressionNesting)
                {
                    // the rest of the outermost expression is no more than this says already.
                    if (reported.Add(outermost))
                    {
                        errors.Report(expression.SourceLocation, VBCompileErrorId.ExpressionNestedTooDeeply, Exceptions.VBCompileError_ExpressionNestedTooDeeply_Verbose);
                    }

                    // what is held where only a name may be (the member of a member access) is pruned with what holds it.
                    var at = depth;
                    while (!SyntaxTreePruning.HoldsAnyExpressionWhere(path[at - 1], path[at]))
                    {
                        at--;
                    }

                    var prunedNode = path[at];
                    lineStarts ??= LineStarts(content);
                    if (!pruned.ContainsKey(prunedNode))
                    {
                        var text = TextAt(content, lineStarts, anchorOffset, prunedNode.SourceLocation.Range);
                        pruned.Add(prunedNode, new UnbuiltExpressionTriviaNode(prunedNode.Identity, prunedNode.SourceLocation, text, [])
                        {
                            Names = NamesWrittenIn(text, prunedNode.SourceLocation.Range.Start),
                        });
                    }
                    var holder = at - 1;
                    while (holder >= 0 && holders.TryAdd(path[holder], holder))
                    {
                        holder--;
                    }

                    continue;
                }
            }

            var inner = expression is null ? nesting : nesting + 1;
            foreach (var child in node.ChildNodes().Reverse())
            {
                // the arguments of a bare call statement (`F 1, x`, `F (x)`) are nested in its call, as those of `Call F(1, x)` are in its index expression, and
                // are reported with its callee, once for the statement.
                if (node is CallStatementNode call && !ReferenceEquals(child, call.Callee))
                {
                    pending.Push((child, depth + 1, 1, call.Callee));
                    continue;
                }

                pending.Push((child, depth + 1, outermost is null ? 0 : inner, outermost));
            }
        }

        return pruned.Count == 0 ? module : (TNode)SyntaxTreePruning.Replaced(module, pruned, holders);
    }

    // a tree salvaged from a parse that failed, pruned as a module that was read is (PruneExpressionsNestedTooDeeply), or none if that fails too: no tree comes
    // back with an expression deeper than the limit.
    private TNode? PrunedOrNone<TNode>(Uri uri, TNode? salvaged, string content, SourcePosition anchorOffset, ErrorListener errors) where TNode : SyntaxNode
    {
        if (salvaged is null)
        {
            return null;
        }

        try
        {
            return PruneExpressionsNestedTooDeeply(salvaged, content, anchorOffset, errors);
        }
        catch (Exception pruneFailure)
        {
            _logger.LogDebug(pruneFailure, "The partial tree salvaged for {uri} could not be pruned to the limit; none is salvaged.", uri);
            return null;
        }
    }

    // where each line of a text begins: a line ends at its line feed, as the lexer counts lines (a carriage return before it is the end of its line).
    private static int[] LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var index = text.IndexOf('\n'); index >= 0; index = text.IndexOf('\n', index + 1))
        {
            starts.Add(index + 1);
        }

        return [.. starts];
    }

    // the text at a range of the module, whose positions are in the document a module parsed as its fragment is anchored in.
    private static string TextAt(string text, int[] lineStarts, SourcePosition anchorOffset, SourceRange range)
    {
        int OffsetOf(SourcePosition position)
        {
            var line = position.Line - anchorOffset.Line;
            var character = line == 0 ? position.Character - anchorOffset.Character : position.Character;
            return Math.Clamp(lineStarts[Math.Clamp(line, 0, lineStarts.Length - 1)] + character, 0, text.Length);
        }

        var start = OffsetOf(range.Start);
        return text[start..Math.Max(start, OffsetOf(range.End))];
    }

    // the names written in the text of an expression the tree keeps as text, which starts at a position of the document, as the lexer reads them: a word of
    // its own, in brackets or not, and never what a literal or a comment holds. A word right after a `!` is the key of a dictionary access, the text of a string.
    private static ImmutableArray<UnbuiltName> NamesWrittenIn(string text, SourcePosition start)
    {
        var lexer = new VBALexer(new AntlrInputStream(text));
        lexer.RemoveErrorListeners();
        var tokens = lexer.GetAllTokens();
        var names = ImmutableArray.CreateBuilder<UnbuiltName>();
        IToken? previous = null;
        var member = false;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Type is VBALexer.SINGLEQUOTE or VBALexer.REM)
            {
                while (index + 1 < tokens.Count && tokens[index + 1].Type != VBALexer.NEWLINE)
                {
                    index++;
                }

                continue;
            }

            if (token.Type is VBALexer.WS or VBALexer.LINE_CONTINUATION)
            {
                previous = token;
                continue;
            }

            var key = previous?.Type == VBALexer.EXCLAMATIONPOINT;
            var close = token.Type == VBALexer.L_SQUARE_BRACKET ? IndexOfClosingBracket(tokens, index) : -1;
            if (close > index + 1)
            {
                var first = tokens[index + 1];
                if (!key)
                {
                    var name = text[first.StartIndex..tokens[close].StartIndex];
                    names.Add(new UnbuiltName(name, RangeOf(first, name.Length), member));
                }

                index = close;
            }
            else if (!key && IsWord(token))
            {
                names.Add(new UnbuiltName(token.Text, RangeOf(token, token.Text.Length), member));
            }

            member = token.Type == VBALexer.DOT;
            previous = tokens[index];
        }

        return names.ToImmutable();

        SourceRange RangeOf(IToken token, int length)
        {
            var at = new SourcePosition(start.Line + token.Line - 1, (token.Line == 1 ? start.Character : 0) + token.Column);
            return new SourceRange(at, new SourcePosition(at.Line, at.Character + length));
        }
    }

    // the `]` that closes the `[` at an index of the tokens of a text, on its line; -1 if there is none.
    private static int IndexOfClosingBracket(IList<IToken> tokens, int index)
    {
        for (var at = index + 1; at < tokens.Count && tokens[at].Type != VBALexer.NEWLINE; at++)
        {
            if (tokens[at].Type == VBALexer.R_SQUARE_BRACKET)
            {
                return at;
            }
        }

        return -1;
    }

    // a token that is a name, or a keyword spelled as one: what an identifier is written as.
    private static bool IsWord(IToken token)
        => token.Type == VBALexer.IDENTIFIER || (token.Text.Length > 0 && char.IsLetter(token.Text[0]) && token.Text.All(c => char.IsLetterOrDigit(c) || c == '_'));

    // an expression that the expressions in it are nested in (RD-VBAL 2.6.1: an operand, an argument, what parentheses hold, the owner of a member access, the
    // callee of an index expression, what New, TypeOf...Is and AddressOf are written around). What is written around an argument to pass it by name (a:=) or
    // ByVal nests it in nothing more than its call; Debug.Print takes an output list (MS-VBAL 5.4.5.8.1, the list of the Print statement of 5.4.5.8), not
    // arguments, so neither it, an item of its list, nor a Spc or Tab clause nests what it prints.
    private static ExpressionNode? AsALevelOfNesting(SyntaxNode node)
        => node is ExpressionNode expression and not (NamedArgumentNode or ByValArgumentExpressionNode or ObjectPrintExpressionNode or PrintOutputItemNode
            or PrintSpcClauseNode or PrintTabClauseNode) ? expression : null;
}

// Registered via AddParseListener (fires live, interleaved with the parser's own recursive descent) —
// not walked later like DeclarationsParseTreeListener. Deliberately has no AST-building side effects
// of its own: its only job is to fail fast, during parsing itself, before pathological nesting can
// exhaust the stack past the point of no return. See ParseOnce's remarks for why a walk-time-only
// guard can't substitute for this.
// ceiling: how many rules the parse may be inside at once; a module parser's own is MaxRuleDepth.
internal sealed class StackDepthGuardListener(int ceiling) : VBAParserBaseListener
{
    /// <summary>
    /// How many rules the parse may be inside at once: 32,000. Past it, the parse stops, and the module is a syntax error that it is nested too deeply to be read
    /// (RD-VBAL 2.6.1). This is a limit of the implementation, and not of the language: an expression nested past
    /// <see cref="ModuleParser.MaxExpressionNesting"/> is the language's own syntax error, <c>VBC00060</c>, and the ceiling is more than twice as deep as the
    /// deepest such expression RD-VBA reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rules a level of nesting puts the parse inside of, measured on the grammar: 1 for parentheses, a print item, <c>Spc(</c>, <c>Tab(</c>,
    /// <c>TypeOf</c>, a unary minus and <c>Not</c>; 6 for a call, however it is written (<c>Abs(</c>, <c>o.F(</c>, <c>Call F(G(</c>, <c>F G(</c>,
    /// <c>F(ByVal</c>, <c>F(a:=</c>); 2 for a parenthesized right operand (<c>1 + (</c>). A chain of operators (<c>2 ^ 2 ^ …</c>, <c>1 + 1 + …</c>) and a chain
    /// of argument lists (<c>a(1)(2)…</c>) do not nest the parse at all: 13 to 18 rules, however long. The deepest expression RD-VBA reads, 2,500 nested calls,
    /// is 15,012 rules deep in a procedure; the ceiling leaves it a margin of as much again, and more.
    /// </para>
    /// <para>
    /// Blocks nest the parse too: 3 rules for a <c>For</c> loop, 4 for an <c>If</c> or a <c>With</c> block, and 5 for a <c>Select Case</c>. A module whose
    /// blocks are nested about 6,400 deep reaches the ceiling.
    /// </para>
    /// <para>
    /// The parse runs on a stack of its own (<see cref="LargeStackThread"/>), which the ceiling is well inside of: the depth at which a parse stops is the same
    /// whatever the size of its frames, in a Debug build as in a Release one. It also stops before its stack is deep enough to slow the process down: every
    /// collection of the garbage collector scans the stack of every thread, and a parse that ran to the end of its stack, a hundred thousand levels and more,
    /// made each one many times slower while it ran. The check of the stack itself
    /// (<see cref="System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack"/>) is kept as a backstop.
    /// </para>
    /// </remarks>
    internal const int MaxRuleDepth = 32_000;

    private int _depth;

    public override void EnterEveryRule([NotNull] ParserRuleContext context)
    {
        if (++_depth > ceiling)
        {
            throw new InsufficientExecutionStackException($"The parse is inside more than {ceiling} rules.");
        }

        System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
    }

    public override void ExitEveryRule([NotNull] ParserRuleContext context) => _depth--;
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

// the output lists of a parse, registered live: each with its last token, which the parser sets before it says the rule is exited.
internal sealed class OutputListCollector : VBAParserBaseListener
{
    public List<VBAParser.OutputListContext> Lists { get; } = [];

    public override void ExitOutputList([NotNull] VBAParser.OutputListContext context) => Lists.Add(context);
}

// the names of a parse's declarations that are reserved identifiers (MS-VBAL 3.3.5.2), registered live: a name the rule read whole, which is no identifier,
// or an identifier that is one reserved word the grammar reads as a name (Len, Array, Then). A member of a user-defined type can be named with any reserved
// identifier (MS-VBAL 5.2.3.3 <reserved-name-member-dcl>), and is not collected.
internal sealed class ReservedDeclarationNameCollector : VBAParserBaseListener
{
    // the words of the grammar's keyword rule that are reserved identifiers: reserved names, special forms, statement keywords, marker keywords and literal
    // identifiers. The others (Name, Text, Step, Error...) are identifiers.
    private static readonly HashSet<int> ReservedWords =
    [
        VBAParser.ABS, VBAParser.CBOOL, VBAParser.CBYTE, VBAParser.CCUR, VBAParser.CDATE, VBAParser.CDBL, VBAParser.CDEC, VBAParser.CINT, VBAParser.CLNG,
        VBAParser.CLNGLNG, VBAParser.CLNGPTR, VBAParser.CSNG, VBAParser.CSTR, VBAParser.CVAR, VBAParser.CVERR, VBAParser.DATE, VBAParser.DEBUG,
        VBAParser.DOEVENTS, VBAParser.FIX, VBAParser.INT, VBAParser.LEN, VBAParser.LENB, VBAParser.ME, VBAParser.PSET, VBAParser.SGN, VBAParser.STRING,
        VBAParser.ARRAY, VBAParser.INPUT, VBAParser.INPUTB, VBAParser.LBOUND, VBAParser.UBOUND,
        VBAParser.CLOSE, VBAParser.END, VBAParser.GET, VBAParser.LOCK, VBAParser.OPEN, VBAParser.PRINT, VBAParser.PUT, VBAParser.SEEK, VBAParser.UNLOCK,
        VBAParser.WRITE,
        VBAParser.ANY, VBAParser.BYREF, VBAParser.BYVAL, VBAParser.IN, VBAParser.OPTIONAL, VBAParser.PARAMARRAY, VBAParser.PRESERVE, VBAParser.SHARED,
        VBAParser.SPC, VBAParser.TAB, VBAParser.THEN, VBAParser.TO, VBAParser.UNTIL, VBAParser.WITHEVENTS,
        VBAParser.TRUE, VBAParser.FALSE, VBAParser.NOTHING, VBAParser.NULL, VBAParser.ATTRIBUTE,
    ];

    // the reserved identifiers the lexer reads as an IDENTIFIER: the future-reserved ones except CDecl, and the ones reserved for the implementation's use except
    // Attribute (MS-VBAL 3.3.5.2).
    private static readonly HashSet<string> ReservedIdentifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Decimal", "DefDec", "LINEINPUT", "VB_Base", "VB_Control", "VB_Creatable", "VB_Customizable", "VB_Description", "VB_Exposed", "VB_Ext_KEY",
        "VB_GlobalNameSpace", "VB_HelpID", "VB_Invoke_Func", "VB_Invoke_Property", "VB_Invoke_PropertyPut", "VB_Invoke_PropertyPutRef", "VB_MemberFlags",
        "VB_Name", "VB_PredeclaredId", "VB_ProcData", "VB_TemplateDerived", "VB_UserMemId", "VB_VarDescription", "VB_VarHelpID", "VB_VarMemberFlags",
        "VB_VarProcData", "VB_VarUserMemId",
    };

    // the reserved names MS-VBA compiles as the name of an Enum member, where MS-VBAL 3.3.5.2 reserves them: RD-VBA compiles them too, and a warning says the
    // specification reserves the name.
    private static readonly HashSet<int> EnumMemberNamesMSVBAAccepts = [VBAParser.DATE, VBAParser.DOEVENTS, VBAParser.CDEC];

    public List<IToken> Parameters { get; } = [];

    public List<IToken> Names { get; } = [];

    public override void ExitArg([NotNull] VBAParser.ArgContext context)
    {
        if (context.unrestrictedIdentifier() is { exception: null, Start.TokenIndex: >= 0 } name && name.ChildCount > 0
            && (name.identifier() is null ? name.Start : ReservedWordOf(name.identifier())) is { } word)
        {
            Parameters.Add(word);
        }
    }

    public override void ExitVariableSubStmt([NotNull] VBAParser.VariableSubStmtContext context) => Add(context.identifier());

    public override void ExitConstSubStmt([NotNull] VBAParser.ConstSubStmtContext context) => Add(context.identifier());

    public override void ExitFunctionName([NotNull] VBAParser.FunctionNameContext context) => Add(context.identifier());

    public override void ExitSubroutineName([NotNull] VBAParser.SubroutineNameContext context) => Add(context.identifier());

    public override void ExitEnumerationStmt_Constant([NotNull] VBAParser.EnumerationStmt_ConstantContext context)
    {
        if (ReservedWordOf(context.identifier()) is { } word && !EnumMemberNamesMSVBAAccepts.Contains(word.Type))
        {
            Names.Add(word);
        }
    }

    public override void ExitEnumerationStmt([NotNull] VBAParser.EnumerationStmtContext context) => Add(context.identifier());

    public override void ExitDeclareStmt([NotNull] VBAParser.DeclareStmtContext context) => Add(context.identifier());

    public override void ExitEventStmt([NotNull] VBAParser.EventStmtContext context) => Add(context.identifier());

    public override void ExitUdtDeclaration([NotNull] VBAParser.UdtDeclarationContext context) => Add(ReservedWordOf(context.untypedIdentifier()));

    private void Add(VBAParser.IdentifierContext? identifier) => Add(ReservedWordOf(identifier));

    private void Add(IToken? word)
    {
        if (word is not null)
        {
            Names.Add(word);
        }
    }

    private static IToken? ReservedWordOf(VBAParser.IdentifierContext? identifier)
        => identifier is { exception: null } ? ReservedWordOf(identifier.untypedIdentifier() ?? identifier.typedIdentifier()?.untypedIdentifier()) : null;

    private static IToken? ReservedWordOf(VBAParser.UntypedIdentifierContext? identifier)
        => identifier is { exception: null } ? identifier.identifierValue() switch
        {
            { exception: null } value when value.keyword() is { exception: null, Start: { TokenIndex: >= 0 } word } && ReservedWords.Contains(word.Type) => word,
            { exception: null } value when value.IDENTIFIER()?.Symbol is { TokenIndex: >= 0 } word && ReservedIdentifiers.Contains(word.Text) => word,
            _ => null,
        } : null;
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

    // a listener of the same module and anchor, with no error yet.
    public ErrorListener Fresh() => new(_uri, anchorOffset);

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

        // a semantic predicate is the grammar's, and no part of the language: where one fails (`Debug.Print Spc(1`, a Spc clause that is not complete), what
        // the parser could not read is said as ANTLR says it where no alternative is viable (DefaultErrorStrategy.ReportNoViableAlternative): the input from
        // where the rule's decision began to the token it failed at, and not the predicate's code.
        if (e is FailedPredicateException { Context: ParserRuleContext { Start: { } start } } && offendingSymbol is not null
            && recognizer is Parser { InputStream: ITokenStream tokens })
        {
            msg = $"no viable alternative at input {EscapeWSAndQuote(tokens.GetText(start, offendingSymbol))}";
        }

        // one error where the parse stopped: a rule that fails at a token, and the rule it is in that fails at the same token, are one syntax error, said as the
        // parse first said it.
        if (_reportedAt.Contains(location))
        {
            return;
        }

        // an error before one the parse reported is in tokens it reads again: the decision that failed looked past them, to the end of the statement
        // (Debug.Print 1 -, at the line end), and the statement fails before that, at the error the parse reports now, which is the one error. The parse's
        // errors are in the order of their positions, so the ones past this one are the last it reported; the errors of the directives, and of the parts of
        // the module parsed on their own, are not the parse's.
        while (_reported.Count > 0 && _reported[^1].Location.Range.Start.CompareTo(position) > 0)
        {
            ForgetTheLastReported();
        }

        // a statement has one syntax error, the first the parse reports in it: what the parse fails at after it, with no end of a statement between them
        // (`Mod = 1`: the operator is taken out, and the rule the statement is in fails at `=`), is the rest of the statement it could not read.
        if (InTheStatementOfTheLastReported(recognizer, offendingSymbol))
        {
            return;
        }

        var error = VBSyntaxErrorInfo.For(VBCompileErrorId.SyntaxError, location, msg) with { StatementStart = StatementStartOf(recognizer, offendingSymbol) };
        _lastReportedAt = offendingSymbol;
        _reported.Add(error);
        _reportedAt.Add(location);
        _errors.Add(error);
    }

    // the words legalLabelIdentifier's predicate names no label with; before EndsABlock, which is made of them.
    private static readonly IntervalSet NoLabelName = new(VBALexer.DOEVENTS, VBALexer.END, VBALexer.CLOSE, VBALexer.ELSE, VBALexer.LOOP, VBALexer.NEXT,
        VBALexer.RANDOMIZE, VBALexer.REM, VBALexer.RESUME, VBALexer.RETURN, VBALexer.STOP, VBALexer.WEND);

    // what ends a block in any rule (End If, Next, Loop, End Sub, ...), and the end of the module: where the parse fails at one, the error is the block's that
    // is not closed. The ends of a statement are no block's.
    private static readonly IntervalSet EndsABlock = BlockEnds();

    private static IntervalSet BlockEnds()
    {
        var atn = VBAParser._ATN;
        var block = atn.ruleToStartState[VBAParser.RULE_block];
        var ends = new IntervalSet();
        foreach (var state in atn.states)
        {
            for (var i = 0; i < state.NumberOfTransitions; i++)
            {
                if (state.Transition(i) is RuleTransition { target: var target, followState: var follow } && target == block)
                {
                    ends.AddAll(atn.NextTokens(follow));
                }
            }
        }

        ends.Add(TokenConstants.Eof);
        foreach (var type in new[] { TokenConstants.Epsilon, VBALexer.WS, VBALexer.LINE_CONTINUATION, VBALexer.NEWLINE, VBALexer.COLON, VBALexer.SINGLEQUOTE, VBALexer.REM })
        {
            ends.Remove(type);
        }

        // a label can be written before the end of a block (L1: Loop): what begins one - a name, a number - ends no block, and the end after it does. The words
        // no label is named (legalLabelIdentifier's predicate) are left: Loop, Next, Wend and Else end blocks.
        var label = atn.NextTokens(atn.ruleToStartState[VBAParser.RULE_statementLabelDefinition]);
        foreach (var type in label.ToList())
        {
            if (!NoLabelName.Contains(type))
            {
                ends.Remove(type);
            }
        }

        return ends;
    }

    // where the statement whose block the parse failed to end begins, when it failed at what ends a block where a statement begins, or in a Select Case
    // outside the blocks of its Cases: the innermost statement of a procedure, else the declaration of the module. A block that is not closed is reported
    // where its end was expected, which is in the statement that begins it.
    private SourcePosition? StatementStartOf(IRecognizer recognizer, IToken? offendingSymbol)
    {
        var parser = recognizer as Parser;
        var at = parser?.Context;
        if (offendingSymbol is null
            || !(EndsABlock.Contains(offendingSymbol.Type) && BeginsAStatement(parser?.InputStream as ITokenStream, offendingSymbol))
            && !InASelectOutsideItsCases(at))
        {
            return null;
        }

        for (var context = at; context is not null; context = context.Parent as ParserRuleContext)
        {
            if (context is VBAParser.BlockStmtContext or VBAParser.ModuleDeclarationsElementContext && context.Start is { } start)
            {
                return new SourcePosition(start.Line - 1, start.Column).AnchoredAt(anchorOffset);
            }
        }

        return null;
    }

    // whether a token is where a statement begins: first on its line, after a statement's separator, or after the number of its line (10 Loop, -10 Loop), and the
    // end of the module wherever it is. A block's end written after an expression (b = 1 Loop) ends no block: the error is that statement's.
    private static bool BeginsAStatement(ITokenStream? stream, IToken token)
    {
        if (token.Type == TokenConstants.Eof || stream is null)
        {
            return true;
        }

        var before = TokenBefore(stream, token.TokenIndex);
        if (before?.Type is VBALexer.HEXLITERAL or VBALexer.OCTLITERAL or VBALexer.FLOATLITERAL or VBALexer.INTEGERLITERAL)
        {
            var sign = TokenBefore(stream, before.TokenIndex);
            var line = sign?.Type == VBALexer.MINUS ? TokenBefore(stream, sign.TokenIndex) : sign;
            return line is null || line.Type == VBALexer.NEWLINE;
        }

        return before is null || before.Type is VBALexer.NEWLINE or VBALexer.COLON;
    }

    // the token before the one at an index, past the blanks; null at the start of the module.
    private static IToken? TokenBefore(ITokenStream stream, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            if (stream.Get(i) is { Type: not (VBALexer.WS or VBALexer.LINE_CONTINUATION) } token)
            {
                return token;
            }
        }

        return null;
    }

    // whether the parse is in a Select Case and in the block of none of its Cases: there it reads a Case or the End Select and no statement, so where it fails
    // at any other token (Select Case a, then b = 1), the Select is not closed.
    private static bool InASelectOutsideItsCases(ParserRuleContext? context)
    {
        for (; context is not null; context = context.Parent as ParserRuleContext)
        {
            switch (context)
            {
                case VBAParser.SelectCaseStmtContext:
                    return true;
                case VBAParser.BlockStmtContext or VBAParser.ModuleDeclarationsElementContext:
                    return false;
            }
        }

        return false;
    }

    // the syntax errors the parse reported, in the order of their positions, and where they are.
    private readonly List<VBSyntaxErrorInfo> _reported = [];
    private readonly HashSet<SourceLocation> _reportedAt = [];

    // the token the parse failed at for the last error it reported.
    private IToken? _lastReportedAt;

    // whether the parse fails at a token of the statement it last reported an error in: one after the token of that error, with no end of a statement from
    // that token on - an error at the end of a statement is the last of its statement.
    private bool InTheStatementOfTheLastReported(IRecognizer recognizer, IToken? offendingSymbol)
    {
        if (_lastReportedAt is not { TokenIndex: >= 0 } last || offendingSymbol is not { TokenIndex: >= 0 } offending || offending.TokenIndex <= last.TokenIndex
            || recognizer is not Parser { InputStream: ITokenStream tokens } || _reported.Count == 0)
        {
            return false;
        }

        for (var index = last.TokenIndex; index < offending.TokenIndex; index++)
        {
            if (StatementBoundErrorStrategy.EndsAStatement(tokens.Get(index).Type))
            {
                return false;
            }
        }

        return true;
    }

    // the last error the parse reported is no longer one. It is near the end of the module's errors, which are searched from there.
    private void ForgetTheLastReported()
    {
        var error = _reported[^1];
        _reported.RemoveAt(_reported.Count - 1);
        _reportedAt.Remove(error.Location);
        _errors.RemoveAt(_errors.FindLastIndex(existing => ReferenceEquals(existing, error)));
    }

    // a token's text as ANTLR quotes it in an error (DefaultErrorStrategy.EscapeWSAndQuote, which is not public): its line breaks and tabs escaped.
    private static string EscapeWSAndQuote(string text)
        => $"'{text.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t")}'";

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
        _reported.RemoveAll(error => error.Location.Range.Start.CompareTo(first) >= 0);
        _reportedAt.RemoveWhere(location => location.Range.Start.CompareTo(first) >= 0);
        _errors.AddRange(blockErrors);
    }

    // reports what is wrong at a token, in place of what the grammar said there: its error at the token, if it has one, is the consequence.
    public void ReplaceGrammarErrorAt(IToken token, VBCompileErrorId id, string verbose)
    {
        var start = new SourcePosition(token.Line - 1, token.Column).AnchoredAt(anchorOffset);
        var end = new SourcePosition(token.Line - 1, token.Column + token.Text.Length).AnchoredAt(anchorOffset);
        _errors.RemoveAll(error => error.ErrorId == (int)VBCompileErrorId.SyntaxError && error.Location.Range.Start.Equals(start));
        _reported.RemoveAll(error => error.Location.Range.Start.Equals(start));
        _reportedAt.RemoveWhere(location => location.Range.Start.Equals(start));
        Report(new SourceLocation(_uri, new(start, end)), id, verbose);
    }

    // whether the grammar refused something of the module. VBC00061 (OutputItemsNotSeparated) is one too: the RD-VBA grammar reads no item written right after
    // another, and a module that has one does not run (RD-VBAL 2.6.1).
    public bool HasGrammarErrors => _errors.Any(error => error.ErrorId is (int)VBCompileErrorId.SyntaxError or (int)VBCompileErrorId.OutputItemsNotSeparated);

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
