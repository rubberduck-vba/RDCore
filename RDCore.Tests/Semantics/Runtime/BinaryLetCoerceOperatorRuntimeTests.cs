using RDCore.Runtime.Semantics.Operators;
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

    // Not covered here: a source whose let-coercion itself fails (e.g. Double.MaxValue -> Integer,
    // which should be Overflow). OperatorRuntimeSemantics<T,F>.Evaluate's operand-validation step drops
    // any operand whose ValidateOperand result has a null Result (i.e. any coercion failure) from the
    // array passed to EvaluateExpressionResult, instead of short-circuiting on the coercion error — so
    // a binary operator's EvaluateExpressionResult then indexes into a shrunk array and throws
    // IndexOutOfRangeException rather than surfacing the coercion's own error. This is a pre-existing
    // defect in the shared base pipeline (affects every operator family, not just Let-Coerce), well
    // outside this operator's own scope; flagged as a follow-up rather than fixed here.
}
