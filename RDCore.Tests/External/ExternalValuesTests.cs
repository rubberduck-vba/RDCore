using RDCore.External.Protocol;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RDCore.Tests.External;

/// <summary>
/// A value that crosses to or from the external host reads back as the value it was written as, of the same type: the two ends speak the same neutral values, and
/// only an object is not what it was - it is its handle, which the end that holds it turns back into it.
/// </summary>
[TestClass]
[TestCategory("External host")]
public sealed class ExternalValuesTests
{
    private static object? RoundTrip(object? value, Func<object, long>? handleOf = null, Func<long, object>? objectOf = null)
        => ExternalValues.FromWire(ExternalValues.ToWire(value, handleOf ?? (_ => 0)), objectOf ?? (_ => new object()));

    [TestMethod]
    public void EveryScalar_ReadsBackAsTheSameValue_OfTheSameType()
    {
        object[] scalars =
        [
            true, (byte)7, (sbyte)-7, (short)-300, (ushort)300, -70000, 70000u, -1L << 40, 1UL << 63,
            1.5f, Math.PI, 123.456789m, "quack", string.Empty,
            new DateTime(2026, 10, 10, 14, 30, 15),
        ];

        foreach (var scalar in scalars)
        {
            var back = RoundTrip(scalar);
            Assert.AreEqual(scalar.GetType(), back!.GetType(), $"{scalar} crossed as {back.GetType()}");
            Assert.AreEqual(scalar, back);
        }
    }

    [TestMethod]
    public void AFloatingPointNumber_CrossesWithoutLosingADigit()
    {
        Assert.AreEqual(0.1 + 0.2, RoundTrip(0.1 + 0.2));
        Assert.AreEqual(float.Epsilon, RoundTrip(float.Epsilon));
    }

    [TestMethod]
    public void EmptyNullAndAnArgumentLeftOut_AreToldApart()
    {
        Assert.IsNull(RoundTrip(null));
        Assert.AreSame(DBNull.Value, RoundTrip(DBNull.Value));
        Assert.AreSame(Missing.Value, RoundTrip(Missing.Value));
    }

    [TestMethod]
    public void ACurrencyAndAnError_KeepWhatTheyAre_WhichTheirNumbersAloneWouldNot()
    {
        Assert.AreEqual(12.3456m, Assert.IsInstanceOfType<CurrencyWrapper>(RoundTrip(new CurrencyWrapper(12.3456m))).WrappedObject);
        Assert.AreEqual(448, Assert.IsInstanceOfType<ErrorWrapper>(RoundTrip(new ErrorWrapper(448))).ErrorCode);
    }

    [TestMethod]
    public void AnObject_CrossesAsItsHandle_AndNothingAsNoObject()
    {
        var held = new object();
        var other = new object();

        var back = RoundTrip(held, handleOf: value => ReferenceEquals(value, held) ? 42 : -1, objectOf: handle => handle == 42 ? other : new object());

        Assert.AreSame(other, back);
        Assert.IsNull(Assert.IsInstanceOfType<DispatchWrapper>(RoundTrip(new DispatchWrapper(null))).WrappedObject);
    }

    [TestMethod]
    public void AnArray_KeepsItsElementType_ItsBounds_AndWhereEachElementIs()
    {
        var array = (int[,])Array.CreateInstance(typeof(int), [2, 3], [1, 0]);
        for (var row = 1; row <= 2; row++)
        {
            for (var column = 0; column <= 2; column++)
            {
                array[row, column] = (row * 10) + column;
            }
        }

        var back = Assert.IsInstanceOfType<int[,]>(RoundTrip(array));

        Assert.AreEqual(1, back.GetLowerBound(0));
        Assert.AreEqual(2, back.GetUpperBound(0));
        Assert.AreEqual(0, back.GetLowerBound(1));
        Assert.AreEqual(2, back.GetUpperBound(1));
        Assert.AreEqual(21, back[2, 1]);
        Assert.AreEqual(12, back[1, 2]);
    }

    [TestMethod]
    public void AnArrayOfAnything_IsAnArrayOfAnything_WhoseElementsAreEachWhatTheyAre()
    {
        var back = Assert.IsInstanceOfType<object?[]>(RoundTrip(new object?[] { 1, "two", null, new CurrencyWrapper(3m) }));

        Assert.AreEqual(1, back[0]);
        Assert.AreEqual("two", back[1]);
        Assert.IsNull(back[2]);
        Assert.IsInstanceOfType<CurrencyWrapper>(back[3]);
    }

    [TestMethod]
    public void AnArrayWithNoElements_CrossesAsOne()
    {
        var back = Assert.IsInstanceOfType<string[]>(RoundTrip(Array.Empty<string>()));
        Assert.IsEmpty(back);
    }
}
