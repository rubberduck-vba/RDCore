using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using System.Reflection;

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

    private static readonly VBType VbLong = VBLongType.TypeInfo, VbDouble = VBDoubleType.TypeInfo,
        VbCurrency = VBCurrencyType.TypeInfo, VbString = VBStringType.TypeInfo, VbDate = VBDateType.TypeInfo,
        VbEmpty = VBEmptyType.TypeInfo, VbNull = VBNullType.TypeInfo;

    public static IEnumerable<object[]> Grid()
    {
        // numeric & numeric: the table doesn't distinguish between numeric subtypes (unlike the
        // arithmetic table), so a couple of representative pairs cover the "any numeric type" match.
        yield return [VbLong, VbLong, VbString];
        yield return [VbDouble, VbCurrency, VbString];

        yield return [VbLong, VbString, VbString];
        yield return [VbString, VbLong, VbString];
        yield return [VbString, VbString, VbString];
        yield return [VbDate, VbString, VbString];
        yield return [VbString, VbDate, VbString];
        yield return [VbLong, VbDate, VbString];
        yield return [VbDate, VbLong, VbString];
        yield return [VbEmpty, VbLong, VbString];
        yield return [VbLong, VbEmpty, VbString];
        yield return [VbEmpty, VbEmpty, VbString];

        // a lone Null operand still resolves to String (it's exempted from let-coercion at evaluation,
        // not from effective-type resolution — see BinaryConcatOperatorRuntimeTests for the
        // runtime-evaluation half of this, which used to crash on this exact case):
        yield return [VbNull, VbLong, VbString];
        yield return [VbLong, VbNull, VbString];
        yield return [VbNull, VbString, VbString];
        yield return [VbString, VbNull, VbString];

        yield return [VbNull, VbNull, VbNull];
    }

    public static string GetTestName(MethodInfo method, object[] data)
        => $"({((VBType)data[0]).Name}, {((VBType)data[1]).Name}):{((VBType)data[2]).Name}";

    [TestMethod]
    [DynamicData(nameof(Grid), DynamicDataDisplayName = nameof(GetTestName))]
    public void ResolvesEffectiveValueType(VBType lhs, VBType rhs, VBType expected)
    {
        var result = DetermineEffectiveType(Op(), lhs, rhs);
        Assert.IsTrue(result.IsApplicable, $"({lhs.Name}, {rhs.Name}) expected {expected.Name}, got type mismatch");
        Assert.AreEqual(expected, result.Result);
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
