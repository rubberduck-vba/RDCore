using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
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
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for <see cref="BinaryLetAssignmentOperatorRuntimeSemantics"/> — MS-VBAL
/// 5.4.3.8 Let-assignment, modeled as the reserved synthetic "__let_op" binary operator: the source
/// is Let-coerced to the target's declared type, then written through the target's current binding.
/// Exercises real sessions (<see cref="RuntimeSessionComposer"/>) rather than mocks so the write is
/// verified through the exact same <c>ISessionSymbols.Resolver</c> the CallStackFrame reconciliation
/// work made uniform across every heap tier.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.8 Let-assignment")]
public sealed class BinaryLetAssignmentOperatorRuntimeTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;
    private static readonly SyntaxNodeId NodeId = new(TestUri.TestBinaryOpUri().AbsolutePath, [1]);

    private static readonly VBBinaryOperatorExpressionNode ThrowawayBinary = new(
        "__let_op", NodeId, TestLocations.TestLocation,
        [
            new LiteralExpressionNode(default, TestLocations.TestLocationLHS, new VBLongValue(0)),
            new LiteralExpressionNode(default, TestLocations.TestLocationRHS, new VBLongValue(0)),
        ]);

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static IRuntimeSession ComposeSession(params Symbol[] symbols)
        => RuntimeSessionComposer.Compose(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new Provider(symbols));

    private static VBStandardModuleSymbol Module(string name) => new(Root, Root, name);

    private static VBModuleFieldVariableMemberSymbol ModuleField(Uri moduleUri, string name, VBType type)
        => new(Root, moduleUri, name, ScopeKind.Module, type, R, R, AccessModifier.Implicit);

    private static BinaryLetAssignmentOperatorRuntimeSemantics Sut(ILetCoercionRuntimeSemanticsProvider provider)
        => new(provider, Substitute.For<IVerboseMessageBuilder>());

    private static RuntimeSemanticsEvaluationResult Evaluate(
        BinaryLetAssignmentOperatorRuntimeSemantics op, ISymbolResolver resolver, Symbol target, VBTypedValue source)
        => op.Evaluate(resolver, new ConversionOperationSemanticContext(), ThrowawayBinary, new VBSymbolDescValue(target), source);

    [TestMethod]
    public void Evaluate_SameTypeSource_WritesTheValue_ReadableThroughTheResolver()
    {
        var module = Module("Mod1");
        var field = ModuleField(module.Uri, "Total", VBLongType.TypeInfo);
        var session = ComposeSession(module, field);
        var op = Sut(FakeCoercionProvider());

        var result = Evaluate(op, session.Symbols.Resolver, field, new VBLongValue(5));

        Assert.IsNull(result.ErrorInfo, result.ErrorInfo?.Description);
        Assert.AreEqual(5, session.Symbols.Resolver.GetValue(field).Value.BoxedValue);
    }

    [TestMethod]
    public void Evaluate_SourceNeedsRealCoercion_RoundsAndWrites()
        // 2.67 -> 3 via Banker's rounding (MS-VBAL 5.5.1.2.1.1) - proves this operator drives the real
        // let-coercion pipeline, not just a same-type passthrough.
    {
        var module = Module("Mod1");
        var field = ModuleField(module.Uri, "Total", VBLongType.TypeInfo);
        var session = ComposeSession(module, field);
        var op = Sut(RealCoercionProvider());

        var result = Evaluate(op, session.Symbols.Resolver, field, new VBDoubleValue(2.67));

        Assert.IsNull(result.ErrorInfo, result.ErrorInfo?.Description);
        var handle = session.Symbols.Resolver.GetValue(field);
        Assert.AreEqual(3, handle.Value.BoxedValue);
    }

    [TestMethod]
    public void Evaluate_SourceCannotCoerce_ReturnsErrorWithoutWriting()
    {
        var module = Module("Mod1");
        var field = ModuleField(module.Uri, "Total", VBLongType.TypeInfo);
        var session = ComposeSession(module, field);
        var op = Sut(RealCoercionProvider());
        var before = session.Symbols.Resolver.GetValue(field).Value.BoxedValue;

        var result = Evaluate(op, session.Symbols.Resolver, field, new VBStringValue("not a number"));

        Assert.IsNotNull(result.ErrorInfo);
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, result.ErrorInfo!.ErrorId);
        Assert.AreEqual(before, session.Symbols.Resolver.GetValue(field).Value.BoxedValue, "a failed coercion must not have written anything");
    }

    [TestMethod]
    public void Evaluate_LocalVariableTarget_WritesThroughTheActiveCallStackFrame()
        // proves assignment works uniformly across heap tiers: a local resolves and writes through
        // exactly the same ISessionSymbols.Resolver a module field does, once its frame is active.
    {
        var procedure = new StaticSymbol("DoWork", SymbolKindExt.Procedure, VBVoidType.TypeInfo);
        var local = new VBLocalVariableSymbol(Root, Root, "i", ScopeKind.Local, R, R, ResolvedType: VBLongType.TypeInfo);
        var session = ComposeSession(local);
        var frame = session.Symbols.CreateFrame(NodeId, procedure);
        frame.Push(local, VBLongType.TypeInfo.DefaultValue);
        session.CallStack.TryPush(frame);
        var op = Sut(FakeCoercionProvider());

        var result = Evaluate(op, session.Symbols.Resolver, local, new VBLongValue(42));

        Assert.IsNull(result.ErrorInfo, result.ErrorInfo?.Description);
        Assert.AreEqual(42, session.Symbols.Resolver.GetValue(local).Value.BoxedValue);
    }

    [TestMethod]
    public void Evaluate_ReadOnlyTarget_ReturnsInternalError_DoesNotThrow()
        // MS-VBAL static semantics should already reject an assignment to a Const at compile time;
        // this only guards the runtime path against ever throwing if that check were ever skipped.
        // The base OperatorRuntimeSemantics.Evaluate pipeline wraps an InternalError() sentinel into a
        // populated VBRuntimeErrorInfo before it ever reaches a caller of the public Evaluate() entry
        // point - IsInternalError itself only describes that pre-wrap sentinel, never the public result.
    {
        var target = new VBConstantMemberSymbol(Root, Root, "Pi", ScopeKind.Module, VBLongType.TypeInfo, R, R, AccessModifier.Implicit);
        var resolver = Substitute.For<ISymbolResolver>();
        resolver.GetValue(target).Returns(new ConstantBindingHandle(VBLongType.TypeInfo.DefaultValue.RuntimeValue));
        var op = Sut(FakeCoercionProvider());

        var result = Evaluate(op, resolver, target, new VBLongValue(1));

        Assert.IsNotNull(result.ErrorInfo);
        Assert.AreEqual((int)VBRuntimeErrorId.InternalError, result.ErrorInfo!.ErrorId);
    }

    private static ILetCoercionRuntimeSemanticsProvider FakeCoercionProvider()
    {
        var provider = Substitute.For<ILetCoercionRuntimeSemanticsProvider>();
        provider.EvaluateLetCoercionSemantics(default!, default!, default)
            .ReturnsForAnyArgs(call => LetCoercionResult.Success(call.ArgAt<LetCoercionStackFrame>(2).SourceValue));
        return provider;
    }

    private static ILetCoercionRuntimeSemanticsProvider RealCoercionProvider()
    {
        var fmt = Substitute.For<IVerboseMessageBuilder>();
        var handle = new ProviderHandle();
        ILetCoercionRuntimeSemantics[] strategies =
        [
            new VBNumericLetCoercionTypeRuntimeSemantics(fmt, handle),
            new VBStringLetCoercionRuntimeSemantics(fmt),
            new VBDateLetCoercionRuntimeSemantics(handle, fmt),
            new VBBooleanLetCoercionRuntimeSemantics(handle, fmt),
        ];
        var provider = new LetCoercionRuntimeSemanticsProvider(strategies, fmt);
        handle.Inner = provider;
        return provider;
    }

    private sealed class ProviderHandle : ILetCoercionRuntimeSemanticsProvider
    {
        public ILetCoercionRuntimeSemanticsProvider Inner { get; set; } = default!;
        public LetCoercionResult EvaluateLetCoercionSemantics(ISymbolResolver r, VBOperatorExpression e, LetCoercionStackFrame f)
            => Inner.EvaluateLetCoercionSemantics(r, e, f);
        public LetCoercionAnalysisContext Analyze(ISymbolResolver r, ILetCoercionSemanticContextBuilder b, VBOperatorExpression e, LetCoercionStackFrame f)
            => Inner.Analyze(r, b, e, f);
    }
}
