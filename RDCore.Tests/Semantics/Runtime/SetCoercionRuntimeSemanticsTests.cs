using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.SetCoercion;
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
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.5.2.2 Set-coercion")]
public sealed class SetCoercionRuntimeSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly ExpressionNode ThrowawayExpression =
        new LiteralExpressionNode(new(TestUri.TestModuleUri().AbsolutePath, [1]), TestLocations.TestLocation, VBUnknownType.TypeInfo.DefaultValue);

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static IRuntimeSession ComposeSession(params Symbol[] symbols)
        => RuntimeSessionComposer.Compose(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new Provider(symbols));

    private static SetCoercionRuntimeSemantics Sut() => new(Substitute.For<IVerboseMessageBuilder>());

    private static VBObjectValue CreateInstance(IRuntimeSession session, VBClassModuleSymbol classModule)
    {
        var objectId = session.Objects.CreateObject();
        session.Symbols.CreateInstance(objectId, classModule);
        return new VBObjectValue(objectId);
    }

    [TestMethod]
    public void NothingSource_ClassDestination_SucceedsAsNothing()
    {
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");
        var session = ComposeSession(widget);

        var result = Sut().EvaluateSetCoercion(session, ThrowawayExpression, VBObjectValue.Nothing, new VBClassType(widget, []));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.IsInstanceOfType<VBObjectValue>(result.Result);
        Assert.IsTrue(((VBObjectValue)result.Result!).IsNothing());
    }

    [TestMethod]
    public void NothingSource_ObjectDestination_SucceedsAsNothing()
    {
        var session = ComposeSession();

        var result = Sut().EvaluateSetCoercion(session, ThrowawayExpression, VBObjectValue.Nothing, VBObjectType.TypeInfo);

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.IsTrue(((VBObjectValue)result.Result!).IsNothing());
    }

    [TestMethod]
    public void NothingSource_VariantDestination_SucceedsAsNothing()
    {
        var session = ComposeSession();

        var result = Sut().EvaluateSetCoercion(session, ThrowawayExpression, VBObjectValue.Nothing, VBVariantType.TypeInfo);

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.IsInstanceOfType<VBVariantValue>(result.Result);
    }

    [TestMethod]
    public void SameClassSource_ClassDestination_SucceedsAsCopyOfReference()
    {
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");
        var session = ComposeSession(widget);
        var instance = CreateInstance(session, widget);

        var result = Sut().EvaluateSetCoercion(session, ThrowawayExpression, instance, new VBClassType(widget, []));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(instance.Value, ((VBObjectValue)result.Result!).Value);
    }

    [TestMethod]
    public void DifferentIncompatibleClassSource_ClassDestination_IsTypeMismatch()
    {
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");
        var gadget = new VBClassModuleSymbol(Root, Root, "Gadget");
        var session = ComposeSession(widget, gadget);
        var instance = CreateInstance(session, widget);

        var result = Sut().EvaluateSetCoercion(session, ThrowawayExpression, instance, new VBClassType(gadget, []));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, result.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void AnyClassSource_ObjectDestination_Succeeds()
    {
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");
        var session = ComposeSession(widget);
        var instance = CreateInstance(session, widget);

        var result = Sut().EvaluateSetCoercion(session, ThrowawayExpression, instance, VBObjectType.TypeInfo);

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
    }

    [TestMethod]
    public void AnyClassSource_VariantDestination_Succeeds()
    {
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");
        var session = ComposeSession(widget);
        var instance = CreateInstance(session, widget);

        var result = Sut().EvaluateSetCoercion(session, ThrowawayExpression, instance, VBVariantType.TypeInfo);

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.IsInstanceOfType<VBVariantValue>(result.Result);
    }

    [TestMethod]
    public void NonObjectSource_ClassDestination_IsObjectRequired()
    {
        var widget = new VBClassModuleSymbol(Root, Root, "Widget");
        var session = ComposeSession(widget);

        var result = Sut().EvaluateSetCoercion(session, ThrowawayExpression, new VBLongValue(5), new VBClassType(widget, []));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual((int)VBRuntimeErrorId.ObjectRequired, result.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void NonObjectSource_ObjectDestination_IsObjectRequired()
    {
        var session = ComposeSession();

        var result = Sut().EvaluateSetCoercion(session, ThrowawayExpression, new VBLongValue(5), VBObjectType.TypeInfo);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual((int)VBRuntimeErrorId.ObjectRequired, result.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void ImplementingClassSource_InterfaceClassDestination_Succeeds()
        // closes the loop this class's own remarks documented as a known limitation: Supertypes only
        // ever matched an exact same class until VBClassModuleSymbol.ImplementedInterfaces existed.
        // Widget Implements IWidget - an instance of Widget must now Set-coerce to IWidget's own
        // declared type, not just to Widget's.
    {
        var iWidget = new VBClassModuleSymbol(Root, Root, "IWidget");
        var widget = new VBClassModuleSymbol(Root, Root, "Widget") { ImplementedInterfaces = [iWidget] };
        var session = ComposeSession(iWidget, widget);
        var instance = CreateInstance(session, widget);

        var result = Sut().EvaluateSetCoercion(session, ThrowawayExpression, instance, VBClassType.FromClassModule(iWidget));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
    }

    [TestMethod]
    public void NonObjectSource_VariantDestination_IsTypeMismatch()
        // MS-VBAL 5.5.2.2.2's own table: unlike Object/Class (Object required), a Variant destination
        // for a non-object source is specifically a Type mismatch - counterintuitive, verified directly
        // against the authoritative Microsoft Learn mirror of this exact table.
    {
        var session = ComposeSession();

        var result = Sut().EvaluateSetCoercion(session, ThrowawayExpression, new VBLongValue(5), VBVariantType.TypeInfo);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, result.ErrorInfo!.ErrorId);
    }
}
