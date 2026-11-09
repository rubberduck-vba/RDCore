using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Model.Symbols;

namespace RDCore.Runtime.LanguageCore.StdLib.Abstract;

/// <summary>
/// <strong>MS-VBAL 6.1.2.3 Conversion Module</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the standard library <c>VBA.Conversion</c> module.
/// </remarks>
[StdLibModule]
public interface IStdConversionModule
{
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.1 CBool</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBBooleanType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBBooleanValue> CBool([StdLibValue] VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.2 CByte</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBByteType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBByteValue> CByte([StdLibValue] VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.3 CCur</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBCurrencyType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBCurrencyValue> CCur([StdLibValue] VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.4 CDate</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBDateType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBDateValue> CDate([StdLibValue] VBVariantValue expression);
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.4 CVDate</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBVariantValue"/>/<see cref="VBDateType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> CVDate(VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.5 CDbl</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBDoubleType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBDoubleValue> CDbl([StdLibValue] VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.6 CDec</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBDecimalType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> CDec(VBVariantValue expression);


    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.7 CInt</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBIntegerType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBIntegerValue> CInt([StdLibValue] VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.8 CLng</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBLongType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBLongValue> CLng([StdLibValue] VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.9 CLngLng</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBLongLongType"/> <em>explicit-coercion</em> function.<br/>
    /// 👉 This function is statically invalid in a 32-bit execution environment.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBLongLongValue> CLngLng([StdLibValue] VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.10 CLngPtr</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBLongPtrType_x86"/>/<see cref="VBLongPtrType_x64"/> <em>explicit-coercion</em> function.<br/>
    /// 👉 The <see cref="VBType"/> returned by this function depends on the bitness of the execution environment.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBLongPtrValue> CLngPtr([StdLibValue] VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.11 CSng</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBSingleType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBSingleValue> CSng([StdLibValue] VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.12 CStr</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBStringType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBStringValue> CStr([StdLibValue] VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.13 CVar</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBVariantType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> CVar(VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.14 CVErr</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBVariantValue"/>/<see cref="VBErrorType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> CVErr([StdLibValue] VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.15 Error</strong>
    /// </summary>
    /// <remarks>
    /// Gets the localized <em>string</em> <c>Message</c> of the specified <em>error number</em> value (declared type is <see cref="VBVariantType"/>).
    /// </remarks>
    /// <param name="errorNumber">The <em>error number</em>; when omitted, the number of the most recent run-time error.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Error(VBVariantValue? errorNumber = default);
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.15 Error$</strong>
    /// </summary>
    /// <remarks>
    /// Gets the localized <em>string</em> <c>Message</c> of the specified <em>error number</em> value.
    /// </remarks>
    /// <param name="errorNumber">The <em>error number</em>; when omitted, the number of the most recent run-time error.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember("Error$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> ErrorStr(VBVariantValue? errorNumber = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.16 Fix</strong>
    /// </summary>
    /// <remarks>
    /// Gets the integer portion of a numeric value.
    /// </remarks>
    /// <param name="number">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Fix([StdLibValue(ValueDestination.AnyValue)] VBVariantValue number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.17 Hex</strong>
    /// </summary>
    /// <remarks>
    /// Gets the hexadecimal (string) representation of the specified numeric value (declared type is <see cref="VBVariantType"/>).
    /// </remarks>
    /// <param name="number">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Hex(VBVariantValue number);
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.17 Hex$</strong>
    /// </summary>
    /// <remarks>
    /// Gets the hexadecimal (string) representation of the specified numeric value.
    /// </remarks>
    /// <param name="number">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember("Hex$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> HexStr(VBVariantValue number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.18 Int</strong>
    /// </summary>
    /// <remarks>
    /// Gets the integer portion of the specified numeric value.
    /// </remarks>
    /// <param name="number">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Int([StdLibValue(ValueDestination.AnyValue)] VBVariantValue number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.19 Oct</strong>
    /// </summary>
    /// <remarks>
    /// Gets the octal (string) representation the specified numeric value (the declared type is <see cref="VBVariantType"/>).
    /// </remarks>
    /// <param name="number">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Oct(VBVariantValue number);
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.19 Oct$</strong>
    /// </summary>
    /// <remarks>
    /// Gets the octal (string) representation the specified numeric value.
    /// </remarks>
    /// <param name="number">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember("Oct$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> OctStr(VBVariantValue number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.20 Str</strong>
    /// </summary>
    /// <remarks>
    /// Gets the string representation the specified value (the declared type is <see cref="VBVariantType"/>).
    /// </remarks>
    /// <param name="number">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Str(VBVariantValue number);
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.20 Str$</strong>
    /// </summary>
    /// <remarks>
    /// Gets the string representation the specified value.
    /// </remarks>
    /// <param name="number">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember("Str$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> StrStr(VBVariantValue number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.21 Val</strong>
    /// </summary>
    /// <remarks>
    /// Extracts a numeric value from the specified <see cref="VBStringValue"/> value.
    /// </remarks>
    /// <param name="value">The <em>string</em> to read a number from.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBDoubleValue> Val(VBStringValue value);
}
