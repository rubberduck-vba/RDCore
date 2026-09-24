using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// <see cref="RuntimeExpressionEvaluator"/> is the runtime analogue of
/// <c>ExpressionStaticSemanticsEvaluator</c>: given a real, arbitrarily-nested expression tree, it
/// evaluates every node end to end through a real <see cref="IRuntimeSession"/>, short-circuiting on
/// the first error. Exercises the exact same public <c>Evaluate</c> entry point a future interpreter uses.
/// </summary>
[TestClass]
public sealed class RuntimeExpressionEvaluatorTests
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

    private static ICallStackFrame PushFrame(IRuntimeSession session)
    {
        var procedure = new StaticSymbol("DoWork", SymbolKindExt.Procedure, VBVoidType.TypeInfo);
        var frame = session.Symbols.CreateFrame(NodeId, procedure);
        session.CallStack.TryPush(frame);
        return frame;
    }

    private static VBParameterSymbol Local(string name, VBType type)
        => new(Root, ProcedureUri, name, R, R, ParameterKind.ImplicitByRef, type);

    private static RuntimeExpressionEvaluator Evaluator()
        => new(new OperatorRuntimeSemanticsProvider(RealCoercionProvider(), Substitute.For<IVerboseMessageBuilder>()));

    // The real Numeric let-coercion strategy - for operators to determine their effective type for
    // real, rather than the identity passthrough a bare NSubstitute fake would give every operand.
    private static ILetCoercionRuntimeSemanticsProvider RealCoercionProvider()
    {
        var formatter = Substitute.For<IVerboseMessageBuilder>();
        var handle = new ProviderHandle();
        ILetCoercionRuntimeSemantics[] strategies = [new VBNumericLetCoercionTypeRuntimeSemantics(formatter, handle)];
        var provider = new LetCoercionRuntimeSemanticsProvider(strategies, formatter);
        handle.Inner = provider;
        return provider;
    }

    private sealed class ProviderHandle : ILetCoercionRuntimeSemanticsProvider
    {
        public ILetCoercionRuntimeSemanticsProvider Inner { get; set; } = default!;
        public LetCoercionResult EvaluateLetCoercionSemantics(ISymbolResolver resolver, ExpressionNode expression, LetCoercionStackFrame frame)
            => Inner.EvaluateLetCoercionSemantics(resolver, expression, frame);
        public RDCore.SDK.Semantics.Analysis.LetCoercionAnalysisContext Analyze(ISymbolResolver resolver, RDCore.SDK.Semantics.Builders.ILetCoercionSemanticContextBuilder builder, ExpressionNode expression, LetCoercionStackFrame frame)
            => Inner.Analyze(resolver, builder, expression, frame);
    }

    private static SimpleNameExpressionNode SimpleName(string name) => new(NodeId, TestLocations.TestLocation, name);

    [TestMethod]
    public void SimpleName_ReadsTheCurrentlyBoundLocalValue()
    {
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        PushFrame(session).Push(x, new VBLongValue(42));

        var result = Evaluator().Evaluate(session, SimpleName("x"), new(ProcedureUri));

        Assert.IsNull(result.ErrorInfo, result.ErrorInfo?.Description);
        Assert.AreEqual(42, ((VBLongValue)result.Result!).Value);
    }

    [TestMethod]
    public void SimpleName_ReadsAModuleField_NotMisreadAsAnImplicitCall()
        // Regression: VBModuleFieldVariableMemberSymbol (and Const/EnumConst/instance/UDT fields) are
        // ALSO VBReturningMemberSymbol - the same base Function/Property Get share - so a check against
        // that base type alone would wrongly treat a plain field read as an implicit call. Never
        // reachable before S9a's own tests were the first to read a module-level symbol by bare name.
    {
        var moduleUri = TestUri.TestModuleUri();
        var counter = new VBModuleFieldVariableMemberSymbol(Root, moduleUri, "counter", ScopeKind.Module, VBLongType.TypeInfo, R, R, AccessModifier.Implicit);
        var session = ComposeSession(counter);

        var result = Evaluator().Evaluate(session, SimpleName("counter"), new(ProcedureUri));

        Assert.IsNull(result.ErrorInfo, result.ErrorInfo?.Description);
        Assert.AreEqual(0, ((VBLongValue)result.Result!).Value);
    }

    [TestMethod]
    public void SimpleName_AnUndefinedName_ReturnsInternalError_DoesNotThrow()
    {
        var session = ComposeSession();

        var result = Evaluator().Evaluate(session, SimpleName("Nowhere"), new(ProcedureUri));

        Assert.IsTrue(result.IsInternalError);
    }

    [TestMethod]
    public void Me_ResolvesTheImplicitParameterZero_LikeInstanceExpressionRuntimeSemanticsDoes()
    {
        var meParameter = new VBParameterSymbol(Root, ProcedureUri, Tokens.Me, R, R, ParameterKind.ImplicitByRef, VBObjectType.TypeInfo);
        var session = ComposeSession(meParameter);
        var objectId = session.Objects.CreateObject();
        PushFrame(session).Push(meParameter, new VBObjectValue(objectId));

        var result = Evaluator().Evaluate(session, new InstanceExpressionNode(NodeId, TestLocations.TestLocation), new(ProcedureUri));

        Assert.IsNull(result.ErrorInfo, result.ErrorInfo?.Description);
        Assert.AreEqual(objectId, ((VBObjectValue)result.Result!).Value);
    }

    [TestMethod]
    public void New_CreatesALiveObject_ReadableThroughItsInstance()
    {
        var classModule = new VBClassModuleSymbol(Root, Root, "Class1");
        var session = ComposeSession(classModule);
        var newExpression = new NewExpressionNode(NodeId, TestLocations.TestLocation, SimpleName("Class1"));

        var result = Evaluator().Evaluate(session, newExpression, new(ProcedureUri));

        Assert.IsNull(result.ErrorInfo, result.ErrorInfo?.Description);
        var objectValue = Assert.IsInstanceOfType<VBObjectValue>(result.Result);
        Assert.IsFalse(objectValue.IsNothing());
        Assert.IsTrue(session.Symbols.TryGetInstance(objectValue.Value, out _));
    }

    [TestMethod]
    public void MemberAccess_ReadsAnInstanceField()
    {
        var classModule = new VBClassModuleSymbol(Root, Root, "Class1");
        var field = new VBInstanceFieldVariableMemberSymbol(Root, classModule.Uri, "State", R, R, VBLongType.TypeInfo, AccessModifier.Implicit);
        classModule = classModule with { DefaultInterfaceMembers = [field] };
        var obj = Local("obj", VBObjectType.TypeInfo);
        var session = ComposeSession(classModule, field, obj);
        var instance = session.Symbols.CreateInstance(session.Objects.CreateObject(), classModule);
        instance.GetValue(field).SetValue(session.Symbols.Resolver, new RDCore.SDK.Model.Values.Runtime.VBRuntimeValue<int>(7));
        PushFrame(session).Push(obj, new VBObjectValue(instance.ObjectId));

        var memberAccess = new MemberAccessExpressionNode(NodeId, TestLocations.TestLocation, SimpleName("obj"), new SimpleNameExpressionNode(NodeId, TestLocations.TestLocation, "State"));
        var result = Evaluator().Evaluate(session, memberAccess, new(ProcedureUri));

        Assert.IsNull(result.ErrorInfo, result.ErrorInfo?.Description);
        Assert.AreEqual(7, ((VBLongValue)result.Result!).Value);
    }

    [TestMethod]
    public void MemberAccess_APropertyOrMethodMember_DefersAsInternalError()
        // a call is not a field read, and must never be silently (and wrongly) treated as one.
    {
        var classModule = new VBClassModuleSymbol(Root, Root, "Class1");
        var method = new VBProcedureMemberSymbol(Root, classModule.Uri, "DoSomething", ScopeKind.Instance, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Implicit);
        classModule = classModule with { DefaultInterfaceMembers = [method] };
        var obj = Local("obj", VBObjectType.TypeInfo);
        var session = ComposeSession(classModule, method, obj);
        var instance = session.Symbols.CreateInstance(session.Objects.CreateObject(), classModule);
        PushFrame(session).Push(obj, new VBObjectValue(instance.ObjectId));

        var memberAccess = new MemberAccessExpressionNode(NodeId, TestLocations.TestLocation, SimpleName("obj"), new SimpleNameExpressionNode(NodeId, TestLocations.TestLocation, "DoSomething"));
        var result = Evaluator().Evaluate(session, memberAccess, new(ProcedureUri));

        Assert.IsTrue(result.IsInternalError);
    }

    // The array itself arrives as a literal's already-known static value here - SimpleName_
    // ReadsTheCurrentlyBoundArrayLocalValue_CellsIntact below is what exercises the frame
    // Push/GetValue round trip; nothing about Index's own dispatch depends on it.
    [TestMethod]
    public void Index_ReadsAnArrayElement()
    {
        var array = new VBFixedSizeArrayValue([(1, 3)], VBLongType.TypeInfo);
        array.TrySetElement(new RDCore.SDK.Model.Values.Bindings.ValueBindingHandle(new RDCore.SDK.Model.Values.Runtime.VBRuntimeValue<int>(99)), 2);
        var session = ComposeSession();

        var index = new IndexExpressionNode(NodeId, TestLocations.TestLocation, new LiteralExpressionNode(NodeId, TestLocations.TestLocation, array),
            [new LiteralExpressionNode(NodeId, TestLocations.TestLocation, new VBIntegerValue(2))]);
        var result = Evaluator().Evaluate(session, index, new(ProcedureUri));

        Assert.IsNull(result.ErrorInfo, result.ErrorInfo?.Description);
        Assert.AreEqual(99, ((VBLongValue)result.Result!).Value);
    }

    [TestMethod]
    public void Index_OutOfRangeSubscript_ReturnsSubscriptOutOfRange()
    {
        var array = new VBFixedSizeArrayValue([(1, 3)], VBLongType.TypeInfo);
        var session = ComposeSession();

        var index = new IndexExpressionNode(NodeId, TestLocations.TestLocation, new LiteralExpressionNode(NodeId, TestLocations.TestLocation, array),
            [new LiteralExpressionNode(NodeId, TestLocations.TestLocation, new VBIntegerValue(9))]);
        var result = Evaluator().Evaluate(session, index, new(ProcedureUri));

        Assert.AreEqual((int)VBRuntimeErrorId.SubscriptOutOfRange, result.ErrorInfo?.ErrorId);
    }

    [TestMethod]
    public void SimpleName_ReadsTheCurrentlyBoundArrayLocalValue_CellsIntact()
    {
        var arr = Local("arr", new VBFixedSizeArrayType(VBLongType.TypeInfo));
        var session = ComposeSession(arr);
        var array = new VBFixedSizeArrayValue([(1, 3)], VBLongType.TypeInfo);
        array.TrySetElement(new RDCore.SDK.Model.Values.Bindings.ValueBindingHandle(new RDCore.SDK.Model.Values.Runtime.VBRuntimeValue<int>(99)), 2);
        PushFrame(session).Push(arr, array);

        var result = Evaluator().Evaluate(session, SimpleName("arr"), new(ProcedureUri));

        Assert.IsNull(result.ErrorInfo, result.ErrorInfo?.Description);
        var roundTripped = (VBArrayValue)result.Result!;
        Assert.AreEqual(99, ((VBLongValue)roundTripped[2]!).Value);
    }

    [TestMethod]
    public void TypeOfIs_TheObjectsOwnClass_IsTrue()
    {
        var classModule = new VBClassModuleSymbol(Root, Root, "Class1");
        var obj = Local("obj", VBObjectType.TypeInfo);
        var session = ComposeSession(classModule, obj);
        var instance = session.Symbols.CreateInstance(session.Objects.CreateObject(), classModule);
        PushFrame(session).Push(obj, new VBObjectValue(instance.ObjectId));

        var typeOfIs = new TypeOfIsExpressionNode(NodeId, TestLocations.TestLocation, SimpleName("obj"), SimpleName("Class1"));
        var result = Evaluator().Evaluate(session, typeOfIs, new(ProcedureUri));

        Assert.IsNull(result.ErrorInfo, result.ErrorInfo?.Description);
        Assert.AreEqual(VBBooleanValue.True, result.Result);
    }

    [TestMethod]
    public void TypeOfIs_AnUnrelatedClass_IsFalse()
    {
        var actualClass = new VBClassModuleSymbol(Root, Root, "Class1");
        var otherClass = new VBClassModuleSymbol(Root, Root, "Class2");
        var obj = Local("obj", VBObjectType.TypeInfo);
        var session = ComposeSession(actualClass, otherClass, obj);
        var instance = session.Symbols.CreateInstance(session.Objects.CreateObject(), actualClass);
        PushFrame(session).Push(obj, new VBObjectValue(instance.ObjectId));

        var typeOfIs = new TypeOfIsExpressionNode(NodeId, TestLocations.TestLocation, SimpleName("obj"), SimpleName("Class2"));
        var result = Evaluator().Evaluate(session, typeOfIs, new(ProcedureUri));

        Assert.IsNull(result.ErrorInfo, result.ErrorInfo?.Description);
        Assert.AreEqual(VBBooleanValue.False, result.Result);
    }

    [TestMethod]
    public void TypeOfIs_ThroughAnImplementedInterface_IsTrue()
    {
        var iface = new VBClassModuleSymbol(Root, Root, "IShape");
        var classModule = new VBClassModuleSymbol(Root, Root, "Circle") { ImplementedInterfaces = [iface] };
        var obj = Local("obj", VBObjectType.TypeInfo);
        var session = ComposeSession(iface, classModule, obj);
        var instance = session.Symbols.CreateInstance(session.Objects.CreateObject(), classModule);
        PushFrame(session).Push(obj, new VBObjectValue(instance.ObjectId));

        var typeOfIs = new TypeOfIsExpressionNode(NodeId, TestLocations.TestLocation, SimpleName("obj"), SimpleName("IShape"));
        var result = Evaluator().Evaluate(session, typeOfIs, new(ProcedureUri));

        Assert.IsNull(result.ErrorInfo, result.ErrorInfo?.Description);
        Assert.AreEqual(VBBooleanValue.True, result.Result);
    }

    [TestMethod]
    public void BinaryOperator_EvaluatesBothOperandsFirst_ThenTheOperator()
    {
        var x = Local("x", VBLongType.TypeInfo);
        var y = Local("y", VBLongType.TypeInfo);
        var session = ComposeSession(x, y);
        var frame = PushFrame(session);
        frame.Push(x, new VBLongValue(1));
        frame.Push(y, new VBLongValue(2));

        var addition = new VBBinaryOperatorExpressionNode(Tokens.AdditionOp, NodeId, TestLocations.TestLocation, SimpleName("x"), SimpleName("y"));
        var result = Evaluator().Evaluate(session, addition, new(ProcedureUri));

        Assert.IsNull(result.ErrorInfo, result.ErrorInfo?.Description);
        Assert.AreEqual(3, ((VBLongValue)result.Result!).Value);
    }

    [TestMethod]
    public void UnaryOperator_NegatesItsOperand()
    {
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        PushFrame(session).Push(x, new VBLongValue(5));

        var negation = new VBUnaryOperatorExpressionNode(Tokens.NegationOp, NodeId, TestLocations.TestLocation, [SimpleName("x")]);
        var result = Evaluator().Evaluate(session, negation, new(ProcedureUri));

        Assert.IsNull(result.ErrorInfo, result.ErrorInfo?.Description);
        Assert.AreEqual(-5, ((VBLongValue)result.Result!).Value);
    }

    [TestMethod]
    public void BinaryOperator_AnUndefinedLeftOperand_ShortCircuits_TheRightOperandIsNeverTouched()
        // the right operand resolves to a real, declared symbol that was never pushed onto any frame -
        // evaluating it would throw KeyNotFoundException (see InstanceExpressionRuntimeSemanticsTests'
        // own unbound-read case). Reaching a clean InternalError result instead of an uncaught exception
        // is the proof the right operand was never evaluated at all.
    {
        var y = Local("y", VBLongType.TypeInfo);
        var session = ComposeSession(y);

        var addition = new VBBinaryOperatorExpressionNode(Tokens.AdditionOp, NodeId, TestLocations.TestLocation, SimpleName("Nowhere"), SimpleName("y"));
        var result = Evaluator().Evaluate(session, addition, new(ProcedureUri));

        Assert.IsTrue(result.IsInternalError);
    }

    [TestMethod]
    public void SimpleName_ABareFunctionReference_ReturnsInternalError_DoesNotThrow()
        // MS-VBAL §5.6.10: a bare reference to a Function is an implicit call, not a value read.
    {
        var function = new VBFunctionMemberSymbol(Root, ProcedureUri, "DoStuff", ScopeKind.Module, SymbolKindExt.Function, VBLongType.TypeInfo, R, R, AccessModifier.Public);
        var session = ComposeSession(function);

        var result = Evaluator().Evaluate(session, SimpleName("DoStuff"), new(ProcedureUri));

        Assert.IsTrue(result.IsInternalError);
    }

    [TestMethod]
    public void AddressOf_NeverEvaluatesItsTarget_EvenWhenTheTargetWouldOtherwiseSucceed()
    {
        var array = new VBFixedSizeArrayValue([(1, 3)], VBLongType.TypeInfo);
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        PushFrame(session).Push(x, new VBLongValue(2));

        var addressOfX = new AddressOfExpressionNode(NodeId, TestLocations.TestLocation, SimpleName("x"));
        var index = new IndexExpressionNode(NodeId, TestLocations.TestLocation,
            new LiteralExpressionNode(NodeId, TestLocations.TestLocation, array), [addressOfX]);

        var result = Evaluator().Evaluate(session, index, new(ProcedureUri));

        Assert.IsTrue(result.IsInternalError);
    }

    [TestMethod]
    public void NestedExpression_EvaluatesEndToEnd()
        // (x + y) * 2
    {
        var x = Local("x", VBLongType.TypeInfo);
        var y = Local("y", VBLongType.TypeInfo);
        var session = ComposeSession(x, y);
        var frame = PushFrame(session);
        frame.Push(x, new VBLongValue(3));
        frame.Push(y, new VBLongValue(4));

        var sum = new VBBinaryOperatorExpressionNode(Tokens.AdditionOp, NodeId, TestLocations.TestLocation, SimpleName("x"), SimpleName("y"));
        var literalTwo = new LiteralExpressionNode(NodeId, TestLocations.TestLocation, new VBLongValue(2));
        var product = new VBBinaryOperatorExpressionNode(Tokens.MultiplicationOp, NodeId, TestLocations.TestLocation, sum, literalTwo);

        var result = Evaluator().Evaluate(session, product, new(ProcedureUri));

        Assert.IsNull(result.ErrorInfo, result.ErrorInfo?.Description);
        Assert.AreEqual(14, ((VBLongValue)result.Result!).Value);
    }
}
