using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Numerics;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdFinancialModule"/>
/// <remarks>
/// Every member is a function of its arguments alone: the session is what a <c>String</c> given for an optional argument is read by, the regional settings
/// of its environment, as a Let-coercion to <c>Double</c> reads it.
/// <para>
/// The specification states what each function computes, not how - and it is the how that decides the last digits of a result, and which arguments have
/// one at all. So each is computed the way MS-VBA computes it, the VB runtime's algorithm - which <c>Microsoft.VisualBasic.Financial</c> ports to .NET -
/// evaluated as MS-VBA's x87 code evaluates it: each statement's expression in the 80-bit extended format (<see cref="Float80"/>), its value stored in a
/// <c>Double</c>, and a power by <see cref="Math.Pow"/>. The few places MS-VBA does otherwise - the order of <see cref="FV"/>'s terms, the totals of
/// <see cref="NPV"/> and the quotient of <see cref="SYD"/> kept in 80 bits - are its own. What a function returns is MS-VBA's to the last bit (recorded in
/// Excel, grid <c>oracle\s43</c>: 30 random calls of each function and the cases of F01-F09).
/// </para>
/// <para>
/// 👉 The specification names no error for any of these, so they are MS-VBA's, tabulated in RD-VBAL §6.1.2.6: an argument a function cannot compute with
/// is error 5, and so is an iteration that finds no result; a result that overflows is error 6 rather than an infinity, the error MS-VBA raises for a
/// <c>Double</c> calculation that leaves the range; and <see cref="MIRR"/> with no payment to finance is error 11, the division by zero it would take.
/// </para>
/// </remarks>
/// <param name="session">The session whose regional settings a <c>String</c> argument is read by.</param>
public sealed class StdFinancial(IRuntimeSession session) : IStdFinancialModule
{
    // MS-VBAL 6.1.2.6.1.1: "If omitted, the data value 2 (double-declining method) is assumed."
    private const double DefaultFactor = 2;

    // MS-VBAL 6.1.2.6.1.4 and 6.1.2.6.1.11: "If omitted, Guess is the data value 0.1 (10 percent)."
    private const double DefaultGuess = 0.1;

    // the iterations of Rate and IRR: at most 40 steps of the secant method, which have found the result once it is within 1E-07 - of the imbalance, for
    // Rate; of the rate and of 1E-09 of the largest cash flow, for IRR - and, when two points have the same imbalance, a step of 1E-05 aside.
    private const int MaximumSteps = 40;
    private const double Epsilon = 0.0000001;
    private const double Step = 0.00001;

    // a value as MS-VBA's x87 code holds it while it computes: 80 bits, rounded to a Double where it is stored (D).
    private readonly record struct X(Float80 Value)
    {
        public static implicit operator X(double value) => new(Float80.FromDouble(value));
        public static X operator +(X a, X b) => new(Float80.Add(a.Value, b.Value, out _));
        public static X operator -(X a, X b) => new(Float80.Subtract(a.Value, b.Value, out _));
        public static X operator *(X a, X b) => new(Float80.Multiply(a.Value, b.Value, out _));
        public static X operator /(X a, X b) => new(Float80.Divide(a.Value, b.Value, out _));
        public static X operator -(X a) => new(a.Value.Negate());
        public double D => Value.ToDouble(out _);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> DDB(
        VBDoubleValue cost, VBDoubleValue salvage, VBDoubleValue life, VBDoubleValue period, VBVariantValue? factor = default)
    {
        if (!TryReadOptional(factor, DefaultFactor, out var rateFactor, out var error))
        {
            return error;
        }

        if (rateFactor <= 0 || salvage.Value < 0 || period.Value <= 0 || period.Value > life.Value)
        {
            return InvalidArgument(nameof(DDB),
                "the salvage is not negative, the period is greater than 0 and no greater than the life, and the factor is greater than 0");
        }

        return Result(nameof(DDB), Depreciation(cost.Value, salvage.Value, life.Value, period.Value, rateFactor));
    }

    // a declining balance loses Factor / Life of what is left of it each period, and stops at the salvage value: before the period, what is left is
    // what Cost declines to over the periods already gone - which also answers a fractional period.
    private static double Depreciation(double cost, double salvage, double life, double period, double factor)
    {
        if (cost <= 0)
        {
            return 0;
        }

        if (life < 2 || (life == 2 && period <= 1))
        {
            return ((X)cost - salvage).D;
        }

        if (life == 2)
        {
            return 0;
        }

        if (period <= 1)
        {
            var first = ((X)cost * factor / life).D;
            var depreciable = ((X)cost - salvage).D;
            return first > depreciable ? depreciable : first;
        }

        var left = (((X)life - factor) / life).D;
        var gone = ((X)period - 1).D;
        var depreciation = ((X)factor * cost / life * Math.Pow(left, gone)).D;
        var total = ((X)cost * ((X)1 - Math.Pow(left, period))).D;
        var excess = ((X)total - cost + salvage).D;
        if (excess > 0)
        {
            depreciation = ((X)depreciation - excess).D;
        }

        return depreciation >= 0 ? depreciation : 0;
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

    // the annuity equation solved for fv, with a rate r per period over n periods and payments made at the start of a period (d = 1) or its end (0):
    // pv·(1+r)ⁿ + pmt·(1+r·d)·((1+r)ⁿ−1)/r + fv = 0, or pv + pmt·n + fv = 0 at a rate of 0.
    private static double FutureValue(double rate, double periods, double payment, double present, double early)
    {
        if (rate == 0)
        {
            return (-(X)present - (X)payment * periods).D;
        }

        var due = early != 0 ? ((X)1 + rate).D : 1;
        var growth = Math.Pow(((X)1 + rate).D, periods);
        return (-(X)present * growth - (X)payment * due * ((X)growth - 1) / rate).D;
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> IPmt(
        VBDoubleValue rate, VBDoubleValue per, VBDoubleValue nPer, VBDoubleValue pV,
        VBVariantValue? fV = default, VBVariantValue? due = default)
    {
        if (!TryReadOptional(fV, 0, out var future, out var error) || !TryReadDue(due, out var early, out error))
        {
            return error;
        }

        return Interest(nameof(IPmt), rate.Value, per.Value, nPer.Value, pV.Value, future, early, out var interest, out error)
            ? Result(nameof(IPmt), interest)
            : error;
    }

    // what the period's payment pays of interest: the rate times the balance outstanding at the start of the period - the future value of the periods
    // before it, of the payment the annuity makes. A payment due at the start of the first period is made before anything has earned interest; one due
    // at the start of a later period is counted as paid at the end of the one before, from a present value the first payment has reduced.
    private static bool Interest(
        string function, double rate, double per, double periods, double present, double future, double early,
        out double interest, out RuntimeSemanticsEvaluationResult<VBDoubleValue> error)
    {
        interest = 0;
        if (!TryPeriodPayment(function, rate, per, periods, present, future, early, out var payment, out error))
        {
            return false;
        }

        if (early != 0 && per == 1)
        {
            return true;
        }

        if (early != 0)
        {
            present = ((X)present + payment).D;
        }

        var balance = FutureValue(rate, ((X)per - (early != 0 ? 2 : 1)).D, payment, present, early: 0);
        interest = ((X)balance * rate).D;
        return true;
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> PPmt(
        VBDoubleValue rate, VBDoubleValue per, VBDoubleValue nPer, VBDoubleValue pV,
        VBVariantValue? fV = default, VBVariantValue? due = default)
    {
        if (!TryReadOptional(fV, 0, out var future, out var error)
            || !TryReadDue(due, out var early, out error)
            || !TryPeriodPayment(nameof(PPmt), rate.Value, per.Value, nPer.Value, pV.Value, future, early, out var payment, out error)
            || !Interest(nameof(PPmt), rate.Value, per.Value, nPer.Value, pV.Value, future, early, out var interest, out error))
        {
            return error;
        }

        // what is not interest is principal.
        return Result(nameof(PPmt), ((X)payment - interest).D);
    }

    // the payment made in a period, which IPmt and PPmt split into interest and principal.
    private static bool TryPeriodPayment(
        string function, double rate, double per, double periods, double present, double future, double early,
        out double payment, out RuntimeSemanticsEvaluationResult<VBDoubleValue> error)
    {
        // "payment period in the range 1 through NPer": a period that is part of the annuity at all, which a fraction of one either side of that range
        // still is.
        if (per <= 0 || per >= periods + 1)
        {
            payment = default;
            error = InvalidArgument(function, $"the period is greater than 0 and less than one more than the number of periods ({periods})");
            return false;
        }

        return TryPayment(function, rate, periods, present, future, early, out payment, out error);
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

        if (start <= -1)
        {
            return InvalidArgument(nameof(IRR), "the guess is a rate greater than -1");
        }

        if (flows.Length <= 1)
        {
            return InvalidArgument(nameof(IRR), "there are at least two cash flows");
        }

        return InternalRate(flows, start) is { } irr ? Result(nameof(IRR), irr) : NoResult(nameof(IRR));
    }

    // the rate at which the cash flows' net present value is 0, by the secant method from the guess and a point 1E-05 from it, toward where the value
    // falls: found once the rate moved less than 1E-07 and the value is within 1E-09 of the largest flow. A step to -1 or below goes halfway from the
    // last rate to -1 instead. null when 40 steps do not find it, or when two points have the same value, a step aside too.
    private static double? InternalRate(double[] flows, double guess)
    {
        var largest = flows.Max(Math.Abs);
        var accuracy = ((X)largest * Epsilon * 0.01).D;

        var (rate0, rate1) = (guess, 0d);
        var npv0 = NetPresentValue(flows, rate0);
        rate1 = npv0 > 0 ? ((X)rate0 + Step).D : ((X)rate0 - Step).D;
        if (rate1 <= -1)
        {
            return null;
        }

        var npv1 = NetPresentValue(flows, rate1);
        for (var step = 0; step < MaximumSteps; step++)
        {
            if (npv1 == npv0)
            {
                rate0 = rate1 > rate0 ? ((X)rate0 - Step).D : ((X)rate0 + Step).D;
                npv0 = NetPresentValue(flows, rate0);
                if (npv1 == npv0)
                {
                    return null;
                }
            }

            rate0 = ((X)rate1 - ((X)rate1 - rate0) * npv1 / ((X)npv1 - npv0)).D;
            if (rate0 <= -1)
            {
                rate0 = (((X)rate1 - 1) * 0.5).D;
            }

            npv0 = NetPresentValue(flows, rate0);
            var moved = rate0 > rate1 ? ((X)rate0 - rate1).D : ((X)rate1 - rate0).D;
            if (Math.Abs(npv0) < accuracy && moved < Epsilon)
            {
                return rate0;
            }

            (npv0, npv1) = (npv1, npv0);
            (rate0, rate1) = (rate1, rate0);
        }

        return null;
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> MIRR(
        VBResizableArrayValue valueArray, VBDoubleValue finance_Rate, VBDoubleValue reinvest_Rate)
    {
        if (!TryReadCashFlows(nameof(MIRR), valueArray, out var flows, out var error))
        {
            return error;
        }

        // a rate of -1 discounts or grows every flow to nothing, and a single flow has no period to spread a rate over.
        if (finance_Rate.Value == -1 || reinvest_Rate.Value == -1 || flows.Length <= 1)
        {
            return InvalidArgument(nameof(MIRR), "both rates are other than -1 and there are at least two cash flows");
        }

        // the payments are financed: their net present value at the finance rate. The receipts are reinvested: theirs at the reinvestment rate, grown to
        // the last flow. The modified rate is the one that grows the first into the second over the periods between.
        var payments = NetPresentValue(flows, finance_Rate.Value, Flows.Payments);
        if (payments == 0)
        {
            // with no payment there is nothing to grow, and the division by it is a division by zero.
            return RuntimeSemanticsEvaluationResult<VBDoubleValue>.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.DivisionByZero, default, "MIRR: the cash flows include no payment (negative) to finance."));
        }

        var receipts = NetPresentValue(flows, reinvest_Rate.Value, Flows.Receipts);
        var grown = ((X)reinvest_Rate.Value + 1).D;
        var growth = (-(X)receipts * Math.Pow(grown, flows.Length) / ((X)payments * ((X)finance_Rate.Value + 1))).D;
        if (growth < 0)
        {
            return InvalidArgument(nameof(MIRR), "the reinvested receipts and the financed payments are of opposite signs");
        }

        var root = ((X)1 / ((X)flows.Length - 1)).D;
        return Result(nameof(MIRR), ((X)Math.Pow(growth, root) - 1).D);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> NPer(
        VBDoubleValue rate, VBDoubleValue pmt, VBDoubleValue pV, VBVariantValue? fV = default, VBVariantValue? due = default)
    {
        if (!TryReadOptional(fV, 0, out var future, out var error) || !TryReadDue(due, out var early, out error))
        {
            return error;
        }

        var (r, payment, present) = (rate.Value, pmt.Value, pV.Value);
        if (r <= -1)
        {
            return InvalidArgument(nameof(NPer), "the rate is greater than -1");
        }

        if (r == 0)
        {
            // the payments alone make up the difference, a payment at a time - unless there are none.
            return payment == 0
                ? InvalidArgument(nameof(NPer), "a payment other than 0 is made when the rate is 0")
                : Result(nameof(NPer), (-((X)present + future) / payment).D);
        }

        // the annuity equation solved for n: the logarithm of what the payments and the future value are worth over what they and the present value
        // are, in periods - which has to be the logarithm of a positive number: a payment that never covers the interest never pays anything off.
        var payments = early != 0 ? ((X)payment * ((X)1 + r) / r).D : ((X)payment / r).D;
        var atEnd = (-(X)future + payments).D;
        var atStart = ((X)present + payments).D;
        if (atEnd < 0 && atStart < 0)
        {
            (atEnd, atStart) = ((-(X)1 * atEnd).D, (-(X)1 * atStart).D);
        }
        else if (atEnd <= 0 || atStart <= 0)
        {
            return InvalidArgument(nameof(NPer), "the payments can reach the future value from the present value at this rate");
        }

        var growth = ((X)r + 1).D;
        return Result(nameof(NPer), (((X)Math.Log(atEnd) - Math.Log(atStart)) / Math.Log(growth)).D);
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

        return Result(nameof(NPV), NetPresentValue(flows, rate.Value));
    }

    // which cash flows a net present value counts: MIRR finances the payments and reinvests the receipts.
    private enum Flows
    {
        All,
        Payments,
        Receipts,
    }

    // "The NPV investment begins one period before the date of the first cash flow value": the first flow is discounted by one period, and each later
    // one by one more. The discount and the total are kept in 80 bits from one flow to the next, as MS-VBA keeps them.
    private static double NetPresentValue(double[] flows, double rate, Flows counted = Flows.All)
    {
        X discount = 1;
        X total = 0;
        foreach (var flow in flows)
        {
            discount = discount + discount * rate;
            if (!(counted is Flows.Payments && flow > 0) && !(counted is Flows.Receipts && flow < 0))
            {
                total = total + (X)flow / discount;
            }
        }

        return total.D;
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
            return ((-(X)future - present) / periods).D;
        }

        var due = early != 0 ? ((X)1 + rate).D : 1;
        var growth = Math.Pow(((X)rate + 1).D, periods);
        return ((-(X)future - (X)present * growth) / ((X)due * ((X)growth - 1)) * rate).D;
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
            return (-(X)future - (X)payment * periods).D;
        }

        var due = early != 0 ? ((X)1 + rate).D : 1;
        var growth = Math.Pow(((X)1 + rate).D, periods);
        return (-((X)future + (X)payment * due * (((X)growth - 1) / rate)) / growth).D;
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

        if (nPer.Value <= 0)
        {
            return InvalidArgument(nameof(Rate), "the number of periods is greater than 0");
        }

        return AnnuityRate(nPer.Value, pmt.Value, pV.Value, future, early, start) is { } rate ? Result(nameof(Rate), rate) : NoResult(nameof(Rate));
    }

    // the rate at which the annuity equation balances, by the secant method from the guess and half or twice it - half when the imbalance there is
    // positive: found once the imbalance is within 1E-07 of 0. null when 40 steps do not find it, or when two points have the same imbalance, a step
    // aside too.
    private static double? AnnuityRate(double periods, double payment, double present, double future, double early, double guess)
    {
        var rate0 = guess;
        var y0 = Imbalance(rate0, periods, payment, present, future, early);
        var rate1 = y0 > 0 ? ((X)rate0 / 2).D : ((X)rate0 * 2).D;
        var y1 = Imbalance(rate1, periods, payment, present, future, early);
        for (var step = 0; step < MaximumSteps; step++)
        {
            if (y1 == y0)
            {
                rate0 = rate1 > rate0 ? ((X)rate0 - Step).D : ((X)rate0 + Step).D;
                y0 = Imbalance(rate0, periods, payment, present, future, early);
                if (y1 == y0)
                {
                    return null;
                }
            }

            rate0 = ((X)rate1 - ((X)rate1 - rate0) * y1 / ((X)y1 - y0)).D;
            y0 = Imbalance(rate0, periods, payment, present, future, early);
            if (Math.Abs(y0) < Epsilon)
            {
                return rate0;
            }

            (y0, y1) = (y1, y0);
            (rate0, rate1) = (rate1, rate0);
        }

        return null;
    }

    // what the annuity equation leaves over at a rate: pv·(1+r)ⁿ + pmt·(1+r·d)·((1+r)ⁿ−1)/r + fv.
    private static double Imbalance(double rate, double periods, double payment, double present, double future, double early)
    {
        if (rate == 0)
        {
            return ((X)present + (X)payment * periods + future).D;
        }

        var growth = Math.Pow(((X)rate + 1).D, periods);
        var due = early != 0 ? ((X)1 + rate).D : 1;
        return ((X)present * growth + (X)payment * due * ((X)growth - 1) / rate + future).D;
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> SLN(VBDoubleValue cost, VBDoubleValue salvage, VBDoubleValue life)
        => life.Value == 0
            ? InvalidArgument(nameof(SLN), "the life is other than 0")
            // the depreciable amount, spread evenly over the life.
            : Result(nameof(SLN), (((X)cost.Value - salvage.Value) / life.Value).D);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> SYD(
        VBDoubleValue cost, VBDoubleValue salvage, VBDoubleValue life, VBDoubleValue period)
    {
        if (salvage.Value < 0 || period.Value <= 0 || period.Value > life.Value)
        {
            return InvalidArgument(nameof(SYD),
                "the salvage is not negative, and the period is greater than 0 and no greater than the life");
        }

        // each period takes the years still remaining, over the sum of the digits of every year of the life: the first of five takes 5/15 of the
        // depreciable amount, the last 1/15.
        var (c, s, l, p) = (cost.Value, salvage.Value, life.Value, period.Value);
        return Result(nameof(SYD), (((X)c - s) / ((X)l * ((X)l + 1)) * ((X)l + 1 - p) * 2).D);
    }

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

    // a Double calculation that overflows raises error 6 rather than producing an infinity, and so does one that has no numeric result at all - an
    // infinity over an infinity, of a power past the range of a Double (MS-VBA: PV(0.05, 20000, -100) is 6 - grid oracle\s43 V01-V05). A negative zero
    // is just zero.
    private static RuntimeSemanticsEvaluationResult<VBDoubleValue> Result(string function, double value)
        => double.IsFinite(value)
            ? RuntimeSemanticsEvaluationResult<VBDoubleValue>.Success(new VBDoubleValue(value == 0 ? 0 : value))
            : RuntimeSemanticsEvaluationResult<VBDoubleValue>.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.Overflow, default, double.IsNaN(value)
                    ? $"{function}: the result is no number, of a calculation past the range of a Double."
                    : $"{function}: the result is too large for a Double."));

    private static RuntimeSemanticsEvaluationResult<VBDoubleValue> InvalidArgument(string function, string rule)
        => RuntimeSemanticsEvaluationResult<VBDoubleValue>.Error(VBRuntimeErrorInfo.For(
            VBRuntimeErrorId.InvalidProcedureCallOrArgument, default, $"{function}: {rule}."));

    private static RuntimeSemanticsEvaluationResult<VBDoubleValue> NoResult(string function)
        => InvalidArgument(function, $"the iteration finds a result within {MaximumSteps} steps");
}
