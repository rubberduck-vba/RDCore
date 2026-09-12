using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Pins the effective (runtime) value-type table of the <c>&amp;</c> operator, per
/// <strong>MS-VBAL §5.6.9.4</strong>: numeric/String/Date/Empty operands (with at most one side
/// <c>Null</c>) resolve to <c>String</c>; only <c>Null &amp; Null</c> resolves to <c>Null</c>.
/// </summary>
/// <remarks>
/// Doesn't cover the table's <c>Byte() &amp; Byte()</c> row: <see cref="VBArrayType.DefaultValue"/> is
/// a single static-lazy singleton on the base type (always a generic Variant-item resizable array), so
/// every array subtype's own <c>.TypeInfo.DefaultValue.TypeInfo</c> — this harness's only way to
/// synthesize an operand from a bare <c>VBType</c> — actually resolves back to the generic
/// <c>VBResizableArrayType</c>, never the specific <c>VBResizableByteArrayType</c>. Pre-existing,
/// unrelated to concatenation; flagged as a follow-up rather than fixed here. Separately (and not
/// blocked by the same issue, since it constructs a real value instead of going through
/// <c>DefaultValue</c>), <c>BinaryConcatOperatorRuntimeTests</c> has an <c>[Ignore]</c>d evaluation-level
/// test for the same row, pending Byte()-array-to-string let-coercion (MS-VBAL §5.5.1.2.6).
/// </remarks>
[TestClass]
[TestCategory("RD-VBAL §5.0.2.1 Operator Evaluation")]
[TestCategory("MS-VBAL 5.6.9.4 & Operator (runtime semantics)")]
public sealed class BinaryConcatOperatorEffectiveTypeTests : OperatorConcatRuntimeSemanticsTests
{
    private static BinaryConcatOperatorRuntimeSemantics Op() => new(FakeProvider(), Formatter());

    [TestMethod]
    public void ResolvesEffectiveValueType_AcrossTheOperandGrid()
    {
        VBType vbLong = VBLongType.TypeInfo, vbDouble = VBDoubleType.TypeInfo, vbCurrency = VBCurrencyType.TypeInfo,
            vbString = VBStringType.TypeInfo, vbDate = VBDateType.TypeInfo, vbEmpty = VBEmptyType.TypeInfo,
            vbNull = VBNullType.TypeInfo;

        (VBType lhs, VBType rhs, VBType expected)[] grid =
        [
            // numeric & numeric: the table doesn't distinguish between numeric subtypes (unlike the
            // arithmetic table), so a couple of representative pairs cover the "any numeric type" match.
            (vbLong, vbLong, vbString), (vbDouble, vbCurrency, vbString),

            (vbLong, vbString, vbString), (vbString, vbLong, vbString),
            (vbString, vbString, vbString),
            (vbDate, vbString, vbString), (vbString, vbDate, vbString),
            (vbLong, vbDate, vbString), (vbDate, vbLong, vbString),
            (vbEmpty, vbLong, vbString), (vbLong, vbEmpty, vbString), (vbEmpty, vbEmpty, vbString),

            // a lone Null operand still resolves to String (it's exempted from let-coercion at
            // evaluation, not from effective-type resolution — see BinaryConcatOperatorRuntimeTests
            // for the runtime-evaluation half of this, which used to crash on this exact case):
            (vbNull, vbLong, vbString), (vbLong, vbNull, vbString),
            (vbNull, vbString, vbString), (vbString, vbNull, vbString),

            (vbNull, vbNull, vbNull),
        ];

        var failures = new List<string>();
        foreach (var (lhs, rhs, expected) in grid)
        {
            var result = DetermineEffectiveType(Op(), lhs, rhs);
            if (!result.IsApplicable || !Equals(result.Result, expected))
            {
                failures.Add($"({lhs.Name}, {rhs.Name}) expected {expected.Name}, got {(result.IsApplicable ? result.Result!.Name : "type mismatch")}");
            }
        }

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }

    [TestMethod]
    public void BooleanOperand_IsTypeMismatch()
        // MS-VBAL §5.6.9.4's value-type table lists "any numeric type" but never "Boolean" — confirmed
        // against the clean Microsoft Learn mirror, not just the locally-garbled table — unlike
        // arithmetic/relational/logical, which each explicitly special-case Boolean. A narrow,
        // deliberate divergence from Boolean's usual free numeric coercion elsewhere; flagged here
        // rather than "fixed", since MS-Office VBA itself is known to accept `True & "x"` — this is a
        // spec-vs-implementation gap, not an RDCore bug.
        => Assert.IsNotNull(DetermineEffectiveType(Op(), VBBooleanType.TypeInfo, VBStringType.TypeInfo).ErrorInfo);

    [TestMethod]
    public void ObjectOperand_IsTypeMismatch()
        => Assert.IsNotNull(DetermineEffectiveType(Op(), VBObjectType.TypeInfo, VBLongType.TypeInfo).ErrorInfo);
}
