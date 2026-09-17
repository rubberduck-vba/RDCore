using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.Expressions;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for <see cref="NewExpressionRuntimeSemantics"/> — MS-VBAL 5.6.8:
/// evaluating a New expression instantiates a new object and yields it. Exercises real sessions
/// (<see cref="RuntimeSessionComposer"/>) so instantiation is verified through the exact same
/// ISessionObjects/ISessionSymbols the object-instance-storage work wired up.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.8 New Expressions")]
public sealed class NewExpressionRuntimeSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private static readonly NewExpressionNode ThrowawayNew = new(
        new(TestUri.TestModuleUri().AbsolutePath, [1]), TestLocations.TestLocation,
        new SimpleNameExpressionNode(default, TestLocations.TestLocation, "Class1"));

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static IRuntimeSession ComposeSession(params Symbol[] symbols)
        => RuntimeSessionComposer.Compose(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new Provider(symbols));

    private static VBClassModuleSymbol ClassModule(string name = "Class1") => new(Root, Root, name);

    private static VBInstanceFieldVariableMemberSymbol Field(Uri moduleUri, string name)
        => new(Root, moduleUri, name, R, R, VBLongType.TypeInfo, AccessModifier.Implicit);

    [TestMethod]
    public void Evaluate_ClassModuleTarget_CreatesALiveObject_ReadableThroughItsInstance()
    {
        var classModule = ClassModule();
        var field = Field(classModule.Uri, "State");
        var session = ComposeSession(classModule, field);

        var result = NewExpressionRuntimeSemantics.Instance.Evaluate(session, new(), ThrowawayNew, new VBSymbolDescValue(classModule));

        Assert.IsNull(result.ErrorInfo, result.ErrorInfo?.Description);
        var objectValue = Assert.IsInstanceOfType<VBObjectValue>(result.Result);
        Assert.IsFalse(objectValue.IsNothing());
        Assert.IsTrue(session.Symbols.TryGetInstance(objectValue.Value, out var instance));
        Assert.AreEqual(VBLongType.TypeInfo.DefaultValue.Handle, instance!.GetValue(field));
    }

    [TestMethod]
    public void Evaluate_TwoInstancesOfTheSameClass_AreIndependentObjects()
    {
        var classModule = ClassModule();
        var session = ComposeSession(classModule);

        var first = NewExpressionRuntimeSemantics.Instance.Evaluate(session, new(), ThrowawayNew, new VBSymbolDescValue(classModule));
        var second = NewExpressionRuntimeSemantics.Instance.Evaluate(session, new(), ThrowawayNew, new VBSymbolDescValue(classModule));

        var firstValue = (VBObjectValue)first.Result!;
        var secondValue = (VBObjectValue)second.Result!;
        Assert.AreNotEqual(firstValue.Value, secondValue.Value);
    }

    [TestMethod]
    public void Evaluate_NonClassTarget_ReturnsErrorWithoutCreatingAnObject()
    {
        var module = new VBStandardModuleSymbol(Root, Root, "Mod1");
        var field = new VBModuleFieldVariableMemberSymbol(Root, module.Uri, "Total", ScopeKind.Module, VBLongType.TypeInfo, R, R, AccessModifier.Implicit);
        var session = ComposeSession(module, field);

        var result = NewExpressionRuntimeSemantics.Instance.Evaluate(session, new(), ThrowawayNew, new VBSymbolDescValue(field));

        Assert.IsNotNull(result.ErrorInfo);
        Assert.AreEqual((int)VBRuntimeErrorId.ActiveXComponentCantCreateObject, result.ErrorInfo!.ErrorId);
    }
}
