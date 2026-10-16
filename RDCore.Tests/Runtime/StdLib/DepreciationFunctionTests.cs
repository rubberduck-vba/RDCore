using RDCore.Runtime.Execution;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Globalization;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.6.1</strong> The depreciation functions: <c>DDB</c>'s declining balance,
/// <c>SLN</c>'s straight line, and <c>SYD</c>'s sum of the years' digits.
/// </summary>
/// <remarks>
/// The specification gives <c>DDB</c> a formula — "Depreciation / Period = ((Cost - Salvage) * Factor) /
/// Life" — that does not depend on the period, and so cannot be the one that computes it. A declining balance
/// loses <c>Factor / Life</c> of what is left of it each period, and stops at the salvage value; that is what
/// these assert, and it is what the VB runtime's own function, ported to .NET as
/// <c>Microsoft.VisualBasic.Financial</c>, computes too, but for three corners noted where they are tested.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.6 Financial")]
public sealed class DepreciationFunctionTests
{
    private static readonly StdFinancial Financial = new();

    // a supplied optional argument, which arrives wrapped in the Variant it is declared as.
    private static VBVariantValue Variant(double value) => new(new VBDoubleValue(value));

    private static double ValueOf(RuntimeSemanticsEvaluationResult<VBDoubleValue> result)
    {
        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Verbose);
        return result.Result!.Value;
    }

    private static int ErrorOf(RuntimeSemanticsEvaluationResult<VBDoubleValue> result)
    {
        Assert.IsFalse(result.IsSuccess, $"expected an error, got {result.Result?.Value}");
        return result.ErrorInfo!.ErrorId;
    }

    // to twelve significant digits, or to 1E-12 below 1.
    private static void AreClose(double expected, RuntimeSemanticsEvaluationResult<VBDoubleValue> result)
        => Assert.AreEqual(expected, ValueOf(result), Math.Max(Math.Abs(expected), 1) * 1e-12);

    [TestMethod]
    [DataRow(1, 4000)]
    [DataRow(2, 2400)]
    [DataRow(3, 1440)]
    [DataRow(4, 864)]
    [DataRow(5, 296)]
    public void DDB_DeclinesByTwiceTheStraightLineRate_DownToTheSalvageValue(double period, double expected)
        // 40% of what is left each year: 4000 of 10000, 2400 of 6000, 1440 of 3600, 864 of 2160 - and the last
        // year only takes the 296 that is left above the salvage value of 1000.
        => AreClose(expected, Financial.DDB(new(10000), new(1000), new(5), new(period)));

    [TestMethod]
    [DataRow(3, 600)]
    [DataRow(4, 0)]
    [DataRow(5, 0)]
    public void DDB_OnceTheBalanceHasReachedTheSalvageValue_IsZero(double period, double expected)
        // 10000 declines to 6000 and 3600, and the third year takes only the 600 left above the salvage value of
        // 3000: after it there is nothing left to take.
        => AreClose(expected, Financial.DDB(new(10000), new(3000), new(5), new(period)));

    [TestMethod]
    [DataRow(1.5, 2100)]
    [DataRow(1, 1600)]
    public void DDB_DeclinesByTheFactorGiven(double factor, double expected)
        // 150% and 100% of the straight-line rate, in the second year: 30% of 7000, 20% of 8000.
        => AreClose(expected, Financial.DDB(new(10000), new(1000), new(5), new(2), Variant(factor)));

    [TestMethod]
    [DataRow(2.5, 1859.03200617956)]
    [DataRow(0.5, 4000)]
    public void DDB_OfAFractionalPeriod_DeclinesByTheFractionOfAPeriodGoneBefore(double period, double expected)
        // what is left after a period and a half is 10000 * 0.6^1.5; any period up to the first is the first's.
        => AreClose(expected, Financial.DDB(new(10000), new(1000), new(5), new(period)));

    [TestMethod]
    public void DDB_ThatTheFirstPeriodCannotTakeInFull_TakesWhatIsLeftAboveTheSalvageValue()
        => AreClose(100, Financial.DDB(new(1000), new(900), new(3), new(1)));

    [TestMethod]
    public void DDB_OfASalvageValueAboveTheCost_IsNegativeInTheFirstPeriod()
        // the first period takes its share of the cost, but no more than the cost less the salvage value - which
        // here is less than nothing - as the VB runtime's own function answers too. Every later period is 0.
        => AreClose(-1000, Financial.DDB(new(1000), new(2000), new(5), new(1)));

    [TestMethod]
    public void DDB_OfAnAssetThatCostNothing_IsZero()
        => Assert.AreEqual(0, ValueOf(Financial.DDB(new(0), new(0), new(5), new(1))));

    [TestMethod]
    [DataRow(10000, 1000, 5, 0, 2)]
    [DataRow(10000, 1000, 5, 6, 2)]
    [DataRow(10000, 1000, 0, 1, 2)]
    [DataRow(10000, 1000, 5, 1, 0)]
    [DataRow(10000, -1, 5, 1, 2)]
    public void DDB_OfAPeriodOutsideTheLife_OrANonPositiveFactor_OrANegativeSalvage_IsError5(double cost, double salvage, double life, double period, double factor)
        // "All arguments MUST be positive numbers" - but for the cost and the salvage value: an asset that cost
        // nothing depreciates by nothing, and one worth nothing at the end is an ordinary one.
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument,
            ErrorOf(Financial.DDB(new(cost), new(salvage), new(life), new(period), Variant(factor))));

    [TestMethod]
    public void DDB_WithAFactorOtherThanTwo_OverATwoPeriodLife_DeclinesByThatFactor()
        // a factor of 1 over a life of 2 takes half the cost in the first period. The VB runtime's own function
        // answers 900 here, the whole depreciable amount: over a life of exactly 2 it ignores the factor.
        => AreClose(500, Financial.DDB(new(1000), new(100), new(2), new(1), Variant(1)));

    [TestMethod]
    public void DDB_WithAnExplicitEmptyFactor_IsError5()
        // Empty is not an omitted argument but a value, which as a number is 0 (MS-VBAL 5.5.1.2.11) - and a factor
        // of 0 is not one an asset can depreciate by, where an omitted factor is 2.
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument,
            ErrorOf(Financial.DDB(new(2400), new(300), new(10), new(1), new(VBEmptyValue.Empty))));

    [TestMethod]
    [DataRow(1, 900)]
    [DataRow(1.5, 0)]
    public void DDB_OverALifeShorterThanTwo_TakesEverythingInTheFirstPeriod_AndNothingAfter(double period, double expected)
        // twice the straight-line rate over a life of 1.5 is a rate above 100%. The VB runtime's own function
        // answers 900 for both periods: over a life under 2 it takes the whole depreciable amount in every one.
        => AreClose(expected, Financial.DDB(new(1000), new(100), new(1.5), new(period)));

    [TestMethod]
    public void DDB_WithAFactorGreaterThanTheLife_TakesEverythingInTheFirstPeriod_AndNothingAfter()
        // a factor of 4 over a life of 3 is a rate above 100%, so the first period takes all 900 and none is left.
        // The VB runtime's own function answers 11.11 for the third period here: (1 - 4/3) squared is positive.
        => Assert.AreEqual(0, ValueOf(Financial.DDB(new(1000), new(100), new(3), new(3), Variant(4))));

    [TestMethod]
    public void DDB_FromSource_TakesTheDoubleDecliningFactorWhenItIsLeftOut()
    {
        // "If omitted, the data value 2 (double-declining method) is assumed" - an omitted factor arrives as null,
        // where an explicit Empty is a factor of 0.
        var output = new RuntimeOutputBuffer();
        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem: null, [], output, standardLibrary: true, "Debug.Print DDB(2400, 300, 10, 1)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.HasCount(1, output.Lines);
        Assert.AreEqual(480, double.Parse(output.Lines[0].Trim(), CultureInfo.InvariantCulture));
    }

    [TestMethod]
    [DataRow(30000, 7500, 10, 2250)]
    [DataRow(1000, 2000, 5, -200)]
    [DataRow(10000, 1000, -5, -1800)]
    public void SLN_SpreadsTheDepreciableAmountEvenly(double cost, double salvage, double life, double expected)
        => AreClose(expected, Financial.SLN(new(cost), new(salvage), new(life)));

    [TestMethod]
    public void SLN_OverALifeOfZero_IsError5()
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Financial.SLN(new(10000), new(1000), new(0))));

    [TestMethod]
    [DataRow(30000, 7500, 10, 1, 4090.909090909091)]
    [DataRow(30000, 7500, 10, 10, 409.09090909090907)]
    [DataRow(10000, 1000, 5, 3, 1800)]
    [DataRow(10000, 1000, 5, 2.5, 2100)]
    [DataRow(10000, 1000, 0.5, 0.5, 24000)]
    public void SYD_TakesTheYearsRemainingOverTheSumOfTheYearsDigits(double cost, double salvage, double life, double period, double expected)
        // the third year of five takes 3/15 of 9000. A life under 1 makes the sum of the digits less than 1, so its
        // period takes more than the depreciable amount - as the VB runtime's own function does.
        => AreClose(expected, Financial.SYD(new(cost), new(salvage), new(life), new(period)));

    [TestMethod]
    [DataRow(10000, 1000, 5, 6)]
    [DataRow(10000, 1000, 5, 0)]
    [DataRow(10000, -1, 5, 1)]
    public void SYD_OfAPeriodOutsideTheLife_OrANegativeSalvage_IsError5(double cost, double salvage, double life, double period)
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Financial.SYD(new(cost), new(salvage), new(life), new(period))));
}
