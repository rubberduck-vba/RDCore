using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for the Let-coercion operator runtime semantics (RD-VBAL §5.6.9.9): the
/// effective type is the coercion target when that target is an intrinsic, class or user-defined type,
/// and a successful result is the source value let-coerced to it.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.6.9.9 Let-Coercion Operator")]
public sealed class BinaryLetCoerceOperatorRuntimeTests : OperatorLetCoerceRuntimeSemanticsTests
{
    private BinaryLetCoerceOperatorRuntimeSemantics LetCoerce() => new(RealCoercionProvider(), Formatter());

    [TestMethod]
    public void IntrinsicTargetType_ResolvesEffectiveTypeToTheTarget()
        => Assert.AreEqual(VBIntegerType.TypeInfo, DetermineEffectiveType(LetCoerce(), VBIntegerType.TypeInfo).Result);

    [TestMethod]
    public void NonIntrinsicNonClassNonUserDefinedTargetType_IsTypeMismatch()
        // per the operator's own contract: only VBIntrinsicType/VBClassType/VBUserDefinedType targets
        // resolve an effective type; anything else (e.g. the VBUnknownType sentinel) is a type mismatch.
        => Assert.IsNotNull(DetermineEffectiveType(LetCoerce(), VBUnknownType.TypeInfo).ErrorInfo);

    [TestMethod]
    public void NumericSource_RoundsToNearestIntegralTarget()
        // cross-checked against LetCoercionRuntimeProviderTests.Dispatch_ResolvesTheNumericStrategy...,
        // which pins the same Double-2.67-to-Integer-3 rounding directly on the provider.
        => AssertResult<VBIntegerValue>(Evaluate(LetCoerce(), new VBDoubleValue(2.67), VBIntegerType.TypeInfo), (short)3);

    [TestMethod]
    public void NumericSource_CoercesToStringTarget()
        => AssertResult<VBStringValue>(Evaluate(LetCoerce(), new VBLongValue(5), VBStringType.TypeInfo), "5");

    [TestMethod]
    public void NumericSource_OutOfTargetRange_IsOverflow()
        => AssertError(Evaluate(LetCoerce(), new VBDoubleValue(double.MaxValue), VBIntegerType.TypeInfo), VBRuntimeErrorId.Overflow);

    [TestMethod]
    public void DateSource_CoercesToDateTarget_CopiesTheSourceDate()
        => AssertResult<VBDateValue>(Evaluate(LetCoerce(), new VBDateValue(3), VBDateType.TypeInfo), 3d);

    [TestMethod]
    public void NumericSource_CoercesToDateTarget_InterpretsAsStandardDoubleRepresentation()
        // MS-VBAL 5.5.1.2.3: the source is first let-coerced to Double, then interpreted as a standard
        // Double representation of a date/time.
        => AssertResult<VBDateValue>(Evaluate(LetCoerce(), new VBLongValue(5), VBDateType.TypeInfo), 5d);

    [TestMethod]
    public void DateSource_CoercesToNumericTarget_YieldsStandardDoubleRepresentation()
        => AssertResult<VBLongValue>(Evaluate(LetCoerce(), new VBDateValue(5), VBLongType.TypeInfo), 5);

    [TestMethod]
    public void BooleanSource_CoercesToDateTarget_ViaStandardDoubleRepresentation()
        // MS-VBAL 5.5.1.2.3 lists Boolean alongside "any numeric type" for this rule; True's Double
        // representation is -1 (MS-VBAL 5.5.1.2.2).
        => AssertResult<VBDateValue>(Evaluate(LetCoerce(), new VBBooleanValue(true), VBDateType.TypeInfo), -1d);

    [TestMethod]
    public void BooleanSource_CoercesToBooleanTarget_CopiesTheSourceValue()
        => AssertResult<VBBooleanValue>(Evaluate(LetCoerce(), new VBBooleanValue(true), VBBooleanType.TypeInfo), true);

    [TestMethod]
    public void NumericSource_CoercesToBooleanTarget_ZeroIsFalseNonZeroIsTrue()
    {
        AssertResult<VBBooleanValue>(Evaluate(LetCoerce(), new VBLongValue(0), VBBooleanType.TypeInfo), false);
        AssertResult<VBBooleanValue>(Evaluate(LetCoerce(), new VBLongValue(42), VBBooleanType.TypeInfo), true);
    }

    [TestMethod]
    public void BooleanSource_CoercesToNumericTarget_FalseIsZeroTrueIsMinusOne()
    {
        AssertResult<VBLongValue>(Evaluate(LetCoerce(), new VBBooleanValue(false), VBLongType.TypeInfo), 0);
        AssertResult<VBLongValue>(Evaluate(LetCoerce(), new VBBooleanValue(true), VBLongType.TypeInfo), -1);
    }

    [TestMethod]
    public void BooleanSource_CoercesToByteTarget_TrueIsTwoHundredFiftyFive()
        // MS-VBAL 5.5.1.2.2: Byte is the one destination-type exception to Boolean's usual -1 (Byte is
        // unsigned, so True's "all bits set" representation is 255, not -1).
        => AssertResult<VBByteValue>(Evaluate(LetCoerce(), new VBBooleanValue(true), VBByteType.TypeInfo), (byte)255);
}
