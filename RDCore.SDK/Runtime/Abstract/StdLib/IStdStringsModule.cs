using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.2.11 Strings Module</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the standard library <c>VBA.Strings</c> module.
/// <para>
/// Three families of member recur here, and the declarations are shaped to keep them apart. A
/// <c>$</c>-suffixed function is a member of its own, identical to the unsuffixed one but returning
/// <c>String</c> rather than <c>Variant</c>; the two differ in nothing else, so C# cannot overload them
/// and each states its VBA name with <see cref="StdLibMemberAttribute"/>. A <c>B</c>-suffixed function
/// counts in <em>bytes</em> rather than in characters. And <c>Compare As VbCompareMethod</c> recurs
/// wherever a comparison happens, defaulting to <c>vbBinaryCompare</c> as the specification has it —
/// <em>not</em> to the calling module's own <c>Option Compare</c>, which these functions do not consult.
/// </para>
/// </remarks>
[StdLibModule]
public interface IStdStringsModule
{
    #region 6.1.2.11.1 StdStrings: Public Functions

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.1 Asc</strong> Gets the character code of the first character of a string.
    /// </summary>
    /// <param name="stringValue">The string whose first character to report the code of.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBIntegerValue> Asc(VBStringValue stringValue);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.2 AscB</strong> Gets the value of the first <em>byte</em> of a string, rather than of its first character.
    /// </summary>
    /// <param name="stringValue">The string whose first byte to report the value of.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBIntegerValue> AscB(VBStringValue stringValue);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.3 AscW</strong> Gets the <em>Unicode</em> code point of the first character of a string.
    /// </summary>
    /// <param name="stringValue">The string whose first character to report the code point of.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBIntegerValue> AscW(VBStringValue stringValue);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.4 Chr</strong> Gets the character a character code names.
    /// </summary>
    /// <param name="charCode">The character code.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Chr(VBLongValue charCode);

    /// <inheritdoc cref="Chr"/>
    [StdLibMember("Chr$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> ChrString(VBLongValue charCode);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.5 ChrB</strong> Gets a single-<em>byte</em> string holding the given value, rather than the character that value names.
    /// </summary>
    /// <param name="charCode">The byte value.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> ChrB(VBLongValue charCode);

    /// <inheritdoc cref="ChrB"/>
    [StdLibMember("ChrB$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> ChrBString(VBLongValue charCode);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.6 ChrW</strong> Gets the character a <em>Unicode</em> code point names.
    /// </summary>
    /// <param name="charCode">The Unicode code point.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> ChrW(VBLongValue charCode);

    /// <inheritdoc cref="ChrW"/>
    [StdLibMember("ChrW$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> ChrWString(VBLongValue charCode);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.7 Filter</strong> Gets a zero-based array of the elements of an array that match, or that do not match, a substring.
    /// </summary>
    /// <param name="sourceArray">The one-dimensional array of strings to search.</param>
    /// <param name="match">The substring to search for.</param>
    /// <param name="include"><c>True</c> to return the elements containing <paramref name="match"/>, <c>False</c> to return the ones that do not.</param>
    /// <param name="compare">The string comparison to use.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Filter(
        VBResizableArrayValue sourceArray, VBStringValue match,
        VBBooleanValue? include = default, VBCompareMethod compare = VBCompareMethod.VBBinaryCompare);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.8 Format</strong> Gets a string formatted according to a format expression.
    /// </summary>
    /// <param name="expression">Any valid expression.</param>
    /// <param name="format">A valid named or user-defined format expression.</param>
    /// <param name="firstDayOfWeek">The day the week starts on.</param>
    /// <param name="firstWeekOfYear">The week the year starts on.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Format(
        VBVariantValue expression, VBVariantValue? format = default,
        VBDayOfWeek firstDayOfWeek = VBDayOfWeek.VBSunday,
        VBFirstWeekOfYear firstWeekOfYear = VBFirstWeekOfYear.VBFirstJan1);

    /// <inheritdoc cref="Format"/>
    [StdLibMember("Format$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> FormatString(
        VBVariantValue expression, VBVariantValue? format = default,
        VBDayOfWeek firstDayOfWeek = VBDayOfWeek.VBSunday,
        VBFirstWeekOfYear firstWeekOfYear = VBFirstWeekOfYear.VBFirstJan1);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.10 FormatCurrency</strong> Gets a string formatted as a currency value, with the currency symbol of the environment's own regional settings.
    /// </summary>
    /// <param name="expression">The expression to format.</param>
    /// <param name="numDigitsAfterDecimal">How many places to the right of the decimal separator to show. <c>-1</c> uses the environment's regional settings.</param>
    /// <param name="includeLeadingDigit">Whether a leading zero is shown for a fractional value.</param>
    /// <param name="useParensForNegativeNumbers">Whether a negative value is parenthesized.</param>
    /// <param name="groupDigits">Whether digits are grouped with the environment's own group separator.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBStringValue> FormatCurrency(
        VBVariantValue expression, VBLongValue? numDigitsAfterDecimal = default,
        VBTriState includeLeadingDigit = VBTriState.VBUseDefault,
        VBTriState useParensForNegativeNumbers = VBTriState.VBUseDefault,
        VBTriState groupDigits = VBTriState.VBUseDefault);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.11 FormatDateTime</strong> Gets a string formatted as a date, a time, or both.
    /// </summary>
    /// <param name="expression">The date expression to format.</param>
    /// <param name="namedFormat">Which of the named date/time formats to use.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBStringValue> FormatDateTime(
        VBVariantValue expression, VBDateTimeFormat namedFormat = VBDateTimeFormat.VBGeneralDate);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.12 FormatNumber</strong> Gets a string formatted as a number.
    /// </summary>
    /// <param name="expression">The expression to format.</param>
    /// <param name="numDigitsAfterDecimal">How many places to the right of the decimal separator to show. <c>-1</c> uses the environment's regional settings.</param>
    /// <param name="includeLeadingDigit">Whether a leading zero is shown for a fractional value.</param>
    /// <param name="useParensForNegativeNumbers">Whether a negative value is parenthesized.</param>
    /// <param name="groupDigits">Whether digits are grouped with the environment's own group separator.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBStringValue> FormatNumber(
        VBVariantValue expression, VBLongValue? numDigitsAfterDecimal = default,
        VBTriState includeLeadingDigit = VBTriState.VBUseDefault,
        VBTriState useParensForNegativeNumbers = VBTriState.VBUseDefault,
        VBTriState groupDigits = VBTriState.VBUseDefault);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.13 FormatPercent</strong> Gets a string formatted as a percentage — the expression multiplied by 100, with a trailing <c>%</c>.
    /// </summary>
    /// <param name="expression">The expression to format.</param>
    /// <param name="numDigitsAfterDecimal">How many places to the right of the decimal separator to show. <c>-1</c> uses the environment's regional settings.</param>
    /// <param name="includeLeadingDigit">Whether a leading zero is shown for a fractional value.</param>
    /// <param name="useParensForNegativeNumbers">Whether a negative value is parenthesized.</param>
    /// <param name="groupDigits">Whether digits are grouped with the environment's own group separator.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBStringValue> FormatPercent(
        VBVariantValue expression, VBLongValue? numDigitsAfterDecimal = default,
        VBTriState includeLeadingDigit = VBTriState.VBUseDefault,
        VBTriState useParensForNegativeNumbers = VBTriState.VBUseDefault,
        VBTriState groupDigits = VBTriState.VBUseDefault);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.14 InStr</strong> Gets the position of the first occurrence of one string within another, or <c>0</c> when there is none.
    /// </summary>
    /// <remarks>
    /// 👉 Its parameters are named for their position rather than for what they mean, because what they
    /// mean depends on how many are supplied: with <paramref name="arg3"/> absent,
    /// <paramref name="arg1"/> is the string searched and <paramref name="arg2"/> the pattern, from
    /// position 1; with it present, <paramref name="arg1"/> is the start position,
    /// <paramref name="arg2"/> the string searched and <paramref name="arg3"/> the pattern.
    /// </remarks>
    /// <param name="arg1">The start position, or the string to search.</param>
    /// <param name="arg2">The string to search, or the pattern to search for.</param>
    /// <param name="arg3">The pattern to search for, when a start position was given.</param>
    /// <param name="compare">The string comparison to use.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> InStr(
        VBVariantValue? arg1 = default, VBVariantValue? arg2 = default, VBVariantValue? arg3 = default,
        VBCompareMethod compare = VBCompareMethod.VBBinaryCompare);

    /// <inheritdoc cref="InStr"/>
    RuntimeSemanticsEvaluationResult<VBVariantValue> InStrB(
        VBVariantValue? arg1 = default, VBVariantValue? arg2 = default, VBVariantValue? arg3 = default,
        VBCompareMethod compare = VBCompareMethod.VBBinaryCompare);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.15 InStrRev</strong> Gets the position of the <em>last</em> occurrence of one string within another, or <c>0</c> when there is none.
    /// </summary>
    /// <param name="stringCheck">The string to search.</param>
    /// <param name="stringMatch">The pattern to search for.</param>
    /// <param name="start">The position to search back from. <c>-1</c> searches from the last character.</param>
    /// <param name="compare">The string comparison to use.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBLongValue> InStrRev(
        VBStringValue stringCheck, VBStringValue stringMatch, VBLongValue? start = default,
        VBCompareMethod compare = VBCompareMethod.VBBinaryCompare);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.16 Join</strong> Gets a string of every element of an array, separated by a delimiter.
    /// </summary>
    /// <param name="sourceArray">The one-dimensional array whose elements to join.</param>
    /// <param name="delimiter">What to separate the elements with. A single space when unspecified; a zero-length string joins them with nothing between.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBStringValue> Join(VBResizableArrayValue sourceArray, VBVariantValue? delimiter = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.17 LCase</strong> Gets a string with every uppercase letter cased down.
    /// </summary>
    /// <param name="string">The string to convert.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> LCase(VBVariantValue @string);

    /// <inheritdoc cref="LCase"/>
    [StdLibMember("LCase$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> LCaseString(VBVariantValue @string);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.19 Left</strong> Gets a given number of characters from the start of a string.
    /// </summary>
    /// <param name="string">The string to take characters from.</param>
    /// <param name="length">How many characters to take. More than the string holds returns the whole string.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Left(VBVariantValue @string, VBLongValue length);

    /// <inheritdoc cref="Left"/>
    RuntimeSemanticsEvaluationResult<VBVariantValue> LeftB(VBVariantValue @string, VBLongValue length);

    /// <inheritdoc cref="Left"/>
    [StdLibMember("Left$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> LeftString(VBVariantValue @string, VBLongValue length);

    /// <inheritdoc cref="LeftB"/>
    [StdLibMember("LeftB$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> LeftBString(VBVariantValue @string, VBLongValue length);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.22 Len</strong> Gets the number of characters in a string, or the number of bytes a value of some other type occupies.
    /// </summary>
    /// <param name="expression">The string, or a variable of any type.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Len(VBVariantValue expression);

    /// <inheritdoc cref="Len"/>
    RuntimeSemanticsEvaluationResult<VBVariantValue> LenB(VBVariantValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.23 LTrim</strong> Gets a string with its <em>leading</em> spaces removed.
    /// </summary>
    /// <param name="string">The string to trim.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> LTrim(VBVariantValue @string);

    /// <inheritdoc cref="LTrim"/>
    [StdLibMember("LTrim$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> LTrimString(VBVariantValue @string);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.23 RTrim</strong> Gets a string with its <em>trailing</em> spaces removed.
    /// </summary>
    /// <param name="string">The string to trim.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> RTrim(VBVariantValue @string);

    /// <inheritdoc cref="RTrim"/>
    [StdLibMember("RTrim$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> RTrimString(VBVariantValue @string);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.23 Trim</strong> Gets a string with both its leading and its trailing spaces removed.
    /// </summary>
    /// <param name="string">The string to trim.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Trim(VBVariantValue @string);

    /// <inheritdoc cref="Trim"/>
    [StdLibMember("Trim$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> TrimString(VBVariantValue @string);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.25 Mid</strong> Gets a given number of characters of a string, from a given position.
    /// </summary>
    /// <param name="string">The string to take characters from.</param>
    /// <param name="start">The 1-based position of the first character to take.</param>
    /// <param name="length">How many characters to take. Everything from <paramref name="start"/> to the end of the string when unspecified.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Mid(VBVariantValue @string, VBLongValue start, VBVariantValue? length = default);

    /// <inheritdoc cref="Mid"/>
    RuntimeSemanticsEvaluationResult<VBVariantValue> MidB(VBVariantValue @string, VBLongValue start, VBVariantValue? length = default);

    /// <inheritdoc cref="Mid"/>
    [StdLibMember("Mid$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> MidString(VBVariantValue @string, VBLongValue start, VBVariantValue? length = default);

    /// <inheritdoc cref="MidB"/>
    [StdLibMember("MidB$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> MidBString(VBVariantValue @string, VBLongValue start, VBVariantValue? length = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.28 MonthName</strong> Gets the name of a month, in the environment's own regional settings.
    /// </summary>
    /// <param name="month">The 1-based number of the month.</param>
    /// <param name="abbreviate"><c>True</c> for the abbreviated name.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBStringValue> MonthName(VBLongValue month, VBBooleanValue? abbreviate = default);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.29 Replace</strong> Gets a string with a number of occurrences of one substring replaced by another.
    /// </summary>
    /// <param name="expression">The string to search.</param>
    /// <param name="find">The substring to search for.</param>
    /// <param name="replace">What to replace it with.</param>
    /// <param name="start">The 1-based position to start at. The returned string begins there, not at the start of <paramref name="expression"/>.</param>
    /// <param name="count">How many occurrences to replace. <c>-1</c> replaces every one of them.</param>
    /// <param name="compare">The string comparison to use.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBStringValue> Replace(
        VBStringValue expression, VBStringValue find, VBStringValue replace,
        VBLongValue? start = default, VBLongValue? count = default,
        VBCompareMethod compare = VBCompareMethod.VBBinaryCompare);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.30 Right</strong> Gets a given number of characters from the <em>end</em> of a string.
    /// </summary>
    /// <param name="string">The string to take characters from.</param>
    /// <param name="length">How many characters to take. More than the string holds returns the whole string.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Right(VBVariantValue @string, VBLongValue length);

    /// <inheritdoc cref="Right"/>
    RuntimeSemanticsEvaluationResult<VBVariantValue> RightB(VBVariantValue @string, VBLongValue length);

    /// <inheritdoc cref="Right"/>
    [StdLibMember("Right$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> RightString(VBVariantValue @string, VBLongValue length);

    /// <inheritdoc cref="RightB"/>
    [StdLibMember("RightB$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> RightBString(VBVariantValue @string, VBLongValue length);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.33 Space</strong> Gets a string of a given number of spaces.
    /// </summary>
    /// <param name="number">How many spaces.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Space(VBLongValue number);

    /// <inheritdoc cref="Space"/>
    [StdLibMember("Space$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> SpaceString(VBLongValue number);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.35 Split</strong> Gets a zero-based array of the substrings of a string, split on a delimiter.
    /// </summary>
    /// <param name="expression">The string to split. A zero-length string yields an array with no elements at all.</param>
    /// <param name="delimiter">What to split on. A single space when unspecified.</param>
    /// <param name="limit">How many substrings to return at most. <c>-1</c> returns all of them.</param>
    /// <param name="compare">The string comparison to use.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Split(
        VBStringValue expression, VBVariantValue? delimiter = default, VBLongValue? limit = default,
        VBCompareMethod compare = VBCompareMethod.VBBinaryCompare);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.36 StrComp</strong> Gets <c>-1</c>, <c>0</c> or <c>1</c> as one string sorts before, with, or after another.
    /// </summary>
    /// <param name="string1">The first string.</param>
    /// <param name="string2">The second string.</param>
    /// <param name="compare">The string comparison to use.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> StrComp(
        VBVariantValue string1, VBVariantValue string2,
        VBCompareMethod compare = VBCompareMethod.VBBinaryCompare);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.37 StrConv</strong> Gets a string converted as a <see cref="VBStrConv"/> conversion says — cased, widened, narrowed, or transliterated.
    /// </summary>
    /// <param name="string">The string to convert.</param>
    /// <param name="conversion">Which conversion to apply.</param>
    /// <param name="localeID">The locale to convert for.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> StrConv(VBVariantValue @string, VBStrConv conversion, VBLongValue localeID);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.38 String</strong> Gets a string of a given character, repeated a given number of times.
    /// </summary>
    /// <remarks>
    /// 👉 Its VBA name is <c>String</c>, which is also a reserved type name — so it is stated here rather
    /// than taken from this method's own, which cannot be it.
    /// </remarks>
    /// <param name="number">How many times to repeat the character.</param>
    /// <param name="character">The character to repeat, as a character code or as a string whose first character is used.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember("String")]
    RuntimeSemanticsEvaluationResult<VBVariantValue> Repeat(VBLongValue number, VBVariantValue character);

    /// <inheritdoc cref="Repeat"/>
    [StdLibMember("String$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> RepeatString(VBLongValue number, VBVariantValue character);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.40 StrReverse</strong> Gets a string with its characters in reverse order.
    /// </summary>
    /// <param name="expression">The string to reverse.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBStringValue> StrReverse(VBStringValue expression);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.41 UCase</strong> Gets a string with every lowercase letter cased up.
    /// </summary>
    /// <param name="string">The string to convert.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> UCase(VBVariantValue @string);

    /// <inheritdoc cref="UCase"/>
    [StdLibMember("UCase$")]
    RuntimeSemanticsEvaluationResult<VBStringValue> UCaseString(VBVariantValue @string);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.11.1.43 WeekdayName</strong> Gets the name of a day of the week, in the environment's own regional settings.
    /// </summary>
    /// <param name="weekday">The number of the day within the week, counted from <paramref name="firstDayOfWeek"/>.</param>
    /// <param name="abbreviate"><c>True</c> for the abbreviated name.</param>
    /// <param name="firstDayOfWeek">The day the week starts on.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBStringValue> WeekdayName(
        VBLongValue weekday, VBBooleanValue? abbreviate = default,
        VBDayOfWeek firstDayOfWeek = VBDayOfWeek.VBUseSystemDayOfWeek);

    #endregion
}
