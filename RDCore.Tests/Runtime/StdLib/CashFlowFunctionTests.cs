using RDCore.Runtime.Execution;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Globalization;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.6.1</strong> The functions of a series of cash flows: <c>NPV</c>, <c>IRR</c> and
/// <c>MIRR</c>, which read them from a <c>Double()</c> "to interpret the order of payments and receipts".
/// </summary>
/// <remarks>
/// The expected values are the exact ones, which the VB runtime's own functions, ported to .NET as
/// <c>Microsoft.VisualBasic.Financial</c>, agree with to twelve significant digits, or an iterated rate to within
/// 1E-07, except where a test says otherwise.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.6 Financial")]
public sealed class CashFlowFunctionTests
{
    private static readonly StdFinancial Financial = new();

    // "accurate to within 0.00001 percent": what an iterated rate is compared to.
    private const double IteratedAccuracy = 1e-7;

    // the cash flows of the Office documentation's IRR, MIRR and NPV examples: a payment, then four receipts.
    private static readonly double[] Example = [-70000, 22000, 25000, 28000, 31000];

    // a supplied optional argument, which arrives wrapped in the Variant it is declared as.
    private static VBVariantValue Variant(double value) => new(new VBDoubleValue(value));

    // the cash flows the way a Double() argument arrives, from the given lower bound.
    private static VBResizableArrayValue Flows(params double[] values) => FlowsFrom(0, values);

    private static VBResizableArrayValue FlowsFrom(int lowerBound, params double[] values)
    {
        var array = new VBResizableArrayValue(
            values.Length == 0 ? [] : [(lowerBound, lowerBound + values.Length - 1)], VBDoubleType.TypeInfo);
        for (var index = 0; index < values.Length; index++)
        {
            array.TrySetElement(new ValueBindingHandle(new VBDoubleValue(values[index]).RuntimeValue), lowerBound + index);
        }

        return array;
    }

    // an outlay, then the same receipt the given number of times.
    private static double[] Instalments(double outlay, double instalment, int count)
        => [outlay, .. Enumerable.Repeat(instalment, count)];

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

    private static double Printed(string line) => double.Parse(line.Trim(), CultureInfo.InvariantCulture);

    [TestMethod]
    public void NPV_DiscountsTheFirstFlowByOnePeriod()
        // "The NPV investment begins one period before the date of the first cash flow value".
        => AreClose(19312.570209535184, Financial.NPV(new(0.0625), Flows(Example)));

    [TestMethod]
    [DataRow(0.1, new[] { -10000d, 3000, 4200, 6800 }, 1188.4434123352228)]
    [DataRow(0, new[] { -100d, 50, 60 }, 10)]
    [DataRow(0.1, new[] { 0d, 100 }, 82.64462809917356)]
    [DataRow(0.1, new[] { 100d }, 90.9090909090909)]
    public void NPV_IsTheSumOfTheDiscountedFlows(double rate, double[] flows, double expected)
        // a leading zero is still a period: the 100 after it is discounted by two.
        => AreClose(expected, Financial.NPV(new(rate), Flows(flows)));

    [TestMethod]
    public void NPV_OfAnArrayThatDoesNotStartAtZero_ReadsItFromItsLowerBound()
        => AreClose(19312.570209535184, Financial.NPV(new(0.0625), FlowsFrom(1, Example)));

    [TestMethod]
    public void NPV_AtARateOfMinusOne_OrOfNoFlows_IsError5()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Financial.NPV(new(-1), Flows(100, 200))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Financial.NPV(new(0.1), Flows())));
    }

    [TestMethod]
    public void IRR_IsTheRateAtWhichTheFlowsAreWorthNothing()
        => Assert.AreEqual(0.17743588442252728, ValueOf(Financial.IRR(Flows(Example))), IteratedAccuracy);

    [TestMethod]
    [DataRow(new[] { -70000d, 12000, 15000, 18000, 21000, 26000 }, 0.1, 0.08663094803653161)]
    [DataRow(new[] { -70000d, 12000, 15000, 18000, 21000 }, 0.1, -0.021244848273410992)]
    [DataRow(new[] { -70000d, 12000, 15000 }, -0.1, -0.44350694133474056)]
    [DataRow(new[] { 0d, 0, -100, 110 }, 0.1, 0.1)]
    [DataRow(new[] { -100d, 230, -132 }, 0.3, 0.2)]
    [DataRow(new[] { -1d, 11 }, 0.1, 10)]
    public void IRR_StartsFromTheGuess(double[] flows, double guess, double expected)
        // the first three rows are the IRR examples of Excel's documentation. -100, 230, -132 balances at both
        // 10% and 20%, and which one is found is up to the guess.
        => Assert.AreEqual(expected, ValueOf(Financial.IRR(Flows(flows), Variant(guess))), IteratedAccuracy);

    [TestMethod]
    public void IRR_WithAnOmittedGuess_StartsFromTenPercent()
        // "If omitted, guess is the data value 0.1 (10 percent)." These flows balance at both 20% and -10%, and a
        // guess of 0 finds the other one.
        => Assert.AreEqual(0.2, ValueOf(Financial.IRR(Flows(-100, 210, -108))), IteratedAccuracy);

    [TestMethod]
    public void IRR_WithAnExplicitEmptyGuess_IsAGuessOfZero()
    {
        // Empty is not an omitted argument but a value, which as a number is 0 (MS-VBAL 5.5.1.2.11): from it these
        // flows find -10%, where from an omitted guess they find 20%.
        Assert.AreEqual(-0.1, ValueOf(Financial.IRR(Flows(-100, 210, -108), new(VBEmptyValue.Empty))), IteratedAccuracy);
        Assert.AreEqual(ValueOf(Financial.IRR(Flows(-100, 210, -108), Variant(0))), ValueOf(Financial.IRR(Flows(-100, 210, -108), new(VBEmptyValue.Empty))));
    }

    [TestMethod]
    [DataRow(-100000, 600, 360, 0.0050058250067624074)]
    [DataRow(-25000, 500, 60, 0.006183413161253964)]
    [DataRow(-1000, 20, 10, -0.22003386210264734)]
    [DataRow(-1000, 20, 40, -0.01047032576311746)]
    public void IRR_OfAnOutlayRepaidInEqualInstalments_IsTheirRatePerPeriod(double outlay, double instalment, int count, double expected)
        // a 30-year mortgage and a five-year car loan, paid monthly; and two investments only partly returned,
        // whose rate is negative. The VB runtime's own function finds none of these within its forty tries.
        => Assert.AreEqual(expected, ValueOf(Financial.IRR(Flows(Instalments(outlay, instalment, count)))), IteratedAccuracy);

    [TestMethod]
    [DataRow(-0.7)]
    [DataRow(1.9)]
    public void IRR_FromAGuessFarFromTheRate_StillFindsIt(double guess)
        => Assert.AreEqual(0.17743588442252728, ValueOf(Financial.IRR(Flows(Example), Variant(guess))), IteratedAccuracy);

    [TestMethod]
    public void IRR_OfALongInvestmentOnlyPartlyReturned_FromAGuessFarAbove_StillFindsIt()
        // forty years of 1000 a month return 480,000 of 800,000, a rate just below 0. From a guess of 30%, the
        // logarithm of what the outlay is worth over what the returns are falls too slowly to reach it in twenty
        // tries; the ratio itself, which the iteration measures up to e^4, falls fast enough. The VB runtime's own
        // function finds it neither from 30% nor from the default guess.
        => Assert.AreEqual(-0.0019682687904386776, ValueOf(Financial.IRR(Flows(Instalments(-800000, 1000, 480)), Variant(0.3))), IteratedAccuracy);

    [TestMethod]
    [DataRow(-0.5)]
    [DataRow(1.5)]
    [DataRow(2)]
    public void IRR_OfAMortgage_FromAGuessFarFromTheRate_StillFindsIt(double guess)
        // the VB runtime's own function finds this one only from a guess close to the rate, and from none of
        // these.
        => Assert.AreEqual(0.0050058250067624074, ValueOf(Financial.IRR(Flows(Instalments(-100000, 600, 360)), Variant(guess))), IteratedAccuracy);

    [TestMethod]
    public void IRR_AtARootTheFlowsOnlyTouch_IsThatRoot()
        // -100 + 220/(1+r) - 121/(1+r)² is -(10 - 11/(1+r))²: 0 at 10% and negative either side, so it changes
        // sign nowhere, and an imbalance of exactly 0 is all there is to find - here from the default guess, 10%,
        // the root itself.
        => Assert.AreEqual(0.1, ValueOf(Financial.IRR(Flows(-100, 220, -121))), IteratedAccuracy);

    [TestMethod]
    public void IRR_LandingOnARootTheFlowsOnlyTouch_IsThatRoot()
        // from a guess a few units in the last place above 10%, the iteration lands on the root, where the
        // imbalance is exactly 0 and negative either side of it: that is still the root.
        => Assert.AreEqual(0.1, ValueOf(Financial.IRR(Flows(-100, 220, -121), Variant(0.1000000000000003))), IteratedAccuracy);

    [TestMethod]
    [DataRow(new[] { 100d, 200, 300 })]
    [DataRow(new[] { -100d, -200, -300 })]
    [DataRow(new[] { 0d, 0, 0 })]
    [DataRow(new[] { -100d })]
    public void IRR_WithoutBothAPaymentAndAReceipt_IsError5(double[] flows)
        // "The array MUST contain at least one negative value (a payment) and one positive value (a receipt)".
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Financial.IRR(Flows(flows))));

    [TestMethod]
    public void IRR_WithNoRateThatBalances_IsError5()
        // 100 - 300/(1+r) + 300/(1+r)² is never zero: "If IRR can't find a result after 20 tries, it fails."
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Financial.IRR(Flows(100, -300, 300))));

    [TestMethod]
    public void IRR_WithAGuessOfMinusOneOrLess_IsError5()
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Financial.IRR(Flows(-100, 110), Variant(-1))));

    [TestMethod]
    [DataRow(new[] { -120000d, 39000, 30000, 21000, 37000, 46000 }, 0.1, 0.12, 0.12609413036590514)]
    [DataRow(new[] { -120000d, 39000, 30000, 21000 }, 0.1, 0.12, -0.04804465524998082)]
    [DataRow(new[] { -100d, 121 }, 0.1, 0.1, 0.21)]
    [DataRow(new[] { -1000d, 500, -200, 800, 300 }, 0.08, 0.1, 0.12032989991144444)]
    public void MIRR_GrowsTheFinancedPaymentsIntoTheReinvestedReceipts(double[] flows, double financeRate, double reinvestRate, double expected)
        // the first two rows are the MIRR examples of Excel's documentation. A payment in the middle of the flows is
        // still financed from the first.
        => AreClose(expected, Financial.MIRR(Flows(flows), new(financeRate), new(reinvestRate)));

    [TestMethod]
    public void MIRR_OfTheOfficeExample_FinancesAtTenPercentAndReinvestsAtTwelve()
        => AreClose(0.15512706281927663, Financial.MIRR(Flows(Example), new(0.1), new(0.12)));

    [TestMethod]
    public void MIRR_WithNoPaymentToFinance_IsError11()
        // the modified rate is the receipts over the payments, and there are none: a division by zero.
        => Assert.AreEqual((int)VBRuntimeErrorId.DivisionByZero, ErrorOf(Financial.MIRR(Flows(100, 200, 300), new(0.1), new(0.12))));

    [TestMethod]
    public void MIRR_WithNoReceiptToReinvest_IsMinusOne()
        // nothing comes back, which is a return of -100%.
        => AreClose(-1, Financial.MIRR(Flows(-100, -200, -300), new(0.1), new(0.12)));

    [TestMethod]
    public void MIRR_AtARateOfMinusOne_OrOfASingleFlow_IsError5()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Financial.MIRR(Flows(Example), new(-1), new(0.12))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Financial.MIRR(Flows(Example), new(0.1), new(-1))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Financial.MIRR(Flows(-100), new(0.1), new(0.1))));
    }

    [TestMethod]
    public void CashFlowFunctions_FromSource_TakeADoubleArray()
    {
        // source cannot fill an array yet, so the variable is given one before the body runs; the call site then
        // coerces it to the Double() the declaration asks for, the way it would any other argument. The named
        // arguments spell the specification's own names, underscores and all.
        var values = new VBModuleFieldVariableMemberSymbol(
            TestUri.WorkspaceRoot(), RuntimeSourceHarness.ModuleUri, "Values", ScopeKind.Module,
            new VBFixedSizeArrayType(VBDoubleType.TypeInfo), SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);
        var array = new VBFixedSizeArrayValue([(0, Example.Length - 1)], VBDoubleType.TypeInfo);
        for (var index = 0; index < Example.Length; index++)
        {
            array.TrySetElement(new ValueBindingHandle(new VBDoubleValue(Example[index]).RuntimeValue), index);
        }

        var output = new RuntimeOutputBuffer();
        var (_, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, [values], output, standardLibrary: true,
            session => session.Symbols.Resolver.GetValue(values).SetValue(
                session.Symbols.Resolver, new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(array))),
            "Debug.Print NPV(0.0625, Values)",
            "Debug.Print IRR(Values)",
            "Debug.Print IRR(Values, Guess:=0.1)",
            "Debug.Print MIRR(Values, 0.1, 0.12)",
            "Debug.Print MIRR(Values, Reinvest_Rate:=0.12, Finance_Rate:=0.1)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind,
            $"{outcome.ErrorInfo?.ErrorId} {outcome.ErrorInfo?.Description} | {outcome.ErrorInfo?.Verbose}");
        var printed = output.Lines;
        Assert.HasCount(5, printed);
        // Debug.Print shows fifteen significant digits at most: a unit of the fifteenth is 1E-10 of 19312.57...
        Assert.AreEqual(19312.570209535184, Printed(printed[0]), 1e-10);
        Assert.AreEqual(0.17743588442252728, Printed(printed[1]), IteratedAccuracy);
        Assert.AreEqual(0.17743588442252728, Printed(printed[2]), IteratedAccuracy);
        Assert.AreEqual(0.15512706281927663, Printed(printed[3]), 1e-15);
        Assert.AreEqual(0.15512706281927663, Printed(printed[4]), 1e-15);
    }
}
