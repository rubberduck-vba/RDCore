using RDCore.Runtime.Execution;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.Tests.Cli;
using System.Globalization;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.6.1</strong> The annuity functions — <c>FV</c>, <c>PV</c>, <c>Pmt</c>, <c>NPer</c>,
/// <c>Rate</c>, and the <c>IPmt</c> / <c>PPmt</c> split of a payment — which are one equation solved for a
/// different unknown each time.
/// </summary>
/// <remarks>
/// "For all arguments, cash paid out (such as deposits to savings) is represented by negative numbers; cash
/// received (such as dividend checks) is represented by positive numbers" — so borrowing 8000 is a present
/// value of -8000 to the lender, repaid by positive payments. The expected values are the exact ones, which the
/// VB runtime's own functions, ported to .NET as <c>Microsoft.VisualBasic.Financial</c>, agree with to twelve
/// significant digits, or an iterated rate to within 1E-07, except where a test says otherwise.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.6 Financial")]
public sealed class AnnuityFunctionTests
{
    private static readonly StdFinancial Financial = new();

    // "accurate to within 0.00001 percent": what an iterated rate is compared to.
    private const double IteratedAccuracy = 1e-7;

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

    private static IReadOnlyList<string> Run(params string[] body)
    {
        var output = new RuntimeOutputBuffer();
        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem: null, [], output, standardLibrary: true, body);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind,
            $"{outcome.ErrorInfo?.ErrorId} {outcome.ErrorInfo?.Description} | {outcome.ErrorInfo?.Verbose}");
        return output.Lines;
    }

    private static int RunToError(params string[] body)
    {
        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem: null, [], output: null, standardLibrary: true, body);

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        return outcome.ErrorInfo!.ErrorId;
    }

    private static double PrintedNumber(IReadOnlyList<string> output)
    {
        Assert.HasCount(1, output);
        return double.Parse(output[0].Trim(), CultureInfo.InvariantCulture);
    }

    // Debug.Print shows fifteen significant digits of a Double at most, so a printed number is its value to within
    // a unit of the fifteenth.
    private static void IsPrinted(double expected, IReadOnlyList<string> output)
        => Assert.AreEqual(expected, PrintedNumber(output), Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(expected))) - 14));

    [TestMethod]
    [DataRow(0.06 / 12, 120, -100, -1000, 0, 18207.331414678578)]
    [DataRow(0.06 / 12, 120, -100, -1000, 1, 18289.27108808181)]
    [DataRow(0.005, 10, -200, -500, 1, 2581.403374060179)]
    [DataRow(0.11 / 12, 35, -2000, 0, 1, 82846.24637189951)]
    [DataRow(0.05, 10.5, -100, 0, 0, 1338.2406087049155)]
    [DataRow(0.05, -10, -100, 0, 0, -772.1734929184812)]
    public void FV_GrowsThePresentValueAndEveryPayment(double rate, double nPer, double pmt, double pv, int due, double expected)
        // the third and fourth rows are the FV examples of Excel's own documentation, whose FV is this one.
        => AreClose(expected, Financial.FV(new(rate), new(nPer), new(pmt), Variant(pv), Variant(due)));

    [TestMethod]
    public void FV_WithAnOmittedPresentValue_TakesItAsZero()
        // "If omitted, the data value 0 is assumed."
        => AreClose(1257.7892535548829, Financial.FV(new(0.05), new(10), new(-100)));

    [TestMethod]
    public void FV_AtARateOfZero_AddsThePaymentsUp()
        => AreClose(2000, Financial.FV(new(0), new(10), new(-100), Variant(-1000), Variant(1)));

    [TestMethod]
    public void FV_ThatOverflows_IsError6()
        // (1.1)^10000 is past the largest Double. VBA raises Overflow for a Double calculation that leaves the
        // range rather than returning an infinity, and so does this - where the VB runtime's own function returns
        // the infinity.
        => Assert.AreEqual((int)VBRuntimeErrorId.Overflow, ErrorOf(Financial.FV(new(0.1), new(10000), new(-1), Variant(-1))));

    [TestMethod]
    [DataRow(0.0825, 20, -50000, 1000000, 1, 316811.95778831956)]
    [DataRow(0.0825, 20, -50000, 1000000, 0, 277054.5976721123)]
    [DataRow(0.08 / 12, 12 * 20, 500, 0, 0, -59777.14585118802)]
    [DataRow(0.05, 1000, -100, 0, 0, 2000)]
    [DataRow(0.05, 20000, -100, 0, 0, 2000)]
    [DataRow(-0.05, 10, -100, 0, 0, 1340.3651402301862)]
    public void PV_DiscountsTheFutureValueAndEveryPayment(double rate, double nPer, double pmt, double fv, int due, double expected)
        // the first row is the example of the Office documentation, word for word. Over a thousand periods the
        // present value approaches the perpetuity, 100 / 5% - and over twenty thousand it is that, although
        // (1.05)^20000 is too large for a Double: the VB runtime's own function answers NaN.
        => AreClose(expected, Financial.PV(new(rate), new(nPer), new(pmt), Variant(fv), Variant(due)));

    [TestMethod]
    public void PV_AtARateOfMinusOne_IsError6()
        // (1 + -1)^10 is zero, and the present value is a division by it.
        => Assert.AreEqual((int)VBRuntimeErrorId.Overflow, ErrorOf(Financial.PV(new(-1), new(10), new(-100))));

    [TestMethod]
    [DataRow(0.1 / 12, 48, -8000, 0, 0, 202.90066747797752)]
    [DataRow(0.1 / 12, 48, -8000, 0, 1, 201.22380245749838)]
    [DataRow(0.08 / 12, 10, 10000, 0, 0, -1037.0320893591522)]
    [DataRow(0.06 / 12, 18 * 12, 0, 50000, 0, -129.08116086799092)]
    [DataRow(0.05, 10, 1000, -500, 0, -89.75228748272835)]
    [DataRow(0.05 / 12, 360, 200000, 0, 0, -1073.643246024278)]
    [DataRow(0.05, 20000, 1000, 0, 0, -50)]
    [DataRow(0, 48, -8000, 1000, 1, 145.83333333333334)]
    public void Pmt_SpreadsTheDifferenceOverThePeriods(double rate, double nPer, double pv, double fv, int due, double expected)
        // over twenty thousand periods the payment is the interest alone, 5% of 1000, where the VB runtime's own
        // function answers NaN.
        => AreClose(expected, Financial.Pmt(new(rate), new(nPer), new(pv), Variant(fv), Variant(due)));

    [TestMethod]
    public void Pmt_OverNoPeriods_IsError5()
        // there is no payment that spreads anything over no periods at all.
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Financial.Pmt(new(0.05), new(0), new(1000))));

    [TestMethod]
    public void Pmt_WithADueOtherThanZeroOrOne_TakesItAsTheBeginningOfThePeriod()
        // the specification describes 0 and 1 only; any value other than 0 is true, and payments at the
        // beginning of the period are what 1 means.
        => AreClose(201.22380245749838, Financial.Pmt(new(0.1 / 12), new(48), new(-8000), Variant(0), Variant(2)));

    [TestMethod]
    public void Pmt_AtARateOfMinusOne_IsZero_NotNegativeZero()
        => Assert.AreEqual("0", ValueOf(Financial.Pmt(new(-1), new(10), new(1000))).ToString(CultureInfo.InvariantCulture));

    [TestMethod]
    [DataRow(0.1 / 12, -200, 8000, 0, 0, 48.8582651211073)]
    [DataRow(0.1 / 12, -200, 8000, 0, 1, 48.36136005227496)]
    [DataRow(0.12 / 12, -100, -1000, 10000, 1, 59.673865674294625)]
    [DataRow(0.12 / 12, -100, -1000, 0, 0, -9.578594039813167)]
    [DataRow(0, -100, 1000, -500, 1, 5)]
    [DataRow(0.1, 0, -100, 200, 0, 7.2725408973417185)]
    [DataRow(-0.05, -100, 1000, 0, 0, 7.904836547339712)]
    public void NPer_CountsThePeriodsTheAnnuityTakes(double rate, double pmt, double pv, double fv, int due, double expected)
        // the last-but-one row makes no payments at all: it is compounding alone doubling the balance.
        => AreClose(expected, Financial.NPer(new(rate), new(pmt), new(pv), Variant(fv), Variant(due)));

    [TestMethod]
    [DataRow(0, 0, 1000)]
    [DataRow(-1, -100, 1000)]
    [DataRow(0.1, -50, 1000)]
    [DataRow(0.1, -100, 1000)]
    public void NPer_WhenNoNumberOfPeriodsCouldDoIt_IsError5(double rate, double pmt, double pv)
        // no payment at no interest; a rate that discounts everything to nothing; and a payment that never covers
        // the interest, or only exactly covers it, so it never pays anything off.
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Financial.NPer(new(rate), new(pmt), new(pv))));

    [TestMethod]
    [DataRow(0.1 / 12, 1, 48, -8000, 0, 0, 66.66666666666667)]
    [DataRow(0.1 / 12, 48, 48, -8000, 0, 0, 1.676865020479153)]
    [DataRow(0.1, 3, 3, 8000, 0, 0, -292.4471299093656)]
    [DataRow(0.1 / 12, 2, 48, -8000, 0, 1, 64.98980164618752)]
    [DataRow(0.05, 3, 10, -1000, 500, 1, 43.73846717430509)]
    [DataRow(0.05 / 12, 360, 360, 200000, 0, 0, -4.454951228316506)]
    public void IPmt_IsTheInterestOnTheBalanceOutstanding(double rate, double per, double nPer, double pv, double fv, int due, double expected)
        // the first period of a loan pays a whole period's interest on all of it: 8000 at 10% / 12.
        => AreClose(expected, Financial.IPmt(new(rate), new(per), new(nPer), new(pv), Variant(fv), Variant(due)));

    [TestMethod]
    [DataRow(0.05, 1, 10, 100, 1000000, 0, -5)]
    [DataRow(0.0288, 1, 3, 0, 3943510.9131790535, 0, 0)]
    [DataRow(0.05, 5, 10, 0, 1000000, 0, 17133.732808649453)]
    [DataRow(0.05, 5, 10, 0, 1000000, 1, 16317.840770142337)]
    public void IPmt_OfASavingsPlan_IsTheInterestOnWhatHasBeenSaved(double rate, double per, double nPer, double pv, double fv, int due, double expected)
        // the first period earns interest on the present value alone - on nothing at all, for a plan that starts
        // from nothing - however large the future value the payments are saved towards.
        => AreClose(expected, Financial.IPmt(new(rate), new(per), new(nPer), new(pv), Variant(fv), Variant(due)));

    [TestMethod]
    public void IPmt_OfTheFirstPaymentDueAtTheBeginning_IsZero()
        // it is made before anything has had a period to earn interest.
        => Assert.AreEqual(0, ValueOf(Financial.IPmt(new(0.1 / 12), new(1), new(48), new(-8000), Variant(0), Variant(1))));

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(49)]
    [DataRow(50)]
    public void IPmt_OfAPeriodOutsideTheAnnuity_IsError5(double per)
        // "payment period in the range 1 through NPer", which a fraction of a period either side of it still is -
        // as it is to the VB runtime's own function - but a whole one is not.
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Financial.IPmt(new(0.1 / 12), new(per), new(48), new(-8000))));

    [TestMethod]
    [DataRow(0.5, 67.23078503519196)]
    [DataRow(48.5, 0.8401720042627381)]
    public void IPmt_OfAFractionOfAPeriodEitherSideOfTheAnnuity_IsStillAnAnswer(double per, double expected)
        => AreClose(expected, Financial.IPmt(new(0.1 / 12), new(per), new(48), new(-8000)));

    [TestMethod]
    [DataRow(14420, 0, 0, -50)]
    [DataRow(20000, 0, 0, -2.3809523809523814)]
    [DataRow(20000, 500, 1, 20.408163265306122)]
    public void IPmt_OfAPeriodFarIntoALongAnnuity_IsStillAnAnswer(double per, double fv, int due, double expected)
        // 1.05 to the power of 20000 is far beyond a Double, and so is what the payments made by the 14420th period
        // have grown to; what that is over the growth of the whole annuity is not. The VB runtime's own function
        // answers NaN for all three.
        => AreClose(expected, Financial.IPmt(new(0.05), new(per), new(20000), new(1000), Variant(fv), Variant(due)));

    [TestMethod]
    [DataRow(0.1 / 12, 1, 48, -8000, 0, 0, 136.23400081131086)]
    [DataRow(0.1 / 12, 48, 48, -8000, 0, 0, 201.22380245749838)]
    [DataRow(0.08, 10, 10, 200000, 0, 0, -27598.053462421376)]
    [DataRow(0.1 / 12, 1, 48, -8000, 0, 1, 201.22380245749838)]
    [DataRow(0, 1, 48, -8000, 0, 0, 166.66666666666666)]
    [DataRow(0.1 / 12, 2, 48, -8000, 0, 1, 136.23400081131086)]
    [DataRow(0.05, 3, 10, -1000, 500, 0, 43.826896949708)]
    [DataRow(0.05, 3, 10, -1000, 500, 1, 41.73990185686476)]
    [DataRow(-0.05, 3, 10, -1000, 200, 1, 94.70096731025713)]
    public void PPmt_IsWhatThePaymentPaysOffOnceTheInterestIsPaid(double rate, double per, double nPer, double pv, double fv, int due, double expected)
        // a first payment due at the beginning of the period is all principal, there being no interest yet.
        => AreClose(expected, Financial.PPmt(new(rate), new(per), new(nPer), new(pv), Variant(fv), Variant(due)));

    [TestMethod]
    [DataRow(1, 1.2298272132899146E-15)]
    [DataRow(50, 5.227761818279862E-07)]
    public void PPmt_OfALoanThatBarelyPaysAnythingOff_IsStillExact(double per, double expected)
        // at 50% a period over a hundred periods, the interest is all but the whole payment, and the principal is
        // what is left of it - which as a difference would be all rounding: the VB runtime's own function answers
        // 0 for both.
        => Assert.AreEqual(expected, ValueOf(Financial.PPmt(new(0.5), new(per), new(100), new(-1000))), expected * 1e-12);

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(49)]
    [DataRow(50)]
    public void PPmt_OfAPeriodOutsideTheAnnuity_IsError5(double per)
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Financial.PPmt(new(0.1 / 12), new(per), new(48), new(-8000))));

    [TestMethod]
    public void PPmt_AndIPmt_AddUpToThePayment()
    {
        var payment = ValueOf(Financial.Pmt(new(0.05 / 12), new(360), new(200000)));
        var interest = ValueOf(Financial.IPmt(new(0.05 / 12), new(120), new(360), new(200000)));
        var principal = ValueOf(Financial.PPmt(new(0.05 / 12), new(120), new(360), new(200000)));

        Assert.AreEqual(payment, interest + principal, 1e-9);
    }

    [TestMethod]
    [DataRow(48, -200, 8000, 0, 0, 0.1, 0.007701472488202044)]
    [DataRow(48, -200, 8000, 0, 1, 0.1, 0.008052981923906034)]
    [DataRow(48, -200, 8000, 0, 0, 0, 0.007701472488202044)]
    [DataRow(48, -200, 8000, 0, 0, 0.9, 0.007701472488202044)]
    [DataRow(360, -1073.64, 200000, 0, 0, 0.1, 0.004166644536345542)]
    [DataRow(60, -250, 10000, -2000, 0, 0.1, 0.017307008674430364)]
    [DataRow(240, -1000, 0, 1000000, 0, 0.1, 0.010066782941539579)]
    [DataRow(120, -100, 0, 20000, 0, 0.1, 0.007984103181033108)]
    [DataRow(10, 0, -1000, 2000, 0, 0.1, 0.07177346253629316)]
    [DataRow(10, -90, 1000, 0, 0, 0.1, -0.01871166542290458)]
    public void Rate_IsTheRateAtWhichTheAnnuityBalances(double nPer, double pmt, double pv, double fv, int due, double guess, double expected)
        // a loan; a 30-year mortgage; a loan with a balloon; two savings plans, deposits made towards a sum; the
        // tenth root of 2, compounding alone doubling the balance; and a loan repaid by less than was borrowed.
        => Assert.AreEqual(expected, ValueOf(Financial.Rate(new(nPer), new(pmt), new(pv), Variant(fv), Variant(due), Variant(guess))), IteratedAccuracy);

    [TestMethod]
    [DataRow(60, -100, 100, 7500, 1, 0.007741239876093791)]
    [DataRow(120, -100, 10, 15000, 1, 0.0035848628238188684)]
    [DataRow(120, -500, 1, 71900, 1, 0.002911433372973386)]
    [DataRow(120, -500, 1, 71900, 0, 0.0029577101120509164)]
    [DataRow(120, -500, 100, 71900, 0, 0.002989013967145951)]
    [DataRow(108, -871.21, 9755.36, 101220.67832398658, 1, 0.003685130976742861)]
    [DataRow(120, -100, 1000, 14568.537946613511, 0, 0.004999999999999481)]
    [DataRow(360, -400, 5200, 264923.20449585986, 1, 0.0034000000000001763)]
    [DataRow(480, -100, 1200, 186000.12902711597, 0, 0.004999999999999821)]
    [DataRow(480, -100, 1500, 183708.63829278492, 1, 0.00499999999999982)]
    [DataRow(508, -924.02, 29906.73, 841403207.4303956, 1, 0.02206194883602405)]
    [DataRow(480, -100, 1000, 188191.61976145758, 0, 0.005)]
    [DataRow(480, 100, -1000, -188191.61976145758, 0, 0.005)]
    public void Rate_OfASavingsPlanWithABonusToStartIt_IsTheRateOfTheSavings(double nPer, double pmt, double pv, double fv, int due, double expected)
        // a present value of the future value's sign can give the equation a second root, below the rate at which
        // the payments are only the interest on the present value - 500% a period for the fifth row; the first
        // three, paying a bonus of no more than a payment at the start of each period, have none - and from a
        // guess of 10% a ratio of what is received to what is paid can head for it. For the other rows that rate
        // is from 3.2% to 10%, no more than the guess. In five of them the first bracket holds the second root:
        // that is found, and then the lower root, heading down from the bracket's lower end. In the eleventh, a
        // step from above lands below the rate, so the first bracket holds the rate itself, and nothing is found
        // below it. The last two are one plan with its signs reversed, from a guess of exactly its interest-only
        // rate, where the imbalance rounds to 0: the guess is taken as the second root, and the lower one is found
        // below it, the same way in both. The VB runtime's own function finds neither the eleventh row nor the
        // last.
        => Assert.AreEqual(expected, ValueOf(Financial.Rate(new(nPer), new(pmt), new(pv), Variant(fv), Variant(due))), IteratedAccuracy);

    [TestMethod]
    [DataRow(191, -292.2357407372892, 1560.3127525952086, 235207.16792899993, 0.5, 0.01338830314338588)]
    [DataRow(360, -100, 5000, 49964.4718552713, 0.022, 0.002999999999999612)]
    [DataRow(360, 100, -5000, -49964.4718552713, 0.022, 0.002999999999999612)]
    [DataRow(252, -400, 6400, 173813.0383463367, 0.05, 0.004799999999999772)]
    [DataRow(480, -100, 1200, 186000.12902711597, -0.02, 0.004999999999999821)]
    [DataRow(480, -100, 1200, 186000.12902711597, -0.05, 0.004999999999999821)]
    public void Rate_OfASavingsPlanWithABonus_FromAGuessAwayFromTheRate_IsTheRateOfTheSavings(
        double nPer, double pmt, double pv, double fv, double guess, double expected)
        // from 50%, above both roots, the first bracket holds the second root, at 18.7%: that is found, and then
        // the lower root, heading down from the bracket's lower end. From 2.2%, above the 2% at which the payments
        // are only the interest on the bonus, the first bracket's lower end is so near the second root, at 1.98%,
        // that a step up from it would bracket that root again: the iteration steps down - and does the same with
        // every sign reversed. From 5%, the first bracket reaches down to -10.7%, where the imbalance in future
        // value is all but flat, and would close too slowly on it: it is closed on the logarithm of what is
        // received over what is paid instead. From -2% and -5%, below the rate, that imbalance is all but flat and
        // falling, and a ratio of what is received to what is paid leads to the rate instead. The VB runtime's own
        // function finds the first, second and fourth, answers the third with the second root, and finds neither
        // of the last two.
        => Assert.AreEqual(expected, ValueOf(Financial.Rate(new(nPer), new(pmt), new(pv), Variant(fv), Variant(0), Variant(guess))), IteratedAccuracy);

    [TestMethod]
    public void Rate_OfASavingsPlanWithNoLowerRoot_FindsTheOneItHasFromAGuessAboveIt()
        // a payout of less than one payment leaves no rate below the trough at which the plan balances: its only
        // root is just below 200%, where the payments are only the interest on the bonus, and from a guess of 300%
        // it is found first, and nothing below it, so it is the result. The VB runtime's own function answers
        // -1.25, which is no rate at all.
        => Assert.AreEqual(1.9999902149290758, ValueOf(Financial.Rate(new(12), new(-1000), new(500), Variant(800), Variant(0), Variant(3))), IteratedAccuracy);

    [TestMethod]
    public void Rate_OfASavingsPlanWhoseLowerRootIsOutOfReach_IsTheSecondRoot()
        // over a period and a half, a payout only just above one payment puts the lower root at -99.9%, which
        // heading down from the second root, at 629%, does not reach in the tries a guess of 3000% leaves: the
        // second root, found first, is the result, as it is for the VB runtime's own function.
        => Assert.AreEqual(6.294156735276298, ValueOf(Financial.Rate(new(1.5), new(-1000), new(100), Variant(1001), Variant(0), Variant(30))), IteratedAccuracy);

    [TestMethod]
    public void Rate_WithAnOmittedGuess_StartsFromTenPercent()
        // "If omitted, guess is the data value 0.1 (10 percent)." This annuity balances at both 10% and 0%, and a
        // guess of 0 finds the other one.
        => Assert.AreEqual(0.1, ValueOf(Financial.Rate(new(2), new(210), new(-100), Variant(-320))), IteratedAccuracy);

    [TestMethod]
    public void Rate_WithAMissingGuess_StartsFromTenPercent()
        // Missing is an omitted argument, whoever passes it on.
        => Assert.AreEqual(0.1, ValueOf(Financial.Rate(new(2), new(210), new(-100), Variant(-320), Variant(0), new(VBMissingValue.Missing))), IteratedAccuracy);

    [TestMethod]
    public void Rate_WithAnExplicitEmptyGuess_StartsFromZero()
        // Empty is not an omitted argument but a value, which as a number is 0 (MS-VBAL 5.5.1.2.11): a guess of 0,
        // which finds the annuity's other root.
        => Assert.AreEqual(0, ValueOf(Financial.Rate(new(2), new(210), new(-100), Variant(-320), Variant(0), new(VBEmptyValue.Empty))), IteratedAccuracy);

    [TestMethod]
    public void Pmt_WithAnExplicitEmptyFutureValueAndDue_IsTheSameAsLeavingThemOut()
        // Empty is 0, and 0 is what an omitted FV or Due is assumed to be.
        => AreClose(202.90066747797752, Financial.Pmt(new(0.1 / 12), new(48), new(-8000), new(VBEmptyValue.Empty), new(VBEmptyValue.Empty)));

    [TestMethod]
    [DataRow(48, -200, 8000, 0, -0.8, 0.007701472488202044)]
    [DataRow(360, -1073.64, 200000, 0, 1.02, 0.004166644536345542)]
    [DataRow(360, -1073.64, 200000, 0, 1.91, 0.004166644536345542)]
    [DataRow(120, -100, 0, 20000, -0.5, 0.007984103181033108)]
    public void Rate_FromAGuessFarFromTheRate_StillFindsIt(double nPer, double pmt, double pv, double fv, double guess, double expected)
        // -80%, 102%, 191% and -50% a period, for a loan, a mortgage and a savings plan at under 1%: the VB
        // runtime's own function finds none of them within its forty tries. From -50%, the savings plan's first
        // step would take the iteration to a rate it could not come back from, if a step were not limited in
        // length.
        => Assert.AreEqual(expected, ValueOf(Financial.Rate(new(nPer), new(pmt), new(pv), Variant(fv), Variant(0), Variant(guess))), IteratedAccuracy);

    [TestMethod]
    public void Rate_OfAnAnnuityThatBalancesAtZero_IsZero()
        // 10 payments of 100 repay 1000 with no interest at all. The VB runtime's own function cannot find this
        // one: its test of the result is how far the equation is from balancing, and near a rate of zero that is
        // noise above its tolerance. The test here is where the rate changes sign, and it does at 0.
        => Assert.AreEqual(0, ValueOf(Financial.Rate(new(10), new(-100), new(1000))), IteratedAccuracy);

    [TestMethod]
    public void Rate_AtARootTheAnnuityOnlyTouches_IsThatRoot()
        // -100·(1+r)² + 200·(2+r) - 300 is -100·r²: 0 at 0 and negative either side, so it changes sign nowhere,
        // and an imbalance of exactly 0 is all there is to find - here from a guess of 0, the root itself.
        => Assert.AreEqual(0, ValueOf(Financial.Rate(new(2), new(200), new(-100), Variant(-300), Variant(0), Variant(0))), IteratedAccuracy);

    [TestMethod]
    [DataRow(0, -200, 8000)]
    [DataRow(-48, -200, 8000)]
    public void Rate_OverNoPeriods_IsError5(double nPer, double pmt, double pv)
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Financial.Rate(new(nPer), new(pmt), new(pv))));

    [TestMethod]
    public void Rate_WithNoRateThatBalances_IsError5()
        // 1000 grows at any rate, with no payment and no future value to offset it, so there is nothing to
        // iterate towards. The VB runtime's own function answers -0.906 here, where 1000 * (1+r)^10 has fallen
        // below its tolerance on the way to -1 without ever reaching 0.
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Financial.Rate(new(10), new(0), new(1000))));

    [TestMethod]
    public void Rate_ThatIteratesWithoutFindingOne_IsError5()
        // 100·(1+r)² - 300·(2+r) + 600 is never zero - "If Rate can't find a result after 20 tries, it fails."
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Financial.Rate(new(2), new(-300), new(100), Variant(600))));

    [TestMethod]
    public void AnOptionalArgumentThatIsNull_IsError94()
        // MS-VBAL 5.5.1.2.10: Let-coercing Null to anything but a Variant raises "Invalid use of Null".
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull,
            ErrorOf(Financial.Pmt(new(0.05), new(10), new(1000), new(VBNullValue.Null))));

    [TestMethod]
    public void AnOptionalArgumentThatIsAString_IsError13()
    {
        // even one that reads as a number: parsing it takes the regional settings of a session this module does
        // not have, where the call site coerces a String to a required Double parameter with them.
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch,
            ErrorOf(Financial.Pmt(new(0.1 / 12), new(48), new(-8000), new(new VBStringValue("1")))));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, RunToError("Debug.Print Pmt(0.1 / 12, 48, -8000, , \"1\")"));
    }

    [TestMethod]
    public void Pmt_FromSource_TakesIntegerArgumentsAndOmittedOptionals()
        // 48 and -8000 are Integer literals, Let-coerced to the Double parameters at the call site; FV and Due are
        // left out, and arrive as the null an omitted argument is.
        => IsPrinted(202.90066747797752, Run("Debug.Print Pmt(0.1 / 12, 48, -8000)"));

    [TestMethod]
    public void Pmt_FromSource_TakesANamedOptionalArgument()
        => IsPrinted(201.22380245749838, Run("Debug.Print Pmt(0.1 / 12, 48, -8000, Due:=1)"));

    [TestMethod]
    public void PV_FromSource_TakesAnOptionalArgumentLeftOutInTheMiddle()
        => IsPrinted(810.7821675644053, Run("Debug.Print PV(0.05, 10, -100, , 1)"));

    [TestMethod]
    public void Rate_FromSource_StartsFromTheDefaultGuess()
        // the omitted guess arrives as null, and the default stands in for it - where a guess of 0 finds the other
        // root, 0.
        => Assert.AreEqual(0.1, PrintedNumber(Run("Debug.Print Rate(2, 210, -100, -320)")), IteratedAccuracy);

    [TestMethod]
    public void Rate_FromSource_WithAnExplicitEmptyGuess_StartsFromZero()
        => Assert.AreEqual(0, PrintedNumber(Run("Debug.Print Rate(2, 210, -100, -320, , Empty)")), IteratedAccuracy);

    [TestMethod]
    public async Task Rate_FromSource_OfAGuessPassedOn_TellsAnOmittedOneFromAnExplicitEmpty()
    {
        // an omitted Optional Variant is Missing, and passed on it is still an omitted argument; an explicit Empty
        // passed on through the same parameter is a guess of 0.
        var printed = await ModuleWorkspace.RunAsync([], ModuleWorkspace.StandardModule("Program",
            "Option Explicit",
            "Private Function RateFrom(Optional guess As Variant) As Double",
            "    RateFrom = Rate(2, 210, -100, -320, , guess)",
            "End Function",
            "Public Sub Main()",
            "    Debug.Print RateFrom()",
            "    Debug.Print RateFrom(Empty)",
            "End Sub"));

        Assert.HasCount(2, printed, string.Join(" | ", printed));
        Assert.AreEqual(0.1, double.Parse(printed[0], CultureInfo.InvariantCulture), IteratedAccuracy);
        Assert.AreEqual(0, double.Parse(printed[1], CultureInfo.InvariantCulture), IteratedAccuracy);
    }

    [TestMethod]
    [DataRow("Debug.Print Pmt(0.05, 0, 1000)")]
    [DataRow("Debug.Print PPmt(0.1 / 12, 49, 48, -8000)")]
    public void AnArgumentAFunctionCannotComputeWith_FromSource_IsRunTimeError5(string statement)
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, RunToError(statement));
}
