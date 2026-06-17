using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.2.7 Interaction Module</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the standard library <c>VBA.Interaction</c> module.<br/>
/// ℹ️ <strong>This interface is currently incomplete.</strong>
/// </remarks>
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
    /// <strong>MS-VBAL 6.1.2.7.1.1 IMEStatus</strong>
    /// </summary>
    /// <remarks>
    /// Returns a <see cref="VBIMEStatus"/> value representing the current implementation-dependant <em>Input Method Editor</em> (IME) mode.<br/>
    /// The valid <c>interval</c> parameter values are defined in <see cref="StdDateIntervals"/>.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInformation__IMEStatus();

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.2 IsArray</strong>
    /// </summary>
    /// <remarks>
    /// Tests a provided value to check if it is a <see cref="VBArrayValue"/>.
    /// </remarks>
    /// <param name="arg">The data value to be tested</param>
    /// <returns>Returns a <see cref="VBBooleanValue"/> that is <c>True</c> if the specified argument is an array, <c>False</c> otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInformation__IsArray(VBVariantValue arg);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.3 IsDate</strong>
    /// </summary>
    /// <remarks>
    /// Tests a provided value to check if it is a <see cref="VBDateValue"/>.
    /// </remarks>
    /// <param name="arg">The data value to be tested</param>
    /// <returns>Returns a <see cref="VBBooleanValue"/> that is <c>True</c> if the specified argument is a date, <c>False</c> otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInformation__IsDate(VBVariantValue arg);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.4 IsEmpty</strong>
    /// </summary>
    /// <remarks>
    /// Tests a provided value to check if it is a <see cref="VBEmptyValue"/>.
    /// </remarks>
    /// <param name="arg">The data value to be tested</param>
    /// <returns>Returns a <see cref="VBBooleanValue"/> that is <c>True</c> if the specified argument value is <see cref="VBEmptyValue"/>, <c>False</c> otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInformation__IsEmpty(VBVariantValue arg);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.5 IsError</strong>
    /// </summary>
    /// <remarks>
    /// Tests a provided value to check if it is a <see cref="VBErrorValue"/>.
    /// </remarks>
    /// <param name="arg">The data value to be tested</param>
    /// <returns>Returns a <see cref="VBBooleanValue"/> that is <c>True</c> if the specified argument value is <see cref="VBErrorValue"/>, <c>False</c> otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInformation__IsError(VBVariantValue arg);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.6 IsMissing</strong>
    /// </summary>
    /// <remarks>
    /// Tests a provided value to check if it is a <see cref="VBMissingValue"/>.<br/>
    /// 👉 The value can only be a <see cref="VBMissingValue"/> if it is the <see cref="VBVariantValue"/> of an <strong>optional parameter that was not supplied</strong>. 
    /// </remarks>
    /// <param name="arg">The data value to be tested</param>
    /// <returns>Returns a <see cref="VBBooleanValue"/> that is <c>True</c> if the specified argument value is <see cref="VBMissingValue"/>, <c>False</c> otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInformation__IsMissing(VBVariantValue arg);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.7 IsNull</strong>
    /// </summary>
    /// <remarks>
    /// Tests a provided value to check if it is a <see cref="VBNullValue"/>.
    /// </remarks>
    /// <param name="arg">The data value to be tested</param>
    /// <returns>Returns a <see cref="VBBooleanValue"/> that is <c>True</c> if the specified argument value is <see cref="VBNullValue"/>, <c>False</c> otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInformation__IsNull(VBVariantValue arg);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.8 IsNumeric</strong>
    /// </summary>
    /// <remarks>
    /// Tests a provided value to check if it is a <see cref="VBNumericTypedValue"/>.
    /// </remarks>
    /// <param name="arg">The data value to be tested</param>
    /// <returns>Returns a <see cref="VBBooleanValue"/> that is <c>True</c> if the specified argument value is <see cref="VBNumericTypedValue"/>, <c>False</c> otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInformation__IsNumeric(VBVariantValue arg);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.9 IsObject</strong>
    /// </summary>
    /// <remarks>
    /// Tests a provided value to check if it is a <see cref="VBObjectValue"/>.
    /// </remarks>
    /// <param name="arg">The data value to be tested</param>
    /// <returns>Returns a <see cref="VBBooleanValue"/> that is <c>True</c> if the specified argument value is <see cref="VBObjectValue"/>, <c>False</c> otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdInformation__IsObject(VBVariantValue arg);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.10 QBColor</strong>
    /// </summary>
    /// <remarks>
    /// Gets the RGB color value for a specified color value used by earlier versions of Visual Basic.
    /// </remarks>
    /// <param name="arg">The data value to be tested</param>
    /// <returns>Returns a <see cref="VBLongValue"/> in the range <c>0-15</c>.</returns>
    RuntimeSemanticsEvaluationResult StdInformation__QBColor(VBVariantValue arg);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.11 RGB</strong>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBLongValue"/> representing a RGB color value from its specified parts.
    /// </remarks>
    /// <param name="red">A value in the <see cref="VBByteValue"/> range (0-255) representing the <strong>red</strong> component of the color.</param>
    /// <param name="green">A value in the <see cref="VBByteValue"/> range (0-255) representing the <strong>green</strong> component of the color.</param>
    /// <param name="blue">A value in the <see cref="VBByteValue"/> range (0-255) representing the <strong>blue</strong> component of the color.</param>
    /// <returns>Returns a <see cref="VBLongValue"/> representing a 24-bit RGB color value.</returns>
    RuntimeSemanticsEvaluationResult StdInformation__RGB(VBIntegerValue red, VBIntegerValue green, VBIntegerValue blue);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.12 TypeName</strong>
    /// </summary>
    /// <remarks>
    /// Gets the name of the <em>data type</em> of the specified value.<br/>
    /// 👉 If the provided value is a <see cref="VBArrayValue"/>, the returned string contains the <em>item data type</em> of the array appended with a pair of empty parentheses, e.g. <c>"Byte()"</c> for an array of <see cref="VBByteValue"/> items.
    /// </remarks>
    /// <returns>Returns a <see cref="VBStringValue"/> representing the name of the <em>data type</em> of the provided value.</returns>
    RuntimeSemanticsEvaluationResult StdInformation__TypeName(VBVariantValue arg);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.7.1.13 VarType</strong>
    /// </summary>
    /// <remarks>
    /// Gets the <em>subtype</em> of the specified <see cref="VBVariantValue"/>.<br/>
    /// 👉 If the provided value is a <see cref="VBArrayValue"/>, the returned string contains the <em>item data type</em> of the array appended with a pair of empty parentheses, e.g. <c>"Byte()"</c> for an array of <see cref="VBByteValue"/> items.
    /// </remarks>
    /// <returns>Returns a <see cref="VBStringValue"/> representing the name of the <em>data type</em> of the provided value.</returns>
    RuntimeSemanticsEvaluationResult StdInformation__VarType(VBVariantValue arg);
    #endregion
}
