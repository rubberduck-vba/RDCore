using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.2.7 Information Module</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the standard library <c>VBA.Information</c> module.
/// </remarks>
[StdLibModule]
public interface IStdInformationModule
{
    /// <summary>
    /// Holds the <em>data values</em> corresponding to each <em>legacy</em> 16-bit <c>QBColor</c> value.
    /// </summary>
    /// <remarks>
    /// 💥 Any other value raises run-time error 5 <see cref="VBRuntimeErrorId.InvalidProcedureCallOrArgument"/>.
    /// </remarks>
    public static class StdInformationQBColors
    {
        public const int Black = 0x000000;
        public const int Blue = 0x800000;
        public const int Green = 0x008000;
        public const int Cyan = 0x808000;
        public const int Red = 0x800000;
        public const int Majenta = 0x800080;
        public const int Yellow = 0x808000;
        public const int White = 0xC0C0C0;
        public const int Gray = 0x808080;
        public const int LightBlue = 0xFF0000;
        public const int LightGreen = 0x00FF00;
        public const int LightCyan = 0xFFFF00;
        public const int LightRed = 0xFF0000;
        public const int LightMajenta = 0xFF00FF;
        public const int LightYellow = 0xFFFF00;
        public const int BrightWhite = 0xFFFFFF;

        internal static Dictionary<StdInformationQBColor, int> QBColorRGBValues = new()
        {
            [StdInformationQBColor.Black] = Black,
            [StdInformationQBColor.Blue] = Blue,
            [StdInformationQBColor.Green] = Green,
            [StdInformationQBColor.Cyan] = Cyan,
            [StdInformationQBColor.Red] = Red,
            [StdInformationQBColor.Majenta] = Majenta,
            [StdInformationQBColor.Yellow] = Yellow,
            [StdInformationQBColor.White] = White,
            [StdInformationQBColor.Gray] = Gray,
            [StdInformationQBColor.LightBlue] = LightBlue,
            [StdInformationQBColor.LightGreen] = LightGreen,
            [StdInformationQBColor.LightCyan] = LightCyan,
            [StdInformationQBColor.LightRed] = LightRed,
            [StdInformationQBColor.LightMajenta] = LightMajenta,
            [StdInformationQBColor.LightYellow] = LightYellow,
            [StdInformationQBColor.BrightWhite] = BrightWhite
        };

        public static bool TryGetQBColorRGBValue(StdInformationQBColor qbColorValue, out int rgbColorValue)
            => QBColorRGBValues.TryGetValue(qbColorValue, out rgbColorValue);
    }

    /// <summary>
    /// A formalisation of the legal underlying <em>legacy</em> 16-bit <c>QBColor</c> values.
    /// </summary>
    public enum StdInformationQBColor
    {
        Black = 0,
        Blue = 1,
        Green = 2,
        Cyan = 3,
        Red = 4,
        Majenta = 5,
        Yellow = 6,
        White = 7,
        Gray = 8,
        LightBlue = 9,
        LightGreen = 10,
        LightCyan = 11,
        LightRed = 12,
        LightMajenta = 13,
        LightYellow = 14,
        BrightWhite = 15
    }

    #region 6.1.2.7.1 StdInformation: Public Functions

    /// <summary>
    /// <strong>MS-VBAL 6.1.3.2 Err Class</strong> Gets the <em>error object</em>: the single
    /// <c>ErrObject</c> instance reflecting the error state of the active VBA environment.
    /// </summary>
    /// <remarks>
    /// 👉 Not a member MS-VBAL lists under this module: <strong>§6.1.3.2</strong> describes the error
    /// object as the default instance of a global class module named <c>Err</c>, while MS-VBA exposes it
    /// as this zero-argument function of <c>Information</c>, returning an instance of a class named
    /// <c>ErrObject</c>. The two are the same thing seen from source — a bare <c>Err</c> yields the error
    /// object either way, because a standard module's members are promoted to the project scope — and
    /// this is the arrangement that also makes <c>ErrObject</c> nameable in an <c>As</c> clause.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(ReturnType = typeof(IStdErrClass))]
    RuntimeSemanticsEvaluationResult<VBObjectValue> Err();

    /// <summary>
    /// Gets the <em>line number</em> the most recent run-time error was raised at — the nearest
    /// line-number label at or before the faulting statement, or <c>0</c> when none precedes it.
    /// </summary>
    /// <remarks>
    /// 👉 Not a member MS-VBAL documents at all, and hidden in MS-VBA, which nonetheless exposes it from
    /// this module. A line number is sticky: it labels every statement after it until the next one, so this
    /// answers for a statement that carries no label of its own. A <em>named</em> label never sets it.
    /// <para>
    /// 🎯 <strong>RD-VBA returns a <c>Long</c> where MS-VBA returns <c>ushort</c> resolution.</strong> A
    /// deliberate divergence: MS-VBA wraps <c>Erl</c> around on any line number that does not fit, so a
    /// program numbered past 65535 is told it faulted at a line it has not got. Widening it costs nothing
    /// and makes every legal line number label representable.
    /// </para>
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBLongValue> Erl();

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.1 IMEStatus</strong>
    /// </summary>
    /// <remarks>
    /// Returns a <see cref="VBIMEStatus"/> value representing the current implementation-dependant <em>Input Method Editor</em> (IME) mode.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(ReturnType = typeof(VBIMEStatus))]
    RuntimeSemanticsEvaluationResult<VBLongValue> IMEStatus();

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.2 IsArray</strong>
    /// </summary>
    /// <remarks>
    /// Tests a provided value to check if it is a <see cref="VBArrayValue"/>.
    /// </remarks>
    /// <param name="arg">The data value to be tested</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBBooleanValue> IsArray(VBVariantValue arg);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.3 IsDate</strong>
    /// </summary>
    /// <remarks>
    /// Tests a provided value to check if it is a <see cref="VBDateValue"/>.
    /// </remarks>
    /// <param name="arg">The data value to be tested</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBBooleanValue> IsDate(VBVariantValue arg);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.4 IsEmpty</strong>
    /// </summary>
    /// <remarks>
    /// Tests a provided value to check if it is a <see cref="VBEmptyValue"/>.
    /// </remarks>
    /// <param name="arg">The data value to be tested</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBBooleanValue> IsEmpty(VBVariantValue arg);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.5 IsError</strong>
    /// </summary>
    /// <remarks>
    /// Tests a provided value to check if it is a <see cref="VBErrorValue"/>.
    /// </remarks>
    /// <param name="arg">The data value to be tested</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBBooleanValue> IsError(VBVariantValue arg);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.6 IsMissing</strong>
    /// </summary>
    /// <remarks>
    /// Tests a provided value to check if it is a <see cref="VBMissingValue"/>.<br/>
    /// 👉 The value can only be a <see cref="VBMissingValue"/> if it is the <see cref="VBVariantValue"/> of an <strong>optional parameter that was not supplied</strong>.
    /// </remarks>
    /// <param name="arg">The data value to be tested</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBBooleanValue> IsMissing(VBVariantValue arg);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.7 IsNull</strong>
    /// </summary>
    /// <remarks>
    /// Tests a provided value to check if it is a <see cref="VBNullValue"/>.
    /// </remarks>
    /// <param name="arg">The data value to be tested</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBBooleanValue> IsNull(VBVariantValue arg);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.8 IsNumeric</strong>
    /// </summary>
    /// <remarks>
    /// Tests a provided value to check if it is a <see cref="VBNumericTypedValue"/>.
    /// </remarks>
    /// <param name="arg">The data value to be tested</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBBooleanValue> IsNumeric(VBVariantValue arg);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.9 IsObject</strong>
    /// </summary>
    /// <remarks>
    /// Tests a provided value to check if it is a <see cref="VBObjectValue"/>.
    /// </remarks>
    /// <param name="arg">The data value to be tested</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBBooleanValue> IsObject(VBVariantValue arg);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.10 QBColor</strong>
    /// </summary>
    /// <remarks>
    /// Gets the RGB color value for a specified color value used by earlier versions of Visual Basic.
    /// </remarks>
    /// <param name="color">A value in the 0-15 range naming one of the <em>legacy</em> 16-bit colors.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBLongValue> QBColor(VBIntegerValue color);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.11 RGB</strong>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBLongValue"/> representing a RGB color value from its specified parts.
    /// </remarks>
    /// <param name="red">A value in the <see cref="VBByteValue"/> range (0-255) representing the <strong>red</strong> component of the color.</param>
    /// <param name="green">A value in the <see cref="VBByteValue"/> range (0-255) representing the <strong>green</strong> component of the color.</param>
    /// <param name="blue">A value in the <see cref="VBByteValue"/> range (0-255) representing the <strong>blue</strong> component of the color.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBLongValue> RGB(VBIntegerValue red, VBIntegerValue green, VBIntegerValue blue);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.12 TypeName</strong>
    /// </summary>
    /// <remarks>
    /// Gets the name of the <em>data type</em> of the specified value.<br/>
    /// 👉 If the provided value is a <see cref="VBArrayValue"/>, the returned string contains the <em>item data type</em> of the array appended with a pair of empty parentheses, e.g. <c>"Byte()"</c> for an array of <see cref="VBByteValue"/> items.
    /// </remarks>
    /// <param name="arg">The data value to name the type of.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBStringValue> TypeName(VBVariantValue arg);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.13 VarType</strong>
    /// </summary>
    /// <remarks>
    /// Gets the <em>subtype</em> of the specified <see cref="VBVariantValue"/>.<br/>
    /// 👉 An array's subtype is <see cref="VBVarType.VBArray"/> combined with the <em>item data type</em> of the array, e.g. <c>8209</c> for an array of <see cref="VBByteValue"/> items.
    /// </remarks>
    /// <param name="varName">The data value to report the subtype of.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(ReturnType = typeof(VBVarType))]
    RuntimeSemanticsEvaluationResult<VBLongValue> VarType(VBVariantValue varName);
    #endregion
}
