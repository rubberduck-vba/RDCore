using RDCore.Runtime.Semantics.Operators.Relational;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

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
        var reference = new VBObjectValue(new MemoryAddress(42));
        AssertResult<VBBooleanValue>(Evaluate(Is(), reference, reference), true);
    }

    [TestMethod]
    public void DifferentReferences_False()
        => AssertResult<VBBooleanValue>(
            Evaluate(Is(), new VBObjectValue(new MemoryAddress(1)), new VBObjectValue(new MemoryAddress(2))), false);

    [TestMethod]
    public void NothingIsNothing_True()
        => AssertResult<VBBooleanValue>(Evaluate(Is(), VBObjectValue.Nothing, VBObjectValue.Nothing), true);

    [TestMethod]
    public void ObjectIsNotNothing_False()
        => AssertResult<VBBooleanValue>(Evaluate(Is(), new VBObjectValue(new MemoryAddress(1)), VBObjectValue.Nothing), false);

    [TestMethod]
    public void NonObjectLeftHandSide_IsObjectRequired()
        => AssertError(Evaluate(Is(), new VBLongValue(5), VBObjectValue.Nothing), VBRuntimeErrorId.ObjectRequired);

    [TestMethod]
    public void NonObjectRightHandSide_IsObjectRequired()
        => AssertError(Evaluate(Is(), VBObjectValue.Nothing, new VBLongValue(5)), VBRuntimeErrorId.ObjectRequired);
}
