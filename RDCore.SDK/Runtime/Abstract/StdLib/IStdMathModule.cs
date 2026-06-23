using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.2.10 Math Module</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the standard library <c>VBA.Math</c> module.
/// </remarks>
public interface IStdMathModule
{
    #region 6.1.2.10.1 StdMath: Public Functions

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.10.1 Abs</strong>
    /// </summary>
    /// <remarks>
    /// Gets the <em>absolute value</em> of a numeric value.
    /// </remarks>
    /// <param name="Number">Any <em>data value</em></param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdMath__Abs(VBVariantValue Number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.10.2 Atn</strong>
    /// </summary>
    /// <remarks>
    /// Gets the <em>arctangent</em> of a numeric value.
    /// </remarks>
    /// <param name="Number">Any <see cref="VBDoubleValue"/> containing a valid numeric value (i.e. not <c>NaN</c>).</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdMath__Atn(VBDoubleValue Number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.10.3 Cos</strong>
    /// </summary>
    /// <remarks>
    /// Gets the <em>cosine</em> of a numeric value.
    /// </remarks>
    /// <param name="Number">Any <see cref="VBDoubleValue"/> containing a valid numeric value (i.e. not <c>NaN</c>) <em>representing an angle in radians</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdMath__Cos(VBDoubleValue Number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.10.4 Exp</strong>
    /// </summary>
    /// <remarks>
    /// Gets the value of <c>e</c> (the base of natural logarithms, ~<c>2.718282</c>) raised to a specified <em>power</em>.
    /// </remarks>
    /// <param name="Number">Any <see cref="VBDoubleValue"/> containing a valid numeric value (i.e. not <c>NaN</c>) representing the <em>power</em> to raise <c>e</c> by.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdMath__Exp(VBDoubleValue Number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.10.5 Log</strong>
    /// </summary>
    /// <remarks>
    /// Gets the <em>natural logarithm</em> of a numeric value.
    /// </remarks>
    /// <param name="Number">Any <see cref="VBDoubleValue"/> containing a valid numeric value (i.e. not <c>NaN</c>) <em>greater than zero</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdMath__Log(VBDoubleValue Number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.10.6 Rnd</strong>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBSingleValue"/> representing a <em>pseudo-random</em> numeric value <strong>less than</strong> <c>1</c> but <strong>greater than or equal to</strong> <c>0</c>.<br/><br/>
    /// 👉 The <see cref="StdMath__Randomize(VBVariantValue)"/> procedure should be invoked before this function is invoked, to initialize a <em>seed</em> based on the <em>system timer</em>.<br/> 
    /// The behavior of this function is <em>implementation-defined</em> (unspecified) otherwise.
    /// </remarks>
    /// <param name="Number">Any <see cref="VBSingleValue"/> containing a valid numeric value (i.e. not <c>NaN</c>) specifying the <em>runtime semantics</em> of the function.<br/>
    /// <strong>Optional</strong>: a <see cref="VBMissingValue"/> parameter behaves as if an integer value greater than zero was supplied.<br/>
    /// 👉 If the value is:
    /// <list type="bullet">
    /// <item><strong>less than</strong> <c>0</c>, function returns the value determined by using its input as a <em>seed</em>.</item>
    /// <item><strong>equal to</strong> <c>0</c>, function returns the most recently generated <em>pseudo-random</em> value in the sequence determined by the current <em>seed</em>.</item>
    /// <item><strong>omitted</strong> or <strong>greater than</strong> <c>0</c>, function returns the next <em>pseudo-random</em> value in the sequence determined by the current <em>seed</em>.</item>
    /// </list>
    /// </param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdMath__Rnd(VBVariantValue Number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.10.7 Round</strong>
    /// </summary>
    /// <remarks>
    /// Gets a numeric value <em>rounded</em> to a specified number of <em>decimal places</em>.<br/>
    /// </remarks>
    /// <param name="Number">Any <see cref="VBVariantValue"/> containing the numeric value to be rounded.</param>
    /// <param name="NumDigitsAfterDecimal">Any <see cref="VBLongValue"/> containing representing the <em>number of decimal places</em> to the <em>right</em> of the <em>decimal separator</em> are included in the rounded result.<br/><strong>Optional</strong>: <see cref="VBLongType.DefaultValue"/> if omitted.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdMath__Round(VBVariantValue Number, VBLongValue NumDigitsAfterDecimal);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.10.8 Sgn</strong>
    /// </summary>
    /// <remarks>
    /// Gets an integer value representing the <em>sign</em> of a specified number. If that number is:
    /// <list type="bullet">
    /// <item><em>equal to zero</em>, the function returns <c>0</c>;</item>
    /// <item><em>greater than zero</em>, the function returns <c>1</c>;</item>
    /// <item><em>less than zero</em>, the function returns <c>-1</c>.</item>
    /// </list>
    /// </remarks>
    /// <param name="Number">Any <see cref="VBDoubleValue"/> containing a valid numeric value (i.e. not <c>NaN</c>).</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdMath__Sgn(VBVariantValue Number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.10.9 Sin</strong>
    /// </summary>
    /// <remarks>
    /// Gets the <em>sine</em> of a numeric value.
    /// </remarks>
    /// <param name="Number">Any <see cref="VBDoubleValue"/> containing a valid numeric value (i.e. not <c>NaN</c>) <em>representing an angle in radians</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdMath__Sin(VBDoubleValue Number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.10.10 Sqr</strong>
    /// </summary>
    /// <remarks>
    /// Gets the <em>square root</em> of a numeric value.
    /// </remarks>
    /// <param name="Number">Any <see cref="VBDoubleValue"/> containing a valid numeric value (i.e. not <c>NaN</c>) <em>greater than</em> <c>0</c>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdMath__Sqr(VBDoubleValue Number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.10.11 Tan</strong>
    /// </summary>
    /// <remarks>
    /// Gets the <em>tangent</em> of a numeric value.
    /// </remarks>
    /// <param name="Number">Any <see cref="VBDoubleValue"/> containing a valid numeric value (i.e. not <c>NaN</c>) <em>representing an angle in radians</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdMath__Tan(VBDoubleValue Number);

    #endregion

    #region 6.1.2.10.2 StdMath: Public Subroutines

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.10.2.1 Randomize</strong>
    /// </summary>
    /// <remarks>
    /// Initializes the <em>pseudo-random</em> number generator configured in the current <em>host environment</em>.
    /// </remarks>
    /// <param name="Number">A <see cref="VBNumericTypedValue"/> representing a <em>seed value</em>.<br/><strong>Optional</strong>: <see cref="VBMissingValue"/> or <see cref="VBEmptyValue"/> yields a new <em>seed value</em> from the <em>host-provided</em> pseudo-random number generator.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdMath__Randomize(VBVariantValue Number);

    #endregion
}

