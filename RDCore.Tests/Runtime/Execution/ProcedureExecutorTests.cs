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
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
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
        var numericCoercion = new VBNumericLetCoercionTypeRuntimeSemantics(formatter, handle);
        var variantCoercion = new VBVariantTypeLetCoercionRuntimeSemantics(handle, formatter);
        var letCoercion = new LetCoercionRuntimeSemanticsProvider(
            [numericCoercion, booleanCoercion, variantCoercion], formatter);
        handle.Inner = letCoercion;
        var expressionEvaluator = new RuntimeExpressionEvaluator(new OperatorRuntimeSemanticsProvider(letCoercion, formatter));
        var statements = new StatementRuntimeSemanticsProvider(expressionEvaluator, letCoercion, new SetCoercionRuntimeSemantics(formatter), formatter);
        var conditions = new ConditionEvaluator(expressionEvaluator, booleanCoercion);
        var withStatement = new WithStatementRuntimeSemantics(new SetCoercionRuntimeSemantics(formatter), letCoercion);
        var withTargets = new WithTargetEvaluator(expressionEvaluator, withStatement);
        var cases = new CaseMatchEvaluator(expressionEvaluator, letCoercion, formatter);
        var forLoop = new ForLoopEvaluator(expressionEvaluator, letCoercion, formatter);
        var forEach = new ForEachEvaluator(expressionEvaluator, letCoercion, new SetCoercionRuntimeSemantics(formatter), formatter);
        var jumpTable = new JumpTableEvaluator(expressionEvaluator, numericCoercion);
        var errorHandling = new ErrorHandlingEvaluator(expressionEvaluator, numericCoercion);
        return new ProcedureExecutor(statements, conditions, withTargets, cases, forLoop, forEach, jumpTable, errorHandling);
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
    public void SelectCase_ANullSelector_SkipsEveryClause_GoesStraightToCaseElse()
        // MS-VBAL 5.4.2.10: "If select-expression is the data value Null, only the case-else-clause is
        // executed" - a real "=" comparison against Null doesn't produce a plain Boolean result, so this
        // also proves the range clause is never actually evaluated for a Null selector, not just skipped
        // after evaluating to False. Null can only ever reach a selector through a Variant (real MS-VBA
        // never lets a Long hold Null), so n is Variant-typed, bound to a Variant wrapping Null -
        // ExecuteCaseHeader's own Null check has to unwrap that box to recognize it.
    {
        var list = Lower("Select Case n", "Case 5", "x = 1", "Case Else", "x = 2", "End Select");
        var n = Local("n", VBVariantType.TypeInfo);
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(n, x);
        var frame = PushFrame(session, (n, new VBVariantValue(VBNullValue.Null)), (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(2, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void ForLoop_CountsFromStartToEndInclusive()
    {
        var list = Lower("For i = 1 To 3", "s = s + i", "Next");
        var i = Local("i", VBLongType.TypeInfo);
        var s = Local("s", VBLongType.TypeInfo);
        var session = ComposeSession(i, s);
        var frame = PushFrame(session, (i, new VBLongValue(0)), (s, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(6, session.Symbols.Resolver.GetValue(s).Value.BoxedValue); // 1 + 2 + 3
        Assert.AreEqual(4, session.Symbols.Resolver.GetValue(i).Value.BoxedValue); // holds its post-loop value
    }

    [TestMethod]
    public void ForLoop_NeverEntersTheBody_WhenStartAlreadyExceedsEnd()
    {
        var list = Lower("For i = 5 To 1", "s = 999", "Next");
        var i = Local("i", VBLongType.TypeInfo);
        var s = Local("s", VBLongType.TypeInfo);
        var session = ComposeSession(i, s);
        var frame = PushFrame(session, (i, new VBLongValue(0)), (s, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(0, session.Symbols.Resolver.GetValue(s).Value.BoxedValue);
        Assert.AreEqual(5, session.Symbols.Resolver.GetValue(i).Value.BoxedValue); // still Let-assigned once
    }

    [TestMethod]
    public void ForLoop_WithAnExplicitStep_CountsByThatStep()
    {
        var list = Lower("For i = 10 To 0 Step -2", "s = s + i", "Next");
        var i = Local("i", VBLongType.TypeInfo);
        var s = Local("s", VBLongType.TypeInfo);
        var session = ComposeSession(i, s);
        var frame = PushFrame(session, (i, new VBLongValue(0)), (s, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(30, session.Symbols.Resolver.GetValue(s).Value.BoxedValue); // 10+8+6+4+2+0
        Assert.AreEqual(-2, session.Symbols.Resolver.GetValue(i).Value.BoxedValue);
    }

    [TestMethod]
    public void ForLoop_WithANonIntegerStep_CountsFractionally()
        // proves Step isn't assumed integral: IsOutOfRange/Increment operate on the counter's own
        // declared type (Double here), not on some internal integer-only stepping assumption.
    {
        var list = Lower("For i = 0 To 2 Step 0.5", "n = n + 1", "Next");
        var i = Local("i", VBDoubleType.TypeInfo);
        var n = Local("n", VBLongType.TypeInfo);
        var session = ComposeSession(i, n);
        var frame = PushFrame(session, (i, new VBDoubleValue(0)), (n, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(5, session.Symbols.Resolver.GetValue(n).Value.BoxedValue); // 0, 0.5, 1, 1.5, 2
        Assert.AreEqual(2.5, session.Symbols.Resolver.GetValue(i).Value.BoxedValue); // post-loop overshoot value
    }

    [TestMethod]
    public void ExitFor_FromANestedIf_BreaksOutCleanly()
    {
        var list = Lower("For i = 1 To 100", "If i = 3 Then Exit For", "s = i", "Next");
        var i = Local("i", VBLongType.TypeInfo);
        var s = Local("s", VBLongType.TypeInfo);
        var session = ComposeSession(i, s);
        var frame = PushFrame(session, (i, new VBLongValue(0)), (s, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(3, session.Symbols.Resolver.GetValue(i).Value.BoxedValue);
        Assert.AreEqual(2, session.Symbols.Resolver.GetValue(s).Value.BoxedValue); // last completed iteration
    }

    [TestMethod]
    public void ForNext_WithNoEnclosingForOpener_ReportsForLoopNotInitialized()
        // MS-VBAL 5.4.2.3: a GoTo landing directly on Next without its own For header ever running is
        // error 92. Hand-built rather than parsed - the real parser/lowering pair always emits a ForNext
        // whose own Matching resolves to a real ForOpener, so this simulates the GoTo-bypass scenario
        // directly against a minimal instruction list.
    {
        var forNext = new Instruction(0, null, InstructionKind.ForNext, 0, [], null, null, 999, null);
        var list = new InstructionList([forNext], new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), new Dictionary<SyntaxNodeId, int>());
        var session = ComposeSession();
        var frame = PushFrame(session);

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.ForLoopNotInitialized, outcome.ErrorInfo!.ErrorId);
    }

    private static VBFixedSizeArrayValue LongArray(params int[] values)
    {
        var array = new VBFixedSizeArrayValue([(0, values.Length - 1)], VBLongType.TypeInfo);
        for (var i = 0; i < values.Length; i++)
        {
            array.TrySetElement(new ValueBindingHandle(new VBLongValue(values[i]).RuntimeValue), i);
        }
        return array;
    }

    [TestMethod]
    public void ForEachLoop_OverAnArray_VisitsEveryElementInOrder()
        // Dim arr(0 To 2) As Long: arr(0)=10, arr(1)=20, arr(2)=30 - a real array LOCAL, round-tripped
        // through Push/GetValue like any other symbol (VBArrayType.CreateValue no longer throws).
    {
        var list = Lower("For Each item In arr", "s = s + item", "Next");
        var item = Local("item", VBLongType.TypeInfo);
        var s = Local("s", VBLongType.TypeInfo);
        var arr = Local("arr", new VBFixedSizeArrayType(VBLongType.TypeInfo));
        var session = ComposeSession(item, s, arr);
        var frame = PushFrame(session, (item, new VBLongValue(0)), (s, new VBLongValue(0)), (arr, LongArray(10, 20, 30)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(60, session.Symbols.Resolver.GetValue(s).Value.BoxedValue);
        Assert.AreEqual(30, session.Symbols.Resolver.GetValue(item).Value.BoxedValue); // holds the last element
    }

    [TestMethod]
    public void LetAssigningAnArrayIntoAVariant_ThenIndexingIt_ReadsTheElement()
        // Dim arr(...) As Long: Dim v As Variant: v = arr: x = v(1) - the full real path, not a
        // pre-built VBVariantValue: Let-assignment itself must wrap the array (MS-VBAL 5.5.1.2.12,
        // "any type except a class or Nothing" - an array is neither), round-trip it through storage,
        // and EvaluateIndex must unwrap it back out again.
    {
        var list = Lower("v = arr", "x = v(1)");
        var x = Local("x", VBLongType.TypeInfo);
        var v = Local("v", VBVariantType.TypeInfo);
        var arr = Local("arr", new VBFixedSizeArrayType(VBLongType.TypeInfo));
        var session = ComposeSession(x, v, arr);
        var frame = PushFrame(session, (x, new VBLongValue(0)), (v, VBVariantType.TypeInfo.DefaultValue), (arr, LongArray(10, 20, 30)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(20, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void IndexingIntoAVariantHoldingAnArray_ReadsTheElement()
        // x = v(1) - same unwrap EvaluateIndex needs for a Variant-typed array as ForEach does.
    {
        var list = Lower("x = v(1)");
        var x = Local("x", VBLongType.TypeInfo);
        var v = Local("v", VBVariantType.TypeInfo);
        var session = ComposeSession(x, v);
        var frame = PushFrame(session, (x, new VBLongValue(0)), (v, new VBVariantValue(LongArray(10, 20, 30))));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(20, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void ForEachLoop_OverAVariantHoldingAnArray_VisitsEveryElementInOrder()
        // Dim v As Variant: v = arr - v's own declared type is Variant, but its bound VALUE is a
        // VBVariantValue wrapping the same array LongArray builds for the declared-array test above;
        // ExecuteForEachOpener must unwrap it to reach the real VBArrayValue underneath.
    {
        var list = Lower("For Each item In v", "s = s + item", "Next");
        var item = Local("item", VBLongType.TypeInfo);
        var s = Local("s", VBLongType.TypeInfo);
        var v = Local("v", VBVariantType.TypeInfo);
        var session = ComposeSession(item, s, v);
        var frame = PushFrame(session, (item, new VBLongValue(0)), (s, new VBLongValue(0)), (v, new VBVariantValue(LongArray(10, 20, 30))));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(60, session.Symbols.Resolver.GetValue(s).Value.BoxedValue);
    }

    [TestMethod]
    public void ForEachLoop_OverAnEmptyArray_NeverEntersTheBody()
        // Dim arr(0 To -1) As Long - a validly-declared, zero-length array (LongArray() with no values):
        // Size is 0, a real frame local like any other array test below (SessionStorage mints it a
        // synthetic address instead of asking the allocator for zero bytes).
    {
        var list = Lower("For Each item In arr", "s = 999", "Next");
        var item = Local("item", VBLongType.TypeInfo);
        var s = Local("s", VBLongType.TypeInfo);
        var arr = Local("arr", new VBFixedSizeArrayType(VBLongType.TypeInfo));
        var session = ComposeSession(item, s, arr);
        var frame = PushFrame(session, (item, new VBLongValue(0)), (s, new VBLongValue(0)), (arr, LongArray()));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(0, session.Symbols.Resolver.GetValue(s).Value.BoxedValue);
    }

    [TestMethod]
    public void ForEachLoop_OverAnUninitializedArray_ReportsForLoopNotInitialized()
        // Dim arr() As Long, never ReDim'd: no dimensions at all, not merely a declared-empty one -
        // real VBA raises error 92 here (the collection was never properly set up), it doesn't silently
        // skip the body the way MS-VBAL §5.4.2.4's "array has no elements" wording covers a zero-length
        // declared array.
    {
        var list = Lower("For Each item In arr", "s = 999", "Next");
        var item = Local("item", VBLongType.TypeInfo);
        var s = Local("s", VBLongType.TypeInfo);
        var arr = Local("arr", new VBFixedSizeArrayType(VBLongType.TypeInfo));
        var session = ComposeSession(item, s, arr);
        var frame = PushFrame(session, (item, new VBLongValue(0)), (s, new VBLongValue(0)), (arr, new VBFixedSizeArrayValue([])));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.ForLoopNotInitialized, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void ExitForEach_FromANestedIf_BreaksOutCleanly()
    {
        var list = Lower("For Each item In arr", "If item = 20 Then Exit For", "s = item", "Next");
        var item = Local("item", VBLongType.TypeInfo);
        var s = Local("s", VBLongType.TypeInfo);
        var arr = Local("arr", new VBFixedSizeArrayType(VBLongType.TypeInfo));
        var session = ComposeSession(item, s, arr);
        var frame = PushFrame(session, (item, new VBLongValue(0)), (s, new VBLongValue(0)), (arr, LongArray(10, 20, 30)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(20, session.Symbols.Resolver.GetValue(item).Value.BoxedValue);
        Assert.AreEqual(10, session.Symbols.Resolver.GetValue(s).Value.BoxedValue); // last completed iteration
    }

    [TestMethod]
    public void ForEachLoop_OverNothing_ReportsObjectVariableNotSet()
        // enumerating an object collection means invoking its _NewEnum member - on Nothing, that
        // invocation itself is MS-VBAL error 91.
    {
        var list = Lower("For Each item In coll", "s = 999", "Next");
        var item = Local("item", VBObjectType.TypeInfo);
        var s = Local("s", VBLongType.TypeInfo);
        var coll = Local("coll", VBObjectType.TypeInfo);
        var session = ComposeSession(item, s, coll);
        var frame = PushFrame(session, (item, VBObjectValue.Nothing), (s, new VBLongValue(0)), (coll, VBObjectValue.Nothing));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.ObjectVariableOrWithBlockVariableNotSet, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void ForEachLoop_OverAnObjectWithNoNewEnumMember_ReportsObjectDoesNotSupportThisPropertyOrMethod()
    {
        var list = Lower("For Each item In coll", "s = 999", "Next");
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");
        var item = Local("item", VBObjectType.TypeInfo);
        var s = Local("s", VBLongType.TypeInfo);
        var coll = Local("coll", VBObjectType.TypeInfo);
        var session = ComposeSession(widget, item, s, coll);
        var instance = session.Symbols.CreateInstance(session.Objects.CreateObject(), widget);
        var frame = PushFrame(session, (item, VBObjectValue.Nothing), (s, new VBLongValue(0)), (coll, new VBObjectValue(instance.ObjectId)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void ForEachLoop_OverAnObjectWithANewEnumMember_IsRecognized_ButDefersAsInternalError()
        // VB_UserMemId = -4 ("_NewEnum") is structurally recognized, but actually enumerating it means
        // invoking it and then the COM IEnumVARIANT-shaped methods on whatever it returns - real
        // procedure invocation, which doesn't exist yet.
    {
        var list = Lower("For Each item In coll", "s = 999", "Next");
        var newEnum = (VBTypeMemberSymbol)new VBFunctionMemberSymbol(Root, Root, "_NewEnum", ScopeKind.Module, SymbolKindExt.Function, VBObjectType.TypeInfo, R, R, AccessModifier.Public)
            .With(SymbolProperties.UserMemId, WellKnownDispIds.NewEnum);
        var widget = new VBClassModuleSymbol(Root, Root, "Widget") { Members = [newEnum] };
        var item = Local("item", VBObjectType.TypeInfo);
        var s = Local("s", VBLongType.TypeInfo);
        var coll = Local("coll", VBObjectType.TypeInfo);
        var session = ComposeSession(widget, newEnum, item, s, coll);
        var instance = session.Symbols.CreateInstance(session.Objects.CreateObject(), widget);
        var frame = PushFrame(session, (item, VBObjectValue.Nothing), (s, new VBLongValue(0)), (coll, new VBObjectValue(instance.ObjectId)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.InternalError, outcome.Kind);
    }

    [TestMethod]
    public void ForEachLoop_OverAScalarValue_ReportsTypeMismatch()
    {
        var list = Lower("For Each item In n", "s = 999", "Next");
        var item = Local("item", VBLongType.TypeInfo);
        var s = Local("s", VBLongType.TypeInfo);
        var n = Local("n", VBLongType.TypeInfo);
        var session = ComposeSession(item, s, n);
        var frame = PushFrame(session, (item, new VBLongValue(0)), (s, new VBLongValue(0)), (n, new VBLongValue(5)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void ForEachNext_WithNoEnclosingForEachOpener_ReportsForLoopNotInitialized()
        // Same shape as ForNext_WithNoEnclosingForOpener_ReportsForLoopNotInitialized - a GoTo landing
        // directly on the closer without its own opener having run is error 92 for For Each too.
    {
        var forEachNext = new Instruction(0, null, InstructionKind.ForEachNext, 0, [], null, null, 999, null);
        var list = new InstructionList([forEachNext], new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), new Dictionary<SyntaxNodeId, int>());
        var session = ComposeSession();
        var frame = PushFrame(session);

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.ForLoopNotInitialized, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void WhileWendLoop_RunsWhileTheConditionIsTrue()
    {
        var list = Lower("While x < 3", "x = x + 1", "Wend");
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        var frame = PushFrame(session, (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(3, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void WhileWendLoop_NeverEntersTheBody_WhenTheConditionStartsFalse()
    {
        var list = Lower("While x < 0", "x = 999", "Wend");
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        var frame = PushFrame(session, (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(0, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void DoWhileLoop_RunsWhileTheConditionIsTrue()
    {
        var list = Lower("Do While x < 3", "x = x + 1", "Loop");
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        var frame = PushFrame(session, (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(3, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void DoUntilLoop_RunsUntilTheConditionIsTrue()
        // proves the opposite polarity from Do While: the condition being tracked is "stop", not "go".
    {
        var list = Lower("Do Until x = 3", "x = x + 1", "Loop");
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        var frame = PushFrame(session, (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(3, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void DoLoopWhile_RunsTheBodyAtLeastOnce_EvenWhenTheConditionStartsFalse()
        // post-test: the body always runs once before the condition is ever checked.
    {
        var list = Lower("Do", "x = x + 1", "Loop While x < 0");
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        var frame = PushFrame(session, (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void DoLoopUntil_RunsUntilTheConditionIsTrue()
    {
        var list = Lower("Do", "x = x + 1", "Loop Until x = 3");
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        var frame = PushFrame(session, (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(3, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void BareDoLoop_RunsUntilExitDo()
        // Do...Loop (no condition of its own) already lowers as a plain unconditional Jump back to its
        // own body - this proves that + ExitLoop's own dispatch together produce a real, terminating loop.
    {
        var list = Lower("Do", "x = x + 1", "If x = 3 Then Exit Do", "Loop");
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        var frame = PushFrame(session, (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(3, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void ExitDo_FromANestedIfInsideAPreTestLoop_BreaksOutCleanly()
    {
        var list = Lower("Do While x < 100", "x = x + 1", "If x = 3 Then Exit Do", "Loop", "y = 1");
        var x = Local("x", VBLongType.TypeInfo);
        var y = Local("y", VBLongType.TypeInfo);
        var session = ComposeSession(x, y);
        var frame = PushFrame(session, (x, new VBLongValue(0)), (y, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(3, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(y).Value.BoxedValue);
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

    [TestMethod]
    public void GoSub_RunsTheHandler_ThenReturnsToTheStatementAfterGoSub()
    {
        var list = Lower("GoSub Handler", "x = x + 1", "Exit Sub", "Handler:", "x = 100", "Return");
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        var frame = PushFrame(session, (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(101, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void Return_WithoutGoSub_ReportsReturnWithoutGoSub()
    {
        var list = Lower("Return");
        var session = ComposeSession();
        var frame = PushFrame(session);

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.ReturnWithoutGoSub, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void NestedGoSub_ReturnsInLifoOrder()
        // First's GoSub Second nests inside First's own handler; each Return must go back to ITS OWN
        // GoSub's continuation, innermost first (MS-VBAL §5.4.2.14's "LIFO manner") - s accumulates a
        // decimal digit per statement, so the final value encodes the exact order they ran in.
    {
        var list = Lower(
            "GoSub First", "Exit Sub",
            "First:", "s = s * 10 + 1", "GoSub Second", "s = s * 10 + 3", "Return",
            "Second:", "s = s * 10 + 2", "Return");
        var s = Local("s", VBLongType.TypeInfo);
        var session = ComposeSession(s);
        var frame = PushFrame(session, (s, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(123, session.Symbols.Resolver.GetValue(s).Value.BoxedValue);
    }

    [TestMethod]
    public void OnGoTo_BranchesToTheNthLabel()
    {
        var list = Lower("On n GoTo A, B, C", "x = 0", "Exit Sub",
            "A:", "x = 1", "Exit Sub", "B:", "x = 2", "Exit Sub", "C:", "x = 3");
        var n = Local("n", VBLongType.TypeInfo);
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(n, x);
        var frame = PushFrame(session, (n, new VBLongValue(2)), (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(2, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void OnGoTo_SelectorOutOfRange_FallsThroughWithoutBranching()
        // MS-VBAL §5.4.2.13: n zero or greater than the label count completes the statement immediately
        // - the next statement still runs, it just isn't reached via a branch.
    {
        var list = Lower("On n GoTo A", "x = 999", "A:");
        var n = Local("n", VBLongType.TypeInfo);
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(n, x);
        var frame = PushFrame(session, (n, new VBLongValue(0)), (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(999, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void OnGoTo_NegativeSelector_ReportsInvalidProcedureCallOrArgument()
    {
        var list = Lower("On n GoTo A", "A:");
        var n = Local("n", VBLongType.TypeInfo);
        var session = ComposeSession(n);
        var frame = PushFrame(session, (n, new VBLongValue(-1)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void OnGoTo_NegativeSelector_IsCatchableLikeAnyOtherRuntimeError()
        // S7 (GoSub/On...GoTo) and S8 (On Error/Resume) were built and merged independently, off the
        // same base commit, so neither's own test suite could prove the two actually compose: an error
        // raised by ExecuteJumpTable/ExecuteReturn must route through InterceptError exactly like every
        // other error-yielding dispatch site, or an active On Error handler silently fails to catch it.
    {
        var list = Lower("On Error Resume Next", "On n GoTo A", "y = 1", "A:");
        var n = Local("n", VBLongType.TypeInfo);
        var y = Local("y", VBLongType.TypeInfo);
        var session = ComposeSession(n, y);
        var frame = PushFrame(session, (n, new VBLongValue(-1)), (y, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(y).Value.BoxedValue);
    }

    [TestMethod]
    public void Return_WithoutGoSub_IsCatchableLikeAnyOtherRuntimeError()
        // Same composition gap as above, proven for Return's own error 3 instead of On...GoTo's error 5.
    {
        var list = Lower("On Error Resume Next", "Return", "y = 1");
        var y = Local("y", VBLongType.TypeInfo);
        var session = ComposeSession(y);
        var frame = PushFrame(session, (y, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(y).Value.BoxedValue);
    }

    [TestMethod]
    public void OnGoSub_BranchesAndPushesReturn_ThenReturnGoesBackAfterTheOnGoSub()
    {
        var list = Lower("On n GoSub A", "x = x + 1", "Exit Sub", "A:", "x = 100", "Return");
        var n = Local("n", VBLongType.TypeInfo);
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(n, x);
        var frame = PushFrame(session, (n, new VBLongValue(1)), (x, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(101, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void OnGoSub_SelectorOutOfRange_FallsThroughWithoutPushingAnything()
        // Proven indirectly: if the out-of-range fallthrough had pushed a resumption point anyway, the
        // Return right after it would find the stack non-empty and branch instead of erroring.
    {
        var list = Lower("On n GoSub A", "Return", "A:", "Return");
        var n = Local("n", VBLongType.TypeInfo);
        var session = ComposeSession(n);
        var frame = PushFrame(session, (n, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.ReturnWithoutGoSub, outcome.ErrorInfo!.ErrorId);
    }

    private static VBParameterSymbol ArrayLocal(string name) => Local(name, new VBFixedSizeArrayType(VBLongType.TypeInfo));

    [TestMethod]
    public void OnErrorResumeNext_SwallowsAnError_AndContinuesAtTheNextStatement()
    {
        var list = Lower("On Error Resume Next", "x = arr(99)", "y = 1");
        var x = Local("x", VBLongType.TypeInfo);
        var y = Local("y", VBLongType.TypeInfo);
        var arr = ArrayLocal("arr");
        var session = ComposeSession(x, y, arr);
        var frame = PushFrame(session, (x, new VBLongValue(0)), (y, new VBLongValue(0)), (arr, LongArray(10, 20, 30)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(0, session.Symbols.Resolver.GetValue(x).Value.BoxedValue); // never assigned - the RHS errored
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(y).Value.BoxedValue);
    }

    [TestMethod]
    public void OnErrorResumeNext_KeepsCatchingEveryError_NotJustTheFirst()
        // Unlike GoTo, catching via Resume Next does not reset the policy - MS-VBAL §5.4.4's own
        // asymmetry between the two.
    {
        var list = Lower("On Error Resume Next", "x = arr(99)", "x = arr(98)", "y = 1");
        var x = Local("x", VBLongType.TypeInfo);
        var y = Local("y", VBLongType.TypeInfo);
        var arr = ArrayLocal("arr");
        var session = ComposeSession(x, y, arr);
        var frame = PushFrame(session, (x, new VBLongValue(0)), (y, new VBLongValue(0)), (arr, LongArray(10, 20, 30)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(y).Value.BoxedValue);
    }

    [TestMethod]
    public void OnErrorGoTo_BranchesToTheHandlerOnError()
    {
        var list = Lower("On Error GoTo Handler", "x = arr(99)", "Exit Sub", "Handler:", "y = 1");
        var x = Local("x", VBLongType.TypeInfo);
        var y = Local("y", VBLongType.TypeInfo);
        var arr = ArrayLocal("arr");
        var session = ComposeSession(x, y, arr);
        var frame = PushFrame(session, (x, new VBLongValue(0)), (y, new VBLongValue(0)), (arr, LongArray(10, 20, 30)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(y).Value.BoxedValue);
    }

    [TestMethod]
    public void OnErrorGoTo_ResetsPolicyToDisabled_SoASecondErrorInsideTheHandlerPropagates()
    {
        var list = Lower("On Error GoTo Handler", "x = arr(99)", "Exit Sub", "Handler:", "y = arr(98)");
        var x = Local("x", VBLongType.TypeInfo);
        var y = Local("y", VBLongType.TypeInfo);
        var arr = ArrayLocal("arr");
        var session = ComposeSession(x, y, arr);
        var frame = PushFrame(session, (x, new VBLongValue(0)), (y, new VBLongValue(0)), (arr, LongArray(10, 20, 30)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.SubscriptOutOfRange, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void OnErrorGoToZero_DisablesHandling_SoALaterErrorPropagates()
    {
        var list = Lower("On Error Resume Next", "On Error GoTo 0", "x = arr(99)");
        var x = Local("x", VBLongType.TypeInfo);
        var arr = ArrayLocal("arr");
        var session = ComposeSession(x, arr);
        var frame = PushFrame(session, (x, new VBLongValue(0)), (arr, LongArray(10, 20, 30)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.SubscriptOutOfRange, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Resume_ReExecutesTheFaultStatement()
        // The handler fixes the condition that faulted (i points at an out-of-range subscript) before
        // Resume re-runs the exact same statement - this time it succeeds.
    {
        var list = Lower("i = 99", "On Error GoTo Handler", "x = arr(i)", "Exit Sub", "Handler:", "i = 1", "Resume");
        var i = Local("i", VBLongType.TypeInfo);
        var x = Local("x", VBLongType.TypeInfo);
        var arr = ArrayLocal("arr");
        var session = ComposeSession(i, x, arr);
        var frame = PushFrame(session, (i, new VBLongValue(0)), (x, new VBLongValue(0)), (arr, LongArray(10, 20, 30)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(20, session.Symbols.Resolver.GetValue(x).Value.BoxedValue); // arr(1)
    }

    [TestMethod]
    public void ResumeNext_Statement_ContinuesAfterTheFaultStatement()
    {
        var list = Lower("On Error GoTo Handler", "x = arr(99)", "y = 1", "Exit Sub", "Handler:", "Resume Next");
        var x = Local("x", VBLongType.TypeInfo);
        var y = Local("y", VBLongType.TypeInfo);
        var arr = ArrayLocal("arr");
        var session = ComposeSession(x, y, arr);
        var frame = PushFrame(session, (x, new VBLongValue(0)), (y, new VBLongValue(0)), (arr, LongArray(10, 20, 30)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(0, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(y).Value.BoxedValue);
    }

    [TestMethod]
    public void Resume_ALabel_BranchesThere()
    {
        var list = Lower("On Error GoTo Handler", "x = arr(99)", "Exit Sub",
            "Handler:", "Resume Done", "y = 999", "Done:", "y = 1");
        var x = Local("x", VBLongType.TypeInfo);
        var y = Local("y", VBLongType.TypeInfo);
        var arr = ArrayLocal("arr");
        var session = ComposeSession(x, y, arr);
        var frame = PushFrame(session, (x, new VBLongValue(0)), (y, new VBLongValue(0)), (arr, LongArray(10, 20, 30)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(y).Value.BoxedValue); // 999 skipped by the branch
    }

    [TestMethod]
    public void Resume_WithNoActiveError_ReportsResumeWithoutError()
    {
        var list = Lower("Resume");
        var session = ComposeSession();
        var frame = PushFrame(session);

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.ResumeWithoutError, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void ErrorStatement_RaisesTheGivenErrorNumber()
    {
        var list = Lower("Error 5");
        var session = ComposeSession();
        var frame = PushFrame(session);

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual(5, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void ErrorStatement_IsCatchableLikeAnyOtherRuntimeError()
    {
        var list = Lower("On Error Resume Next", "Error 5", "y = 1");
        var y = Local("y", VBLongType.TypeInfo);
        var session = ComposeSession(y);
        var frame = PushFrame(session, (y, new VBLongValue(0)));

        var outcome = Executor().Run(session, frame, list, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(y).Value.BoxedValue);
    }
}
