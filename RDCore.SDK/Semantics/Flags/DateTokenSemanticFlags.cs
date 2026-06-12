namespace RDCore.SDK.Semantics.Flags;

[Flags]
/// <summary>
/// The semantic flags associated with <c>DateTokenParsingSemanticOperation</c>.
/// </summary>
public enum DateTokenSemanticFlags
{
    /// <summary>
    /// The <em>date token</em> specifies a <em>time value</em>.
    /// </summary>
    TimeValueSpecified = 1 << 0,
    /// <summary>
    /// The <em>date token</em> specifies a <em>date value</em>.
    /// </summary>
    /// <remarks>
    /// The implicit <em>date value</em> is then interpreted as "1899/12/30", or <c>VBDateType.Zero</c>
    /// </remarks>
    DateValueSpecified = 1 << 1,
    /// <summary>
    /// The <em>date token</em> specifies a <em>month name</em> in its <c>left-date-value</c>.
    /// </summary>
    MonthNameLeft = 1 << 2,
    /// <summary>
    /// The <em>date token</em> specifies a <em>month name</em> in its <c>middle-date-value</c>.
    /// </summary>
    MonthNameMiddle = 1 << 3,
    /// <summary>
    /// The <em>date token</em> specifies a <em>month name</em> in its <c>right-date-value</c>.
    /// </summary>
    MonthNameRight = 1 << 4,
    /// <summary>
    /// The <em>date token</em> specifies the year in its <c>left-date-value</c>.
    /// </summary>
    YearLeft = 1 << 5,
    /// <summary>
    /// The <em>date token</em> specifies the year in its <c>right-date-value</c>.
    /// </summary>
    YearRight = 1 << 6,
    /// <summary>
    /// The <em>date token</em> specifies the month in its <c>left-date-value</c>.
    /// </summary>
    MonthLeft = 1 << 7,
    /// <summary>
    /// The <em>date token</em> specifies the month in its <c>middle-date-value</c>.
    /// </summary>
    MonthMiddle = 1 << 8,
    /// <summary>
    /// The <em>date token</em> specifies the month in its <c>right-date-value</c>.
    /// </summary>
    MonthRight = 1 << 9,
    /// <summary>
    /// The <em>date token</em> specifies the day in its <c>left-date-value</c>.
    /// </summary>
    DayLeft = 1 << 10,
    /// <summary>
    /// The <em>date token</em> specifies the day in its <c>middle-date-value</c>.
    /// </summary>
    DayMiddle = 1 << 11,
    /// <summary>
    /// The <em>date token</em> specifies the day in its <c>right-date-value</c>.
    /// </summary>
    DayRight = 1 << 12,
    /// <summary>
    /// The <em>date token</em> specifies an <c>ampm</c> (AM/PM) element in long form "am/pm".
    /// </summary>
    AmPmLong = 1 << 13,
    /// <summary>
    /// The <em>date token</em> specifies an <c>ampm</c> (AM/PM) element in short form "a/p".
    /// </summary>
    AmPmShort = 1 << 14,
    /// <summary>
    /// The <em>date token</em> specifies a <em>minutes-value</em> in its <c>time-value</c> element.
    /// </summary>
    MinutesValue = 1 << 15,
    /// <summary>
    /// The <em>date token</em> specifies a <em>seconds-value</em> in its <c>time-value</c> element.
    /// </summary>
    SecondsValue = 1 << 16,

    All = TimeValueSpecified | DateValueSpecified 
        | YearLeft | YearRight | MonthNameLeft | MonthNameMiddle | MonthNameRight | DayLeft 
        | DayMiddle | DayRight | AmPmLong | AmPmShort | MinutesValue | SecondsValue
}
