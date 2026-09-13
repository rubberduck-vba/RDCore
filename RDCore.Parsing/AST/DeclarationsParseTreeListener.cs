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

    private SyntaxNodeId GetCurrentNodeId() => CurrentBuilder.NodeId.Add(CurrentBuilder.ChildCount);

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
    // SimpleNameExpressionNode rather than routed through CaptureIsolatedExpression.
    public override void ExitRaiseEventStmt([NotNull] VBAParser.RaiseEventStmtContext context)
    {
        var id = GetCurrentNodeId();
        var eventName = new SimpleNameExpressionNode(id.Add(0), context.identifier().GetSourceLocation(_rootUri), context.identifier().Name());
        var arguments = context.eventArgumentList()?.eventArgument().Select(argument => CaptureIsolatedExpression(argument.expression())) ?? [];
        var inputs = new ExpressionNode?[] { eventName }.Concat(arguments).Where(input => input is not null).Cast<SyntaxNode>().ToImmutableArray();
        CurrentBuilder.AddChild(new KeywordStatementNode(id, context.GetSourceLocation(_rootUri), Tokens.RaiseEvent, inputs));
    }

    public override void ExitCloseStmt([NotNull] VBAParser.CloseStmtContext context)
        => OnKeywordStatement(Tokens.Close, context, CaptureFileNumbers(context.fileNumberList()));

    public override void ExitResetStmt([NotNull] VBAParser.ResetStmtContext context)
        => OnKeywordStatement(Tokens.Reset, context);

    // End/Stop/Exit (MS-VBAL 5.4.2.4, 5.4.2.11, 5.4.2.12) are all keyword-only, zero-argument
    // statements — same KeywordStatementNode shape as Reset, no dedicated node type needed.
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

    // Re-walks an already-parsed, self-contained expression subtree in isolation, with capture
    // enabled just for that walk, into its own fresh scope. Safe because this only ever runs from an
    // Exit handler — the parser has already fully matched (and moved past) this subtree by then, so
    // the walk touches a finished, static tree, never the live parse. Existing Exit* operator handlers
    // (PopLastChildren-based) don't care whether a matching Enter fired first, so they combine
    // correctly under ParseTreeWalker's ordering exactly as they do under AddParseListener's.
    private ExpressionNode? CaptureIsolatedExpression(VBAParser.ExpressionContext? context)
    {
        if (context is null)
        {
            return null;
        }

        OnEnterParent();
        _isCapturingConditionExpression++;
        ParseTreeWalker.Default.Walk(this, context);
        _isCapturingConditionExpression--;
        return _builderStack.Pop().GetChildren.LastOrDefault() as ExpressionNode;
    }

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
    // Operator alternatives of `expression` (a left-recursive rule) get their Exit called *before*
    // Enter by AddParseListener — the opposite of every ordinary rule (see ParseOnce's remarks on
    // PrecompilerDirectiveListener's own workaround). No Enter override can scope these operands, so
    // none of the handlers below use one: each operator's operands are, at Exit time, always exactly
    // the last N nodes already added to whatever builder is currently active (nothing else can have
    // interleaved, since expression subtrees resolve depth-first) — PopLastChildren reclaims them.
    private SyntaxNode BuildUnary(string token, VBABaseParserRuleContext context)
        => new VBUnaryOperatorExpressionNode(token, GetCurrentNodeId(), context.GetSourceLocation(_rootUri), CurrentBuilder.PopLastChildren(1));
    private SyntaxNode BuildBinary(string token, VBABaseParserRuleContext context)
        => new VBBinaryOperatorExpressionNode(token, GetCurrentNodeId(), context.GetSourceLocation(_rootUri), CurrentBuilder.PopLastChildren(2));

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
        CurrentBuilder.AddChild(BuildUnary(Tokens.NegationOp, context));
    }

    public override void ExitPowOp([NotNull] VBAParser.PowOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        CurrentBuilder.AddChild(BuildBinary(Tokens.PowerOp, context));
    }

    public override void ExitMultOp([NotNull] VBAParser.MultOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        var token = context.DIV() is not null ? Tokens.DivisionOp : Tokens.MultiplicationOp;
        CurrentBuilder.AddChild(BuildBinary(token, context));
    }

    public override void ExitIntDivOp([NotNull] VBAParser.IntDivOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        CurrentBuilder.AddChild(BuildBinary(Tokens.IntegerDivisionOp, context));
    }

    public override void ExitModOp([NotNull] VBAParser.ModOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        CurrentBuilder.AddChild(BuildBinary(Tokens.ModuloOp, context));
    }

    public override void ExitAddOp([NotNull] VBAParser.AddOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        var token = context.MINUS() is not null ? Tokens.SubtractionOp : Tokens.AdditionOp;
        CurrentBuilder.AddChild(BuildBinary(token, context));
    }

    public override void ExitConcatOp([NotNull] VBAParser.ConcatOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        CurrentBuilder.AddChild(BuildBinary(Tokens.ConcatOp, context));
    }

    public override void ExitRelationalOp([NotNull] VBAParser.RelationalOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
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
        CurrentBuilder.AddChild(BuildBinary(token, context));
    }

    public override void ExitLogicalNotOp([NotNull] VBAParser.LogicalNotOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        // `Not` is unary — one operand.
        CurrentBuilder.AddChild(BuildUnary(Tokens.LogicalNotOp, context));
    }

    public override void ExitLogicalAndOp([NotNull] VBAParser.LogicalAndOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        CurrentBuilder.AddChild(BuildBinary(Tokens.LogicalAndOp, context));
    }

    public override void ExitLogicalOrOp([NotNull] VBAParser.LogicalOrOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        CurrentBuilder.AddChild(BuildBinary(Tokens.LogicalOrOp, context));
    }

    public override void ExitLogicalXorOp([NotNull] VBAParser.LogicalXorOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        CurrentBuilder.AddChild(BuildBinary(Tokens.LogicalXOrOp, context));
    }

    public override void ExitLogicalEqvOp([NotNull] VBAParser.LogicalEqvOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        CurrentBuilder.AddChild(BuildBinary(Tokens.LogicalEqvOp, context));
    }

    public override void ExitLogicalImpOp([NotNull] VBAParser.LogicalImpOpContext context)
    {
        if (!IsDeclarationPassExpression)
        {
            return;
        }
        CurrentBuilder.AddChild(BuildBinary(Tokens.LogicalImpOp, context));
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
            if (DateTime.TryParse(dateLiteral.Symbol.Text.Trim('#'), out var rawValue))
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
        else if (context.identifierStatementLabel().legalLabelIdentifier().identifier() is VBAParser.IdentifierContext labelContextA)
        {
            labelLocation = labelContextA.GetSourceLocation(_rootUri);
            name = labelContextA.GetText();
        }
        else if (context.combinedLabels()?.identifierStatementLabel().legalLabelIdentifier().identifier() is VBAParser.IdentifierContext labelContextB)
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
