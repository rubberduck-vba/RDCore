using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Tests.Model.Values;

/// <summary>
/// A numeric value reads as a .NET number whatever its storage: <c>Decimal</c> and <c>Currency</c> keep their runtime value
/// in a storage struct, and reading the value as a number must see through it. The let-coercion of a Decimal or a
/// Currency (to any other numeric type, Boolean or Date) goes through this read.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.5 Runtime Values")]
public sealed class NumericValueManagedRepresentationTests
{
    [TestMethod]
    public void ADecimalsBoxedValue_IsTheDecimal_NotItsStorageStruct()
        => Assert.AreEqual(1.5m, new VBDecimalValue(1.5m).RuntimeValue.BoxedValue);

    [TestMethod]
    public void ACurrencysBoxedValue_IsTheDecimal_NotItsStorageStruct()
        => Assert.AreEqual(1.5m, new VBCurrencyValue(1.5m).RuntimeValue.BoxedValue);

    [TestMethod]
    public void ADecimalReadsAsADouble()
        => Assert.AreEqual(1.5d, ((VBNumericTypedValue)new VBDecimalValue(1.5m)).AsDouble);

    [TestMethod]
    public void ACurrencyReadsAsADouble()
        => Assert.AreEqual(1.5d, ((VBNumericTypedValue)new VBCurrencyValue(1.5m)).AsDouble);

    [TestMethod]
    public void ADecimalKeepsItsFullPrecision()
        => Assert.AreEqual(79228162514264337593543950335m, new VBDecimalValue(decimal.MaxValue).RuntimeValue.BoxedValue);

    [TestMethod]
    public void ACurrencyKeepsItsFourFractionalDigits()
        => Assert.AreEqual(922337203685477.5807m, new VBCurrencyValue(922337203685477.5807m).RuntimeValue.BoxedValue);

    [TestMethod]
    public void APrimitivesBoxedValue_IsTheValueItself()
    {
        Assert.AreEqual(7, new VBRuntimeValue<int>(7).BoxedValue);
        Assert.AreEqual("text", new VBRuntimeValue<string>("text").BoxedValue);
    }
}
