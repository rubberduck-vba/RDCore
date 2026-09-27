using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdStringsModule"/>
/// <remarks>
/// 🚧 Only <see cref="Len"/> and <see cref="LenB"/> are implemented. Every other member returns the error a
/// member nothing implements returns, the same way <see cref="StdInformation"/>'s do: the symbol resolves,
/// the call is well-formed, and the platform has not got the code yet. The declarations are what say the
/// shape each one has to satisfy, so filling them in adds no plumbing.
/// <para>
/// 👉 Stateless <em>so far</em>, which is the only reason it takes no session yet. The rest of the module
/// needs one: a comparison member (<c>InStr</c>, <c>StrComp</c>, <c>Replace</c>, <c>Filter</c>, <c>Split</c>)
/// defaults its <c>compare</c> argument to the calling module's <c>Option Compare</c>, which rides on the
/// executing frame's directives, and <c>Format</c> and its siblings need the environment's culture. Both
/// arrive through <c>IRuntimeSession</c>, the way <see cref="StdInformation"/> already takes it — so this
/// constructor gains one back with the first member that reads either.
/// </para>
/// </remarks>
public sealed class StdStrings : IStdStringsModule
{
    /// <summary>
    /// The error every member that is declared but not written yet returns.
    /// </summary>
    /// <remarks>
    /// TODO one per member as they land.
    /// </remarks>
    private static RuntimeSemanticsEvaluationResult<TValue> NotImplemented<TValue>(string member)
        where TValue : VBTypedValue
        => RuntimeSemanticsEvaluationResult<TValue>.Error(VBRuntimeErrorInfo.For(
            VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError, default,
            $"'{member}' is declared but not implemented yet."));

    /// <summary>
    /// The number of characters in a string, or the number of bytes a value of another type occupies
    /// (<strong>MS-VBAL §6.1.2.11.1.22</strong>).
    /// </summary>
    /// <remarks>
    /// "With user-defined types, <c>Len</c> returns the size as it will be written to the file" — the
    /// concatenation of the members with no padding between them, which is what a <c>Put</c> statement writes
    /// and therefore what this has to agree with. <see cref="LenB"/> is the in-memory size instead.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Len(VBVariantValue expression)
        => Measure(expression, inMemory: false);

    /// <summary>
    /// The number of <em>bytes</em> a value occupies (<strong>MS-VBAL §6.1.2.11.1.22</strong>).
    /// </summary>
    /// <remarks>
    /// "<c>LenB</c> will return the same value as <c>Len</c>, except for strings or UDTs": a string's
    /// characters are two bytes each, and a UDT's "in-memory size, including any implementation-specific
    /// padding between elements" is what <see cref="VBUserDefinedTypeLayout"/> lays out.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> LenB(VBVariantValue expression)
        => Measure(expression, inMemory: true);

    // one measurement with one dial, because the specification defines LenB as Len "except for strings or
    // UDTs" - stating one function and its two exceptions rather than two functions.
    private static RuntimeSemanticsEvaluationResult<VBVariantValue> Measure(VBVariantValue expression, bool inMemory)
    {
        var value = Unwrapped(expression);

        // "If Expression contains the data value Null, Null is returned" - the one case that is not a number.
        if (value is VBNullValue)
        {
            return RuntimeSemanticsEvaluationResult<VBVariantValue>.Success(new VBVariantValue(VBNullValue.Null));
        }

        return Length(value, inMemory) is { } length
            ? RuntimeSemanticsEvaluationResult<VBVariantValue>.Success(new VBVariantValue(new VBLongValue(length)))
            : NotImplemented<VBVariantValue>(inMemory ? nameof(LenB) : nameof(Len));
    }

    private static int? Length(VBTypedValue value, bool inMemory) => value switch
    {
        // a fixed-length String is its declared length, whatever it currently holds.
        VBFixedStringValue fixedString => inMemory ? 2 * fixedString.Length : fixedString.Length,
        // "Returns a Long containing the number of characters in a string", and LenB "returns the number of
        // bytes used to represent that string" - two per character, VBA strings being Unicode.
        VBStringValue text => inMemory ? 2 * text.Length : text.Length,
        // the two UDT sizes: LenB's is the padded in-memory one the value reports, Len's is the unpadded
        // concatenation a record is written from.
        VBUserDefinedTypeValue udt => inMemory ? udt.Size : SerializedSize(udt),
        // "the number of bytes required to store a variable on the current platform", which for every scalar
        // is the size the value itself reports.
        VBEmptyValue => 0,
        _ => value.Size,
    };

    // Len of a UDT is "the size as it will be written to the file": the members' own file widths, added up,
    // with none of the padding LenB counts. A variable-length String member is the case MS-VBAL warns about -
    // "Len might not be able to determine the actual number of storage bytes required when used with
    // variable-length strings in user-defined data types" - and its file width is its characters.
    private static int? SerializedSize(VBUserDefinedTypeValue udt)
    {
        var total = 0;
        for (var index = 0; index < udt.Fields.Length; index++)
        {
            if (udt.FieldAt(index) is not { } field || Length(field, inMemory: false) is not { } width)
            {
                return null;
            }

            total += width;
        }

        return total;
    }

    // a Variant's own TypeInfo mirrors what it wraps while the instance stays a VBVariantValue, so measuring
    // one has to see the wrapped value - "if the variable name is a Variant, Len/LenB treats it the same as a
    // String and always returns the number of characters it contains".
    private static VBTypedValue Unwrapped(VBTypedValue value)
    {
        while (value is VBVariantValue { TypedValue: { } wrapped })
        {
            value = wrapped;
        }

        return value;
    }

    public RuntimeSemanticsEvaluationResult<VBIntegerValue> Asc(VBStringValue stringValue) => NotImplemented<VBIntegerValue>(nameof(Asc));

    public RuntimeSemanticsEvaluationResult<VBIntegerValue> AscB(VBStringValue stringValue) => NotImplemented<VBIntegerValue>(nameof(AscB));

    public RuntimeSemanticsEvaluationResult<VBIntegerValue> AscW(VBStringValue stringValue) => NotImplemented<VBIntegerValue>(nameof(AscW));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> Chr(VBLongValue charCode) => NotImplemented<VBVariantValue>(nameof(Chr));

    public RuntimeSemanticsEvaluationResult<VBStringValue> ChrString(VBLongValue charCode) => NotImplemented<VBStringValue>(nameof(ChrString));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> ChrB(VBLongValue charCode) => NotImplemented<VBVariantValue>(nameof(ChrB));

    public RuntimeSemanticsEvaluationResult<VBStringValue> ChrBString(VBLongValue charCode) => NotImplemented<VBStringValue>(nameof(ChrBString));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> ChrW(VBLongValue charCode) => NotImplemented<VBVariantValue>(nameof(ChrW));

    public RuntimeSemanticsEvaluationResult<VBStringValue> ChrWString(VBLongValue charCode) => NotImplemented<VBStringValue>(nameof(ChrWString));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> Filter( VBResizableArrayValue sourceArray, VBStringValue match, VBBooleanValue? include = default, VBCompareMethod compare = VBCompareMethod.VBBinaryCompare) => NotImplemented<VBVariantValue>(nameof(Filter));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> Format( VBVariantValue expression, VBVariantValue? format = default, VBDayOfWeek firstDayOfWeek = VBDayOfWeek.VBSunday, VBFirstWeekOfYear firstWeekOfYear = VBFirstWeekOfYear.VBFirstJan1) => NotImplemented<VBVariantValue>(nameof(Format));

    public RuntimeSemanticsEvaluationResult<VBStringValue> FormatString( VBVariantValue expression, VBVariantValue? format = default, VBDayOfWeek firstDayOfWeek = VBDayOfWeek.VBSunday, VBFirstWeekOfYear firstWeekOfYear = VBFirstWeekOfYear.VBFirstJan1) => NotImplemented<VBStringValue>(nameof(FormatString));

    public RuntimeSemanticsEvaluationResult<VBStringValue> FormatCurrency( VBVariantValue expression, VBLongValue? numDigitsAfterDecimal = default, VBTriState includeLeadingDigit = VBTriState.VBUseDefault, VBTriState useParensForNegativeNumbers = VBTriState.VBUseDefault, VBTriState groupDigits = VBTriState.VBUseDefault) => NotImplemented<VBStringValue>(nameof(FormatCurrency));

    public RuntimeSemanticsEvaluationResult<VBStringValue> FormatDateTime( VBVariantValue expression, VBDateTimeFormat namedFormat = VBDateTimeFormat.VBGeneralDate) => NotImplemented<VBStringValue>(nameof(FormatDateTime));

    public RuntimeSemanticsEvaluationResult<VBStringValue> FormatNumber( VBVariantValue expression, VBLongValue? numDigitsAfterDecimal = default, VBTriState includeLeadingDigit = VBTriState.VBUseDefault, VBTriState useParensForNegativeNumbers = VBTriState.VBUseDefault, VBTriState groupDigits = VBTriState.VBUseDefault) => NotImplemented<VBStringValue>(nameof(FormatNumber));

    public RuntimeSemanticsEvaluationResult<VBStringValue> FormatPercent( VBVariantValue expression, VBLongValue? numDigitsAfterDecimal = default, VBTriState includeLeadingDigit = VBTriState.VBUseDefault, VBTriState useParensForNegativeNumbers = VBTriState.VBUseDefault, VBTriState groupDigits = VBTriState.VBUseDefault) => NotImplemented<VBStringValue>(nameof(FormatPercent));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> InStr( VBVariantValue? arg1 = default, VBVariantValue? arg2 = default, VBVariantValue? arg3 = default, VBCompareMethod compare = VBCompareMethod.VBBinaryCompare) => NotImplemented<VBVariantValue>(nameof(InStr));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> InStrB( VBVariantValue? arg1 = default, VBVariantValue? arg2 = default, VBVariantValue? arg3 = default, VBCompareMethod compare = VBCompareMethod.VBBinaryCompare) => NotImplemented<VBVariantValue>(nameof(InStrB));

    public RuntimeSemanticsEvaluationResult<VBLongValue> InStrRev( VBStringValue stringCheck, VBStringValue stringMatch, VBLongValue? start = default, VBCompareMethod compare = VBCompareMethod.VBBinaryCompare) => NotImplemented<VBLongValue>(nameof(InStrRev));

    public RuntimeSemanticsEvaluationResult<VBStringValue> Join(VBResizableArrayValue sourceArray, VBVariantValue? delimiter = default) => NotImplemented<VBStringValue>(nameof(Join));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> LCase(VBVariantValue @string) => NotImplemented<VBVariantValue>(nameof(LCase));

    public RuntimeSemanticsEvaluationResult<VBStringValue> LCaseString(VBVariantValue @string) => NotImplemented<VBStringValue>(nameof(LCaseString));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> Left(VBVariantValue @string, VBLongValue length) => NotImplemented<VBVariantValue>(nameof(Left));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> LeftB(VBVariantValue @string, VBLongValue length) => NotImplemented<VBVariantValue>(nameof(LeftB));

    public RuntimeSemanticsEvaluationResult<VBStringValue> LeftString(VBVariantValue @string, VBLongValue length) => NotImplemented<VBStringValue>(nameof(LeftString));

    public RuntimeSemanticsEvaluationResult<VBStringValue> LeftBString(VBVariantValue @string, VBLongValue length) => NotImplemented<VBStringValue>(nameof(LeftBString));



    public RuntimeSemanticsEvaluationResult<VBVariantValue> LTrim(VBVariantValue @string) => NotImplemented<VBVariantValue>(nameof(LTrim));

    public RuntimeSemanticsEvaluationResult<VBStringValue> LTrimString(VBVariantValue @string) => NotImplemented<VBStringValue>(nameof(LTrimString));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> RTrim(VBVariantValue @string) => NotImplemented<VBVariantValue>(nameof(RTrim));

    public RuntimeSemanticsEvaluationResult<VBStringValue> RTrimString(VBVariantValue @string) => NotImplemented<VBStringValue>(nameof(RTrimString));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> Trim(VBVariantValue @string) => NotImplemented<VBVariantValue>(nameof(Trim));

    public RuntimeSemanticsEvaluationResult<VBStringValue> TrimString(VBVariantValue @string) => NotImplemented<VBStringValue>(nameof(TrimString));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> Mid(VBVariantValue @string, VBLongValue start, VBVariantValue? length = default) => NotImplemented<VBVariantValue>(nameof(Mid));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> MidB(VBVariantValue @string, VBLongValue start, VBVariantValue? length = default) => NotImplemented<VBVariantValue>(nameof(MidB));

    public RuntimeSemanticsEvaluationResult<VBStringValue> MidString(VBVariantValue @string, VBLongValue start, VBVariantValue? length = default) => NotImplemented<VBStringValue>(nameof(MidString));

    public RuntimeSemanticsEvaluationResult<VBStringValue> MidBString(VBVariantValue @string, VBLongValue start, VBVariantValue? length = default) => NotImplemented<VBStringValue>(nameof(MidBString));

    public RuntimeSemanticsEvaluationResult<VBStringValue> MonthName(VBLongValue month, VBBooleanValue? abbreviate = default) => NotImplemented<VBStringValue>(nameof(MonthName));

    public RuntimeSemanticsEvaluationResult<VBStringValue> Replace( VBStringValue expression, VBStringValue find, VBStringValue replace, VBLongValue? start = default, VBLongValue? count = default, VBCompareMethod compare = VBCompareMethod.VBBinaryCompare) => NotImplemented<VBStringValue>(nameof(Replace));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> Right(VBVariantValue @string, VBLongValue length) => NotImplemented<VBVariantValue>(nameof(Right));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> RightB(VBVariantValue @string, VBLongValue length) => NotImplemented<VBVariantValue>(nameof(RightB));

    public RuntimeSemanticsEvaluationResult<VBStringValue> RightString(VBVariantValue @string, VBLongValue length) => NotImplemented<VBStringValue>(nameof(RightString));

    public RuntimeSemanticsEvaluationResult<VBStringValue> RightBString(VBVariantValue @string, VBLongValue length) => NotImplemented<VBStringValue>(nameof(RightBString));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> Space(VBLongValue number) => NotImplemented<VBVariantValue>(nameof(Space));

    public RuntimeSemanticsEvaluationResult<VBStringValue> SpaceString(VBLongValue number) => NotImplemented<VBStringValue>(nameof(SpaceString));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> Split( VBStringValue expression, VBVariantValue? delimiter = default, VBLongValue? limit = default, VBCompareMethod compare = VBCompareMethod.VBBinaryCompare) => NotImplemented<VBVariantValue>(nameof(Split));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> StrComp( VBVariantValue string1, VBVariantValue string2, VBCompareMethod compare = VBCompareMethod.VBBinaryCompare) => NotImplemented<VBVariantValue>(nameof(StrComp));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> StrConv(VBVariantValue @string, VBStrConv conversion, VBLongValue localeID) => NotImplemented<VBVariantValue>(nameof(StrConv));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> Repeat(VBLongValue number, VBVariantValue character) => NotImplemented<VBVariantValue>(nameof(Repeat));

    public RuntimeSemanticsEvaluationResult<VBStringValue> RepeatString(VBLongValue number, VBVariantValue character) => NotImplemented<VBStringValue>(nameof(RepeatString));

    public RuntimeSemanticsEvaluationResult<VBStringValue> StrReverse(VBStringValue expression) => NotImplemented<VBStringValue>(nameof(StrReverse));

    public RuntimeSemanticsEvaluationResult<VBVariantValue> UCase(VBVariantValue @string) => NotImplemented<VBVariantValue>(nameof(UCase));

    public RuntimeSemanticsEvaluationResult<VBStringValue> UCaseString(VBVariantValue @string) => NotImplemented<VBStringValue>(nameof(UCaseString));

    public RuntimeSemanticsEvaluationResult<VBStringValue> WeekdayName( VBLongValue weekday, VBBooleanValue? abbreviate = default, VBDayOfWeek firstDayOfWeek = VBDayOfWeek.VBUseSystemDayOfWeek) => NotImplemented<VBStringValue>(nameof(WeekdayName));

}
