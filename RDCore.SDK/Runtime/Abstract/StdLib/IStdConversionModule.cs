using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.2.3 Conversion Module</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the standard library <c>VBA.Conversion</c> module.
/// </remarks>
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
    RuntimeSemanticsEvaluationResult StdConversions__CBool(VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.2 CByte</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBByteType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__CByte(VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.3 CCur</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBCurrencyType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__CCur(VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.4 CDate</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBDateType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__CDate(VBVariantValue expression);
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.4 CVDate</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBVariantValue"/>/<see cref="VBDateType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__CVDate(VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.5 CDbl</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBDoubleType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__CDbl(VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.6 CDec</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBDecimalType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__CDec(VBVariantValue expression);


    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.7 CInt</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBIntegerType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__CInt(VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.8 CLng</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBLongType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__CLng(VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.9 CLngLng</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBLongLongType"/> <em>explicit-coercion</em> function.<br/>
    /// 👉 This function is statically invalid in a 32-bit execution environment.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__CLngLng(VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.10 CLngPtr</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBLongPtrType_x86"/>/<see cref="VBLongPtrType_x64"/> <em>explicit-coercion</em> function.<br/>
    /// 👉 The <see cref="VBType"/> returned by this function depends on the bitness of the execution environment.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__CLngPtr(VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.11 CSng</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBSingleType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__CSng(VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.12 CStr</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBStringType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__CStr(VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.13 CVar</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBVariantType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__CVar(VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.14 CVErr</strong>
    /// </summary>
    /// <remarks>
    /// The <see cref="VBVariantValue"/>/<see cref="VBErrorType"/> <em>explicit-coercion</em> function.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__CVErr(VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.15 Error</strong>
    /// </summary>
    /// <remarks>
    /// Gets the localized <em>string</em> <c>Message</c> of the specified <em>error number</em> value (declared type is <see cref="VBVariantType"/>).
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__Error(VBVariantValue errorNumber);
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.15 Error$</strong>
    /// </summary>
    /// <remarks>
    /// Gets the localized <em>string</em> <c>Message</c> of the specified <em>error number</em> value.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__ErrorStr(VBVariantValue errorNumber);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.16 Fix</strong>
    /// </summary>
    /// <remarks>
    /// Gets the integer portion of a numeric value.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__Fix(VBVariantValue number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.17 Hex</strong>
    /// </summary>
    /// <remarks>
    /// Gets the hexadecimal (string) representation of the specified numeric value (declared type is <see cref="VBVariantType"/>).
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__Hex(VBVariantValue number);
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.17 Hex$</strong>
    /// </summary>
    /// <remarks>
    /// Gets the hexadecimal (string) representation of the specified numeric value.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__HexStr(VBVariantValue number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.18 Int</strong>
    /// </summary>
    /// <remarks>
    /// Gets the integer portion of the specified numeric value.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__Int(VBVariantValue number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.19 Oct</strong>
    /// </summary>
    /// <remarks>
    /// Gets the octal (string) representation the specified numeric value (the declared type is <see cref="VBVariantType"/>).
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__Oct(VBVariantValue number);
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.19 Oct$</strong>
    /// </summary>
    /// <remarks>
    /// Gets the octal (string) representation the specified numeric value.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__OctStr(VBVariantValue number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.20 Str</strong>
    /// </summary>
    /// <remarks>
    /// Gets the string representation the specified value (the declared type is <see cref="VBVariantType"/>).
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__Str(VBVariantValue number);
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.20 Str$</strong>
    /// </summary>
    /// <remarks>
    /// Gets the string representation the specified value.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__StrStr(VBVariantValue number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.3.1.21 Val</strong>
    /// </summary>
    /// <remarks>
    /// Extracts a numeric value from the specified <see cref="VBStringValue"/> value.
    /// </remarks>
    /// <param name="expression">Any <em>data value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdConversions__Val(VBStringValue value);
}
