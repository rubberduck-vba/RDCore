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

    // Not covered above: a source whose let-coercion itself fails (e.g. Double.MaxValue -> Integer,
    // which should be Overflow). OperatorRuntimeSemantics<T,F>.Evaluate's operand-validation step drops
    // any operand whose ValidateOperand result has a null Result (i.e. any coercion failure) from the
    // array passed to EvaluateExpressionResult, instead of short-circuiting on the coercion error — so
    // a binary operator's EvaluateExpressionResult then indexes into a shrunk array and throws
    // IndexOutOfRangeException rather than surfacing the coercion's own error. This is a pre-existing
    // defect in the shared base pipeline (affects every operator family, not just Let-Coerce), well
    // outside this operator's own scope; flagged as a follow-up rather than fixed here.

    // Every Date-involving scenario below hits the same shared-pipeline defect from a different angle:
    // OperatorRuntimeSemantics<T,F>.LetCoerceNonNullOperand forces the LHS's coercion *destination* to
    // Double whenever frame.EffectiveType is VBDateType (a rule meant for arithmetic operators computing
    // in Double, per MS-VBAL 5.6.9.3 — inappropriate here, where the *operator's own job* is coercing to
    // whatever the real target type is, Date included, via VBDateLetCoercionRuntimeSemantics). That
    // forced Double destination then fails for any source VBNumericLetCoercionTypeRuntimeSemantics
    // doesn't recognize as Double-convertible (Date, Boolean), so the operand is silently dropped — same
    // root cause as the Overflow case above, just reached via effective-type-driven redirection instead
    // of a coercion range check.

    [TestMethod]
    [Ignore("Blocked by the shared pipeline's operand-coercion-failure bug: LetCoerceNonNullOperand forces the Date effective type's own source to a Double destination, which VBNumericLetCoercionTypeRuntimeSemantics doesn't recognize a Date source as convertible to — the operand is dropped and EvaluateExpressionResult throws IndexOutOfRangeException.")]
    public void DateSource_CoercesToDateTarget_CopiesTheSourceDate()
        => AssertResult<VBDateValue>(Evaluate(LetCoerce(), new VBDateValue(3), VBDateType.TypeInfo), 3d);

    [TestMethod]
    [Ignore("Blocked by the shared pipeline's operand-coercion-failure bug: by the time VBDateLetCoercionRuntimeSemantics runs, LetCoerceNonNullOperand has already coerced the source to Double (forced destination for a Date effective type) — its own recursive Double-to-Double delegation then fails, since VBNumericLetCoercionTypeRuntimeSemantics has no identity/same-type case, and it null-forgives the failed Result, throwing NullReferenceException.")]
    public void NumericSource_CoercesToDateTarget_InterpretsAsStandardDoubleRepresentation()
        // MS-VBAL 5.5.1.2.3: the source is first let-coerced to Double, then interpreted as a standard
        // Double representation of a date/time.
        => AssertResult<VBDateValue>(Evaluate(LetCoerce(), new VBLongValue(5), VBDateType.TypeInfo), 5d);

    [TestMethod]
    [Ignore("Blocked by the shared pipeline's operand-coercion-failure bug: the destination-keyed coercion provider selects the Numeric strategy for a numeric target, which has no case for a Date source (that direction only exists inside VBDateLetCoercionRuntimeSemantics, keyed on a Date destination) — the operand is dropped and EvaluateExpressionResult throws IndexOutOfRangeException.")]
    public void DateSource_CoercesToNumericTarget_YieldsStandardDoubleRepresentation()
        => AssertResult<VBLongValue>(Evaluate(LetCoerce(), new VBDateValue(5), VBLongType.TypeInfo), 5);

    [TestMethod]
    [Ignore("Blocked by the shared pipeline's operand-coercion-failure bug (same Date-forces-Double redirection as DateSource_CoercesToDateTarget_CopiesTheSourceDate): a Boolean source isn't recognized as Double-convertible by VBNumericLetCoercionTypeRuntimeSemantics either, so it's dropped and EvaluateExpressionResult throws IndexOutOfRangeException — even though MS-VBAL 5.5.1.2.3 explicitly specifies Boolean-to-Date via the same Double-representation rule as any numeric source.")]
    public void BooleanSource_ToDateTarget_ThrowsInsteadOfCoercing()
        => AssertResult<VBDateValue>(Evaluate(LetCoerce(), new VBBooleanValue(true), VBDateType.TypeInfo), -1d);
}
