using RDCore.Runtime.Semantics.Operators.Relational;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for the <c>Is</c> operator runtime semantics: reference-identity
/// comparison of two object references (RD-VBAL §5.0.2.1).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.9.7 Binary 'Is' Operator")]
public sealed class BinaryIsRelationalOperatorRuntimeTests : OperatorRelationalRuntimeSemanticsTests
{
    private BinaryIsRelationalOperatorRuntimeSemantics Is() => new(FakeProvider(), Formatter());

    [TestMethod]
    public void SameReference_True()
    {
        var reference = new VBObjectValue(new VBRuntimeObjectId());
        AssertResult<VBBooleanValue>(Evaluate(Is(), reference, reference), true);
    }

    [TestMethod]
    public void DifferentReferences_False()
        => AssertResult<VBBooleanValue>(
            Evaluate(Is(), new VBObjectValue(new VBRuntimeObjectId()), new VBObjectValue(new VBRuntimeObjectId())), false);

    [TestMethod]
    public void NothingIsNothing_True()
        => AssertResult<VBBooleanValue>(Evaluate(Is(), VBObjectValue.Nothing, VBObjectValue.Nothing), true);

    [TestMethod]
    public void ObjectIsNotNothing_False()
        => AssertResult<VBBooleanValue>(Evaluate(Is(), new VBObjectValue(new VBRuntimeObjectId()), VBObjectValue.Nothing), false);

    // MS-VBAL 5.6.9.7: "the declared type of each expression MUST be a specific class, Object or Variant" - a Variant is compared by the object it holds.
    [TestMethod]
    public void AVariantThatHoldsTheSameReference_True()
    {
        var reference = new VBObjectValue(new VBRuntimeObjectId());
        AssertResult<VBBooleanValue>(Evaluate(Is(), new VBVariantValue(reference), reference), true);
    }

    [TestMethod]
    public void AVariantThatHoldsAnObject_IsNotNothing_False()
        => AssertResult<VBBooleanValue>(Evaluate(Is(), new VBVariantValue(new VBObjectValue(new VBRuntimeObjectId())), new VBVariantValue(VBObjectValue.Nothing)), false);

    [TestMethod]
    public void AVariantThatHoldsNoObject_IsObjectRequired()
        => AssertError(Evaluate(Is(), new VBVariantValue(new VBLongValue(5)), VBObjectValue.Nothing), VBRuntimeErrorId.ObjectRequired);

    // an array has no reference to read: reading one used to throw out of the operator.
    [TestMethod]
    public void AnArray_IsObjectRequired()
        => AssertError(Evaluate(Is(), new VBResizableArrayValue([(0, 1)], VBLongType.TypeInfo), VBObjectValue.Nothing), VBRuntimeErrorId.ObjectRequired);

    [TestMethod]
    public void AVariantThatHoldsAnArray_IsObjectRequired()
        => AssertError(Evaluate(Is(), VBObjectValue.Nothing, new VBVariantValue(new VBResizableArrayValue([(0, 1)]))), VBRuntimeErrorId.ObjectRequired);

    [TestMethod]
    public void NonObjectLeftHandSide_IsObjectRequired()
        => AssertError(Evaluate(Is(), new VBLongValue(5), VBObjectValue.Nothing), VBRuntimeErrorId.ObjectRequired);

    [TestMethod]
    public void NonObjectRightHandSide_IsObjectRequired()
        => AssertError(Evaluate(Is(), VBObjectValue.Nothing, new VBLongValue(5)), VBRuntimeErrorId.ObjectRequired);
}
