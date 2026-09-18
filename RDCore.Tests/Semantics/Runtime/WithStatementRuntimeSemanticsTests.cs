using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.SetCoercion;
using RDCore.Runtime.Semantics.Statements;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
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
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.4.2.21 With Statement")]
public sealed class WithStatementRuntimeSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SyntaxNodeId NodeId = new(TestUri.TestModuleUri().AbsolutePath, [1]);

    private static readonly SimpleNameExpressionNode WithExpression = new(NodeId, TestLocations.TestLocation, "target");

    private static WithStatementNode WithNode() => new(NodeId, TestLocations.TestLocation, WithExpression, new StatementBlock([]));

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static IRuntimeSession ComposeSession(params Symbol[] symbols)
        => RuntimeSessionComposer.Compose(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new Provider(symbols));

    private static VBObjectValue CreateInstance(IRuntimeSession session, VBClassModuleSymbol classModule)
    {
        var objectId = session.Objects.CreateObject();
        session.Symbols.CreateInstance(objectId, classModule);
        return new VBObjectValue(objectId);
    }

    private static WithStatementRuntimeSemantics Sut(ISetCoercionRuntimeSemantics? setCoercion = null)
        => new(setCoercion ?? new SetCoercionRuntimeSemantics(Substitute.For<IVerboseMessageBuilder>()));

    [TestMethod]
    public void ClassTarget_SucceedsThroughRealSetCoercion()
    {
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");
        var session = ComposeSession(widget);
        var instance = CreateInstance(session, widget);

        var result = Sut().Evaluate(session, new WithStatementSemanticContext(), WithNode(), instance);

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
    }

    [TestMethod]
    public void NothingTarget_Succeeds_DoesNotRaiseObjectVariableNotSet()
        // the With statement's own runtime semantics (MS-VBAL 5.4.2.21) never raise error 91 for a
        // Nothing target - Set-coercion's Nothing-passthrough case (5.5.2.2.1) is a success; 91 would
        // only ever come from a later member-access dereference, which isn't modeled here.
    {
        var session = ComposeSession();

        var result = Sut().Evaluate(session, new WithStatementSemanticContext(), WithNode(), VBObjectValue.Nothing);

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
    }

    [TestMethod]
    public void SetCoercionError_Propagates()
    {
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");
        var gadget = new VBClassModuleSymbol(Root, Root, "Gadget");
        var session = ComposeSession(widget, gadget);
        var instance = CreateInstance(session, widget);
        var setCoercion = Substitute.For<ISetCoercionRuntimeSemantics>();
        setCoercion.EvaluateSetCoercion(default!, default!, default!, default!).ReturnsForAnyArgs(
            SetCoercionResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.TypeMismatch, TestLocations.TestLocation, "mismatch")));

        var result = Sut(setCoercion).Evaluate(session, new WithStatementSemanticContext(), WithNode(), instance);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, result.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void UdtTarget_ReturnsInternalError_DocumentedDeferredGap()
    {
        var udt = new VBUserDefinedTypeMemberSymbol(Root, Root, "TPoint", ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        var session = ComposeSession();
        var value = new VBUserDefinedTypeValue(new VBUserDefinedType(udt, []));

        var result = Sut().Evaluate(session, new WithStatementSemanticContext(), WithNode(), value);

        Assert.IsTrue(result.IsInternalError);
    }
}
