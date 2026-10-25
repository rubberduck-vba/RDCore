using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdFinancialModule"/>
/// <remarks>
/// Every member is a function of its arguments alone: the session is what a <c>String</c> given for an optional argument is read by, the regional settings
/// of its environment, as a Let-coercion to <c>Double</c> reads it.
/// <para>
/// The five annuity functions — <see cref="FV"/>, <see cref="PV"/>, <see cref="Pmt"/>, <see cref="NPer"/>
/// and <see cref="Rate"/> — are one equation solved for a different unknown each time: the present value
/// grown over the periods, plus every payment grown to the end, plus the future value, is zero. With a
/// rate <c>r</c> per period over <c>n</c> periods, and payments made <c>d</c> periods early (<c>1</c> when
/// they are due at the start of a period, otherwise <c>0</c>):
/// <code>
/// pv·(1+r)ⁿ + pmt·(1+r·d)·((1+r)ⁿ−1)/r + fv = 0      (r ≠ 0)
/// pv + pmt·n + fv = 0                                (r = 0)
/// </code>
/// <see cref="IPmt"/> and <see cref="PPmt"/> split one of those payments into the interest on the balance
/// outstanding and the rest.
/// </para>
/// <para>
/// 👉 The specification names no error for any of these, so they are decided here, and tabulated in RD-VBAL
/// §6.1.2.6: an argument a function cannot compute with is error 5, and so is an iteration that finds no result;
/// a <c>Double</c> result that overflows is error 6 rather than an infinity, the error MS-VBA raises for a
/// <c>Double</c> calculation that leaves the range; and <see cref="MIRR"/> with no payment to finance is error
/// 11, the division by zero it would take.
/// </para>
/// </remarks>
/// <param name="session">The session whose regional settings a <c>String</c> argument is read by.</param>
public sealed class StdFinancial(IRuntimeSession session) : IStdFinancialModule
{
    // MS-VBAL 6.1.2.6.1.1: "If omitted, the data value 2 (double-declining method) is assumed."
    private const double DefaultFactor = 2;

    // MS-VBAL 6.1.2.6.1.4 and 6.1.2.6.1.11: "If omitted, Guess is the data value 0.1 (10 percent)."
    private const double DefaultGuess = 0.1;

    // MS-VBAL 6.1.2.6.1.4 and 6.1.2.6.1.11: "cycles through the calculation until the result is accurate to
    // within 0.00001 percent. If IRR can't find a result after 20 tries, it fails." A try is one step of the
    // iteration, and a rate is accurate once a root is known to be within 0.00001 percent of it - of a rate that
    // is itself a fraction, so 1E-07.
    private const int MaximumTries = 20;
    private const double Accuracy = 0.00001 / 100;

    // the iteration needs two points to start from. The second is this far from the guess in ln(1 + r): far
    // enough that the difference between the two is well clear of rounding, near enough that the line through
    // them follows the curve.
    private const double SecondPoint = 0.01;

    // the longest first step a try takes before two points straddle a root, in ln(1 + r) - a factor of e in
    // 1 + r - and twice as long each time in a row a step has had to be cut short. A secant step from a stretch
    // where the imbalance hardly changes can otherwise throw the iteration to a rate so far away that it cannot
    // come back in the tries it has left, while a root that really is far away is still reached in a few.
    private const double LongestStep = 1;

    // where the iteration stops measuring a ratio as itself and goes on with its logarithm. That the logarithm is
    // the straighter of the two far enough from the root follows from their shapes; where it takes over was found
    // by trial: any ratio from e^2.5 to e^5 converged on every loan, mortgage and series of cash flows tried, and
    // e⁴ took the fewest tries at the extremes.
    private const double LargestLogRatio = 4;

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> DDB(
        VBDoubleValue cost, VBDoubleValue salvage, VBDoubleValue life, VBDoubleValue period, VBVariantValue? factor = default)
    {
        if (!TryReadOptional(factor, DefaultFactor, out var rateFactor, out var error))
        {
            return error;
        }

        // "All arguments MUST be positive numbers" - but an asset worth nothing at the end is an ordinary one, and
        // one that cost nothing depreciates by nothing rather than being refused. The period has to be one of the
        // asset's life, so a life that is not positive has no period at all.
        if (salvage.Value < 0 || period.Value <= 0 || period.Value > life.Value || rateFactor <= 0)
        {
            return InvalidArgument(nameof(DDB),
                "the salvage is not negative, the period is greater than 0 and no greater than the life, and the factor is greater than 0");
        }

        return Result(nameof(DDB), Depreciation(cost.Value, salvage.Value, life.Value, period.Value, rateFactor));
    }

    // the specification's formula, "((Cost - Salvage) * Factor) / Life", is the same for every period and so
    // cannot be the one that computes it: a declining balance loses Factor / Life of what is left of it each
    // period, and stops at the salvage value. Before the period, what is left is Cost * (1 - Factor / Life)
    // raised to the number of periods already gone - which also answers a fractional period.
    private static double Depreciation(double cost, double salvage, double life, double period, double factor)
    {
        if (cost <= 0)
        {
            // nothing to depreciate.
            return 0;
        }

        var rate = factor / life;
        if (period <= 1)
        {
            // the first period's share of the cost, unless that is more than there is to depreciate.
            return Math.Min(cost * rate, cost - salvage);
        }

        if (rate >= 1)
        {
            // a rate of 100 percent or more took everything there was in the first period.
            return 0;
        }

        var before = cost * Math.Pow(1 - rate, period - 1);
        return Math.Max(0, Math.Min(before * rate, before - salvage));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> FV(
        VBDoubleValue rate, VBDoubleValue nPer, VBDoubleValue pmt, VBVariantValue? pV = default, VBVariantValue? due = default)
    {
        if (!TryReadOptional(pV, 0, out var present, out var error) || !TryReadDue(due, out var early, out error))
        {
            return error;
        }

        return Result(nameof(FV), FutureValue(rate.Value, nPer.Value, pmt.Value, present, early));
    }

    private static double FutureValue(double rate, double periods, double payment, double present, double early)
    {
        if (rate == 0)
        {
            return -(present + payment * periods);
        }

        if (Discount(rate, periods) is (_, var lost))
        {
            return -(present + payment * (1 + rate * early) * lost / rate) * Grown(rate, periods).Growth;
        }

        var (growth, gained) = Grown(rate, periods);
        return -(present * growth + payment * (1 + rate * early) * gained / rate);
    }

    // what 1 grows to over the periods, (1+r)ⁿ, and what it gains, (1+r)ⁿ - 1 - the second computed without the
    // cancellation subtracting the 1 suffers near a rate of 0, where every digit of (1+r)ⁿ but the last few is
    // the 1. Neither is computed from the other: 1 plus a gain of almost -1 is 0, where (1+r)ⁿ is merely tiny. A
    // rate of -1 or less has no logarithm to take them through, and no such precision to keep.
    private static (double Growth, double Gained) Grown(double rate, double periods)
    {
        if (rate <= -1)
        {
            var growth = Math.Pow(1 + rate, periods);
            return (growth, growth - 1);
        }

        var exponent = periods * Log1P(rate);
        return (Math.Exp(exponent), ExpM1(exponent));
    }

    // what 1 is discounted to over the periods, (1+r)⁻ⁿ, and what it loses, 1 - (1+r)⁻ⁿ - for an annuity that
    // grows, whose growth can be too large for a Double long before its discount is too small for one, and whose
    // value the discount states without dividing one infinity by another. null for one that does not grow, where
    // it is the discount that can be too large.
    private static (double Factor, double Lost)? Discount(double rate, double periods)
    {
        if (rate <= -1)
        {
            return null;
        }

        var exponent = periods * Log1P(rate);
        return exponent > 0 ? (Math.Exp(-exponent), -ExpM1(-exponent)) : null;
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> IPmt(
        VBDoubleValue rate, VBDoubleValue per, VBDoubleValue nPer, VBDoubleValue pV,
        VBVariantValue? fV = default, VBVariantValue? due = default)
    {
        if (!TryReadOptional(fV, 0, out var future, out var error)
            || !TryReadDue(due, out var early, out error)
            || !TryPeriodPayment(nameof(IPmt), rate.Value, per.Value, nPer.Value, pV.Value, future, early, out _, out error))
        {
            return error;
        }

        // a payment due at the start of the first period is made before anything has earned interest.
        if (early != 0 && per.Value == 1)
        {
            return Result(nameof(IPmt), 0);
        }

        // any other pays what the balance outstanding at the start of its period earns over it - discounted by a
        // period when it is made at the start of the period, a period before that interest is due.
        var interest = Balance(rate.Value, per.Value - 1, nPer.Value, pV.Value, future) * rate.Value;
        return Result(nameof(IPmt), early != 0 ? interest / (1 + rate.Value) : interest);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> PPmt(
        VBDoubleValue rate, VBDoubleValue per, VBDoubleValue nPer, VBDoubleValue pV,
        VBVariantValue? fV = default, VBVariantValue? due = default)
    {
        if (!TryReadOptional(fV, 0, out var future, out var error)
            || !TryReadDue(due, out var early, out error)
            || !TryPeriodPayment(nameof(PPmt), rate.Value, per.Value, nPer.Value, pV.Value, future, early, out var payment, out error))
        {
            return error;
        }

        // what is not interest is principal: all of a payment due at the start of the first period.
        return Result(nameof(PPmt), early != 0 && per.Value == 1
            ? payment
            : Principal(rate.Value, per.Value, nPer.Value, pV.Value, future, early));
    }

    // the payment made in a period, which IPmt and PPmt split into interest and principal.
    private static bool TryPeriodPayment(
        string function, double rate, double per, double periods, double present, double future, double early,
        out double payment, out RuntimeSemanticsEvaluationResult<VBDoubleValue> error)
    {
        // "payment period in the range 1 through NPer": a period that is part of the annuity at all, which a
        // fraction of one either side of that range still is.
        if (per <= 0 || per >= periods + 1)
        {
            payment = default;
            error = InvalidArgument(function, $"the period is greater than 0 and less than one more than the number of periods ({periods})");
            return false;
        }

        return TryPayment(function, rate, periods, present, future, early, out payment, out error);
    }

    // the balance outstanding once the given number of periods has gone: what the loan and the payments made so
    // far have grown to. Counted that way it is a difference that cancels, and so is what the payments still to
    // make and the future value are worth - but with the payment the annuity equation gives substituted into it,
    // it is -(pv·((1+r)ⁿ - (1+r)ᵗ) - fv·((1+r)ᵗ - 1)) / ((1+r)ⁿ - 1), whose two terms share a sign for a loan, a
    // savings plan and a balloon alike, whenever the payments are due. The powers are taken in ln(1 + r), and for
    // an annuity that grows divided through by (1+r)ⁿ - fv's as (1+r)ᵗ⁻ⁿ·(1 - (1+r)⁻ᵗ) - so that none of them
    // overflows.
    private static double Balance(double rate, double gone, double periods, double present, double future)
    {
        if (rate == 0)
        {
            return -(present * (periods - gone) - future * gone) / periods;
        }

        if (rate <= -1)
        {
            var (atGone, _) = Grown(rate, gone);
            var (atEnd, gained) = Grown(rate, periods);
            return -(present * (atEnd - atGone) - future * (atGone - 1)) / gained;
        }

        var y = Log1P(rate);
        return periods * y > 0
            ? -(present * -ExpM1((gone - periods) * y) - future * Math.Exp((gone - periods) * y) * -ExpM1(-gone * y)) / -ExpM1(-periods * y)
            : -(present * Math.Exp(gone * y) * ExpM1((periods - gone) * y) - future * ExpM1(gone * y)) / ExpM1(periods * y);
    }

    // the principal part of the period's payment: the payment less the interest, but not computed as that
    // difference, which is all rounding once the interest is nearly the whole payment. Each period's principal
    // reduces the balance, and so the next period's interest by the rate times it, which the next principal gains
    // instead: the principal grows by the rate each period. Taken from the annuity equation, it is
    // -(pv + fv)·r·(1+r)ᵗ / ((1 + r·d)·((1+r)ⁿ - 1)), with t the periods before this one - a single product.
    private static double Principal(double rate, double per, double periods, double present, double future, double early)
        => rate == 0
            ? -(present + future) / periods
            : -(present + future) * rate / (1 + rate * early) * GrowthOverGain(rate, per - 1, periods);

    // (1+r)ᵗ / ((1+r)ⁿ - 1), which for an annuity that grows is (1+r)ᵗ⁻ⁿ / (1 - (1+r)⁻ⁿ): the same number, with
    // no power large enough to overflow however many periods there are.
    private static double GrowthOverGain(double rate, double gone, double periods)
    {
        if (rate <= -1)
        {
            return Grown(rate, gone).Growth / Grown(rate, periods).Gained;
        }

        var y = Log1P(rate);
        return periods * y > 0
            ? Math.Exp((gone - periods) * y) / -ExpM1(-periods * y)
            : Math.Exp(gone * y) / ExpM1(periods * y);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> IRR(
        VBResizableArrayValue valueArray, VBVariantValue? guess = default)
    {
        if (!TryReadOptional(guess, DefaultGuess, out var start, out var error)
            || !TryReadCashFlows(nameof(IRR), valueArray, out var flows, out error))
        {
            return error;
        }

        // "The array MUST contain at least one negative value (a payment) and one positive value (a receipt)":
        // without both there is no rate at which the flows balance, and iterating would only fail to find one.
        if (!flows.Any(flow => flow < 0) || !flows.Any(flow => flow > 0))
        {
            return InvalidArgument(nameof(IRR), "the cash flows include at least one payment (negative) and one receipt (positive)");
        }

        if (start <= -1)
        {
            return InvalidArgument(nameof(IRR), "the guess is a rate greater than -1");
        }

        return Solve(CashFlowImbalance(flows), start) is { } irr
            ? Result(nameof(IRR), irr)
            : NoResult(nameof(IRR));
    }

    // the internal rate of return is the rate at which what the flows pay out is worth what they bring in -
    // measured at the first flow, where their value is zero exactly where NPV's is. It is measured one of two
    // ways, whichever is nearer a straight line in ln(1 + r) for the flows at hand:
    private static Func<double, double> CashFlowImbalance(double[] flows)
    {
        var first = Array.FindIndex(flows, flow => flow != 0);
        var alone = flows.Skip(first + 1).All(flow => flow == 0 || (flow > 0) != (flows[first] > 0));

        return alone
            // a single payment and nothing but receipts after it, or the other way round - the ordinary investment:
            // what the first is worth over what the rest are.
            ? y => Straightened(Math.Log(Math.Abs(flows[first])) - first * y - LogTotal(Discounted(flows, first + 1, y), positive: flows[first] < 0))
            // anything else: the logarithm of what is received over what is paid.
            : y => LogBalance(Discounted(flows, 0, y));
    }

    // what one amount is worth over what the amounts that balance it are, given as its logarithm: a ratio that is
    // 1 at the root and, in nearly every case it is used for, grows with the rate - the iteration finds the root
    // either way, only more slowly. The logarithm is nearly straight wherever one amount outweighs all the
    // others - the latest, well below the root; the earliest, well above it - but near the root of a long series
    // of like amounts, those are worth close to their total over the rate, and it is the ratio itself that is. So
    // from the root up to a ratio of e⁴ this is the ratio, less 1, and beyond that and below the root it is the
    // logarithm, joined on without a kink.
    private static double Straightened(double logRatio)
        => logRatio <= 0 ? logRatio
            : logRatio <= LargestLogRatio ? Math.Exp(logRatio) - 1
            : Math.Exp(LargestLogRatio) * (1 + logRatio - LargestLogRatio) - 1;

    // each flow from the given one on, with the logarithm of what discounting it to the first flow multiplies it by.
    private static (double Value, double LogFactor)[] Discounted(double[] flows, int from, double y)
        => [.. flows.Skip(from).Select((flow, index) => (flow, -(from + index) * y))];

    private static double ValueAtFirstFlow(double[] flows, double rate)
    {
        var total = 0d;
        var discount = 1d;
        foreach (var flow in flows)
        {
            total += flow / discount;
            discount *= 1 + rate;
        }

        return total;
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> MIRR(
        VBResizableArrayValue valueArray, VBDoubleValue finance_Rate, VBDoubleValue reinvest_Rate)
    {
        if (!TryReadCashFlows(nameof(MIRR), valueArray, out var flows, out var error))
        {
            return error;
        }

        // a rate of -1 discounts or grows every flow to nothing, and a single flow has no period to spread a
        // rate over.
        if (finance_Rate.Value == -1 || reinvest_Rate.Value == -1 || flows.Length < 2)
        {
            return InvalidArgument(nameof(MIRR), "both rates are other than -1 and there are at least two cash flows");
        }

        // the payments are financed: their value at the first flow, at the finance rate. The receipts are
        // reinvested: their value at the last flow, at the reinvestment rate. The modified rate is the one that
        // grows the first into the second over the periods between.
        var last = flows.Length - 1;
        var payments = 0d;
        var receipts = 0d;
        for (var period = 0; period <= last; period++)
        {
            if (flows[period] < 0)
            {
                payments += flows[period] / Math.Pow(1 + finance_Rate.Value, period);
            }
            else
            {
                receipts += flows[period] * Math.Pow(1 + reinvest_Rate.Value, last - period);
            }
        }

        // with no payment there is nothing to grow, and the division by it is a division by zero.
        if (payments == 0)
        {
            return RuntimeSemanticsEvaluationResult<VBDoubleValue>.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.DivisionByZero, default, "MIRR: the cash flows include no payment (negative) to finance."));
        }

        var growth = -receipts / payments;
        if (growth < 0)
        {
            return InvalidArgument(nameof(MIRR), "the reinvested receipts and the financed payments are of opposite signs");
        }

        return Result(nameof(MIRR), Math.Pow(growth, 1d / last) - 1);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> NPer(
        VBDoubleValue rate, VBDoubleValue pmt, VBDoubleValue pV, VBVariantValue? fV = default, VBVariantValue? due = default)
    {
        if (!TryReadOptional(fV, 0, out var future, out var error) || !TryReadDue(due, out var early, out error))
        {
            return error;
        }

        if (rate.Value == 0)
        {
            // the payments alone make up the difference, a payment at a time - unless there are none.
            return pmt.Value == 0
                ? InvalidArgument(nameof(NPer), "a payment other than 0 is made when the rate is 0")
                : Result(nameof(NPer), -(pV.Value + future) / pmt.Value);
        }

        if (rate.Value <= -1)
        {
            return InvalidArgument(nameof(NPer), "the rate is greater than -1");
        }

        // the annuity equation solved for n: (1+r)ⁿ is the growth below, which has to be positive for there to be
        // any number of periods at all - a payment that never covers the interest never pays anything off.
        var payments = pmt.Value * (1 + rate.Value * early) / rate.Value;
        var growth = (payments - future) / (payments + pV.Value);
        if (!(growth > 0) || double.IsInfinity(growth))
        {
            return InvalidArgument(nameof(NPer), "the payments can reach the future value from the present value at this rate");
        }

        // a growth near 1 is all 1 but for its last few digits, so its logarithm is taken of what it gains instead.
        var gain = -(future + pV.Value) / (payments + pV.Value);
        return Result(nameof(NPer), (Math.Abs(gain) < 0.5 ? Log1P(gain) : Math.Log(growth)) / Log1P(rate.Value));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> NPV(VBDoubleValue rate, VBResizableArrayValue valueArray)
    {
        if (!TryReadCashFlows(nameof(NPV), valueArray, out var flows, out var error))
        {
            return error;
        }

        if (rate.Value == -1 || flows.Length == 0)
        {
            return InvalidArgument(nameof(NPV), "the rate is other than -1 and there is at least one cash flow");
        }

        // "The NPV investment begins one period before the date of the first cash flow value": the first flow is
        // discounted by one period, and each later one by one more.
        return Result(nameof(NPV), ValueAtFirstFlow(flows, rate.Value) / (1 + rate.Value));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> Pmt(
        VBDoubleValue rate, VBDoubleValue nPer, VBDoubleValue pV, VBVariantValue? fV = default, VBVariantValue? due = default)
    {
        if (!TryReadOptional(fV, 0, out var future, out var error)
            || !TryReadDue(due, out var early, out error)
            || !TryPayment(nameof(Pmt), rate.Value, nPer.Value, pV.Value, future, early, out var payment, out error))
        {
            return error;
        }

        return Result(nameof(Pmt), payment);
    }

    private static bool TryPayment(
        string function, double rate, double periods, double present, double future, double early,
        out double payment, out RuntimeSemanticsEvaluationResult<VBDoubleValue> error)
    {
        payment = default;
        error = default;

        // there is no payment that spreads anything over no periods at all.
        if (periods == 0)
        {
            error = InvalidArgument(function, "the number of periods is other than 0");
            return false;
        }

        // and a payment that is not a number is not one to split into interest and principal either.
        payment = Payment(rate, periods, present, future, early);
        if (!double.IsFinite(payment))
        {
            error = Result(function, payment);
            return false;
        }

        return true;
    }

    private static double Payment(double rate, double periods, double present, double future, double early)
    {
        if (rate == 0)
        {
            return -(present + future) / periods;
        }

        if (Discount(rate, periods) is (var discount, var lost))
        {
            return -(future * discount + present) * rate / ((1 + rate * early) * lost);
        }

        var (growth, gained) = Grown(rate, periods);
        return -(future + present * growth) * rate / ((1 + rate * early) * gained);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> PV(
        VBDoubleValue rate, VBDoubleValue nPer, VBDoubleValue pmt, VBVariantValue? fV = default, VBVariantValue? due = default)
    {
        if (!TryReadOptional(fV, 0, out var future, out var error) || !TryReadDue(due, out var early, out error))
        {
            return error;
        }

        return Result(nameof(PV), PresentValue(rate.Value, nPer.Value, pmt.Value, future, early));
    }

    private static double PresentValue(double rate, double periods, double payment, double future, double early)
    {
        if (rate == 0)
        {
            return -(future + payment * periods);
        }

        if (Discount(rate, periods) is (var discount, var lost))
        {
            return -(future * discount + payment * (1 + rate * early) * lost / rate);
        }

        var (growth, gained) = Grown(rate, periods);
        return -(future + payment * (1 + rate * early) * gained / rate) / growth;
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> Rate(
        VBDoubleValue nPer, VBDoubleValue pmt, VBDoubleValue pV,
        VBVariantValue? fV = default, VBVariantValue? due = default, VBVariantValue? guess = default)
    {
        if (!TryReadOptional(fV, 0, out var future, out var error)
            || !TryReadDue(due, out var early, out error)
            || !TryReadOptional(guess, DefaultGuess, out var start, out error))
        {
            return error;
        }

        var (periods, payment, present) = (nPer.Value, pmt.Value, pV.Value);
        if (periods <= 0 || start <= -1)
        {
            return InvalidArgument(nameof(Rate), "the number of periods is greater than 0 and the guess is a rate greater than -1");
        }

        // with no cash going one way, there is no rate at which the annuity balances.
        double[] amounts = [present, payment, future];
        if (!amounts.Any(amount => amount > 0) || !amounts.Any(amount => amount < 0))
        {
            return InvalidArgument(nameof(Rate), "the present value, the payments and the future value include both cash paid out and cash received");
        }

        var (seeking, closing, seekLower) = AnnuityImbalance(periods, payment, present, future, early, start);
        return Solve(seeking, closing, start, seekLower) is { } result
            ? Result(nameof(Rate), result)
            : NoResult(nameof(Rate));
    }

    // the rate is the one at which the annuity equation holds, which it cannot be solved for the way it can for
    // everything else. Every form of the equation has that root, but each is nearly a straight line in ln(1 + r)
    // in a different case, and the iteration is only as quick as its function is straight. So it is measured one
    // of three ways - by one function until a bracket is found and by another to close it, in the third - chosen
    // by the case and, in the third, by where the guess lies - which there also decides whether a lower root is
    // looked for below the one found first:
    private static (Func<double, double> Seeking, Func<double, double> Closing, bool SeekLower) AnnuityImbalance(
        double periods, double payment, double present, double future, double early, double guess)
    {
        (double Value, double LogFactor)[] Now(double y)
            => [(present, 0), (payment, LogAnnuityFactor(y, periods, early)), (future, -periods * y)];

        if (present == 0 || payment == 0 || (present > 0) == (payment > 0))
        {
            // payments saved towards a future value, or compounding alone: the logarithm of what is received over
            // what is paid, which a lump at the end makes close to a straight line.
            Func<double, double> balance = y => LogBalance(Now(y));
            return (balance, balance, false);
        }

        // a present value that the payments balance - a loan, balloon or not, with a future value of the payments'
        // sign or a smaller one of its own: what the present value, and a future value of its sign, are worth over
        // what the payments, and a future value of theirs, are. That is how IRR measures an investment and its
        // returns.
        Func<double, double> ratio = y => Straightened(LogTotal(Now(y), positive: present > 0) - LogTotal(Now(y), positive: present < 0));
        if ((future > 0) != (present > 0) || Math.Abs(future) <= Math.Abs(present))
        {
            return (ratio, ratio, false);
        }

        // unless it is payments towards a larger future value, with a present value of the same sign - a savings
        // plan with a bonus to start it. The equation can have a second root then. No root lies above the rate at
        // which the payments are only the interest on the present value, where there is such a rate: the imbalance
        // in future value is pv + fv there, and above it only grows. From a guess between the two roots, a ratio
        // of what is received to what is paid mostly leads to the second; but the imbalance in future value, which
        // grows with (1+r)ⁿ, is lowest near the second root, and so falls towards the rate from most guesses
        // between them: that is what is measured, compressed, to find a bracket. Where it is falling towards 0 at
        // the guess - below the rate, where it is all but flat, or just below the second root - the ratio is
        // followed instead. Either way the bracket is closed on the logarithm of what is received over what is
        // paid, if that changes sign across it too: it is nearly straight around both roots, where the compressed
        // imbalance is all but flat below the rate and all but a step at the second root. From a guess at or above
        // the interest-only rate, the root found first is usually the second: a lower one is looked for below it
        // with the tries left, and is the result if it is found. That the line between a loan and a savings plan
        // falls where the two values are equal was found by trial.
        var scale = Math.Max(Math.Abs(present), Math.Max(Math.Abs(payment), Math.Abs(future)));
        Func<double, double> compressed = y => CompressedImbalance(
            [(present, periods * y), (payment, LogAnnuityFactor(y, periods, early) + periods * y), (future, 0)], scale);
        var start = Log1P(guess);
        var (atStart, atSecond) = (compressed(start), compressed(start + SecondPoint));
        var falling = (atStart > 0) == (atSecond > 0) && Math.Abs(atSecond) < Math.Abs(atStart);
        var interestOnly = Math.Abs(present) > early * Math.Abs(payment)
            ? Math.Abs(payment) / (Math.Abs(present) - early * Math.Abs(payment))
            : double.PositiveInfinity;
        return (falling ? ratio : compressed, y => LogBalance(Now(y)), guess >= interestOnly);
    }

    // an imbalance, the terms of which are given as values and the logarithms of their factors, as its sign times
    // ln(1 + |imbalance| / scale): the same zero, and the same slope either side of it, but growing only in
    // proportion to ln(1 + r) however far from the root - where an imbalance in future value grows with (1+r)ⁿ.
    private static double CompressedImbalance((double Value, double LogFactor)[] terms, double scale)
    {
        var (received, paid) = (LogTotal(terms, positive: true), LogTotal(terms, positive: false));
        if (received == paid)
        {
            return 0;
        }

        var logMagnitude = Math.Max(received, paid) + Math.Log(-ExpM1(-Math.Abs(received - paid))) - Math.Log(scale);
        var compressed = logMagnitude > 35 ? logMagnitude : Log1P(Math.Exp(logMagnitude));
        return received > paid ? compressed : -compressed;
    }

    // the logarithm of what a payment of 1 a period for the periods is worth now:
    // (1 + r·d)·(1 - (1+r)⁻ⁿ)/r, with each factor taken in ln(1 + r) so that neither overflows nor cancels.
    private static double LogAnnuityFactor(double y, double periods, double early)
        => early * y + (y == 0 ? Math.Log(periods) : LogMagnitudeOfExpM1(-periods * y) - LogMagnitudeOfExpM1(y));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> SLN(VBDoubleValue cost, VBDoubleValue salvage, VBDoubleValue life)
        => life.Value == 0
            ? InvalidArgument(nameof(SLN), "the life is other than 0")
            // the depreciable amount, spread evenly over the life.
            : Result(nameof(SLN), (cost.Value - salvage.Value) / life.Value);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> SYD(
        VBDoubleValue cost, VBDoubleValue salvage, VBDoubleValue life, VBDoubleValue period)
    {
        if (salvage.Value < 0 || period.Value <= 0 || period.Value > life.Value)
        {
            return InvalidArgument(nameof(SYD),
                "the salvage is not negative, and the period is greater than 0 and no greater than the life");
        }

        // each period takes the years still remaining, over the sum of the digits of every year of the life:
        // the first of five takes 5/15 of the depreciable amount, the last 1/15.
        var remaining = life.Value - period.Value + 1;
        var sumOfDigits = life.Value * (life.Value + 1) / 2;
        return Result(nameof(SYD), (cost.Value - salvage.Value) * remaining / sumOfDigits);
    }

    // "Starting with the value of guess, [it] cycles through the calculation until the result is accurate to
    // within 0.00001 percent. If [it] can't find a result after 20 tries, it fails."
    //
    // The iteration runs on y = ln(1 + r) rather than on the rate: every real y is a rate greater than -1, so no
    // step can land on a rate that discounts everything to nothing or worse. Each try is a step of the secant
    // method - the line through the last two points, followed to where it crosses zero - until two points straddle
    // a root. From then on each try is a step of regula falsi, which keeps the root between the last two points,
    // with the Anderson-Björck correction that stops one end of that bracket going stale; and the result is the
    // point the bracket has closed on once it is narrower than 0.00001 percent. A step that is merely short proves
    // nothing on its own: beside a point where the function is steep, a step is short anywhere. The bracket's
    // width, and the distance either side of a point within which a root is looked for, are measured as rates, not
    // in y, where a vast distance near -1 is hardly any rate at all. From a rate of 1E+07 on, two Doubles next to
    // each other in y are 2⁻⁴⁸ of 1 + r apart as rates; past about 5.6E+07 that is wider than the 0.00001 percent
    // either side of a point, which then cannot be told apart from the point itself, so a root there is found only
    // by chance.
    //
    // An imbalance of exactly 0, at the guess or after it, is taken as a root - one the function may only touch,
    // without crossing - unless it is 0 either side of it too, as it is where an annuity of one period balances at
    // every rate. Rounding can make an imbalance 0 where there is no root, as it can make one change sign: where
    // an equation balances only to within rounding - an annuity of one period a unit or two in the last place from
    // balancing, or cash flows with a root of three or more times - a result can come back with no root within
    // 0.00001 percent of it.
    //
    // Before a bracket, a step is cut short at LongestStep, a length that doubles each time in a row it cuts one,
    // and a step that lands where the imbalance has no finite value is taken back halfway for another try. Once
    // there is a bracket, it is closed on the closing function, if that changes sign across it too. Asked to seek
    // a lower root as well - for a savings plan, from a guess at or above its interest-only rate - it looks below
    // the root it finds first, with the tries left: from the lower end of the bracket that root was found in, or
    // from just below a guess that is itself a root, heading down - a secant step that would go up doubling the
    // last step down instead - until it brackets or lands on a root. The lower root is the result if it is found
    // in time, and the first one if not. null is a failure to find a result - and so is a rate within 0.00001
    // percent of -1, which is not one anything can be discounted at.
    private static double? Solve(Func<double, double> imbalance, double guess) => Solve(imbalance, imbalance, guess, seekLower: false);

    private static double? Solve(Func<double, double> seeking, Func<double, double> closing, double guess, bool seekLower)
    {
        var tries = MaximumTries;
        var start = Log1P(guess);
        var result = Iterate(seeking, closing, start, start + SecondPoint, ref tries, seekLower, descending: false, out var below);
        return result is not null && below is { } lower
            ? Iterate(seeking, closing, lower, lower - SecondPoint, ref tries, seekLower: false, descending: true, out _) ?? result
            : result;
    }

    // one run of the iteration from two points, taking tries from those left. below is where to look for a lower
    // root, when asked to seek one, and null otherwise.
    private static double? Iterate(
        Func<double, double> seeking, Func<double, double> closing, double first, double second,
        ref int tries, bool seekLower, bool descending, out double? below)
    {
        below = null;
        var imbalance = seeking;
        var (previous, atPrevious) = (first, imbalance(first));
        if (atPrevious == 0)
        {
            below = seekLower ? first - SecondPoint : null;
            return Beside(imbalance, ExpM1(first)) is not null ? Found(first) : null;
        }

        var (current, atCurrent) = (second, imbalance(second));
        (double Y, double At)? far = null;
        var longest = LongestStep;

        while (tries > 0)
        {
            tries--;
            if (atCurrent == 0)
            {
                return Beside(imbalance, ExpM1(current)) is not null ? Found(current) : null;
            }

            if (far is null && !double.IsFinite(atCurrent) && double.IsFinite(atPrevious))
            {
                current = (current + previous) / 2;
                atCurrent = imbalance(current);
                continue;
            }

            if (!double.IsFinite(atCurrent) || !double.IsFinite(atPrevious))
            {
                return null;
            }

            if (far is null && (atPrevious < 0) != (atCurrent < 0))
            {
                below = seekLower ? Math.Min(previous, current) : null;
                var (atPreviousClosing, atCurrentClosing) = (closing(previous), closing(current));
                if (double.IsFinite(atPreviousClosing) && double.IsFinite(atCurrentClosing)
                    && (atPreviousClosing < 0) != (atCurrentClosing < 0))
                {
                    (imbalance, atPrevious, atCurrent) = (closing, atPreviousClosing, atCurrentClosing);
                }

                far = (previous, atPrevious);
            }

            if (far is { } end)
            {
                var within = current - atCurrent * (current - end.Y) / (atCurrent - end.At);
                var atWithin = imbalance(within);
                if (atWithin == 0)
                {
                    return Beside(imbalance, ExpM1(within)) is not null ? Found(within) : null;
                }

                if ((atWithin < 0) != (atCurrent < 0))
                {
                    far = (current, atCurrent);
                }
                else
                {
                    // the same end stays for another try, so its value is scaled down by how much the last try
                    // gained - which moves the next point towards it rather than creeping up from this side.
                    var gain = 1 - atWithin / atCurrent;
                    far = (end.Y, end.At * (gain > 0 ? gain : 0.5));
                }

                (current, atCurrent) = (within, atWithin);

                if (Math.Abs(ExpM1(current) - ExpM1(far.Value.Y)) < Accuracy)
                {
                    return Found(current);
                }

                continue;
            }

            if (atCurrent == atPrevious)
            {
                return null;
            }

            var next = current - atCurrent * (current - previous) / (atCurrent - atPrevious);
            if (descending && !(next < current))
            {
                next = current - 2 * Math.Abs(current - previous);
            }

            if (Math.Abs(next - current) > longest)
            {
                next = current + Math.CopySign(longest, next - current);
                longest *= 2;
            }
            else
            {
                longest = LongestStep;
            }

            if (Math.Abs(ExpM1(next) - ExpM1(current)) < Accuracy && Straddles(imbalance, ExpM1(next)))
            {
                return Found(next);
            }

            (previous, atPrevious) = (current, atCurrent);
            (current, atCurrent) = (next, imbalance(next));
        }

        return null;
    }

    private static double? Found(double y) => ExpM1(y) is var rate && rate > -1 + Accuracy ? rate : null;

    // whether a root is within 0.00001 percent of the rate either side.
    private static bool Straddles(Func<double, double> imbalance, double rate)
        => Beside(imbalance, rate) is (var below, var above) && (below == 0 || above == 0 || (below < 0) != (above < 0));

    // the imbalance 0.00001 percent either side of the rate - or null where either has no finite value, or both are
    // 0, an imbalance that rounds to 0 all around the rate rather than a root.
    private static (double Below, double Above)? Beside(Func<double, double> imbalance, double rate)
    {
        if (!(rate - Accuracy > -1))
        {
            return null;
        }

        var (below, above) = (imbalance(Log1P(rate - Accuracy)), imbalance(Log1P(rate + Accuracy)));
        return double.IsFinite(below) && double.IsFinite(above) && (below != 0 || above != 0) ? (below, above) : null;
    }

    // the logarithm of what the positive terms total over what the negative ones do. Each term is a value and the
    // logarithm of the factor it is multiplied by; NaN when there is no term of one sign or the other.
    private static double LogBalance((double Value, double LogFactor)[] terms)
        => LogTotal(terms, positive: true) - LogTotal(terms, positive: false);

    // the logarithm of the total of the terms of one sign - summed with the largest factored out, so that no term
    // overflows a Double however far the rate is from the answer.
    private static double LogTotal((double Value, double LogFactor)[] terms, bool positive)
    {
        var logs = terms
            .Where(term => term.Value != 0 && (term.Value > 0) == positive)
            .Select(term => Math.Log(Math.Abs(term.Value)) + term.LogFactor)
            .ToArray();
        if (logs.Length == 0)
        {
            return double.NaN;
        }

        var largest = logs.Max();
        return largest + Math.Log(logs.Sum(log => Math.Exp(log - largest)));
    }

    // e^x - 1, ln(1 + x), and ln|e^x - 1|, without the cancellation computing them the obvious way suffers when
    // x is small - which is exactly where a rate near zero puts them. The corrections are Kahan's and Goldberg's.
    private static double ExpM1(double x)
    {
        var exp = Math.Exp(x);
        return exp == 1 ? x
            // an e^x so large the 1 is lost in the rounding is the answer - where the correction could overflow.
            : exp - 1 == exp ? exp
            : exp - 1 == -1 ? -1
            : (exp - 1) * x / Math.Log(exp);
    }

    private static double Log1P(double x)
    {
        var sum = 1 + x;
        return sum == 1 ? x : Math.Log(sum) * x / (sum - 1);
    }

    // past e^700 the 1 is lost in the rounding anyway, and e^x itself would soon overflow.
    private static double LogMagnitudeOfExpM1(double x) => x > 700 ? x : Math.Log(Math.Abs(ExpM1(x)));

    // an omitted Optional arrives as null - the dispatcher passes null for an argument the call left out, and for
    // a Missing one, such as an omitted Optional Variant passed on - and stands for the value the specification
    // says an omitted one is. An explicit Empty is not an omitted argument but a value: it is 0, as it would be
    // Let-coerced to a Double parameter. Anything else is coerced to a Double the way a Let-coercion would: a String
    // is read in the session's regional settings, as a required Double parameter's argument is (MS-VBA, Excel-verified,
    // oracle\s40 F01-F16).
    private bool TryReadOptional(
        VBVariantValue? argument, double whenOmitted,
        out double number, out RuntimeSemanticsEvaluationResult<VBDoubleValue> error)
    {
        number = whenOmitted;
        error = default;

        switch (Unwrapped(argument))
        {
            case null or VBMissingValue:
                return true;
            case VBEmptyValue:
                // MS-VBAL 5.5.1.2.11: Empty Let-coerced to a numeric type is 0 - so an explicit Empty Guess is a
                // guess of 0, and an explicit Empty Factor is one DDB cannot depreciate by.
                number = 0;
                return true;
            case VBNumericTypedValue numeric:
                number = numeric.AsDouble;
                return true;
            case VBBooleanValue boolean:
                number = boolean.Value.StoredValue;
                return true;
            case VBDateValue date:
                number = date.SerialValue;
                return true;
            case VBNullValue:
                // MS-VBAL 5.5.1.2.10: Let-coercing Null to anything but a Variant "Runtime error 94 (Invalid use
                // of Null) is raised."
                error = RuntimeSemanticsEvaluationResult<VBDoubleValue>.Error(VBRuntimeErrorInfo.For(
                    VBRuntimeErrorId.InvalidUseOfNull, default, "an optional argument of a financial function is Null."));
                return false;
            case VBStringValue text:
                var read = ValueConversions.ToNumeric(text, VBDoubleType.TypeInfo, session.Environment.Regional);
                if (read is { IsSuccess: true, Value: VBNumericTypedValue parsed })
                {
                    number = parsed.AsDouble;
                    return true;
                }

                error = RuntimeSemanticsEvaluationResult<VBDoubleValue>.Error(VBRuntimeErrorInfo.For(
                    read.Error ?? VBRuntimeErrorId.TypeMismatch, default, $"an optional argument of a financial function, \"{text.Value}\", is not a number."));
                return false;
            default:
                error = RuntimeSemanticsEvaluationResult<VBDoubleValue>.Error(VBRuntimeErrorInfo.For(
                    VBRuntimeErrorId.TypeMismatch, default, "an optional argument of a financial function is not a number."));
                return false;
        }
    }

    // "Use the data value 0 if payments are due at the end of the payment period, or use the data value 1 if
    // payments are due at the beginning of the period": the number of periods early a payment is made. The
    // specification describes no other value, and any value other than 0 is True - so it means the beginning.
    private bool TryReadDue(
        VBVariantValue? due, out double early, out RuntimeSemanticsEvaluationResult<VBDoubleValue> error)
    {
        var read = TryReadOptional(due, 0, out var value, out error);
        early = value == 0 ? 0 : 1;
        return read;
    }

    // "The IRR function uses the order of values within the array to interpret the order of payments and
    // receipts": the order MS-VBAL 5.4.2.4.1 enumerates an array in, which for the one-dimensional array meant
    // here is index order, from whatever its lower bound is.
    private static bool TryReadCashFlows(
        string function, VBResizableArrayValue values,
        out double[] flows, out RuntimeSemanticsEvaluationResult<VBDoubleValue> error)
    {
        flows = new double[values.Length];
        error = default;

        for (var index = 0; index < flows.Length; index++)
        {
            if (Unwrapped(values.ElementAt(index)) is not VBNumericTypedValue flow)
            {
                error = RuntimeSemanticsEvaluationResult<VBDoubleValue>.Error(VBRuntimeErrorInfo.For(
                    VBRuntimeErrorId.TypeMismatch, default, $"{function}: a cash flow is not a number."));
                return false;
            }

            flows[index] = flow.AsDouble;
        }

        return true;
    }

    private static VBTypedValue? Unwrapped(VBTypedValue? value)
    {
        while (value is VBVariantValue { TypedValue: { } wrapped })
        {
            value = wrapped;
        }

        return value;
    }

    // a Double calculation that overflows raises error 6 rather than producing an infinity, and one that has no
    // numeric result at all is a call with arguments it cannot be computed from. A negative zero is just zero.
    private static RuntimeSemanticsEvaluationResult<VBDoubleValue> Result(string function, double value)
        => double.IsFinite(value)
            ? RuntimeSemanticsEvaluationResult<VBDoubleValue>.Success(new VBDoubleValue(value == 0 ? 0 : value))
            : double.IsNaN(value)
                ? InvalidArgument(function, "the arguments are ones a result can be computed from")
                : RuntimeSemanticsEvaluationResult<VBDoubleValue>.Error(VBRuntimeErrorInfo.For(
                    VBRuntimeErrorId.Overflow, default, $"{function}: the result is too large for a Double."));

    private static RuntimeSemanticsEvaluationResult<VBDoubleValue> InvalidArgument(string function, string rule)
        => RuntimeSemanticsEvaluationResult<VBDoubleValue>.Error(VBRuntimeErrorInfo.For(
            VBRuntimeErrorId.InvalidProcedureCallOrArgument, default, $"{function}: {rule}."));

    private static RuntimeSemanticsEvaluationResult<VBDoubleValue> NoResult(string function)
        => InvalidArgument(function, $"the iteration finds a result within {MaximumTries} tries");
}
