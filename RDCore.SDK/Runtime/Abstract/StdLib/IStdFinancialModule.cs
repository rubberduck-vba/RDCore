using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.2.6 Financial Module</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the standard library <c>VBA.Financial</c> module.
/// <para>
/// Two conventions run through the module. For the annuity functions, "cash paid out (such as deposits to
/// savings) is represented by negative numbers; cash received (such as dividend checks) is represented by
/// positive numbers", and the cash flows of <see cref="IRR"/>, <see cref="MIRR"/> and <see cref="NPV"/> are
/// signed the same way. And an optional argument is declared <c>Variant</c> with no default value, the
/// specification stating the value an omitted one stands for in its own description instead (<c>0</c> for a
/// present value and for <c>Due</c>, <c>0.1</c> for a <c>Guess</c>, <c>2</c> for a <c>Factor</c>, and <c>0</c>
/// for a future value, which only <see cref="PV"/>'s description leaves unsaid), which is a default a C#
/// signature cannot state either, so an implementation supplies it.
/// </para>
/// </remarks>
[StdLibModule]
public interface IStdFinancialModule
{
    #region 6.1.2.6.1 StdFinancial: Public Functions

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.6.1.1 DDB</strong> Gets the depreciation of an asset for a given period, by the double-declining balance method or another declining rate.
    /// </summary>
    /// <param name="cost">The initial cost of the asset.</param>
    /// <param name="salvage">The value of the asset at the end of its useful life.</param>
    /// <param name="life">The length of the asset's useful life.</param>
    /// <param name="period">The period to depreciate the asset for, in the same unit as <paramref name="life"/>.</param>
    /// <param name="factor">The rate the balance declines at. <c>2</c> (the double-declining method), when unspecified.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBDoubleValue> DDB(
        VBDoubleValue cost, VBDoubleValue salvage, VBDoubleValue life, VBDoubleValue period,
        VBVariantValue? factor = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.6.1.2 FV</strong> Gets the future value of an annuity of fixed periodic payments at a fixed interest rate.
    /// </summary>
    /// <remarks>
    /// 👉 The specification's own declaration omits <c>Optional</c> from <paramref name="pV"/> and
    /// <paramref name="due"/>, where its description of each says what an omitted one means, as the Office
    /// reference does, and every other annuity function declares its <c>Due</c> optional. They are optional here.
    /// </remarks>
    /// <param name="rate">The interest rate per period.</param>
    /// <param name="nPer">The total number of payment periods, in the same unit as <paramref name="rate"/>.</param>
    /// <param name="pmt">The payment made each period.</param>
    /// <param name="pV">The present value, or lump sum, of the future payments. <c>0</c> when unspecified.</param>
    /// <param name="due"><c>0</c> when payments are due at the end of each period, <c>1</c> when they are due at its beginning. <c>0</c> when unspecified.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBDoubleValue> FV(
        VBDoubleValue rate, VBDoubleValue nPer, VBDoubleValue pmt,
        VBVariantValue? pV = default, VBVariantValue? due = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.6.1.3 IPmt</strong> Gets the interest part of an annuity's payment for a given period.
    /// </summary>
    /// <param name="rate">The interest rate per period.</param>
    /// <param name="per">The payment period, in the range 1 through <paramref name="nPer"/>.</param>
    /// <param name="nPer">The total number of payment periods, in the same unit as <paramref name="rate"/>.</param>
    /// <param name="pV">The present value of the payments.</param>
    /// <param name="fV">The balance wanted after the final payment. <c>0</c> when unspecified.</param>
    /// <param name="due"><c>0</c> when payments are due at the end of each period, <c>1</c> when they are due at its beginning. <c>0</c> when unspecified.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBDoubleValue> IPmt(
        VBDoubleValue rate, VBDoubleValue per, VBDoubleValue nPer, VBDoubleValue pV,
        VBVariantValue? fV = default, VBVariantValue? due = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.6.1.4 IRR</strong> Gets the internal rate of return of a series of periodic cash flows.
    /// </summary>
    /// <param name="valueArray">The cash flows, in the order they occur: at least one payment (negative) and one receipt (positive).</param>
    /// <param name="guess">An estimate of the rate to start the calculation from. <c>0.1</c> when unspecified.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBDoubleValue> IRR(
        [StdLibArray(typeof(VBDoubleValue))] VBResizableArrayValue valueArray, VBVariantValue? guess = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.6.1.5 MIRR</strong> Gets the modified internal rate of return of a series of periodic cash flows, whose payments and receipts are financed at different rates.
    /// </summary>
    /// <remarks>
    /// 👉 The underscores are the specification's own, and a named argument has to spell them: a name matches
    /// whatever its case, but not without them.
    /// </remarks>
    /// <param name="valueArray">The cash flows, in the order they occur: at least one payment (negative) and one receipt (positive).</param>
    /// <param name="finance_Rate">The interest rate paid as the cost of financing.</param>
    /// <param name="reinvest_Rate">The interest rate received on gains from reinvesting cash.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBDoubleValue> MIRR(
        [StdLibArray(typeof(VBDoubleValue))] VBResizableArrayValue valueArray,
        VBDoubleValue finance_Rate, VBDoubleValue reinvest_Rate);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.6.1.6 NPer</strong> Gets the number of periods of an annuity of fixed periodic payments at a fixed interest rate.
    /// </summary>
    /// <param name="rate">The interest rate per period.</param>
    /// <param name="pmt">The payment made each period.</param>
    /// <param name="pV">The present value of the payments.</param>
    /// <param name="fV">The balance wanted after the final payment. <c>0</c> when unspecified.</param>
    /// <param name="due"><c>0</c> when payments are due at the end of each period, <c>1</c> when they are due at its beginning. <c>0</c> when unspecified.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBDoubleValue> NPer(
        VBDoubleValue rate, VBDoubleValue pmt, VBDoubleValue pV,
        VBVariantValue? fV = default, VBVariantValue? due = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.6.1.7 NPV</strong> Gets the net present value of a series of periodic cash flows at a discount rate, one period before the first of them.
    /// </summary>
    /// <param name="rate">The discount rate over one period.</param>
    /// <param name="valueArray">The cash flows, in the order they occur.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBDoubleValue> NPV(
        VBDoubleValue rate, [StdLibArray(typeof(VBDoubleValue))] VBResizableArrayValue valueArray);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.6.1.8 Pmt</strong> Gets the payment of an annuity of fixed periodic payments at a fixed interest rate.
    /// </summary>
    /// <param name="rate">The interest rate per period.</param>
    /// <param name="nPer">The total number of payment periods, in the same unit as <paramref name="rate"/>.</param>
    /// <param name="pV">The present value, or lump sum, the payments are worth now.</param>
    /// <param name="fV">The balance wanted after the final payment. <c>0</c> when unspecified.</param>
    /// <param name="due"><c>0</c> when payments are due at the end of each period, <c>1</c> when they are due at its beginning. <c>0</c> when unspecified.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBDoubleValue> Pmt(
        VBDoubleValue rate, VBDoubleValue nPer, VBDoubleValue pV,
        VBVariantValue? fV = default, VBVariantValue? due = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.6.1.9 PPmt</strong> Gets the principal part of an annuity's payment for a given period.
    /// </summary>
    /// <param name="rate">The interest rate per period.</param>
    /// <param name="per">The payment period, in the range 1 through <paramref name="nPer"/>.</param>
    /// <param name="nPer">The total number of payment periods, in the same unit as <paramref name="rate"/>.</param>
    /// <param name="pV">The present value of the payments.</param>
    /// <param name="fV">The balance wanted after the final payment. <c>0</c> when unspecified.</param>
    /// <param name="due"><c>0</c> when payments are due at the end of each period, <c>1</c> when they are due at its beginning. <c>0</c> when unspecified.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBDoubleValue> PPmt(
        VBDoubleValue rate, VBDoubleValue per, VBDoubleValue nPer, VBDoubleValue pV,
        VBVariantValue? fV = default, VBVariantValue? due = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.6.1.10 PV</strong> Gets the present value of an annuity of fixed periodic payments at a fixed interest rate.
    /// </summary>
    /// <remarks>
    /// 👉 The specification's description of <paramref name="fV"/> says nothing of an omitted one, where every
    /// other annuity function's says it is <c>0</c>. It is <c>0</c> here too, by analogy with them.
    /// </remarks>
    /// <param name="rate">The interest rate per period.</param>
    /// <param name="nPer">The total number of payment periods, in the same unit as <paramref name="rate"/>.</param>
    /// <param name="pmt">The payment made each period.</param>
    /// <param name="fV">The balance wanted after the final payment. <c>0</c> when unspecified.</param>
    /// <param name="due"><c>0</c> when payments are due at the end of each period, <c>1</c> when they are due at its beginning. <c>0</c> when unspecified.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBDoubleValue> PV(
        VBDoubleValue rate, VBDoubleValue nPer, VBDoubleValue pmt,
        VBVariantValue? fV = default, VBVariantValue? due = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.6.1.11 Rate</strong> Gets the interest rate per period of an annuity of fixed periodic payments.
    /// </summary>
    /// <param name="nPer">The total number of payment periods.</param>
    /// <param name="pmt">The payment made each period.</param>
    /// <param name="pV">The present value of the payments.</param>
    /// <param name="fV">The balance wanted after the final payment. <c>0</c> when unspecified.</param>
    /// <param name="due"><c>0</c> when payments are due at the end of each period, <c>1</c> when they are due at its beginning. <c>0</c> when unspecified.</param>
    /// <param name="guess">An estimate of the rate to start the calculation from. <c>0.1</c> when unspecified.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBDoubleValue> Rate(
        VBDoubleValue nPer, VBDoubleValue pmt, VBDoubleValue pV,
        VBVariantValue? fV = default, VBVariantValue? due = default, VBVariantValue? guess = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.6.1.12 SLN</strong> Gets the straight-line depreciation of an asset for a single period.
    /// </summary>
    /// <param name="cost">The initial cost of the asset.</param>
    /// <param name="salvage">The value of the asset at the end of its useful life.</param>
    /// <param name="life">The length of the asset's useful life.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBDoubleValue> SLN(VBDoubleValue cost, VBDoubleValue salvage, VBDoubleValue life);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.6.1.13 SYD</strong> Gets the sum-of-years' digits depreciation of an asset for a given period.
    /// </summary>
    /// <param name="cost">The initial cost of the asset.</param>
    /// <param name="salvage">The value of the asset at the end of its useful life.</param>
    /// <param name="life">The length of the asset's useful life.</param>
    /// <param name="period">The period to depreciate the asset for, in the same unit as <paramref name="life"/>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBDoubleValue> SYD(
        VBDoubleValue cost, VBDoubleValue salvage, VBDoubleValue life, VBDoubleValue period);

    #endregion
}
