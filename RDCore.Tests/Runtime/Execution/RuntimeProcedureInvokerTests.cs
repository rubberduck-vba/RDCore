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
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// <see cref="RuntimeProcedureInvoker"/> is S9a's own walking skeleton: a real <c>Call</c>/bare-call
/// statement, parsed and lowered for real, invokes a real callee procedure through the full
/// <see cref="IProcedureInvoker"/> seam, ByVal parameters only, same session.
/// </summary>
[TestClass]
public sealed class RuntimeProcedureInvokerTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    // A module-scoped symbol (a Sub, a module field) needs its own Uri actually nested under the same
    // module the caller's own ProcedureUri resolves from - bare Root alone is a different, unrelated
    // scope ResolveValue's local-to-module walk never reaches from within a procedure.
    private static readonly Uri ModuleUri = TestUri.TestModuleUri();
    private static readonly SourceRange R = SourceRange.Empty;
    private static readonly Uri ProcedureUri = TestUri.TestSubProcUri();
    private static readonly SyntaxNodeId NodeId = new(ProcedureUri.AbsolutePath, [1]);

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static IRuntimeSession ComposeSession(params Symbol[] symbols)
        => RuntimeSessionComposer.Compose(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new Provider(symbols));

    // VBNumericLetCoercionTypeRuntimeSemantics needs itself back to coerce a numeric operand
    // recursively; this handle breaks that construction cycle, same pattern used throughout the suite.
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

    // Builds the full real pipeline, wired for procedure calls. ProcedureInvoker is assigned only after
    // every other collaborator - including the invoker itself, which needs a ProcedureExecutor built
    // from a StatementRuntimeSemanticsProvider built from THIS evaluator - is constructed; see
    // RuntimeExpressionEvaluator.ProcedureInvoker's own doc for why it can't be a constructor parameter.
    private static (ProcedureExecutor Executor, IRuntimeSession Session) Compose(IReadOnlyDictionary<SemanticId, InstructionList> bodies, params Symbol[] symbols)
    {
        var session = ComposeSession(symbols);
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
        var forLoop = new ForLoopEvaluator(expressionEvaluator, letCoercion, formatter);
        var forEach = new ForEachEvaluator(expressionEvaluator, letCoercion, new SetCoercionRuntimeSemantics(formatter), formatter);
        var executor = new ProcedureExecutor(statements, conditions, withTargets, cases, forLoop, forEach);
        expressionEvaluator.ProcedureInvoker = new RuntimeProcedureInvoker(session, bodies, executor);
        expressionEvaluator.LetCoercionProvider = letCoercion;
        return (executor, session);
    }

    private static ICallStackFrame PushCallerFrame(IRuntimeSession session)
    {
        var frame = session.Symbols.CreateFrame(NodeId, new StaticSymbol("Caller", SymbolKindExt.Procedure, VBVoidType.TypeInfo));
        session.CallStack.TryPush(frame);
        return frame;
    }

    [TestMethod]
    public void CallStatement_WithParenthesizedArguments_InvokesTheCalleeByVal()
    {
        var counter = new VBModuleFieldVariableMemberSymbol(Root, ModuleUri, "counter", ScopeKind.Module, VBLongType.TypeInfo, R, R, AccessModifier.Implicit);
        var calleeStub = new VBProcedureMemberSymbol(Root, ModuleUri, "Callee", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Public);
        var n = new VBParameterSymbol(Root, calleeStub.Uri, "n", R, R, ParameterKind.ExplicitByVal, VBLongType.TypeInfo);
        var callee = calleeStub with { Parameters = [n] };

        var calleeBody = Lower("counter = counter + n");
        var callerList = Lower("Call Callee(5)");
        var bodies = new Dictionary<SemanticId, InstructionList> { [callee.SemanticId] = calleeBody };

        var (executor, session) = Compose(bodies, counter, callee);
        var frame = PushCallerFrame(session);

        var outcome = executor.Run(session, frame, callerList, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(5, session.Symbols.Resolver.GetValue(counter).Value.BoxedValue);
    }

    [TestMethod]
    public void CallStatement_ByVal_DoesNotWriteBackToTheCaller()
        // ByRef write-back is a later sub-slice's job (S9a's own scope is ByVal only) - the callee
        // mutating its own copy of the parameter must never be visible to the caller's own argument.
    {
        var calleeStub = new VBProcedureMemberSymbol(Root, ModuleUri, "Callee", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Public);
        var n = new VBParameterSymbol(Root, calleeStub.Uri, "n", R, R, ParameterKind.ExplicitByVal, VBLongType.TypeInfo);
        var callee = calleeStub with { Parameters = [n] };

        var calleeBody = Lower("n = 999");
        var callerList = Lower("x = 1", "Call Callee(x)", "y = x");
        var bodies = new Dictionary<SemanticId, InstructionList> { [callee.SemanticId] = calleeBody };

        var x = new VBParameterSymbol(Root, ProcedureUri, "x", R, R, ParameterKind.ImplicitByRef, VBLongType.TypeInfo);
        var y = new VBParameterSymbol(Root, ProcedureUri, "y", R, R, ParameterKind.ImplicitByRef, VBLongType.TypeInfo);
        var (executor, session) = Compose(bodies, callee, x, y);
        var frame = PushCallerFrame(session);
        frame.Push(x, new VBLongValue(0));
        frame.Push(y, new VBLongValue(0));

        var outcome = executor.Run(session, frame, callerList, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(y).Value.BoxedValue);
    }

    [TestMethod]
    public void BareNoArgumentCall_InvokesTheCallee()
    {
        var counter = new VBModuleFieldVariableMemberSymbol(Root, ModuleUri, "counter", ScopeKind.Module, VBLongType.TypeInfo, R, R, AccessModifier.Implicit);
        var callee = new VBProcedureMemberSymbol(Root, ModuleUri, "Callee", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Public);

        var calleeBody = Lower("counter = counter + 1");
        var callerList = Lower("Callee");
        var bodies = new Dictionary<SemanticId, InstructionList> { [callee.SemanticId] = calleeBody };

        var (executor, session) = Compose(bodies, counter, callee);
        var frame = PushCallerFrame(session);

        var outcome = executor.Run(session, frame, callerList, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(1, session.Symbols.Resolver.GetValue(counter).Value.BoxedValue);
    }

    [TestMethod]
    public void NestedCalls_RunInOrder()
        // Caller calls A, A calls B - proves the call stack genuinely nests rather than each Invoke
        // clobbering some single shared slot.
    {
        var log = new VBModuleFieldVariableMemberSymbol(Root, ModuleUri, "log", ScopeKind.Module, VBLongType.TypeInfo, R, R, AccessModifier.Implicit);
        var aStub = new VBProcedureMemberSymbol(Root, ModuleUri, "A", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Public);
        var bSymbol = new VBProcedureMemberSymbol(Root, ModuleUri, "B", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Public);

        var bBody = Lower("log = log * 10 + 2");
        var aBody = Lower("log = log * 10 + 1", "Call B", "log = log * 10 + 3");
        var callerList = Lower("Call A");
        var bodies = new Dictionary<SemanticId, InstructionList>
        {
            [aStub.SemanticId] = aBody,
            [bSymbol.SemanticId] = bBody,
        };

        var (executor, session) = Compose(bodies, log, aStub, bSymbol);
        var frame = PushCallerFrame(session);

        var outcome = executor.Run(session, frame, callerList, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(123, session.Symbols.Resolver.GetValue(log).Value.BoxedValue);
    }

    [TestMethod]
    public void CallStatement_ByRef_WritesBackToTheCaller()
        // The flip side of CallStatement_ByVal_DoesNotWriteBackToTheCaller: an ImplicitByRef parameter
        // (the MS-VBAL default - no ByVal keyword) is a real reference-parameter binding onto the
        // caller's own storage (§5.3.1.11), not a copy - a write inside the callee is visible to the
        // caller the instant it happens.
    {
        var calleeStub = new VBProcedureMemberSymbol(Root, ModuleUri, "Callee", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Public);
        var n = new VBParameterSymbol(Root, calleeStub.Uri, "n", R, R, ParameterKind.ImplicitByRef, VBLongType.TypeInfo);
        var callee = calleeStub with { Parameters = [n] };

        var calleeBody = Lower("n = 999");
        var callerList = Lower("x = 1", "Call Callee(x)", "y = x");
        var bodies = new Dictionary<SemanticId, InstructionList> { [callee.SemanticId] = calleeBody };

        var x = new VBParameterSymbol(Root, ProcedureUri, "x", R, R, ParameterKind.ImplicitByRef, VBLongType.TypeInfo);
        var y = new VBParameterSymbol(Root, ProcedureUri, "y", R, R, ParameterKind.ImplicitByRef, VBLongType.TypeInfo);
        var (executor, session) = Compose(bodies, callee, x, y);
        var frame = PushCallerFrame(session);
        frame.Push(x, new VBLongValue(0));
        frame.Push(y, new VBLongValue(0));

        var outcome = executor.Run(session, frame, callerList, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(999, session.Symbols.Resolver.GetValue(y).Value.BoxedValue);
    }

    [TestMethod]
    public void CallStatement_ByRef_WithANonVariableArgument_FallsBackToACopyAndDoesNotCrash()
        // MS-VBAL §5.3.1.11's own "otherwise" case: a ByRef parameter whose mapped argument isn't a
        // variable (a literal here) still gets a fresh local, exactly like ByVal - never an error.
    {
        var calleeStub = new VBProcedureMemberSymbol(Root, ModuleUri, "Callee", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Public);
        var n = new VBParameterSymbol(Root, calleeStub.Uri, "n", R, R, ParameterKind.ImplicitByRef, VBLongType.TypeInfo);
        var callee = calleeStub with { Parameters = [n] };

        var calleeBody = Lower("n = 999");
        var callerList = Lower("Call Callee(5)");
        var bodies = new Dictionary<SemanticId, InstructionList> { [callee.SemanticId] = calleeBody };

        var (executor, session) = Compose(bodies, callee);
        var frame = PushCallerFrame(session);

        var outcome = executor.Run(session, frame, callerList, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
    }

    [TestMethod]
    public void CallStatement_ByRef_WithAMismatchedArgumentType_FallsBackToACopyAndDoesNotWriteBack()
        // MS-VBAL §5.3.1.11: a ByRef parameter only gets a real reference binding when its declared type
        // exactly matches the argument's (or is Variant) - a Long parameter fed an Integer argument
        // degrades to the same copy-in-only behavior a mismatched ByVal argument already has.
    {
        var calleeStub = new VBProcedureMemberSymbol(Root, ModuleUri, "Callee", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Public);
        var n = new VBParameterSymbol(Root, calleeStub.Uri, "n", R, R, ParameterKind.ImplicitByRef, VBLongType.TypeInfo);
        var callee = calleeStub with { Parameters = [n] };

        var calleeBody = Lower("n = 999");
        var callerList = Lower("Call Callee(x)");
        var bodies = new Dictionary<SemanticId, InstructionList> { [callee.SemanticId] = calleeBody };

        var x = new VBParameterSymbol(Root, ProcedureUri, "x", R, R, ParameterKind.ImplicitByRef, VBIntegerType.TypeInfo);
        var (executor, session) = Compose(bodies, callee, x);
        var frame = PushCallerFrame(session);
        frame.Push(x, new VBIntegerValue(7));

        var outcome = executor.Run(session, frame, callerList, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual((short)7, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void FunctionCall_ReturnsTheDataValueOfItsOwnFunctionResultVariable()
        // MS-VBAL §5.3.1: "return the data value of the result variable to the invocation site as the
        // function result" - a Function assigning its own name inside its body, called from an
        // expression context, yields that value rather than Void.
    {
        var calleeStub = new VBFunctionMemberSymbol(Root, ModuleUri, "Double", ScopeKind.Module, SymbolKindExt.Function, VBLongType.TypeInfo, R, R, AccessModifier.Public);
        var n = new VBParameterSymbol(Root, calleeStub.Uri, "n", R, R, ParameterKind.ExplicitByVal, VBLongType.TypeInfo);
        var callee = calleeStub with { Parameters = [n] };

        var calleeBody = Lower("Double = n * 2");
        var callerList = Lower("y = Double(21)");
        var bodies = new Dictionary<SemanticId, InstructionList> { [callee.SemanticId] = calleeBody };

        var y = new VBParameterSymbol(Root, ProcedureUri, "y", R, R, ParameterKind.ImplicitByRef, VBLongType.TypeInfo);
        var (executor, session) = Compose(bodies, callee, y);
        var frame = PushCallerFrame(session);
        frame.Push(y, new VBLongValue(0));

        var outcome = executor.Run(session, frame, callerList, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(42, session.Symbols.Resolver.GetValue(y).Value.BoxedValue);
    }

    [TestMethod]
    public void PropertyGetCall_ReturnsTheDataValueOfItsOwnFunctionResultVariable()
        // Property Get has the same function result variable mechanism as a Function (MS-VBAL §5.3.1
        // draws no distinction) - proven independently since RuntimeProcedureInvoker/RuntimeExpressionEvaluator
        // both branch on VBFunctionMemberSymbol or VBPropertyGetMemberSymbol, never just one.
    {
        var getStub = new VBPropertyGetMemberSymbol(Root, ModuleUri, ScopeKind.Module, "Value", R, R, AccessModifier.Public);
        var callee = getStub with { ResolvedType = VBLongType.TypeInfo };

        var calleeBody = Lower("Value = 7");
        var callerList = Lower("y = Value");
        var bodies = new Dictionary<SemanticId, InstructionList> { [callee.SemanticId] = calleeBody };

        var y = new VBParameterSymbol(Root, ProcedureUri, "y", R, R, ParameterKind.ImplicitByRef, VBLongType.TypeInfo);
        var (executor, session) = Compose(bodies, callee, y);
        var frame = PushCallerFrame(session);
        frame.Push(y, new VBLongValue(0));

        var outcome = executor.Run(session, frame, callerList, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(7, session.Symbols.Resolver.GetValue(y).Value.BoxedValue);
    }

    [TestMethod]
    public void BareFunctionNameInsideItsOwnBody_ReadsTheResultVariableWithoutRecursing()
        // A bare "Peek" with no parentheses, from WITHIN Peek's own body, reads its function result
        // variable rather than recursing (that needs Foo(args), see the recursion test below) - if this
        // were mistaken for a call instead, "Peek = Peek + 1" would recurse until Out of Stack Space
        // instead of reading 41 back and producing 42.
    {
        var callee = new VBFunctionMemberSymbol(Root, ModuleUri, "Peek", ScopeKind.Module, SymbolKindExt.Function, VBLongType.TypeInfo, R, R, AccessModifier.Public);

        var calleeBody = Lower("Peek = 41", "Peek = Peek + 1");
        var callerList = Lower("y = Peek");
        var bodies = new Dictionary<SemanticId, InstructionList> { [callee.SemanticId] = calleeBody };

        var y = new VBParameterSymbol(Root, ProcedureUri, "y", R, R, ParameterKind.ImplicitByRef, VBLongType.TypeInfo);
        var (executor, session) = Compose(bodies, callee, y);
        var frame = PushCallerFrame(session);
        frame.Push(y, new VBLongValue(0));

        var outcome = executor.Run(session, frame, callerList, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(42, session.Symbols.Resolver.GetValue(y).Value.BoxedValue);
    }

    [TestMethod]
    public void RecursiveFunctionCall_RecursesViaParenthesizedArguments()
        // Foo(args), even from within Foo's own body, always resolves through EvaluateIndex's own
        // TryResolveCallableSub - the only shape that actually recurses (5! = 120).
    {
        var factStub = new VBFunctionMemberSymbol(Root, ModuleUri, "Fact", ScopeKind.Module, SymbolKindExt.Function, VBLongType.TypeInfo, R, R, AccessModifier.Public);
        var n = new VBParameterSymbol(Root, factStub.Uri, "n", R, R, ParameterKind.ExplicitByVal, VBLongType.TypeInfo);
        var fact = factStub with { Parameters = [n] };

        var factBody = Lower(
            "If n <= 1 Then",
            "Fact = 1",
            "Else",
            "Fact = n * Fact(n - 1)",
            "End If");
        var callerList = Lower("y = Fact(5)");
        var bodies = new Dictionary<SemanticId, InstructionList> { [fact.SemanticId] = factBody };

        var y = new VBParameterSymbol(Root, ProcedureUri, "y", R, R, ParameterKind.ImplicitByRef, VBLongType.TypeInfo);
        var (executor, session) = Compose(bodies, fact, y);
        var frame = PushCallerFrame(session);
        frame.Push(y, new VBLongValue(0));

        var outcome = executor.Run(session, frame, callerList, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind);
        Assert.AreEqual(120, session.Symbols.Resolver.GetValue(y).Value.BoxedValue);
    }

    [TestMethod]
    public void ACalleeRaisingAnError_PropagatesToTheCaller()
        // Proves the round trip a nested call takes through RuntimeProcedureInvoker.Invoke: the callee's
        // own Run() produces an Error outcome, Invoke turns it into a RuntimeSemanticsEvaluationResult
        // error, and ExecuteCall turns THAT back into an Error outcome for the caller's own Run() loop -
        // not just that Error outcomes propagate at all (every other slice's tests already prove that).
        // n \ 0 (integer division) uses only the callee's own parameter, no extra locals - a callee
        // frame only ever has its declared parameters pushed onto it (S9a doesn't hoist Dim locals yet).
    {
        var calleeStub = new VBProcedureMemberSymbol(Root, ModuleUri, "Callee", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Public);
        var n = new VBParameterSymbol(Root, calleeStub.Uri, "n", R, R, ParameterKind.ExplicitByVal, VBLongType.TypeInfo);
        var callee = calleeStub with { Parameters = [n] };

        var calleeBody = Lower("n = n \\ 0");
        var callerList = Lower("Call Callee(5)");
        var bodies = new Dictionary<SemanticId, InstructionList> { [callee.SemanticId] = calleeBody };

        var (executor, session) = Compose(bodies, callee);
        var frame = PushCallerFrame(session);

        var outcome = executor.Run(session, frame, callerList, new RuntimeEvaluationContext(ProcedureUri));

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.DivisionByZero, outcome.ErrorInfo!.ErrorId);
    }
}
