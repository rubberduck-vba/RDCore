using NSubstitute;
using RDCore.Parsing;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.Runtime.Semantics.SetCoercion;
using RDCore.Runtime.Semantics.Statements;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// <see cref="ProcedureExecutor"/> is the walking skeleton for S5 (RD-VBAL §3.5's interpreter): parse a
/// real procedure body, lower it, run it, assert on the frame it left behind — no mocked statement
/// semantics, the same real-session convention <c>RuntimeExpressionEvaluatorTests</c> already uses.
/// </summary>
[TestClass]
public sealed class ProcedureExecutorTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;
    private static readonly Uri ProcedureUri = TestUri.TestSubProcUri();
    private static readonly SyntaxNodeId NodeId = new(ProcedureUri.AbsolutePath, [1]);

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static IRuntimeSession ComposeSession(params Symbol[] symbols)
        => RuntimeSessionComposer.Compose(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new Provider(symbols));

    private static ICallStackFrame PushFrame(IRuntimeSession session, params (VBParameterSymbol Symbol, VBTypedValue Initial)[] locals)
    {
        var procedure = new StaticSymbol("Foo", SymbolKindExt.Procedure, VBVoidType.TypeInfo);
        var frame = session.Symbols.CreateFrame(NodeId, procedure);
        session.CallStack.TryPush(frame);
        foreach (var (symbol, initial) in locals)
        {
            frame.Push(symbol, initial);
        }
        return frame;
    }

    private static VBParameterSymbol Local(string name, VBType type)
        => new(Root, ProcedureUri, name, R, R, ParameterKind.ImplicitByRef, type);

    private static ProcedureExecutor Executor()
    {
        var formatter = Substitute.For<IVerboseMessageBuilder>();
        var handle = new ProviderHandle();
        var booleanCoercion = new VBBooleanLetCoercionRuntimeSemantics(handle, formatter);
        var letCoercion = new LetCoercionRuntimeSemanticsProvider(
            [new VBNumericLetCoercionTypeRuntimeSemantics(formatter, handle), booleanCoercion], formatter);
        handle.Inner = letCoercion;
        var expressionEvaluator = new RuntimeExpressionEvaluator(new OperatorRuntimeSemanticsProvider(letCoercion, formatter));
        var statements = new StatementRuntimeSemanticsProvider(expressionEvaluator, letCoercion, new SetCoercionRuntimeSemantics(formatter), formatter);
        var conditions = new ConditionEvaluator(expressionEvaluator, booleanCoercion);
        var withStatement = new WithStatementRuntimeSemantics(new SetCoercionRuntimeSemantics(formatter), letCoercion);
        var withTargets = new WithTargetEvaluator(expressionEvaluator, withStatement);
        var cases = new CaseMatchEvaluator(expressionEvaluator, letCoercion, formatter);
        return new ProcedureExecutor(statements, conditions, withTargets, cases);
    }

    // VBNumericLetCoercionTypeRuntimeSemantics needs itself back to coerce a numeric operand recursively;
    // this handle breaks that construction cycle, same pattern used throughout the runtime test suite.
    private sealed class ProviderHandle : ILetCoercionRuntimeSemanticsProvider
    {
        public ILetCoercionRuntimeSemanticsProvider Inner { get; set; } = default!;
        public LetCoercionResult EvaluateLetCoercionSemantics(ISymbolResolver resolver, ExpressionNode expression, LetCoercionStackFrame frame)
            => Inner.EvaluateLetCoercionSemantics(resolver, expression, frame);
        public RDCore.SDK.Semantics.Analysis.LetCoercionAnalysisContext Analyze(ISymbolResolver resolver, RDCore.SDK.Semantics.Builders.ILetCoercionSemanticContextBuilder builder, ExpressionNode expression, LetCoercionStackFrame frame)
            => Inner.Analyze(resolver, builder, expression, frame);
    }

    private static InstructionList Lower(params string[] procedureBody)
    {
        var source = $"Sub Foo()\r\n{string.Join("\r\n", procedureBody)}\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var result = InstructionListLowering.Lower(new StatementBlock([.. member.Children]));
        Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors.Select(error => error.Verbose)));
        return result.InstructionList;
    }

    [TestMethod]
    public void AStraightLineProcedure_RunsToCompletion_AndLeavesTheExpectedValuesOnTheFrame()
        // Sub Foo(): x = 1: y = 2: z = x + y: End Sub - the S5 walking skeleton.
    {
        var list = Lower("x = 1", "y = 2", "z = x + y");
        var x = Local("x", VBLongType.TypeInfo);
        var y = Local("y", VBLongType.TypeInfo);
        var z = Local("z", VBLongType.TypeInfo);
        var session = ComposeSession(x, y, z);
        var frame = PushFrame(session, (x, new VBLongValue(0)), (y, new VBLongValue(0)), (z, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
        Assert.AreEqual(2, session.Symbols.Resolver.GetValue(y).Value.BoxedValue);
        Assert.AreEqual(3, session.Symbols.Resolver.GetValue(z).Value.BoxedValue);
    }

    [TestMethod]
    public void ASetAssignment_CreatesALiveObject_AndWritesItThroughTheHandle()
        // Sub Foo(): Set obj = New Widget: End Sub
    {
        var list = Lower("Set obj = New Widget");
        var classModule = new VBClassModuleSymbol(Root, Root, "Widget");
        var obj = Local("obj", VBObjectType.TypeInfo);
        var session = ComposeSession(classModule, obj);
        var frame = PushFrame(session, (obj, VBObjectValue.Nothing));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreNotEqual(VBObjectValue.Nothing.RuntimeValue.BoxedValue, session.Symbols.Resolver.GetValue(obj).Value.BoxedValue);
    }

    [TestMethod]
    public void AWithBlock_ReadsAFieldThroughTheWithRelativeMemberAccess()
        // Sub Foo(): With obj: x = .State: End With: End Sub - obj.State is pre-set directly on the
        // live instance (no field-write statement exists yet), proving the With opener stashes the
        // coerced target and RuntimeEvaluationContext.EnclosingWithTarget resolves it back for a plain
        // ".State" read with no owner in source.
    {
        var list = Lower("With obj", "x = .State", "End With");
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");
        var field = new VBInstanceFieldVariableMemberSymbol(Root, widget.Uri, "State", R, R, VBLongType.TypeInfo, AccessModifier.Implicit);
        widget = widget with { DefaultInterfaceMembers = [field] };
        var obj = Local("obj", VBObjectType.TypeInfo);
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(widget, field, obj, x);
        var instance = session.Symbols.CreateInstance(session.Objects.CreateObject(), widget);
        instance.GetValue(field).SetValue(session.Symbols.Resolver, new RDCore.SDK.Model.Values.Runtime.VBRuntimeValue<int>(7));
        var frame = PushFrame(session, (obj, new VBObjectValue(instance.ObjectId)), (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(7, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void NestedWithBlocks_EachResolveTheirOwnTarget_NotTheOuterOnesStaleStash()
        // Sub Foo(): With a: With b: y = .State: End With: x = .State: End With: End Sub - proves the
        // per-instruction context is recomputed fresh from Instruction.EnclosingWith rather than carried
        // over from whichever With ran most recently.
    {
        var list = Lower("With a", "With b", "y = .State", "End With", "x = .State", "End With");
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");
        var field = new VBInstanceFieldVariableMemberSymbol(Root, widget.Uri, "State", R, R, VBLongType.TypeInfo, AccessModifier.Implicit);
        widget = widget with { DefaultInterfaceMembers = [field] };
        var a = Local("a", VBObjectType.TypeInfo);
        var b = Local("b", VBObjectType.TypeInfo);
        var x = Local("x", VBLongType.TypeInfo);
        var y = Local("y", VBLongType.TypeInfo);
        var session = ComposeSession(widget, field, a, b, x, y);
        var instanceA = session.Symbols.CreateInstance(session.Objects.CreateObject(), widget);
        instanceA.GetValue(field).SetValue(session.Symbols.Resolver, new RDCore.SDK.Model.Values.Runtime.VBRuntimeValue<int>(1));
        var instanceB = session.Symbols.CreateInstance(session.Objects.CreateObject(), widget);
        instanceB.GetValue(field).SetValue(session.Symbols.Resolver, new RDCore.SDK.Model.Values.Runtime.VBRuntimeValue<int>(2));
        var frame = PushFrame(session,
            (a, new VBObjectValue(instanceA.ObjectId)), (b, new VBObjectValue(instanceB.ObjectId)),
            (x, new VBLongValue(0)), (y, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(2, session.Symbols.Resolver.GetValue(y).Value.BoxedValue);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void SelectCase_AValueClause_MatchesByEquality()
    {
        var list = Lower("Select Case n", "Case 2", "x = 1", "Case Else", "x = 2", "End Select");
        var n = Local("n", VBLongType.TypeInfo);
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(n, x);
        var frame = PushFrame(session, (n, new VBLongValue(2)), (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void SelectCase_AComparisonClause_MatchesByTheRealRelationalOperator()
    {
        var list = Lower("Select Case n", "Case Is > 5", "x = 1", "Case Else", "x = 2", "End Select");
        var n = Local("n", VBLongType.TypeInfo);
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(n, x);
        var frame = PushFrame(session, (n, new VBLongValue(9)), (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void SelectCase_AToRangeClause_MatchesInclusively()
    {
        var list = Lower("Select Case n", "Case 1 To 10", "x = 1", "Case Else", "x = 2", "End Select");
        var n = Local("n", VBLongType.TypeInfo);
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(n, x);
        var frame = PushFrame(session, (n, new VBLongValue(10)), (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void SelectCase_NoClauseMatches_FallsThroughToCaseElse()
    {
        var list = Lower("Select Case n", "Case 1, 2, 3", "x = 1", "Case Else", "x = 2", "End Select");
        var n = Local("n", VBLongType.TypeInfo);
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(n, x);
        var frame = PushFrame(session, (n, new VBLongValue(99)), (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(2, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void SelectCase_ASecondCommaSeparatedClause_MatchesWhenTheFirstDoesNot()
        // Case 1, 2, 3 - proves every comma-separated range clause on one Case line is tried, not just the first.
    {
        var list = Lower("Select Case n", "Case 1, 2, 3", "x = 1", "Case Else", "x = 2", "End Select");
        var n = Local("n", VBLongType.TypeInfo);
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(n, x);
        var frame = PushFrame(session, (n, new VBLongValue(2)), (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    [Ignore("Two separate, pre-existing gaps block this: the parser doesn't implement the Null literal " +
        "keyword yet (so 'Select Case Null' can't come from real source), and CallStackFrame.Push silently " +
        "drops a directly-pushed VBNullValue.Null (SymbolAddressTable.TryAllocate rejects its Size => 0). " +
        "The ExecuteSelect/ExecuteCaseHeader short-circuit for a Null selector (MS-VBAL 5.4.2.10) is " +
        "implemented and believed correct, just not exercisable by a test yet.")]
    public void SelectCase_ANullSelector_SkipsEveryClause_GoesStraightToCaseElse()
        // MS-VBAL 5.4.2.10: "If select-expression is the data value Null, only the case-else-clause is
        // executed" - a real "=" comparison against Null doesn't produce a plain Boolean result, so this
        // also proves the range clause is never actually evaluated for a Null selector, not just skipped
        // after evaluating to False.
    {
        var list = Lower("Select Case n", "Case 5", "x = 1", "Case Else", "x = 2", "End Select");
        var n = Local("n", VBLongType.TypeInfo);
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(n, x);
        var frame = PushFrame(session, (n, VBNullValue.Null), (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(2, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void AnUnconditionalGoTo_SkipsTheStatementsBetween()
    {
        var list = Lower("GoTo Skip", "x = 999", "Skip:", "x = 1");
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        var frame = PushFrame(session, (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void ExitSub_StopsBeforeTheStatementsAfterIt()
    {
        var list = Lower("x = 1", "Exit Sub", "x = 999");
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        var frame = PushFrame(session, (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void End_StopsExecution_ReportedAsHalt()
    {
        var list = Lower("x = 1", "End", "x = 999");
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        var frame = PushFrame(session, (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Halt, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void Stop_StopsExecution_ReportedAsBreak()
    {
        var list = Lower("x = 1", "Stop", "x = 999");
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        var frame = PushFrame(session, (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Break, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void AnIfBlock_TrueCondition_RunsTheThenBranch_AndSkipsPastEndIf()
    {
        var list = Lower("If True Then", "x = 1", "End If", "x = x + 10");
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        var frame = PushFrame(session, (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(11, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void AnIfBlock_FalseCondition_SkipsTheThenBranch()
    {
        var list = Lower("If False Then", "x = 1", "End If", "x = x + 10");
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        var frame = PushFrame(session, (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(10, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void AnIfElseBlock_FalseCondition_RunsTheElseBranch()
    {
        var list = Lower("If False Then", "x = 1", "Else", "x = 2", "End If");
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        var frame = PushFrame(session, (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(2, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void AnIfElseIfElseBlock_PicksTheMatchingElseIfBranch()
    {
        var list = Lower("If False Then", "x = 1", "ElseIf True Then", "x = 2", "Else", "x = 3", "End If");
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        var frame = PushFrame(session, (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(2, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void AnInlineIf_TrueCondition_RunsItsStatement()
    {
        var list = Lower("If True Then x = 1");
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        var frame = PushFrame(session, (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void AConditionWithNoParentheses_DoesNotRouteThroughTheExplicitCoercionOperator()
        // If x Then has no "(...)" in source, so it must not synthesize the "__c()_op" explicit
        // let-coercion operator (RD-VBAL §5.6.9.9) - a numeric condition still let-coerces to Boolean
        // (MS-VBAL §5.5.1.2.2) via ConditionEvaluator calling VBBooleanLetCoercionRuntimeSemantics
        // directly instead.
    {
        var list = Lower("If x Then", "y = 1", "End If");
        var x = Local("x", VBLongType.TypeInfo);
        var y = Local("y", VBLongType.TypeInfo);
        var session = ComposeSession(x, y);
        var frame = PushFrame(session, (x, new VBLongValue(42)), (y, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(y).Value.BoxedValue);
    }
}
