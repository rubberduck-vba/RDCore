using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using RDCore.Parsing;
using RDCore.Parsing.Syntax;
using RDCore.SDK;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace RDCore.Parsing.AST;

/// <summary>
/// A <em>listener</em> that builds the AST nodes representing all the directives and declarations in a module.
/// </summary>
/// <param name="moduleNode">The root AST module node.</param>
/// <param name="errors">Collects the token-semantic syntax errors this pass raises (e.g. literal overflow).</param>
internal class DeclarationsParseTreeListener(Uri sourceUri, ModuleNode moduleNode, ErrorListener errors) : VBAParserBaseListener, ISyntaxNodeProvider
{
    private readonly Uri _rootUri = sourceUri;
    private readonly ModuleNode _root = moduleNode;
    private readonly ErrorListener _errors = errors;
    private readonly Stack<DeclarationNodeBuilder> _builderStack = new([new(sourceUri, moduleNode.Identity)]);
    private DeclarationNodeBuilder CurrentBuilder => _builderStack.Peek();

    private SyntaxNodeId GetCurrentNodeId() => CurrentBuilder.AllocateChildId();

    public ImmutableArray<SyntaxNode> SyntaxNodes => [BuildModuleNode()];

    // pathological nesting recurses through the expression rule to an uncatchable stack overflow;
    // this turns it into a catchable exception the boundary net degrades to a located failure.
    public override void EnterEveryRule([NotNull] ParserRuleContext context)
        => RuntimeHelpers.EnsureSufficientExecutionStack();

    public ModuleNode BuildModuleNode()
    {
        // ANTLR error recovery can leave scopes open (an Enter with no matching Exit); unwind to the
        // module builder so a module with syntax errors still yields whatever declarations parsed.
        while (_builderStack.Count > 1)
        {
            _builderStack.Pop();
        }
        return _root with { Children = [.. CurrentBuilder.GetChildren] };
    }

    private void OnEnterParent()
    {
        _builderStack.Push(new(_rootUri, GetCurrentNodeId()));
    }

    private void OnExitParent(Func<DeclarationNodeBuilder, SyntaxNode> provider)
    {
        // error recovery can fire an Exit with no matching Enter — never pop the module builder.
        if (_builderStack.Count <= 1)
        {
            return;
        }
        var node = provider.Invoke(_builderStack.Pop());
        CurrentBuilder.AddChild(node);
    }

    private bool _isInsideProcedure = false;
    private bool _isAfterArgsList = false;
    // `booleanExpression` is only ever an `If`/`ElseIf` condition (MS-VBAL §5.4.2.8) — narrow enough
    // to opt back into expression capture inside a procedure body without the general statement-body
    // pass this flag is waiting on (see EnterArgList's remarks).
    private int _isCapturingConditionExpression = 0;
    // `While`/`Do` (top form)/`With`'s own expression is a bare `expression` with no wrapper rule
    // like `booleanExpression` to hook — capture stays enabled from the construct's own Enter until
    // the very next `block` starts, which can only ever be that construct's own body (the expression
    // can't itself contain a block-bearing construct). EnterBlock only ever decrements what one of
    // these constructs incremented; a procedure body's own `block` never touches this counter.
    private int _isCapturingLoopHeaderExpression = 0;
    private bool IsDeclarationPassExpression => !_isInsideProcedure || !_isAfterArgsList
        || _isCapturingConditionExpression > 0 || _isCapturingLoopHeaderExpression > 0;

    public override void EnterBooleanExpression([NotNull] VBAParser.BooleanExpressionContext context)
        => _isCapturingConditionExpression++;
    public override void ExitBooleanExpression([NotNull] VBAParser.BooleanExpressionContext context)
        => _isCapturingConditionExpression--;

    public override void EnterBlock([NotNull] VBAParser.BlockContext context)
    {
        if (_isCapturingLoopHeaderExpression > 0)
        {
            _isCapturingLoopHeaderExpression--;
        }
    }

    private void OnModuleOptionDirective(SourceLocation location, ModuleOptions value) 
        => CurrentBuilder.AddChild(new ModuleOptionDirectiveNode(GetCurrentNodeId(), location, value));
    private void OnTypeDefDirective(string token, SourceLocation location, IEnumerable<(char from, char? to)> mappings, DefTypeUniversalPrefixMapping? universalMapping = default)
    {
        var prefixMappings = (universalMapping is null ? [] : new[] { universalMapping }).Concat(
            mappings.Select(map => new DefTypePrefixMapping(map.from, map.to))).ToImmutableArray();
        CurrentBuilder.AddChild(new TypeDefDirectiveNode(GetCurrentNodeId(), location, token, prefixMappings));
    }

    public override void EnterAttributeStmt([NotNull] VBAParser.AttributeStmtContext context)
        => OnEnterParent();
    public override void ExitAttributeStmt([NotNull] VBAParser.AttributeStmtContext context)
        => OnExitParent(builder => builder.BuildAttributeDirective(context));
    public override void ExitOptionBaseStmt([NotNull] VBAParser.OptionBaseStmtContext context)
    {
        var location = context.GetSourceLocation(_rootUri);
        // Option Base only accepts a bare 0 or 1; anything else the grammar's numberLiteral admits
        // (hex/oct/float, a suffix, an out-of-range value) is not base 1 and must not throw here.
        var isBase1 = int.TryParse(context.numberLiteral()?.GetText(), out var value) && value == 1;
        OnModuleOptionDirective(location, isBase1 ? ModuleOptions.OptionBase1 : ModuleOptions.OptionBase0);
    }
    public override void ExitOptionCompareStmt([NotNull] VBAParser.OptionCompareStmtContext context)
    {
        var location = context.GetSourceLocation(_rootUri);
        var value = context.TEXT() is not null ? ModuleOptions.OptionCompareText
                : context.DATABASE() is not null ? ModuleOptions.OptionCompareDatabase
                : ModuleOptions.OptionCompareBinary;

        OnModuleOptionDirective(location, value);
    }
    public override void ExitOptionExplicitStmt([NotNull] VBAParser.OptionExplicitStmtContext context)
    {
        var location = context.GetSourceLocation(_rootUri);
        OnModuleOptionDirective(location, ModuleOptions.OptionExplicit);
    }
    public override void ExitOptionPrivateModuleStmt([NotNull] VBAParser.OptionPrivateModuleStmtContext context)
    {
        var location = context.GetSourceLocation(_rootUri);
        OnModuleOptionDirective(location, ModuleOptions.OptionPrivateModule);
    }
    public override void ExitDefDirective([NotNull] VBAParser.DefDirectiveContext context)
    {
        var token = context.defType().GetText();
        var location = context.GetSourceLocation(_rootUri);

        DefTypeUniversalPrefixMapping? universalMapping = default;
        var mappings = new List<(char, char?)>([]);
        foreach (var spec in context.letterSpec())
        {
            if (spec.universalLetterRange() is not null)
            {
                universalMapping = new();
            }
            else if(spec.letterRange() is VBAParser.LetterRangeContext rangeContext)
            {
                var from = rangeContext.singleLetter()[0].GetText()[0];
                var to = rangeContext.singleLetter()[1].GetText()[0];
                mappings.Add(new(from, to));
            }
            else if (spec.singleLetter() is VBAParser.SingleLetterContext specContext)
            {
                mappings.Add(new(specContext.GetText()[0], null));
            }
        }
        OnTypeDefDirective(token, location, mappings, universalMapping);
    }

    public override void EnterImplementsStmt([NotNull] VBAParser.ImplementsStmtContext context) 
        => OnEnterParent();
    public override void ExitImplementsStmt([NotNull] VBAParser.ImplementsStmtContext context)
        => OnExitParent(builder => builder.BuildImplementsDirective(context));


    public override void EnterDeclareStmt([NotNull] VBAParser.DeclareStmtContext context)
        => OnEnterParent();
    public override void ExitDeclareStmt([NotNull] VBAParser.DeclareStmtContext context)
        => OnExitParent(builder => builder.BuildExternalDeclaration(context));

    public override void EnterEventStmt([NotNull] VBAParser.EventStmtContext context)
        => OnEnterParent();
    public override void ExitEventStmt([NotNull] VBAParser.EventStmtContext context)
        => OnExitParent(builder => builder.BuildEventDeclaration(context));

    public override void EnterUdtDeclaration([NotNull] VBAParser.UdtDeclarationContext context)
        => OnEnterParent();
    public override void ExitUdtDeclaration([NotNull] VBAParser.UdtDeclarationContext context)
        => OnExitParent(builder => builder.BuildUserDefinedTypeDeclaration(context));

    public override void EnterUdtMember([NotNull] VBAParser.UdtMemberContext context)
        => OnEnterParent();
    public override void ExitUdtMember([NotNull] VBAParser.UdtMemberContext context)
        => OnExitParent(builder => builder.BuildUserDefinedTypeMember(context));

    public override void EnterEnumerationStmt([NotNull] VBAParser.EnumerationStmtContext context)
        => OnEnterParent();
    public override void ExitEnumerationStmt([NotNull] VBAParser.EnumerationStmtContext context)
        => OnExitParent(builder => builder.BuildEnumDeclaration(context));

    public override void EnterEnumerationStmt_Constant([NotNull] VBAParser.EnumerationStmt_ConstantContext context)
        => OnEnterParent();

    public override void ExitEnumerationStmt_Constant([NotNull] VBAParser.EnumerationStmt_ConstantContext context)
        => OnExitParent(builder => builder.BuildEnumConstDeclaration(context));

    public override void EnterVariableSubStmt([NotNull] VBAParser.VariableSubStmtContext context)
        => OnEnterParent();
    public override void ExitVariableSubStmt([NotNull] VBAParser.VariableSubStmtContext context)
    {
        // recovery can leave Parent.Parent not pointing at the VariableStmt that carries the
        // visibility / Static token (`variableStmt : (DIM | STATIC | visibility) ...`).
        var parent = context.Parent?.Parent as VBAParser.VariableStmtContext;
        var modifier = NodeBuilder.ParseAccessModifier(parent?.visibility()?.GetText());
        var isStatic = parent?.STATIC() is not null;
        OnExitParent(builder => builder.BuildVariableDeclaration(context, modifier, isStatic));
    }

    public override void EnterConstSubStmt([NotNull] VBAParser.ConstSubStmtContext context)
        => OnEnterParent();
    public override void ExitConstSubStmt([NotNull] VBAParser.ConstSubStmtContext context)
    {
        var parent = context.Parent as VBAParser.ConstStmtContext;
        var modifier = NodeBuilder.ParseAccessModifier(parent?.visibility()?.GetText());
        OnExitParent(builder => builder.BuildConstDeclaration(context, _isInsideProcedure ? ConstKind.Local : ConstKind.ModuleMember, modifier));
    }

    // one node per `ReDim` target, built wherever the statement parses — no position guard, like
    // every other declaration listener: the AST has to round-trip, and a `ReDim` outside a
    // procedure body is a downstream compile error ("Only comments may appear after End Sub…"), not
    // a syntax error and not a reason to drop the node. (Today the grammar can't recover a stray
    // statement between members, so the context is only reached inside a procedure body. Nested
    // inside If/ElseIf/Else/While/Do/For/ForEach/Select Case it parents to that branch's own Body —
    // SymbolBuilder.BuildLocals walks the whole body, not just the member's immediate children, to
    // still find it.)
    public override void EnterRedimVariableDeclaration([NotNull] VBAParser.RedimVariableDeclarationContext context)
        => OnEnterParent();
    public override void ExitRedimVariableDeclaration([NotNull] VBAParser.RedimVariableDeclarationContext context)
    {
        // recovery can leave Parent.Parent not pointing at the redimStmt that carries `Preserve`.
        var isPreserve = (context.Parent?.Parent as VBAParser.RedimStmtContext)?.PRESERVE() is not null;
        OnExitParent(builder => builder.BuildRedimDeclaration(context, isPreserve));
    }

    // `If`/`ElseIf`/`Else` (MS-VBAL §5.4.2.8) — each branch's own scope collects its condition (when
    // it has one) followed by whatever the branch body captures today (declarations only; the general
    // statement-body pass is still to come). A branch missing its condition (recovery) builds nothing,
    // matching ExitAsTypeClause's "don't build a broken node" precedent.
    public override void EnterIfStmt([NotNull] VBAParser.IfStmtContext context)
        => OnEnterParent();
    public override void ExitIfStmt([NotNull] VBAParser.IfStmtContext context)
        => OnExitParentIfBuilt(builder => builder.BuildIfBlock(context));

    public override void EnterElseIfBlock([NotNull] VBAParser.ElseIfBlockContext context)
        => OnEnterParent();
    public override void ExitElseIfBlock([NotNull] VBAParser.ElseIfBlockContext context)
        => OnExitParentIfBuilt(builder => builder.BuildElseIfBlock(context));

    public override void EnterElseBlock([NotNull] VBAParser.ElseBlockContext context)
        => OnEnterParent();
    public override void ExitElseBlock([NotNull] VBAParser.ElseBlockContext context)
        => OnExitParent(builder => builder.BuildElseBlock(context));

    // Single-line `If` (MS-VBAL §5.4.2.9) — same push/pop-a-scope technique as the block form above,
    // so the condition and Then/Else bodies build the exact same way with no extra capture logic.
    // `LabelGoTo` is read directly from `context` here (not via an Enter/Exit hook on `listOrLabel`
    // itself) because an Enter callback fires before the rule's own children exist yet — same reason
    // ExitStatementLabelDefinition reads its `lineNumberLabel` straight off the context below.
    public override void EnterIfWithNonEmptyThen([NotNull] VBAParser.IfWithNonEmptyThenContext context)
        => OnEnterParent();
    public override void ExitIfWithNonEmptyThen([NotNull] VBAParser.IfWithNonEmptyThenContext context)
    {
        var labelGoTo = LabelGoTo(context.listOrLabel()?.lineNumberLabel());
        OnExitParentIfBuilt(builder => builder.BuildInlineIfStatement(context, labelGoTo));
    }

    public override void EnterIfWithEmptyThen([NotNull] VBAParser.IfWithEmptyThenContext context)
        => OnEnterParent();
    public override void ExitIfWithEmptyThen([NotNull] VBAParser.IfWithEmptyThenContext context)
        => OnExitParentIfBuilt(builder => builder.BuildInlineIfStatement(context, null));

    public override void EnterSingleLineElseClause([NotNull] VBAParser.SingleLineElseClauseContext context)
        => OnEnterParent();
    public override void ExitSingleLineElseClause([NotNull] VBAParser.SingleLineElseClauseContext context)
    {
        var labelGoTo = LabelGoTo(context.listOrLabel()?.lineNumberLabel());
        OnExitParent(builder => builder.BuildInlineElseBlock(context, labelGoTo));
    }

    // `If x Then 100` / its Else-branch equivalent: MS-VBAL specifies a bare line-number target as
    // equivalent to a GoTo statement targeting that line — synthesized directly, since `lineNumberLabel`
    // is a bare signed number, never reached through the normal `expression` chain.
    //
    // Known, deliberately-unfixed residual (adversarial review PRs #208-224, "worth knowing"): the id
    // this mints is allocated HERE, after any real `sameLineStatement`s that precede it in source have
    // already consumed their own slots — but the resulting GoToStatementNode is then *prepended* to the
    // body ahead of them (see BuildInlineIfStatement/BuildInlineElseBlock). Its id therefore sorts AFTER
    // a statement it textually precedes. Ids stay globally unique either way (verified in
    // SyntaxNodeIdUniquenessTests), so this is an ordering quirk, not a correctness bug — but fixing it
    // properly needs the label's slot reserved at EnterListOrLabel time, before any sameLineStatement
    // can claim one, and single-line If can nest inside its own Then-branch (`If a Then If b Then y`),
    // so a single reserved-id field would get clobbered by the inner statement's own reservation before
    // the outer one consumes it — a stack, keyed to the same Enter/Exit pair as the reservation, is the
    // right fix and wasn't judged worth the added state for how rare "a bare line-number label followed
    // by more colon-separated statements" is in practice.
    private GoToStatementNode? LabelGoTo(VBAParser.LineNumberLabelContext? context)
    {
        if (context is null)
        {
            return null;
        }

        var location = context.GetSourceLocation(_rootUri);
        var (value, overflow) = NumericLiteral.Resolve(context.numberLiteral()?.GetText() ?? string.Empty);
        if (overflow)
        {
            _errors.Report(location, VBCompileErrorId.NumericLiteralOverflow, Exceptions.VBCompileError_NumericLiteralOverflow_Verbose);
        }
        if (context.MINUS() is not null && NumericLiteral.Negate(value) is { } negated)
        {
            value = negated;
        }

        var id = GetCurrentNodeId();
        return new GoToStatementNode(id, location, new LiteralExpressionNode(id.Add(0), location, value));
    }

    // `While...Wend` (MS-VBAL 5.4.2.18). The condition has no wrapper rule, so
    // _isCapturingLoopHeaderExpression (cleared by the loop's own EnterBlock) opts it into capture.
    public override void EnterWhileWendStmt([NotNull] VBAParser.WhileWendStmtContext context)
    {
        OnEnterParent();
        _isCapturingLoopHeaderExpression++;
    }
    public override void ExitWhileWendStmt([NotNull] VBAParser.WhileWendStmtContext context)
    {
        OnExitParentIfBuilt(builder => builder.BuildWhileWendStatement(context));
        // recovery can reach Exit without the body's Block ever starting (and so never consuming the
        // increment above) — clear defensively rather than let a stale "capturing" state leak forward.
        _isCapturingLoopHeaderExpression = 0;
    }

    // `With...End With` (MS-VBAL 5.4.2.19) — same shape and same capture trick as While: a bare
    // expression always precedes the body's block, with no ambiguity to resolve at Exit.
    public override void EnterWithStmt([NotNull] VBAParser.WithStmtContext context)
    {
        OnEnterParent();
        _isCapturingLoopHeaderExpression++;
    }
    public override void ExitWithStmt([NotNull] VBAParser.WithStmtContext context)
    {
        OnExitParentIfBuilt(builder => builder.BuildWithStatement(context));
        _isCapturingLoopHeaderExpression = 0;
    }

    // `Do...Loop` (MS-VBAL 5.4.2.5-7): one grammar rule, three unlabeled alternatives (no condition;
    // condition before the body; condition after it) dispatched here into 5 node types. Which
    // alternative matched isn't knowable at Enter (nothing has been parsed yet), and the trailing form
    // puts its condition *after* the body's own block — so the "capture until the next EnterBlock"
    // trick doesn't fit. Instead the condition, if any, is captured once everything is known, at Exit.
    public override void EnterDoLoopStmt([NotNull] VBAParser.DoLoopStmtContext context)
        => OnEnterParent();
    public override void ExitDoLoopStmt([NotNull] VBAParser.DoLoopStmtContext context)
        => OnExitParentIfBuilt(builder => builder.BuildDoLoopStatement(context, CaptureIsolatedExpression(context.expression())));

    // `For...Next` (MS-VBAL 5.4.2.9). The control-variable assignment `i = 1` is parsed as ONE
    // `expression` — the grammar's own comment explains why ("expression EQ expression refactored to
    // expression to allow SLL") — so it arrives as a top-level `=` VBBinaryOperatorExpressionNode that
    // BuildForStatement splits into Control (Left) / Start (Right). The body uses `unterminatedBlock`,
    // not `block`, and can be entirely absent (an empty-bodied loop) — a live capture window bounded
    // by its Enter isn't reliable there, so CaptureIsolatedExpression handles all four expressions.
    public override void EnterForNextStmt([NotNull] VBAParser.ForNextStmtContext context)
        => OnEnterParent();
    public override void ExitForNextStmt([NotNull] VBAParser.ForNextStmtContext context)
    {
        var assignment = CaptureIsolatedExpression(context.expression(0));
        var end = CaptureIsolatedExpression(context.expression(1));
        var step = CaptureIsolatedExpression(context.stepStmt()?.expression());
        OnExitParentIfBuilt(builder => builder.BuildForStatement(context, assignment, end, step));
    }

    // `For Each...Next` (MS-VBAL 5.4.2.9). Same body/capture shape as For; no assignment to split —
    // the control variable and the collection are two independent expressions.
    public override void EnterForEachStmt([NotNull] VBAParser.ForEachStmtContext context)
        => OnEnterParent();
    public override void ExitForEachStmt([NotNull] VBAParser.ForEachStmtContext context)
    {
        var control = CaptureIsolatedExpression(context.expression(0));
        var collection = CaptureIsolatedExpression(context.expression(1));
        OnExitParentIfBuilt(builder => builder.BuildForEachStatement(context, control, collection));
    }

    // `Select Case` (MS-VBAL 5.4.2.10). Every expression here — the control expression, and each
    // Case line's range clause(s) — uses CaptureIsolatedExpression uniformly: a Case line can carry
    // several comma-separated range clauses with no single reliable boundary rule between them, so
    // the live-window trick doesn't fit any better here than it did for Do/For.
    public override void EnterSelectCaseStmt([NotNull] VBAParser.SelectCaseStmtContext context)
        => OnEnterParent();
    public override void ExitSelectCaseStmt([NotNull] VBAParser.SelectCaseStmtContext context)
        => OnExitParentIfBuilt(builder => builder.BuildSelectCaseStatement(context, CaptureIsolatedExpression(context.selectExpression()?.expression())));

    public override void EnterCaseClause([NotNull] VBAParser.CaseClauseContext context)
        => OnEnterParent();
    public override void ExitCaseClause([NotNull] VBAParser.CaseClauseContext context)
    {
        var rangeClauses = context.rangeClause()
            .Select(CaptureRangeClause)
            .Where(clause => clause is not null)
            .Select(clause => clause!)
            .ToImmutableArray();
        OnExitParentIfBuilt(builder => builder.BuildCaseExpression(context, rangeClauses));
    }

    public override void EnterCaseElseClause([NotNull] VBAParser.CaseElseClauseContext context)
        => OnEnterParent();
    public override void ExitCaseElseClause([NotNull] VBAParser.CaseElseClauseContext context)
        => OnExitParent(builder => builder.BuildCaseElseClause(context));

    // A `rangeClause` is one of three independent shapes (MS-VBAL 5.4.2.10): a `To` range, a
    // comparison, or a plain value. `_children`-position-based IDs don't apply here (these clauses are
    // never added to a builder's children — they're returned, since a Case line can have several), so
    // each clause gets its own id nested under the case clause's own current slot.
    private CaseRangeClauseNode? CaptureRangeClause(VBAParser.RangeClauseContext context, int index)
    {
        var id = GetCurrentNodeId().Add(index);
        var location = context.GetSourceLocation(_rootUri);

        if (context.selectStartValue() is { } startValue && context.selectEndValue() is { } endValue)
        {
            var start = CaptureIsolatedExpression(startValue.expression());
            var end = CaptureIsolatedExpression(endValue.expression());
            return start is null || end is null ? null : new CaseToRangeClauseNode(id, location, start, end);
        }
        if (context.comparisonOperator() is { } comparison)
        {
            var value = CaptureIsolatedExpression(context.expression());
            return value is null ? null : new CaseComparisonRangeClauseNode(id, location, ToComparisonToken(comparison), value);
        }

        var plainValue = CaptureIsolatedExpression(context.expression());
        return plainValue is null ? null : new CaseValueRangeClauseNode(id, location, plainValue);
    }

    private static string ToComparisonToken(VBAParser.ComparisonOperatorContext context)
        => context.EQ() is not null ? Tokens.CompareEqualOp
            : context.NEQ() is not null ? Tokens.CompareNotEqualOp
            : context.GT() is not null ? Tokens.CompareGreaterThanOp
            : context.GEQ() is not null ? Tokens.CompareGreaterThanOrEqualOp
            : context.LT() is not null ? Tokens.CompareLessThanOp
            : context.LEQ() is not null ? Tokens.CompareLessThanOrEqualOp
            : context.IS() is not null ? Tokens.CompareIsOp
            : context.LIKE() is not null ? Tokens.CompareLikeOp
            : context.GetText();

    // Fixed-keyword statements with a positional argument list (MS-VBAL §5.4.5 File Statements, plus
    // Erase/Name/RaiseEvent) — none of these have a body, so unlike every other construct in this
    // listener there's no scope to push: the node is built directly and added to whatever's currently
    // the active builder (OnExpression), exactly like a leaf expression would be. A capture that comes
    // back null (an optional argument genuinely absent, or a deep recovery failure) is simply omitted
    // from Inputs rather than aborting the whole statement — these arguments don't gate one another.
    public override void ExitEraseStmt([NotNull] VBAParser.EraseStmtContext context)
        => OnKeywordStatement(Tokens.Erase, context, [.. context.expression().Select(CaptureIsolatedExpression)]);

    public override void ExitNameStmt([NotNull] VBAParser.NameStmtContext context)
        => OnKeywordStatement(Tokens.Name, context, CaptureIsolatedExpression(context.expression(0)), CaptureIsolatedExpression(context.expression(1)));

    // the event name is a bare identifier, not an expression — synthesized directly as a
    // SimpleNameExpressionNode rather than routed through CaptureIsolatedExpression. Same double-
    // invocation quirk as ExitOnErrorStmt: a bare `RaiseEvent` first calls Exit with
    // context.exception set (identifier() null, harmless), then again after recovery with exception
    // cleared and identifier() non-null but an EMPTY, zero-width rule match (Start.TokenIndex >
    // Stop.TokenIndex) standing in for the name the source never had — GetText() reliably reports ""
    // for that synthesized span, unlike a real (however short) identifier.
    public override void ExitRaiseEventStmt([NotNull] VBAParser.RaiseEventStmtContext context)
    {
        if (context.exception is not null)
        {
            return;
        }
        if (context.identifier() is not { } identifier || identifier.GetText().Length == 0)
        {
            // a bare `RaiseEvent` with no name (recovery) — the grammar's own event name is mandatory.
            CurrentBuilder.AddChild(BuildUnbuiltStatementTrivia(context));
            return;
        }
        var id = GetCurrentNodeId();
        var eventName = new SimpleNameExpressionNode(id.Add(0), identifier.GetSourceLocation(_rootUri), identifier.Name());
        var arguments = context.eventArgumentList()?.eventArgument().Select(argument => CaptureIsolatedExpression(argument.expression())) ?? [];
        var inputs = new ExpressionNode?[] { eventName }.Concat(arguments).Where(input => input is not null).Cast<SyntaxNode>().ToImmutableArray();
        CurrentBuilder.AddChild(new KeywordStatementNode(id, context.GetSourceLocation(_rootUri), Tokens.RaiseEvent, inputs));
    }

    public override void ExitCloseStmt([NotNull] VBAParser.CloseStmtContext context)
        => OnKeywordStatement(Tokens.Close, context, CaptureFileNumbers(context.fileNumberList()));

    public override void ExitResetStmt([NotNull] VBAParser.ResetStmtContext context)
        => OnKeywordStatement(Tokens.Reset, context);

    // End (not a MS-VBAL-numbered statement), Stop (§5.4.2.11), and Exit (§5.4.2.5/.7/.17-19) are all
    // keyword-only, zero-argument statements — same KeywordStatementNode shape as Reset, no dedicated
    // node type needed.
    public override void ExitEndStmt([NotNull] VBAParser.EndStmtContext context)
        => OnKeywordStatement(Tokens.End, context);

    public override void ExitStopStmt([NotNull] VBAParser.StopStmtContext context)
        => OnKeywordStatement(Tokens.Stop, context);

    public override void ExitExitStmt([NotNull] VBAParser.ExitStmtContext context)
    {
        var token = context.EXIT_DO() is not null ? Tokens.ExitDo
            : context.EXIT_FOR() is not null ? Tokens.ExitFor
            : context.EXIT_FUNCTION() is not null ? Tokens.ExitFunction
            : context.EXIT_PROPERTY() is not null ? Tokens.ExitProperty
            : context.EXIT_SUB() is not null ? Tokens.ExitSub
            : context.GetText();
        OnKeywordStatement(token, context);
    }

    // GoTo/GoSub/Return (MS-VBAL §5.4.2.12/.14/.15) — simple, unconditional branching statements.
    public override void ExitGoToStmt([NotNull] VBAParser.GoToStmtContext context)
    {
        if (CaptureIsolatedExpression(context.expression()) is not { } label)
        {
            return;
        }
        CurrentBuilder.AddChild(new GoToStatementNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), label));
    }

    public override void ExitGoSubStmt([NotNull] VBAParser.GoSubStmtContext context)
    {
        if (CaptureIsolatedExpression(context.expression()) is not { } label)
        {
            return;
        }
        CurrentBuilder.AddChild(new GoSubStatementNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), label));
    }

    public override void ExitReturnStmt([NotNull] VBAParser.ReturnStmtContext context)
        => CurrentBuilder.AddChild(new ReturnStatementNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri)));

    // `On Error GoTo <label>` and `On Error Resume Next` (MS-VBAL §5.4.4.1) are the same grammar
    // rule's two alternatives — `GOTO()` is non-null only for the former. A source missing `Next`
    // (e.g. bare `On Error Resume`) makes ANTLR invoke this Exit callback TWICE: once with the real
    // InputMismatchException on `context.exception` (harmless - the first guard below returns), then
    // again after recovery, with `exception` cleared and a synthesized `<missing NEXT>` token standing
    // in for the one the source never had. That second call is the trap: `RESUME()`/`NEXT()` are both
    // non-null, so a plain null-check doesn't catch it — only `Symbol.TokenIndex` does, since a
    // synthesized token is never actually lexed from the input (real tokens are always >= 0). Without
    // both guards, that shape fabricated a full OnErrorResumeStatementNode the source never said,
    // alongside the real syntax error — confirmed via `On Local Error Resume` (no `Next`).
    public override void ExitOnErrorStmt([NotNull] VBAParser.OnErrorStmtContext context)
    {
        if (context.exception is not null)
        {
            return;
        }
        if (context.GOTO() is not null)
        {
            if (CaptureIsolatedExpression(context.expression()) is not { } label)
            {
                CurrentBuilder.AddChild(BuildUnbuiltStatementTrivia(context));
                return;
            }
            CurrentBuilder.AddChild(new OnErrorGoToStatementNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), label));
            return;
        }
        if (context.RESUME() is null || context.NEXT() is not { Symbol.TokenIndex: >= 0 })
        {
            CurrentBuilder.AddChild(BuildUnbuiltStatementTrivia(context));
            return;
        }
        CurrentBuilder.AddChild(new OnErrorResumeStatementNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri)));
    }

    // Bare `Resume`, `Resume <label>`, and `Resume Next` (MS-VBAL §5.4.4.2) are the same grammar
    // rule's three shapes — `NEXT()` non-null picks the dedicated ResumeNextStatementNode, otherwise
    // the (possibly absent) label expression rides on the general ResumeStatementNode.
    public override void ExitResumeStmt([NotNull] VBAParser.ResumeStmtContext context)
    {
        if (context.NEXT() is not null)
        {
            CurrentBuilder.AddChild(new ResumeNextStatementNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri)));
            return;
        }
        var label = CaptureIsolatedExpression(context.expression());
        CurrentBuilder.AddChild(new ResumeStatementNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), label));
    }

    public override void ExitErrorStmt([NotNull] VBAParser.ErrorStmtContext context)
    {
        if (CaptureIsolatedExpression(context.expression()) is not { } number)
        {
            return;
        }
        CurrentBuilder.AddChild(new ErrorStatementNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), number));
    }

    // Let (MS-VBAL §5.4.3.8) and Set (§5.4.3.9) share one shape: both are `[keyword] lExpression =
    // expression`, and only their static/runtime coercion semantics differ, not their syntax.
    public override void ExitLetStmt([NotNull] VBAParser.LetStmtContext context)
        => BuildAssignmentStatement(context, context.LET() is not null ? AssignmentKind.ExplicitLet : AssignmentKind.ImplicitLet, context.lExpression(), context.expression());

    public override void ExitSetStmt([NotNull] VBAParser.SetStmtContext context)
        => BuildAssignmentStatement(context, AssignmentKind.Set, context.lExpression(), context.expression());

    // LSet (§5.4.3.6) and RSet (§5.4.3.7) are also `keyword target = expression`, so they reuse the
    // same node — their target is grammatically a plain `expression`, not `lExpression` like Let/Set
    // (MS-VBAL calls it a `bound-variable-expression`; static semantics narrow it later), so
    // BuildAssignmentStatement's target parameter takes the shared base context type both admit.
    public override void ExitLsetStmt([NotNull] VBAParser.LsetStmtContext context)
        => BuildAssignmentStatement(context, AssignmentKind.LSet, context.expression(0), context.expression(1));

    public override void ExitRsetStmt([NotNull] VBAParser.RsetStmtContext context)
        => BuildAssignmentStatement(context, AssignmentKind.RSet, context.expression(0), context.expression(1));

    private void BuildAssignmentStatement(VBABaseParserRuleContext context, AssignmentKind kind, VBABaseParserRuleContext? targetContext, VBABaseParserRuleContext? valueContext)
    {
        if (CaptureIsolatedExpression(targetContext) is not { } target || CaptureIsolatedExpression(valueContext) is not { } value)
        {
            return;
        }
        CurrentBuilder.AddChild(new AssignmentStatementNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), kind, target, value));
    }

    // Mid/MidB/Mid$/MidB$ (§5.4.3.5): `modeSpecifier(target, start[, length]) = value`. The grammar
    // admits only one optional middle expression, so `expression()`'s length alone (2 vs. 3) tells
    // start/length/value apart without needing the comma count too. IsByteMode and IsStringInput are
    // independent flags off the same modeSpecifier - the $ suffix doesn't change the runtime span
    // mechanics §5.4.3.5 itself describes, but it still has to survive into the node (see
    // MidStatementNode's own remarks: it mirrors the Mid/Mid$ function overloads' VBVariant/VBString
    // split for static semantics).
    public override void ExitMidStatement([NotNull] VBAParser.MidStatementContext context)
    {
        var expressions = context.expression();
        var lengthContext = expressions.Length == 3 ? expressions[1] : null;
        if (CaptureIsolatedExpression(context.lExpression()) is not { } target
            || CaptureIsolatedExpression(expressions[0]) is not { } start)
        {
            return;
        }
        var length = CaptureIsolatedExpression(lengthContext);
        if (CaptureIsolatedExpression(expressions[^1]) is not { } value)
        {
            return;
        }
        var modeSpecifier = context.modeSpecifier();
        var isByteMode = modeSpecifier.MIDB() is not null;
        var isStringInput = modeSpecifier.DOLLAR() is not null;
        CurrentBuilder.AddChild(new MidStatementNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), isByteMode, isStringInput, target, start, length, value));
    }

    // `Call`/bare-call (MS-VBAL §5.4.2.1). `Call Foo(1, 2)` carries its arguments inside the callee's
    // own lExpression tree (an IndexExpressionNode) — the statement's own Arguments stays empty. Only
    // the bare form (`Foo 1, 2`, no `Call`, no parens) has a separate statement-level argument list;
    // `Call` grants no such shape (it always requires the parenthesized form).
    public override void ExitCallStmt([NotNull] VBAParser.CallStmtContext context)
    {
        if (CaptureIsolatedExpression(context.lExpression()) is not { } callee)
        {
            return;
        }
        var arguments = CaptureIsolated(context.argumentList()).Cast<ExpressionNode>().ToImmutableArray();
        CurrentBuilder.AddChild(new CallStatementNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), callee, arguments, context.CALL() is not null));
    }

    public override void ExitSeekStmt([NotNull] VBAParser.SeekStmtContext context)
        => OnKeywordStatement(Tokens.Seek, context, CaptureFileNumber(context.fileNumber()), CaptureIsolatedExpression(context.position()?.expression()));

    public override void ExitLockStmt([NotNull] VBAParser.LockStmtContext context)
        => OnKeywordStatement(Tokens.Lock, context, [CaptureFileNumber(context.fileNumber()), .. CaptureRecordRange(context.recordRange())]);

    public override void ExitUnlockStmt([NotNull] VBAParser.UnlockStmtContext context)
        => OnKeywordStatement(Tokens.Unlock, context, [CaptureFileNumber(context.fileNumber()), .. CaptureRecordRange(context.recordRange())]);

    public override void ExitGetStmt([NotNull] VBAParser.GetStmtContext context)
        => OnKeywordStatement(Tokens.Get, context, CaptureFileNumber(context.fileNumber()), CaptureIsolatedExpression(context.recordNumber()?.expression()), CaptureIsolatedExpression(context.variable()?.expression()));

    public override void ExitPutStmt([NotNull] VBAParser.PutStmtContext context)
        => OnKeywordStatement(Tokens.Put, context, CaptureFileNumber(context.fileNumber()), CaptureIsolatedExpression(context.recordNumber()?.expression()), CaptureIsolatedExpression(context.data()?.expression()));

    public override void ExitLineInputStmt([NotNull] VBAParser.LineInputStmtContext context)
        => OnKeywordStatement(Tokens.LineInput, context, CaptureMarkedFileNumber(context.markedFileNumber()), CaptureIsolatedExpression(context.variableName()?.expression()));

    public override void ExitWidthStmt([NotNull] VBAParser.WidthStmtContext context)
        => OnKeywordStatement(Tokens.Width, context, CaptureMarkedFileNumber(context.markedFileNumber()), CaptureIsolatedExpression(context.lineWidth()?.expression()));

    public override void ExitInputStmt([NotNull] VBAParser.InputStmtContext context)
        => OnKeywordStatement(Tokens.Input, context, [CaptureMarkedFileNumber(context.markedFileNumber()), .. CaptureInputList(context.inputList())]);

    // `Print`/`Write` (MS-VBAL §5.4.5.8-9) and the object-relative bare form don't fit
    // KeywordStatementNode's plain expression-list shape (an output list has Spc/Tab clauses and
    // ;/, separators with real formatting semantics — deliberately not resolved here, only captured).
    // These are leaf statements like every other KeywordStatement, so nothing lands in CurrentBuilder
    // ambiently — CaptureIsolated re-walks the file number and the whole output list explicitly.
    public override void ExitPrintStmt([NotNull] VBAParser.PrintStmtContext context)
        => BuildPrintStatement(context, Tokens.Print, context.markedFileNumber()?.expression(), context.outputList());

    public override void ExitWriteStmt([NotNull] VBAParser.WriteStmtContext context)
        => BuildPrintStatement(context, Tokens.Write, context.markedFileNumber()?.expression(), context.outputList());

    public override void ExitUnqualifiedObjectPrintStmt([NotNull] VBAParser.UnqualifiedObjectPrintStmtContext context)
        => BuildPrintStatement(context, Tokens.Print, null, context.outputList());

    private void BuildPrintStatement(VBABaseParserRuleContext context, string token, VBAParser.ExpressionContext? fileNumberExpression, VBAParser.OutputListContext? outputList)
    {
        var fileNumber = CaptureIsolatedExpression(fileNumberExpression);
        var items = CaptureIsolated(outputList).Cast<PrintOutputItemNode>().ToImmutableArray();
        CurrentBuilder.AddChild(new PrintStatementNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), token, fileNumber, items));
    }

    // `Open` (MS-VBAL §5.4.5.1) needs its own shape — Mode/Access/Lock are keyword choices, not
    // expressions, so it can't ride KeywordStatementNode like the rest of the file statements.
    public override void ExitOpenStmt([NotNull] VBAParser.OpenStmtContext context)
    {
        if (CaptureIsolatedExpression(context.pathName()?.expression()) is not { } pathName
            || CaptureFileNumber(context.fileNumber()) is not { } fileNumber)
        {
            // both required; recovery left one of them unresolved — don't build a broken node.
            return;
        }

        var mode = context.modeClause()?.fileMode() switch
        {
            null => (VBFileMode?)null,
            { } m when m.APPEND() is not null => VBFileMode.Append,
            { } m when m.BINARY() is not null => VBFileMode.Binary,
            { } m when m.INPUT() is not null => VBFileMode.Input,
            { } m when m.OUTPUT() is not null => VBFileMode.Output,
            _ => VBFileMode.Random,
        };
        var access = context.accessClause()?.access() switch
        {
            null => (VBFileAccessMode?)null,
            { } a when a.READ() is not null => VBFileAccessMode.Read,
            { } a when a.WRITE() is not null => VBFileAccessMode.Write,
            _ => VBFileAccessMode.ReadWrite,
        };
        var @lock = context.@lock() switch
        {
            null => (VBFileLockMode?)null,
            { } l when l.SHARED() is not null => VBFileLockMode.Shared,
            { } l when l.LOCK_READ() is not null => VBFileLockMode.Read,
            { } l when l.LOCK_WRITE() is not null => VBFileLockMode.Write,
            _ => VBFileLockMode.ReadWrite,
        };
        var recordLength = CaptureIsolatedExpression(context.lenClause()?.recLength()?.expression());

        CurrentBuilder.AddChild(new OpenStatementNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), pathName, mode, access, @lock, fileNumber, recordLength));
    }

    private void OnKeywordStatement(string token, VBABaseParserRuleContext context, params ExpressionNode?[] inputs)
    {
        var resolvedInputs = inputs.Where(input => input is not null).Cast<SyntaxNode>().ToImmutableArray();
        CurrentBuilder.AddChild(new KeywordStatementNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), token, resolvedInputs));
    }

    // `fileNumber` is `#expression | expression` (MS-VBAL 5.4.5.1.1) — the `#` mark is pure syntax,
    // never modeled separately; either shape resolves to the same underlying expression.
    private ExpressionNode? CaptureFileNumber(VBAParser.FileNumberContext? context)
        => CaptureIsolatedExpression(context?.markedFileNumber()?.expression() ?? context?.unmarkedFileNumber()?.expression());

    private ExpressionNode? CaptureMarkedFileNumber(VBAParser.MarkedFileNumberContext? context)
        => CaptureIsolatedExpression(context?.expression());

    private ExpressionNode?[] CaptureFileNumbers(VBAParser.FileNumberListContext? context)
        => context is null ? [] : [.. context.fileNumber().Select(CaptureFileNumber)];

    private ExpressionNode?[] CaptureInputList(VBAParser.InputListContext? context)
        => context is null ? [] : [.. context.inputVariable().Select(v => CaptureIsolatedExpression(v.expression()))];

    // `recordRange` (MS-VBAL Lock/Unlock statements) is either just a start record, or `start To end` —
    // 1 or 2 expressions, never 0 (the whole recordRange is optional at the call site instead).
    private ExpressionNode?[] CaptureRecordRange(VBAParser.RecordRangeContext? context)
    {
        if (context is null)
        {
            return [];
        }

        var start = CaptureIsolatedExpression(context.startRecordNumber()?.expression());
        return context.endRecordNumber() is { } endContext
            ? [start, CaptureIsolatedExpression(endContext.expression())]
            : [start];
    }

    // Re-walks an already-parsed, self-contained subtree in isolation, with capture enabled just for
    // that walk, into its own fresh scope. Safe because this only ever runs from an Exit handler — by
    // then the outer walk has already fully visited (and moved past) this subtree, so this touches a
    // finished, static tree, not one still being built. Existing Exit* operator/lExpression handlers
    // (PopLastChildren-based) don't care whether a matching Enter fired first, so they combine
    // correctly under this nested walk's ordering exactly as they do under the outer one's. Not tied
    // to `expression` specifically — an `lExpression` or `argumentList` subtree walks exactly the same
    // way, so this accepts any rule context (`CallStatementNode`'s callee/arguments need both).
    private ImmutableArray<SyntaxNode> CaptureIsolated(VBABaseParserRuleContext? context)
    {
        if (context is null)
        {
            return [];
        }

        OnEnterParent();
        _isCapturingConditionExpression++;
        ParseTreeWalker.Default.Walk(this, context);
        _isCapturingConditionExpression--;
        return [.. _builderStack.Pop().GetChildren];
    }

    private ExpressionNode? CaptureIsolatedExpression(VBABaseParserRuleContext? context)
        => CaptureIsolated(context).LastOrDefault() as ExpressionNode;

    // like OnExitParent, but the provider may decline to build a node at all (a branch whose
    // condition recovery left incomplete) rather than always producing one.
    private void OnExitParentIfBuilt(Func<DeclarationNodeBuilder, SyntaxNode?> provider)
    {
        if (_builderStack.Count <= 1)
        {
            return;
        }
        if (provider.Invoke(_builderStack.Pop()) is { } node)
        {
            CurrentBuilder.AddChild(node);
        }
    }

    private bool _isPropertyWriterMember = false;
    private void OnEnterProcedure(bool isPropertyWriter = false)
    {
        _isInsideProcedure = true;
        _isAfterArgsList = false;
        _isPropertyWriterMember = isPropertyWriter;
        OnEnterParent();
    }
    private void OnExitProcedure(Func<DeclarationNodeBuilder, SyntaxNode> provider)
    {
        OnExitParent(provider);
        _isInsideProcedure = false;
        _isAfterArgsList = false;
        _isPropertyWriterMember = false;
    }

    public override void EnterPropertyGetStmt([NotNull] VBAParser.PropertyGetStmtContext context)
        => OnEnterProcedure();

    public override void ExitPropertyGetStmt([NotNull] VBAParser.PropertyGetStmtContext context) 
        => OnExitProcedure(builder => builder.BuildPropertyGetDeclaration(context));

    public override void EnterPropertyLetStmt([NotNull] VBAParser.PropertyLetStmtContext context) 
        => OnEnterProcedure(isPropertyWriter: true);
    public override void ExitPropertyLetStmt([NotNull] VBAParser.PropertyLetStmtContext context) 
        => OnExitProcedure(builder => builder.BuildPropertyLetDeclaration(context));
    public override void EnterPropertySetStmt([NotNull] VBAParser.PropertySetStmtContext context) 
        => OnEnterProcedure(isPropertyWriter: true);
    public override void ExitPropertySetStmt([NotNull] VBAParser.PropertySetStmtContext context) 
        => OnExitProcedure(builder => builder.BuildPropertySetDeclaration(context));

    public override void EnterSubStmt([NotNull] VBAParser.SubStmtContext context)
        => OnEnterProcedure();
    public override void ExitSubStmt([NotNull] VBAParser.SubStmtContext context)
        => OnExitProcedure(builder => builder.BuildProcedureDeclaration(context));

    public override void EnterFunctionStmt([NotNull] VBAParser.FunctionStmtContext context)
        => OnEnterProcedure();
    public override void ExitFunctionStmt([NotNull] VBAParser.FunctionStmtContext context)
        => OnExitProcedure(builder => builder.BuildFunctionDeclaration(context));

    private void OnExpression(ExpressionNode expression) => CurrentBuilder.AddChild(expression);

    public override void ExitAsTypeClause([NotNull] VBAParser.AsTypeClauseContext context)
    {
        // a body-level `ReDim x(1) As Long` carries an asTypeClause; `EnterRedimVariableDeclaration`
        // has pushed a builder for it, so this node now lands on that RedimDeclarationNode (not the
        // enclosing member) — no special-casing needed here.

        // `As` with no type token (half-typed / recovery): the LL error listener already located the
        // "missing type" error — just don't build a broken node.
        if (context.type() is not { } type)
        {
            return;
        }

        // recovery can still leave `type` synthetic: a "<missing …>" placeholder (`Dim a As, b As
        // Long`), or the bare NEW keyword for `As New` with no class name (via complexType's ctNewExpr).
        var typeText = type.GetText();

        // `type` is `(baseType | complexType) ( '(' ')' )?` — keep the trailing array-of `()`
        // (`As Long()`) off the name; IsArrayDef carries it.
        if (type.LPAREN() is not null && typeText.IndexOf('(') is var paren and >= 0)
        {
            typeText = typeText[..paren].TrimEnd();
        }
        if (IdentifierNameExtensions.IsRecoveryPlaceholder(typeText)
            || string.IsNullOrEmpty(typeText)
            || string.Equals(typeText, "New", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var location = context.GetSourceLocation(_rootUri);
        var value = typeText.Split('.');

        var qualifier = value.Length > 1 ? value[0] : null;
        var name = value.Last();

        OnExpression(new AsTypeExpressionNode(GetCurrentNodeId(), location, name, qualifier,
            AsAutoObject: context.NEW() is not null,
            IsArrayDef: type.LPAREN() is not null));
    }

    public override void ExitSimpleNameExpr([NotNull] VBAParser.SimpleNameExprContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        var value = context.identifier().untypedIdentifier()?.GetText()
            ?? context.identifier().typedIdentifier().untypedIdentifier().GetText();
        var location = context.GetSourceLocation(_rootUri);
        OnExpression(new SimpleNameExpressionNode(GetCurrentNodeId(), location, value));
    }

    // `lExpression` (MS-VBAL §5.6.10-16) is left-recursive, same as `expression` — every alternative
    // below follows the same no-Enter-override, PopLastChildren-at-Exit discipline as the operators
    // above (see that block's own remarks on why it stays even though ModuleParser is walk-based now).
    // `expression`'s own `lExpr` label needs no handler of its own: whatever this
    // listener builds here IS already the expression result, passed through transparently (same as
    // `parenthesizedExpr`) — confirmed by ExitSimpleNameExpr already working everywhere `expression`
    // is expected, with no `ExitLExpr` override anywhere.
    public override void ExitInstanceExpr([NotNull] VBAParser.InstanceExprContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        OnExpression(new InstanceExpressionNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri)));
    }

    public override void ExitMemberAccessExpr([NotNull] VBAParser.MemberAccessExprContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        if (CurrentBuilder.LastChild is not ExpressionNode owner || context.unrestrictedIdentifier() is not { } identifier)
        {
            // recovery left the owner unbuilt or the member name absent (a lone trailing `.`) — wrap
            // whatever legitimately parsed content is there in trivia instead of pop-and-discard, or
            // leaving it as a loose sibling silently misrepresenting the source (e.g. `x = Foo.` used
            // to leave a bare "Foo" reading back as `x = Foo`, same erasure shape as New/TypeOf...Is).
            CurrentBuilder.AddChild(BuildUnbuiltExpressionTrivia(context, 1));
            return;
        }
        CurrentBuilder.PopLastChildren(1);
        var id = GetCurrentNodeId();
        var member = new SimpleNameExpressionNode(id.Add(0), identifier.GetSourceLocation(_rootUri), identifier.Name());
        CurrentBuilder.AddChild(new MemberAccessExpressionNode(id, context.GetSourceLocation(_rootUri), owner, member));
    }

    public override void ExitWithMemberAccessExpr([NotNull] VBAParser.WithMemberAccessExprContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        if (context.unrestrictedIdentifier() is not { } identifier)
        {
            // a lone `.` with no member name (recovery) - no owner to reclaim in this with-relative form.
            CurrentBuilder.AddChild(BuildUnbuiltExpressionTrivia(context, 0));
            return;
        }
        var id = GetCurrentNodeId();
        var member = new SimpleNameExpressionNode(id.Add(0), identifier.GetSourceLocation(_rootUri), identifier.Name());
        CurrentBuilder.AddChild(new MemberAccessExpressionNode(id, context.GetSourceLocation(_rootUri), null, member));
    }

    public override void ExitDictionaryAccessExpr([NotNull] VBAParser.DictionaryAccessExprContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        if (CurrentBuilder.LastChild is not ExpressionNode owner || context.unrestrictedIdentifier() is not { } identifier)
        {
            CurrentBuilder.AddChild(BuildUnbuiltExpressionTrivia(context, 1));
            return;
        }
        CurrentBuilder.PopLastChildren(1);
        var id = GetCurrentNodeId();
        var member = new SimpleNameExpressionNode(id.Add(0), identifier.GetSourceLocation(_rootUri), identifier.Name());
        CurrentBuilder.AddChild(new DictionaryAccessExpressionNode(id, context.GetSourceLocation(_rootUri), owner, member));
    }

    public override void ExitWithDictionaryAccessExpr([NotNull] VBAParser.WithDictionaryAccessExprContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        if (context.unrestrictedIdentifier() is not { } identifier)
        {
            // a lone `!` with no member name (recovery) - no owner to reclaim in this with-relative form.
            CurrentBuilder.AddChild(BuildUnbuiltExpressionTrivia(context, 0));
            return;
        }
        var id = GetCurrentNodeId();
        var member = new SimpleNameExpressionNode(id.Add(0), identifier.GetSourceLocation(_rootUri), identifier.Name());
        CurrentBuilder.AddChild(new DictionaryAccessExpressionNode(id, context.GetSourceLocation(_rootUri), null, member));
    }

    // `indexExpr`/`whitespaceIndexExpr` (MS-VBAL §5.6.13) — identical shape, differing only by a
    // line-continuation token that carries no AST meaning. The callee is always exactly one already-
    // built node (whatever built it added itself, depth-first, same as any operator's operand); each
    // argument slot — positional, named, missing, or AddressOf — likewise always contributes exactly
    // one node (see ExitNamedArgument/ExitMissingArgument/ExitAddressOfExpression below), so the total
    // to reclaim is always 1 + the argument count, known directly from the grammar.
    public override void ExitIndexExpr([NotNull] VBAParser.IndexExprContext context)
        => BuildIndexExpression(context, context.argumentList());
    public override void ExitWhitespaceIndexExpr([NotNull] VBAParser.WhitespaceIndexExprContext context)
        => BuildIndexExpression(context, context.argumentList());

    private void BuildIndexExpression(VBABaseParserRuleContext context, VBAParser.ArgumentListContext? argumentList)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        var argumentCount = argumentList?.argument().Length ?? 0;
        var peeked = CurrentBuilder.PeekLastChildren(1 + argumentCount);
        if (peeked.Length != 1 + argumentCount || peeked[0] is not ExpressionNode callee || peeked.Skip(1).Any(node => node is not ExpressionNode))
        {
            // recovery left fewer/wrong-shaped children than the grammar guarantees — wrap whatever IS
            // there in an UnbuiltExpressionTriviaNode rather than pop-and-discard legitimately parsed
            // content into a broken node, or leave it as a loose sibling misrepresenting the source.
            CurrentBuilder.AddChild(BuildUnbuiltExpressionTrivia(context, 1 + argumentCount));
            return;
        }
        CurrentBuilder.PopLastChildren(peeked.Length);
        var arguments = peeked.Skip(1).Cast<ExpressionNode>().ToImmutableArray();
        CurrentBuilder.AddChild(new IndexExpressionNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), callee, arguments));
    }

    // a positional argument's own `expression` (or ByVal-marked expression) flows through
    // transparently, same as `expression`'s `lExpr` alternative — no handler needed here. Only the
    // other three MS-VBAL §5.6.13.1 argument forms need to wrap what their own inner expression (if
    // any) already added, so every argument slot still contributes exactly one node.
    public override void ExitNamedArgument([NotNull] VBAParser.NamedArgumentContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        if (CurrentBuilder.PeekLastChildren(1) is not [ExpressionNode value])
        {
            CurrentBuilder.AddChild(BuildUnbuiltExpressionTrivia(context, 1));
            return;
        }
        CurrentBuilder.PopLastChildren(1);
        CurrentBuilder.AddChild(new NamedArgumentNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), context.unrestrictedIdentifier().Name(), value));
    }

    public override void ExitMissingArgument([NotNull] VBAParser.MissingArgumentContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        OnExpression(new MissingArgumentNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri)));
    }

    public override void ExitAddressOfExpression([NotNull] VBAParser.AddressOfExpressionContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        if (CurrentBuilder.PeekLastChildren(1) is not [ExpressionNode target])
        {
            CurrentBuilder.AddChild(BuildUnbuiltExpressionTrivia(context, 1));
            return;
        }
        CurrentBuilder.PopLastChildren(1);
        CurrentBuilder.AddChild(new AddressOfExpressionNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), target));
    }

    // `objectPrintExpr` (MS-VBAL §5.6, e.g. `Debug.Print "x"`) is part of the lExpression family —
    // same left-recursive PopLastChildren discipline as ExitIndexExpr, since it recurses on the owner.
    // Its own outputList is captured live here (not via CaptureIsolated) because this only ever fires
    // from within an already-active capture region (whatever triggered it, e.g. CallStatementNode's
    // own isolated re-walk of its callee).
    public override void ExitObjectPrintExpr([NotNull] VBAParser.ObjectPrintExprContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        var itemCount = context.outputList()?.outputItem().Length ?? 0;
        var peeked = CurrentBuilder.PeekLastChildren(1 + itemCount);
        if (peeked.Length != 1 + itemCount || peeked[0] is not ExpressionNode owner || peeked.Skip(1).Any(node => node is not PrintOutputItemNode))
        {
            CurrentBuilder.AddChild(BuildUnbuiltExpressionTrivia(context, 1 + itemCount));
            return;
        }
        CurrentBuilder.PopLastChildren(peeked.Length);
        var items = peeked.Skip(1).Cast<PrintOutputItemNode>().ToImmutableArray();
        CurrentBuilder.AddChild(new ObjectPrintExpressionNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), owner, items));
    }

    // `Spc`/`Tab` (MS-VBAL §5.4.5.8.1) — neither rule recurses on itself, so either could take an
    // Enter/scope-push, but PopLastChildren works without one and stays consistent with the rest of
    // this family.
    public override void ExitSpcClause([NotNull] VBAParser.SpcClauseContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        if (CurrentBuilder.PeekLastChildren(1) is not [ExpressionNode count])
        {
            CurrentBuilder.AddChild(BuildUnbuiltExpressionTrivia(context, 1));
            return;
        }
        CurrentBuilder.PopLastChildren(1);
        CurrentBuilder.AddChild(new PrintSpcClauseNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), count));
    }

    public override void ExitTabClause([NotNull] VBAParser.TabClauseContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        ExpressionNode? column = null;
        if (context.tabNumberClause() is { } tabNumberClause)
        {
            // a null column is legitimately `Tab()` bare - only wrap in trivia when the number clause
            // is present in the source but its own expression didn't build; a bare PrintTabClauseNode
            // with column=null there would misrepresent it as the empty form instead of a broken one.
            if (CurrentBuilder.PeekLastChildren(1) is [ExpressionNode value])
            {
                CurrentBuilder.PopLastChildren(1);
                column = value;
            }
            else
            {
                column = BuildUnbuiltExpressionTrivia(tabNumberClause, 1);
            }
        }
        CurrentBuilder.AddChild(new PrintTabClauseNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), column));
    }

    // an outputItem is (value?)(separator?), never both absent (the grammar's 3 alternatives always
    // supply at least one) — whichever of outputClause's 3 forms fired (Spc/Tab/plain expression)
    // already contributed exactly one node, same invariant as an index expression's arguments.
    public override void ExitOutputItem([NotNull] VBAParser.OutputItemContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        ExpressionNode? value = null;
        if (context.outputClause() is { } outputClause)
        {
            // a null value is legitimately a bare separator (`,`/`;`) - only wrap in trivia when the
            // output clause is present in the source but its own expression didn't build.
            if (CurrentBuilder.PeekLastChildren(1) is [ExpressionNode popped])
            {
                CurrentBuilder.PopLastChildren(1);
                value = popped;
            }
            else
            {
                value = BuildUnbuiltExpressionTrivia(outputClause, 1);
            }
        }
        var separator = context.charPosition() is { } position ? (position.SEMICOLON() is not null ? ";" : ",") : null;
        CurrentBuilder.AddChild(new PrintOutputItemNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), value, separator));
    }

    public override void ExitLiteralIdentifier([NotNull] VBAParser.LiteralIdentifierContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }

        var location = context.GetSourceLocation(_rootUri);
        if (context.booleanLiteralIdentifier() is VBAParser.BooleanLiteralIdentifierContext boolLiteralContext)
        {
            if (boolLiteralContext.FALSE() is not null)
            {
                OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, VBBooleanValue.False));
            }
            else if (boolLiteralContext.TRUE() is not null)
            {
                OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, VBBooleanValue.True));
            }
        }
        else if (context.objectLiteralIdentifier() is VBAParser.ObjectLiteralIdentifierContext objLiteralContext)
        {
            if (objLiteralContext.NOTHING() is not null)
            {
                OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, VBObjectValue.Nothing));
            }
        }
        else if (context.variantLiteralIdentifier() is VBAParser.VariantLiteralIdentifierContext variantLiteralContext)
        {
            if (variantLiteralContext.EMPTY() is not null)
            {
                OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, VBEmptyValue.Empty));
            }
        }
    }
    public override void ExitNumberLiteral([NotNull] VBAParser.NumberLiteralContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }

        var location = context.GetSourceLocation(_rootUri);
        var token = (context.INTEGERLITERAL() ?? context.FLOATLITERAL() ?? context.HEXLITERAL() ?? context.OCTLITERAL())?.GetText()
            ?? string.Empty;

        var (value, overflow) = NumericLiteral.Resolve(token);
        if (overflow)
        {
            _errors.Report(location, VBCompileErrorId.NumericLiteralOverflow, Exceptions.VBCompileError_NumericLiteralOverflow_Verbose);
        }
        OnExpression(new LiteralExpressionNode(GetCurrentNodeId(), location, value));
    }
    // Operator alternatives of `expression` (a left-recursive rule) have no Enter override, so none of
    // the handlers below use one: each operator's operands are, at Exit time, always exactly the last
    // N nodes already added to whatever builder is currently active (nothing else can have
    // interleaved, since expression subtrees resolve depth-first) — PopLastChildren reclaims them.
    // This discipline predates ModuleParser's switch to a post-parse ParseTreeWalker (a live
    // AddParseListener fires Exit *before* Enter on a left-recursive alternative — the opposite of
    // every ordinary rule — which no Enter-scoped push/pop could survive); it stays because it's still
    // needed for the OTHER reason it was already built to handle: ANTLR's own error recovery can leave
    // an operator with too few operands (`a = 1 +`) or a non-expression sitting where one's expected —
    // peek the shape first, then fall back to an UnbuiltExpressionTriviaNode (source text + whatever
    // operand(s) WERE there) instead of leaving the operand(s) as loose siblings: `a = 1 +` used to
    // leave a bare "1" sitting where the assignment's own capture would silently adopt it as if
    // `a = 1` were the complete, correct statement — the same erasure/misrepresentation shape as
    // New/TypeOf...Is, just one level up (see that fix's remarks).
    private SyntaxNode BuildUnary(string token, VBABaseParserRuleContext context)
    {
        if (CurrentBuilder.PeekLastChildren(1) is not [ExpressionNode])
        {
            return BuildUnbuiltExpressionTrivia(context, 1);
        }
        return new VBUnaryOperatorExpressionNode(token, GetCurrentNodeId(), context.GetSourceLocation(_rootUri), CurrentBuilder.PopLastChildren(1));
    }
    private SyntaxNode BuildBinary(string token, VBABaseParserRuleContext context)
    {
        if (CurrentBuilder.PeekLastChildren(2) is not [ExpressionNode, ExpressionNode])
        {
            return BuildUnbuiltExpressionTrivia(context, 2);
        }
        return new VBBinaryOperatorExpressionNode(token, GetCurrentNodeId(), context.GetSourceLocation(_rootUri), CurrentBuilder.PopLastChildren(2));
    }
    private void AddIfBuilt(SyntaxNode? node)
    {
        if (node is not null)
        {
            CurrentBuilder.AddChild(node);
        }
    }

    public override void ExitNewExpr([NotNull] VBAParser.NewExprContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        AddIfBuilt(BuildNew(context));
    }

    private SyntaxNode BuildNew(VBABaseParserRuleContext context)
    {
        if (CurrentBuilder.PeekLastChildren(1) is not [ExpressionNode typeExpression])
        {
            return BuildUnbuiltExpressionTrivia(context, 1);
        }
        CurrentBuilder.PopLastChildren(1);
        return new NewExpressionNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), typeExpression);
    }

    // `TypeOf <expr>` (MS-VBAL §5.6.9.4) is its own `expression` alternative purely to keep the grammar
    // SLL — on its own it isn't a complete construct, it only ever means something as the left operand
    // of the `Is <type>` that always follows (see ExitRelationalOp's TypeOf...Is branch, which unwraps
    // this trivia and pairs its one input with the right-hand type expression into a real
    // TypeOfIsExpressionNode). Wrap the inner operand in an UnbuiltExpressionTriviaNode here regardless:
    // in the well-formed case ExitRelationalOp unwraps it immediately and it never survives into the
    // final tree; in a recovery case where no `Is <type>` follows, this is what stops the operand from
    // leaking straight up as a bare, wrongly-meaning value instead of a visibly-unbuilt one.
    public override void ExitTypeofexpr([NotNull] VBAParser.TypeofexprContext context)
        => AddUnbuiltExpressionTriviaIfActive(context, 1);

    private void AddUnbuiltExpressionTriviaIfActive(VBABaseParserRuleContext context, int maxInputs)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        CurrentBuilder.AddChild(BuildUnbuiltExpressionTrivia(context, maxInputs));
    }

    private UnbuiltExpressionTriviaNode BuildUnbuiltExpressionTrivia(VBABaseParserRuleContext context, int maxInputs)
    {
        var inputs = CurrentBuilder.PopLastChildren(maxInputs);
        return new UnbuiltExpressionTriviaNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), context.GetText(), inputs);
    }

    // statement-position counterpart of BuildUnbuiltExpressionTrivia — same rationale (a required
    // sub-expression/identifier missing under recovery must not silently vanish, since that's still
    // reconstructable source text), for guards that sit at statement level instead of inside an
    // expression tree (a bare `RaiseEvent`, an `On Error` shape recovery left incomplete).
    private UnbuiltStatementTriviaNode BuildUnbuiltStatementTrivia(VBABaseParserRuleContext context, int maxInputs = 0)
    {
        var inputs = CurrentBuilder.PopLastChildren(maxInputs);
        return new UnbuiltStatementTriviaNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), context.GetText(), inputs);
    }

    public override void ExitUnaryMinusOp([NotNull] VBAParser.UnaryMinusOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }

        // VBA has no negative-literal token; `Const N = -1` is MINUS over the literal 1 — fold the
        // sign into a bare literal operand instead of wrapping it in an operator node.
        if (CurrentBuilder.LastChild is LiteralExpressionNode literal && NumericLiteral.Negate(literal.StaticValue) is { } negated)
        {
            CurrentBuilder.UpdateLastChild(literal with { StaticValue = negated });
            return;
        }
        AddIfBuilt(BuildUnary(Tokens.NegationOp, context));
    }

    public override void ExitPowOp([NotNull] VBAParser.PowOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        AddIfBuilt(BuildBinary(Tokens.PowerOp, context));
    }

    public override void ExitMultOp([NotNull] VBAParser.MultOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        var token = context.DIV() is not null ? Tokens.DivisionOp : Tokens.MultiplicationOp;
        AddIfBuilt(BuildBinary(token, context));
    }

    public override void ExitIntDivOp([NotNull] VBAParser.IntDivOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        AddIfBuilt(BuildBinary(Tokens.IntegerDivisionOp, context));
    }

    public override void ExitModOp([NotNull] VBAParser.ModOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        AddIfBuilt(BuildBinary(Tokens.ModuloOp, context));
    }

    public override void ExitAddOp([NotNull] VBAParser.AddOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        var token = context.MINUS() is not null ? Tokens.SubtractionOp : Tokens.AdditionOp;
        AddIfBuilt(BuildBinary(token, context));
    }

    public override void ExitConcatOp([NotNull] VBAParser.ConcatOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        AddIfBuilt(BuildBinary(Tokens.ConcatOp, context));
    }

    public override void ExitRelationalOp([NotNull] VBAParser.RelationalOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }

        // `TypeOf <expr> Is <type>` (MS-VBAL §5.6.9.4) arrives here as an ordinary IS relationalOp
        // whose left operand is grammatically a typeofexpr (see that Exit override's own remarks) —
        // recognized from the parse-tree shape itself, not from anything on the builder stack.
        if (context.IS() is not null && context.expression(0) is VBAParser.TypeofexprContext)
        {
            AddIfBuilt(BuildTypeOfIs(context));
            return;
        }

        var token = context.EQ() is not null ? Tokens.CompareEqualOp
            : context.NEQ() is not null ? Tokens.CompareNotEqualOp
            : context.GT() is not null ? Tokens.CompareGreaterThanOp
            : context.GEQ() is not null ? Tokens.CompareGreaterThanOrEqualOp
            : context.LT() is not null ? Tokens.CompareLessThanOp
            : context.LEQ() is not null ? Tokens.CompareLessThanOrEqualOp
            : context.IS() is not null ? Tokens.CompareIsOp
            : context.LIKE() is not null ? Tokens.CompareLikeOp
            : context.GetText();
        AddIfBuilt(BuildBinary(token, context));
    }

    // ExitTypeofexpr always wraps its operand in an UnbuiltExpressionTriviaNode with exactly one input
    // (see its own remarks); unwrap that here and pair the operand with the right-hand type expression.
    // A shape mismatch (recovery left either side incomplete) falls back to the same lossless trivia
    // wrapping every other operator guard uses, rather than building a TypeOfIsExpressionNode missing
    // a required operand.
    private SyntaxNode BuildTypeOfIs(VBAParser.RelationalOpContext context)
    {
        if (CurrentBuilder.PeekLastChildren(2) is not [UnbuiltExpressionTriviaNode { Inputs: [ExpressionNode operand] }, ExpressionNode typeExpression])
        {
            return BuildUnbuiltExpressionTrivia(context, 2);
        }

        CurrentBuilder.PopLastChildren(2);
        return new TypeOfIsExpressionNode(GetCurrentNodeId(), context.GetSourceLocation(_rootUri), operand, typeExpression);
    }

    public override void ExitLogicalNotOp([NotNull] VBAParser.LogicalNotOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        // `Not` is unary — one operand.
        AddIfBuilt(BuildUnary(Tokens.LogicalNotOp, context));
    }

    public override void ExitLogicalAndOp([NotNull] VBAParser.LogicalAndOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        AddIfBuilt(BuildBinary(Tokens.LogicalAndOp, context));
    }

    public override void ExitLogicalOrOp([NotNull] VBAParser.LogicalOrOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        AddIfBuilt(BuildBinary(Tokens.LogicalOrOp, context));
    }

    public override void ExitLogicalXorOp([NotNull] VBAParser.LogicalXorOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        AddIfBuilt(BuildBinary(Tokens.LogicalXOrOp, context));
    }

    public override void ExitLogicalEqvOp([NotNull] VBAParser.LogicalEqvOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        AddIfBuilt(BuildBinary(Tokens.LogicalEqvOp, context));
    }

    public override void ExitLogicalImpOp([NotNull] VBAParser.LogicalImpOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        AddIfBuilt(BuildBinary(Tokens.LogicalImpOp, context));
    }
    public override void ExitLiteralExpression([NotNull] VBAParser.LiteralExpressionContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }

        var location = context.GetSourceLocation(_rootUri);
        if (context.DATELITERAL() is ITerminalNode dateLiteral)
        {
            // MS-VBAL 3.3.3: date literals are locale-independent (English month names, spec-defined
            // month/day disambiguation) - the ambient thread culture must not affect parsing. Mirrors
            // PrecompilerDirectiveListener's ExitLiteralExpr, which already got this fix; this pass
            // never did, so the same literal disagreed between the two passes depending on the host's
            // culture (e.g. "#1-Jan-2020#" fails to parse entirely under a non-English locale whose
            // month abbreviations differ, instead of the invariant English ones MS-VBAL mandates).
            if (DateTime.TryParse(dateLiteral.Symbol.Text.Trim('#'), CultureInfo.InvariantCulture, DateTimeStyles.None, out var rawValue))
            {
                OnExpression(new LiteralExpressionNode(
                    GetCurrentNodeId(), 
                    location, 
                    new VBDateValue(rawValue.ToOADate())));
            }
        }
        else if (context.STRINGLITERAL() is ITerminalNode stringLiteral)
        {
            OnExpression(new LiteralExpressionNode(
                GetCurrentNodeId(), 
                location,
                new VBStringValue(stringLiteral.Symbol.Text[1..^1])));
        }
    }

    private int _parameterIndex = 0;

    public override void EnterArgList([NotNull] VBAParser.ArgListContext context)
    {
        _parameterIndex = 0;
        _isAfterArgsList = false;
    }
    public override void ExitArgList([NotNull] VBAParser.ArgListContext context)
    {
        _parameterIndex = 0;
        _isAfterArgsList = true;
        // NOTE: do NOT set _isInsideProcedure here. It is already set by OnEnterProcedure for a real
        // Sub/Function/Property body; a Declare or Event has an argList but no body, and setting it
        // here left the flag poisoned (declaration-pass expression capture off) for every module-level
        // declaration between a Declare/Event and the next procedure. This whole flag disappears once
        // the listener also builds statement nodes (TokenSemanticsListener).

        if (_isPropertyWriterMember)
        {
            if (CurrentBuilder.LastChild is ParameterDeclarationNode node
                && node.ParameterKind != ParameterKind.ExplicitByVal)
            {
                // we cannot do this before knowing how many parameters there are,
                // because it's only applicable to the RHS/value parameter (last).
                CurrentBuilder.UpdateLastChild(
                    node with { ParameterKind = ParameterKind.ImplicitByVal });
            }
        }
    }
    public override void EnterArg([NotNull] VBAParser.ArgContext context)
        => OnEnterParent();
    public override void ExitArg([NotNull] VBAParser.ArgContext context)
    {
        var isPropertyWriter = _isPropertyWriterMember;
        OnExitParent(builder => builder.BuildParameterDeclaration(context, isPropertyWriter));
        _parameterIndex++;
    }

    public override void ExitStatementLabelDefinition([NotNull] VBAParser.StatementLabelDefinitionContext context)
    {
        string? name = default;
        int? number = default;
        var labelLocation = context.GetSourceLocation(_rootUri);
        var lineNumberLocation = labelLocation;

        if (context.standaloneLineNumberLabel()?.lineNumberLabel() is VBAParser.LineNumberLabelContext numContextA)
        {
            lineNumberLocation = numContextA.GetSourceLocation(_rootUri);
            number = LineNumber(numContextA);
        }
        else if (context.combinedLabels()?.lineNumberLabel() is VBAParser.LineNumberLabelContext numContextB)
        {
            lineNumberLocation = numContextB.GetSourceLocation(_rootUri);
            number = LineNumber(numContextB);
        }
        else if (context.identifierStatementLabel()?.legalLabelIdentifier()?.identifier() is VBAParser.IdentifierContext labelContextA)
        {
            labelLocation = labelContextA.GetSourceLocation(_rootUri);
            name = labelContextA.GetText();
        }
        else if (context.combinedLabels()?.identifierStatementLabel()?.legalLabelIdentifier()?.identifier() is VBAParser.IdentifierContext labelContextB)
        {
            labelLocation = labelContextB.GetSourceLocation(_rootUri);
            name = labelContextB.GetText();
        }

        if (number.HasValue)
        {
            CurrentBuilder.AddChild(new LineNumberNode(GetCurrentNodeId(), lineNumberLocation, number.Value));
        }
        if (name is not null)
        {
            CurrentBuilder.AddChild(new LineLabelNode(GetCurrentNodeId(), labelLocation, name));
        }

        // lineNumberLabel is `MINUS? numberLiteral`, so it can carry a sign or a non-decimal token;
        // only a bare non-negative integer is a line number, anything else contributes no node.
        static int? LineNumber(VBAParser.LineNumberLabelContext context)
            => context.MINUS() is null && int.TryParse(context.numberLiteral()?.GetText(), out var value)
                ? value
                : null;
    }
}
