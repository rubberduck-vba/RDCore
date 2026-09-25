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
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// End-to-end coverage for <c>VBObjectLetCoercionRuntimeSemantics</c>'s default-member resolution
/// (MS-VBAL §5.5.1.2.13's "simple data value"): a real class instance, a real default member body, and
/// a real <see cref="RuntimeProcedureInvoker"/>, routed through the provider's central object dispatch.
/// </summary>
[TestClass]
public sealed class VBObjectLetCoercionDefaultMemberTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly Uri ProcedureUri = TestUri.TestSubProcUri();
    private static readonly SyntaxNodeId NodeId = new(ProcedureUri.AbsolutePath, [1]);
    private static readonly SourceRange R = SourceRange.Empty;

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static IRuntimeSession ComposeSession(params Symbol[] symbols)
        => RuntimeSessionComposer.Compose(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new Provider(symbols));

    // VBNumericLetCoercionTypeRuntimeSemantics/VBObjectLetCoercionRuntimeSemantics both need the
    // provider back to recurse; this handle breaks that construction cycle (same pattern used
    // throughout the suite, e.g. RuntimeProcedureInvokerTests).
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

    // Builds the full real let-coercion pipeline (needed since a default member's own body is a real
    // Let-assignment statement, executed through the real ProcedureExecutor/RuntimeProcedureInvoker
    // seam) plus a VBObjectLetCoercionRuntimeSemantics wired with a real Session and ProcedureInvoker.
    private static (ILetCoercionRuntimeSemanticsProvider LetCoercion, IRuntimeSession Session) Compose(
        IReadOnlyDictionary<SemanticId, InstructionList> bodies, params Symbol[] symbols)
    {
        var session = ComposeSession(symbols);
        var formatter = Substitute.For<IVerboseMessageBuilder>();
        var handle = new ProviderHandle();
        var booleanCoercion = new VBBooleanLetCoercionRuntimeSemantics(handle, formatter);
        var numericCoercion = new VBNumericLetCoercionTypeRuntimeSemantics(formatter, handle);
        var variantCoercion = new VBVariantTypeLetCoercionRuntimeSemantics(handle, formatter);
        var objectCoercion = new VBObjectLetCoercionRuntimeSemantics(handle, formatter);
        var letCoercion = new LetCoercionRuntimeSemanticsProvider(
            [numericCoercion, booleanCoercion, variantCoercion, objectCoercion], formatter);
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
        var executor = new ProcedureExecutor(statements, conditions, withTargets, cases, forLoop, forEach, jumpTable, errorHandling);

        var invoker = new RuntimeProcedureInvoker(session, bodies, executor);
        expressionEvaluator.ProcedureInvoker = invoker;
        expressionEvaluator.LetCoercionProvider = letCoercion;
        objectCoercion.ProcedureInvoker = invoker;
        objectCoercion.Session = session;

        return (letCoercion, session);
    }

    private static readonly VBOperatorExpression ThrowawayExpression = new VBBinaryOperatorExpressionNode(
        "+", NodeId, TestLocations.TestLocation,
        [
            new LiteralExpressionNode(default, TestLocations.TestLocationLHS, new VBIntegerValue((short)0)),
            new LiteralExpressionNode(default, TestLocations.TestLocationRHS, new VBIntegerValue((short)0)),
        ]);

    [TestMethod]
    public void AClassWithADefaultMember_LetCoercesToTheDefaultMembersResult()
        // MS-VBAL 5.5.1.2.13: "Any class -> Any type" - the result is the object's simple data value
        // (its default member's own result), let-coerced to the destination declared type.
    {
        var getStub = new VBPropertyGetMemberSymbol(Root, Root, ScopeKind.Instance, "Value", R, R, AccessModifier.Public);
        // SymbolBuilder.BuildParameters synthesizes this same "Me" parameter at slot 0 of every real
        // class-instance member (rdcore-me-implicit-parameter-design) - mirrored here since this test
        // builds the symbol directly, bypassing that builder.
        var me = new VBParameterSymbol(Root, getStub.Uri, "Me", R, R, ParameterKind.ImplicitByRef, VBObjectType.TypeInfo);
        var defaultMember = (VBTypeMemberSymbol)(getStub with { ResolvedType = VBLongType.TypeInfo, Parameters = [me] })
            .With(SymbolProperties.UserMemId, WellKnownDispIds.Value);
        var widget = new VBClassModuleSymbol(Root, Root, "Widget") { Members = [defaultMember] };

        var bodies = new Dictionary<SemanticId, InstructionList> { [defaultMember.SemanticId] = Lower("Value = 42") };
        var (letCoercion, session) = Compose(bodies, widget, defaultMember);

        var instance = session.Symbols.CreateInstance(session.Objects.CreateObject(), widget);
        var source = new VBObjectValue(instance.ObjectId);
        var frame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, source, new VBTypeDescValue(VBLongType.TypeInfo));

        var result = letCoercion.EvaluateLetCoercionSemantics(session.Symbols.Resolver, ThrowawayExpression, frame);

        Assert.IsTrue(result.IsSuccess, $"unexpected error: {(result.ErrorInfo is null ? "none" : ((VBRuntimeErrorId)result.ErrorInfo.ErrorId).ToString())}");
        Assert.IsInstanceOfType<VBLongValue>(result.Result);
        Assert.AreEqual(42, ((VBLongValue)result.Result!).Value);
    }

    [TestMethod]
    public void AClassWithADefaultMemberDeclaringAnOptionalParameter_UsesItsOwnDefaultValue()
        // MS-VBAL 5.5.1.2.13: the default member only needs to be "compatible with an argument list
        // containing 0 parameters" - an Optional parameter beyond Me still qualifies, and Invoke has no
        // default-argument filling of its own, so GetObjectSimpleDataValue supplies it.
    {
        var getStub = new VBPropertyGetMemberSymbol(Root, Root, ScopeKind.Instance, "Value", R, R, AccessModifier.Public);
        var me = new VBParameterSymbol(Root, getStub.Uri, "Me", R, R, ParameterKind.ImplicitByRef, VBObjectType.TypeInfo);
        var n = new VBParameterSymbol(Root, getStub.Uri, "n", R, R, ParameterKind.ExplicitByVal, VBLongType.TypeInfo, IsOptional: true, DefaultValue: new VBLongValue(42));
        var defaultMember = (VBTypeMemberSymbol)(getStub with { ResolvedType = VBLongType.TypeInfo, Parameters = [me, n] })
            .With(SymbolProperties.UserMemId, WellKnownDispIds.Value);
        var widget = new VBClassModuleSymbol(Root, Root, "Widget") { Members = [defaultMember] };

        var bodies = new Dictionary<SemanticId, InstructionList> { [defaultMember.SemanticId] = Lower("Value = n") };
        var (letCoercion, session) = Compose(bodies, widget, defaultMember);

        var instance = session.Symbols.CreateInstance(session.Objects.CreateObject(), widget);
        var source = new VBObjectValue(instance.ObjectId);
        var frame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, source, new VBTypeDescValue(VBLongType.TypeInfo));

        var result = letCoercion.EvaluateLetCoercionSemantics(session.Symbols.Resolver, ThrowawayExpression, frame);

        Assert.IsTrue(result.IsSuccess, $"unexpected error: {(result.ErrorInfo is null ? "none" : ((VBRuntimeErrorId)result.ErrorInfo.ErrorId).ToString())}");
        Assert.AreEqual(42, ((VBLongValue)result.Result!).Value);
    }

    [TestMethod]
    public void AClassWithADefaultMemberDeclaringARequiredParameter_ReportsObjectDoesntSupportThisPropertyOrMethod()
        // a required (non-Optional, non-ParamArray) parameter beyond Me is NOT "compatible with an
        // argument list containing 0 parameters" - MS-VBAL 5.5.1.2.13's own simple-data-value definition
        // excludes it, same as having no default member at all.
    {
        var getStub = new VBPropertyGetMemberSymbol(Root, Root, ScopeKind.Instance, "Value", R, R, AccessModifier.Public);
        var me = new VBParameterSymbol(Root, getStub.Uri, "Me", R, R, ParameterKind.ImplicitByRef, VBObjectType.TypeInfo);
        var n = new VBParameterSymbol(Root, getStub.Uri, "n", R, R, ParameterKind.ExplicitByVal, VBLongType.TypeInfo);
        var defaultMember = (VBTypeMemberSymbol)(getStub with { ResolvedType = VBLongType.TypeInfo, Parameters = [me, n] })
            .With(SymbolProperties.UserMemId, WellKnownDispIds.Value);
        var widget = new VBClassModuleSymbol(Root, Root, "Widget") { Members = [defaultMember] };

        var bodies = new Dictionary<SemanticId, InstructionList>();
        var (letCoercion, session) = Compose(bodies, widget, defaultMember);

        var instance = session.Symbols.CreateInstance(session.Objects.CreateObject(), widget);
        var source = new VBObjectValue(instance.ObjectId);
        var frame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, source, new VBTypeDescValue(VBLongType.TypeInfo));

        var result = letCoercion.EvaluateLetCoercionSemantics(session.Symbols.Resolver, ThrowawayExpression, frame);

        Assert.IsNotNull(result.ErrorInfo);
        Assert.AreEqual(VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, (VBRuntimeErrorId)result.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void AClassWithNoDefaultMember_ReportsObjectDoesntSupportThisPropertyOrMethod()
    {
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");
        var bodies = new Dictionary<SemanticId, InstructionList>();
        var (letCoercion, session) = Compose(bodies, widget);

        var instance = session.Symbols.CreateInstance(session.Objects.CreateObject(), widget);
        var source = new VBObjectValue(instance.ObjectId);
        var frame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, source, new VBTypeDescValue(VBLongType.TypeInfo));

        var result = letCoercion.EvaluateLetCoercionSemantics(session.Symbols.Resolver, ThrowawayExpression, frame);

        Assert.IsNotNull(result.ErrorInfo);
        Assert.AreEqual(VBRuntimeErrorId.ObjectDoesntSupportThisPropertyOrMethod, (VBRuntimeErrorId)result.ErrorInfo!.ErrorId);
    }
}
