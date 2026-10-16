using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.Tests.Model.Types;

/// <summary>
/// MS-VBAL 5.5.1.2.1: a Double a numeric type holds is one a value of the type is made of - at the very edge of its range, too, where the Double nearest a
/// bound can be past it.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.5.1.2.1 Let-coercion between numeric types")]
public sealed class NumericTypeRangeTests
{
    private static VBNumericType[] Types() =>
    [
        VBByteType.TypeInfo, VBIntegerType.TypeInfo, VBLongType.TypeInfo, VBLongLongType.TypeInfo,
        VBSingleType.TypeInfo, VBDoubleType.TypeInfo, VBCurrencyType.TypeInfo, VBDecimalType.TypeInfo,
    ];

    [TestMethod]
    public void EveryDoubleANumericTypeHolds_AtTheEdgeOfItsRange_MakesAValueOfIt()
    {
        var failures = new List<string>();
        foreach (var type in Types())
        {
            foreach (var edge in new[] { type.ManagedMinValue, Math.BitIncrement(type.ManagedMinValue), type.ManagedMaxValue, Math.BitDecrement(type.ManagedMaxValue) })
            {
                if (!type.Holds(edge))
                {
                    continue;
                }

                try
                {
                    type.CreateValue(edge);
                }
                catch (Exception exception)
                {
                    failures.Add($"{type.Name} holds {edge:R}, and {exception.GetType().Name}");
                }
            }
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    [TestMethod]
    [DataRow(9223372036854775808d, false)] // 2^63, the Double nearest the largest LongLong
    [DataRow(9223372036854774784d, true)]  // the largest Double below it
    [DataRow(-9223372036854775808d, true)] // -2^63 is the smallest LongLong
    public void ALongLong_HoldsTheDoublesInItsRange(double value, bool expected)
        => Assert.AreEqual(expected, VBLongLongType.TypeInfo.Holds(value));

    [TestMethod]
    public void NoNumericType_HoldsNaN()
        => Assert.IsFalse(Types().Any(type => type.Holds(double.NaN)));
}
