using RDCore.Runtime.Semantics.Operators.Arithmetic;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for unary arithmetic operator runtime semantics: the result is computed in
/// the effective type's own CLR representation, and negating the minimum value of a signed integral
/// type surfaces as <see cref="VBRuntimeErrorId.Overflow"/>.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.2.1 Operator Evaluation")]
public sealed class UnaryArithmeticOperatorRuntimeTests : OperatorArithmeticRuntimeSemanticsTests
{
    private UnaryNegationOperatorRuntimeSemantics Neg() => new(FakeProvider(), Formatter());
    private UnaryPlusOperatorRuntimeSemantics Plus() => new(FakeProvider(), Formatter());

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.1 Unary '-' Operator")]
    public void Negation_Integer()
        => AssertResult<VBIntegerValue>(
            Evaluate(Neg(), new VBIntegerValue(5)), (short)-5);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.1 Unary '-' Operator")]
    public void Negation_Long()
        => AssertResult<VBLongValue>(
            Evaluate(Neg(), new VBLongValue(5)), -5);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.1 Unary '-' Operator")]
    public void Negation_Double()
        => AssertResult<VBDoubleValue>(
            Evaluate(Neg(), new VBDoubleValue(2.5)), -2.5d);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.1 Unary '-' Operator")]
    public void Negation_Currency()
        => AssertResult<VBCurrencyValue>(
            Evaluate(Neg(), new VBCurrencyValue(1.5m)), -1.5m);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.1 Unary '-' Operator")]
    public void Negation_IntegerMinValue_Overflows()
        => AssertError(
            Evaluate(Neg(), new VBIntegerValue(short.MinValue)), VBRuntimeErrorId.Overflow);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.1 Unary '-' Operator")]
    public void Negation_DateEffectiveType_ProducesDate()
        => AssertResult<VBDateValue>(
            Evaluate(Neg(), new VBDateValue(3)), -3d);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.1 Unary '-' Operator")]
    public void Negation_Null_IsNull()
    {
        var result = Evaluate(Neg(), VBNullValue.Null);
        Assert.IsNull(result.ErrorInfo);
        Assert.IsInstanceOfType<VBNullValue>(result.Result);
    }

    [TestMethod]
    [TestCategory("RD-VBAL 5.6.9.3.1.1 Unary '+' Operator")]
    public void Plus_Long_IsIdentity()
        => AssertResult<VBLongValue>(
            Evaluate(Plus(), new VBLongValue(5)), 5);

    [TestMethod]
    [TestCategory("RD-VBAL 5.6.9.3.1.1 Unary '+' Operator")]
    public void Plus_Double_IsIdentity()
        => AssertResult<VBDoubleValue>(
            Evaluate(Plus(), new VBDoubleValue(-2.5)), -2.5d);

    [TestMethod]
    [TestCategory("RD-VBAL 5.6.9.3.1.1 Unary '+' Operator")]
    public void Plus_Null_IsNull()
    {
        var result = Evaluate(Plus(), VBNullValue.Null);
        Assert.IsNull(result.ErrorInfo);
        Assert.IsInstanceOfType<VBNullValue>(result.Result);
    }
}
