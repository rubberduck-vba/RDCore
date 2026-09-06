using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.Tests.Model.Types;

[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.1.1 Banker's Rounding")]
public sealed class BankersRoundingTests
{
    [DataTestMethod]
    [DataRow(2.5, 2)]   // tie -> even
    [DataRow(3.5, 4)]   // tie -> even
    [DataRow(0.5, 0)]   // tie -> even
    [DataRow(-2.5, -2)] // tie -> even
    [DataRow(2.4, 2)]
    [DataRow(2.67, 3)]  // regression: used to truncate to 2
    [DataRow(-2.67, -3)]
    [DataRow(1.0, 1)]
    public void RoundsToNearestIntegerTiesToEven(double input, int expected)
        => Assert.AreEqual(expected, VBNumericType.BankersRounding(input));
}
