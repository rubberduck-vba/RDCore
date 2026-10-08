using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// The let-coercion operator's runtime semantics (MS-VBAL §5.6.6): the operator a pair of parentheses
/// around an expression is.
/// </summary>
/// <remarks>
/// "A parenthesized expression evaluates to the simple data value of its enclosed expression", and its
/// declared type is the enclosed expression's — so the operator's effective type is always the operand's
/// own, and its result is always a value equal to the operand. What the operator changes is not the
/// value or the type but the <em>classification</em>: what comes out is a value, where what went in may
/// have been a variable. That is the whole of what <c>Foo (x)</c> means, and the reason it cannot be
/// aliased to a <c>ByRef</c> parameter — see the end-to-end cases in <c>HostExecuteHandlerTests</c>.
/// </remarks>
[TestClass]
[TestCategory("RD-VBAL §5.6.6 Parenthesized Expressions")]
public sealed class UnaryLetCoerceOperatorRuntimeTests : OperatorLetCoerceRuntimeSemanticsTests
{
    private UnaryLetCoerceOperatorRuntimeSemantics LetCoerce() => new(RealCoercionProvider(), Formatter());

    [TestMethod]
    // never the arithmetic table the unary base falls back to, which would make a String operand's
    // effective type Double and a Boolean's Integer.
    public void EffectiveType_IsTheOperandsOwn()
    {
        Assert.AreEqual(VBLongType.TypeInfo, DetermineEffectiveType(LetCoerce(), new VBLongValue(1)).Result);
        Assert.AreEqual(VBStringType.TypeInfo, DetermineEffectiveType(LetCoerce(), new VBStringValue("a")).Result);
        Assert.AreEqual(VBBooleanType.TypeInfo, DetermineEffectiveType(LetCoerce(), new VBBooleanValue(true)).Result);
        Assert.AreEqual(VBDateType.TypeInfo, DetermineEffectiveType(LetCoerce(), new VBDateValue(3)).Result);
    }

    [TestMethod]
    public void ANumericOperand_YieldsTheSameValueAndType()
        => AssertResult<VBLongValue>(Evaluate(LetCoerce(), new VBLongValue(42)), 42);

    [TestMethod]
    public void AStringOperand_YieldsTheSameValueAndType()
        // the sharpest case: under the arithmetic fallback this would have become a Double, and a
        // non-numeric string would have been a type mismatch rather than the string it is.
        => AssertResult<VBStringValue>(Evaluate(LetCoerce(), new VBStringValue("hello")), "hello");

    [TestMethod]
    public void ABooleanOperand_YieldsTheSameValueAndType()
        => AssertResult<VBBooleanValue>(Evaluate(LetCoerce(), new VBBooleanValue(true)), true);

    [TestMethod]
    public void ADateOperand_YieldsTheSameValueAndType()
        => AssertResult<VBDateValue>(Evaluate(LetCoerce(), new VBDateValue(3)), 3d);

    [TestMethod]
    public void ADoubleOperand_IsNotRounded()
        // a coercion to the operand's own type converts nothing, which is the point: no rounding, no
        // widening, no narrowing — only the classification changes.
        => AssertResult<VBDoubleValue>(Evaluate(LetCoerce(), new VBDoubleValue(2.67)), 2.67);

    public static IEnumerable<object[]> ValuesOnlyAVariantHolds()
    {
        yield return [VBNullValue.Null];
        yield return [VBEmptyValue.Empty];
        yield return [new VBErrorValue(5)];
    }

    [TestMethod]
    [DynamicData(nameof(ValuesOnlyAVariantHolds))]
    public void ANullEmptyOrErrorOperand_IsItself(VBTypedValue operand)
    {
        // MS-VBAL 5.6.6: the value of a parenthesized expression is the value of the expression it encloses.
        var result = Evaluate(LetCoerce(), operand);

        Assert.IsNull(result.ErrorInfo);
        Assert.AreEqual(operand, result.Result);
    }
}
