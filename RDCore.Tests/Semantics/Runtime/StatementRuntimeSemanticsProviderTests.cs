using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.Runtime.Semantics.SetCoercion;
using RDCore.Runtime.Semantics.Statements;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
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
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// <see cref="StatementRuntimeSemanticsProvider"/> is the statement analogue of
/// <see cref="RuntimeExpressionEvaluator"/>: dispatches a real <see cref="StatementNode"/> by its own
/// C# type, against a real session, no mocked statement semantics.
/// </summary>
[TestClass]
public sealed class StatementRuntimeSemanticsProviderTests
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

    private static SimpleNameExpressionNode SimpleName(string name) => new(NodeId, TestLocations.TestLocation, name);

    private static AssignmentStatementNode Assignment(AssignmentKind kind, ExpressionNode target, ExpressionNode value)
        => new(NodeId, TestLocations.TestLocation, kind, target, value);

    private static StatementRuntimeSemanticsProvider Provider_()
    {
        var formatter = Substitute.For<IVerboseMessageBuilder>();
        var numericCoercion = new VBNumericLetCoercionTypeRuntimeSemantics(formatter, new ProviderHandle());
        var letCoercion = new LetCoercionRuntimeSemanticsProvider([numericCoercion], formatter);
        var expressionEvaluator = new RuntimeExpressionEvaluator(new OperatorRuntimeSemanticsProvider(letCoercion, formatter));
        var print = new PrintOutputEvaluator(expressionEvaluator, new VBStringLetCoercionRuntimeSemantics(formatter), numericCoercion);
        return new StatementRuntimeSemanticsProvider(expressionEvaluator, letCoercion, new SetCoercionRuntimeSemantics(formatter), print, formatter);
    }

    private sealed class ProviderHandle : ILetCoercionRuntimeSemanticsProvider
    {
        public ILetCoercionRuntimeSemanticsProvider Inner { get; set; } = default!;
        public LetCoercionResult EvaluateLetCoercionSemantics(ISymbolResolver resolver, ExpressionNode expression, LetCoercionStackFrame frame)
            => Inner.EvaluateLetCoercionSemantics(resolver, expression, frame);
        public RDCore.SDK.Semantics.Analysis.LetCoercionAnalysisContext Analyze(ISymbolResolver resolver, RDCore.SDK.Semantics.Builders.ILetCoercionSemanticContextBuilder builder, ExpressionNode expression, LetCoercionStackFrame frame)
            => Inner.Analyze(resolver, builder, expression, frame);
    }

    [TestMethod]
    [DataRow(AssignmentKind.ImplicitLet)]
    [DataRow(AssignmentKind.ExplicitLet)]
    public void LetAssignment_WritesTheEvaluatedValue_ReturnsNext(AssignmentKind kind)
    {
        var x = Local("x", VBLongType.TypeInfo);
        var session = ComposeSession(x);
        PushFrame(session, (x, new VBLongValue(0)));

        var statement = Assignment(kind, SimpleName("x"), new LiteralExpressionNode(NodeId, TestLocations.TestLocation, new VBLongValue(42)));
        var outcome = Provider_().Execute(session, new(ProcedureUri), statement);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Next, outcome.Kind);
        Assert.AreEqual(42, session.Symbols.Resolver.GetValue(x).Value.BoxedValue);
    }

    [TestMethod]
    public void LetAssignment_AnUndefinedTarget_DefersAsInternalError()
    {
        var session = ComposeSession();

        var statement = Assignment(AssignmentKind.ImplicitLet, SimpleName("Nowhere"), new LiteralExpressionNode(NodeId, TestLocations.TestLocation, new VBLongValue(1)));
        var outcome = Provider_().Execute(session, new(ProcedureUri), statement);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.InternalError, outcome.Kind);
    }

    [TestMethod]
    public void LetAssignment_AMemberAccessTarget_DefersAsInternalError()
        // needs procedure-invocation machinery (a Property Let call) that doesn't exist yet - same
        // documented scope limit as BinaryLetAssignmentOperatorRuntimeSemantics itself.
    {
        var session = ComposeSession();
        var target = new MemberAccessExpressionNode(NodeId, TestLocations.TestLocation, SimpleName("obj"), SimpleName("Prop"));

        var statement = Assignment(AssignmentKind.ImplicitLet, target, new LiteralExpressionNode(NodeId, TestLocations.TestLocation, new VBLongValue(1)));
        var outcome = Provider_().Execute(session, new(ProcedureUri), statement);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.InternalError, outcome.Kind);
    }

    [TestMethod]
    public void ANotYetWiredStatementKind_DefersAsInternalError()
    {
        var session = ComposeSession();
        var statement = new KeywordStatementNode(NodeId, TestLocations.TestLocation, Tokens.Stop, []);

        var outcome = Provider_().Execute(session, new(ProcedureUri), statement);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.InternalError, outcome.Kind);
    }

    [TestMethod]
    public void SetAssignment_ANewObjectReference_WritesThroughTheHandle_ReturnsNext()
    {
        var classModule = new VBClassModuleSymbol(Root, Root, "Widget");
        var obj = Local("obj", VBObjectType.TypeInfo);
        var session = ComposeSession(classModule, obj);
        PushFrame(session, (obj, VBObjectValue.Nothing));

        var statement = Assignment(AssignmentKind.Set, SimpleName("obj"), new NewExpressionNode(NodeId, TestLocations.TestLocation, SimpleName("Widget")));
        var outcome = Provider_().Execute(session, new(ProcedureUri), statement);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Next, outcome.Kind);
        Assert.AreNotEqual(VBObjectValue.Nothing.RuntimeValue.BoxedValue, session.Symbols.Resolver.GetValue(obj).Value.BoxedValue,
            "Set should have written a live object reference, not left the target as Nothing");
    }

    [TestMethod]
    public void SetAssignment_ANonObjectSource_IsAnError_NotAnInternalError()
        // MS-VBAL §5.5.2.2.2: Set-coercion requires an object reference source - a real, reportable
        // run-time error (Object required / Type mismatch), not this pass defeing to something unwired.
    {
        var obj = Local("obj", VBObjectType.TypeInfo);
        var session = ComposeSession(obj);
        PushFrame(session, (obj, VBObjectValue.Nothing));

        var statement = Assignment(AssignmentKind.Set, SimpleName("obj"), new LiteralExpressionNode(NodeId, TestLocations.TestLocation, new VBLongValue(1)));
        var outcome = Provider_().Execute(session, new(ProcedureUri), statement);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.IsNotNull(outcome.ErrorInfo);
    }

    [TestMethod]
    public void SetAssignment_AnUndefinedTarget_DefersAsInternalError()
    {
        var session = ComposeSession();

        var statement = Assignment(AssignmentKind.Set, SimpleName("Nowhere"), new NewExpressionNode(NodeId, TestLocations.TestLocation, SimpleName("Widget")));
        var outcome = Provider_().Execute(session, new(ProcedureUri), statement);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.InternalError, outcome.Kind);
    }
}
