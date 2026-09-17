using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.Expressions;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for <see cref="InstanceExpressionRuntimeSemantics"/> — MS-VBAL 5.6.11: Me
/// resolves exactly like any other parameter, nothing special-cased. Exercises a real session and a
/// real <c>CallStackFrame</c> so the read is verified through the exact same
/// <c>ISessionSymbols.Resolver</c> every other local/parameter resolves through.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.11 Instance Expressions")]
public sealed class InstanceExpressionRuntimeSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;
    private static readonly SyntaxNodeId NodeId = new(TestUri.TestSubProcUri().AbsolutePath, [1]);

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static IRuntimeSession ComposeSession(params Symbol[] symbols)
        => RuntimeSessionComposer.Compose(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new Provider(symbols));

    // mirrors exactly what SymbolBuilder.BuildParameters synthesizes at slot 0 of a class-module member.
    private static VBParameterSymbol MeParameter(Uri procedureUri)
        => new(Root, procedureUri, "Me", R, R, ParameterKind.ImplicitByRef, VBObjectType.TypeInfo);

    [TestMethod]
    public void Evaluate_MeBoundOnTheActiveFrame_ReturnsTheLiveObjectReference()
    {
        var procedure = new StaticSymbol("DoWork", SymbolKindExt.Procedure, VBVoidType.TypeInfo);
        var meParameter = MeParameter(Root);
        var session = ComposeSession(meParameter);
        var frame = session.Symbols.CreateFrame(NodeId, procedure);
        var objectId = session.Objects.CreateObject();
        frame.Push(meParameter, new VBObjectValue(objectId));
        session.CallStack.TryPush(frame);

        var result = InstanceExpressionRuntimeSemantics.Instance.Evaluate(session, new(), ThrowawayNode(), new VBSymbolDescValue(meParameter));

        Assert.IsNull(result.ErrorInfo, result.ErrorInfo?.Description);
        var objectValue = Assert.IsInstanceOfType<VBObjectValue>(result.Result);
        Assert.AreEqual(objectId, objectValue.Value);
    }

    [TestMethod]
    public void Evaluate_MeNotBoundOnAnyFrame_Throws()
        // the design decision is explicit: Me's binding comes from procedure invocation pushing an
        // activation frame - that machinery doesn't exist yet, so nothing has bound it here.
    {
        var meParameter = MeParameter(Root);
        var session = ComposeSession(meParameter);

        Assert.ThrowsExactly<KeyNotFoundException>(
            () => InstanceExpressionRuntimeSemantics.Instance.Evaluate(session, new(), ThrowawayNode(), new VBSymbolDescValue(meParameter)));
    }

    [TestMethod]
    public void Evaluate_MalformedInput_ReturnsInternalError_DoesNotThrow()
    {
        var session = ComposeSession();

        var result = InstanceExpressionRuntimeSemantics.Instance.Evaluate(session, new(), ThrowawayNode(), new VBLongValue(0));

        Assert.IsTrue(result.IsInternalError);
    }

    private static InstanceExpressionNode ThrowawayNode() => new(NodeId, TestLocations.TestLocation);
}
