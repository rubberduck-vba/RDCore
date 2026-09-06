using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Model.Values;

[TestClass]
[TestCategory("MS-VBAL 3.4.1 Conditional Compilation Const Directive")]
public sealed class PrecompilerConstantExpressionTests
{
    [TestMethod]
    [DataRow("1", typeof(VBIntegerValue))]
    [DataRow("-1", typeof(VBIntegerValue))]
    [DataRow("40000", typeof(VBLongValue))]
    [DataRow("3.5", typeof(VBDoubleValue))]
    [DataRow("\"debug\"", typeof(VBStringValue))]
    [DataRow("True", typeof(VBBooleanValue))]
    [DataRow("false", typeof(VBBooleanValue))]
    [DataRow("Empty", typeof(VBEmptyValue))]
    [DataRow("Null", typeof(VBNullValue))]
    public void TryParse_TypesLiteralsPerMsVbal332(string source, Type expected)
    {
        Assert.IsTrue(PrecompilerConstantExpression.TryParse(source, out var value), source);
        Assert.IsInstanceOfType(value, expected);
    }

    [TestMethod]
    public void TryParse_Integer_HasValue()
    {
        Assert.IsTrue(PrecompilerConstantExpression.TryParse("-1", out var value));
        Assert.AreEqual((short)-1, ((VBIntegerValue)value!).Value);
    }

    [TestMethod]
    public void TryParse_String_UnescapesDoubledQuotes()
    {
        Assert.IsTrue(PrecompilerConstantExpression.TryParse("\"a\"\"b\"", out var value));
        Assert.AreEqual("a\"b", ((VBStringValue)value!).Value);
    }

    [TestMethod]
    public void TryParse_Date_IsOaSerial()
    {
        Assert.IsTrue(PrecompilerConstantExpression.TryParse("#2024-01-15#", out var value));
        Assert.AreEqual(new DateTime(2024, 1, 15).ToOADate(), ((VBDateValue)value!).SerialValue);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(null)]
    [DataRow("SomeOtherConst")]
    [DataRow("1 + 2")]
    public void TryParse_RejectsNonLiterals(string? source)
        => Assert.IsFalse(PrecompilerConstantExpression.TryParse(source, out _));
}
