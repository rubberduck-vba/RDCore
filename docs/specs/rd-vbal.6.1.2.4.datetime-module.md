# 6.1.2.4 DateTime Module

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.4** DateTime Module](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ccbe147a-4f63-4f72-8404-b4e5b1ad4427).

The `DateTime` module is represented in the SDK by the interface
[IStdDateTimeModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdDateTimeModule.html), and implemented by
[StdDateTime](../api/RDCore.Runtime.StdLib.StdDateTime.html).

A function the specification declares with no `As` clause returns a `Variant`, which holds the type its description says: `DateDiff` a `Long`, `DatePart`
and the functions of a part of a date an `Integer`, and `DateAdd`, `DateSerial`, `DateValue`, `TimeSerial` and `TimeValue` a `Date`.

## The Session Is the System

The properties read the clock of the session ([ISessionClock](../api/RDCore.SDK.Runtime.Abstract.Execution.ISessionClock.html)), and `Calendar` is its
state too. The session is the program's *system*: assigning `Time` ("Sets the system time") sets the time of the session's clock, which goes on from there as
the machine's does; the machine's own clock is never changed. An `End` resets both, to the machine's clock and the Gregorian calendar.

`Now`, `Date` and `Time` are to the second; `Timer` has the fraction of a second the clock has ("The sub-second resolution is implementation dependent").
`Date$` is `mm-dd-yyyy`, in the current calendar, and `Time$` is `hh:mm:ss`.

## Dates

A `Date` is the serial number of OLE Automation: days since December 30, 1899, with the time as the fraction of a day, whose sign is the date's even before
1899 (−1.25 is December 29, 1899 at 6:00). The arithmetic is done on a line of time and turned back into a serial, which is how an hour added to a date before
1899 goes forward. The range of dates is January 1, 100 to December 31, 9999.

The parts of a date are those of the date **rounded to the nearest second**, as MS-VBA has them: `Second(#0:00:00.6#)` is `1`.

The year, the month, the week and the day are in the current calendar: Gregorian, or Hijri (the tabular, "Kuwaiti" calendar of `System.Globalization.HijriCalendar`)
when `Calendar` is `vbCalHijri`. Hours, minutes and seconds are the same in both. A date before the first day of the Hijri calendar (July 18, 622) has no Hijri
year, month or day.

## Errors

**MS-VBAL §6.1.2.4** names error 5 for an interval that is none and for a `DateAdd` before the year 100, and says only that "an error occurs" elsewhere, so
the rest is decided here:

|When|Error|
|---|---|
|An interval that is none of `yyyy`, `q`, `m`, `y`, `d`, `w`, `ww`, `h`, `n`, `s` (in any case)|`5`|
|A result outside the range of dates (`DateAdd`, `DateSerial`, `TimeSerial`)|`5`|
|A date before the first day of the current calendar|`5`|
|A first day of the week, a first week of the year or a calendar that is no value of its enumeration|`5`|
|A `DateDiff` that does not fit a `Long` (seconds across the centuries)|`6`|
|A `Null` date given to `DateAdd`, `DateDiff` or `DatePart`|`94`: it is let-coerced like any other value; only the functions of a part of a date say `Null` returns `Null`|
|A string that is no date (`DateValue`, `TimeValue`, `Time = `)|`13`, the error of its let-coercion|

## What the Specification Leaves Open

- **`Weekday`** counts from `FirstDayOfWeek` as 1, as MS-VBA does: `Weekday(d, vbMonday)` of a Monday is `1`. The specification's table lists the constants
  with Sunday as 1, which is the count for the default `vbSunday`.
- **`DateDiff`**: a weekday (`"w"`) is counted on the day of the week of `Date1`, and a calendar week (`"ww"`) on `FirstDayOfWeek`; both count `Date2` and
  not `Date1`, as the specification says. `FirstWeekOfYear` is validated and has no effect: no interval of `DateDiff` counts weeks of the year.
- **`DatePart("ww")`** follows `FirstWeekOfYear`: under `vbFirstFourDays` and `vbFirstFullWeek`, a day in the first days of January can be in the last week
  of the year before (January 1, 2021 is week 53 with `vbMonday, vbFirstFourDays`).
- **`DateAdd`** rounds `Number` to the nearest whole number half to even, as the language rounds; `"w"` adds days, like `"d"` and `"y"`.
- **`TimeValue`** is declared to take a `String`, and also said to return `Null` of `Null`: a `Null` cannot be passed to a `String` parameter, and is error 94.
- **`Date`** has no `Property Let` in the specification. MS-VBA's `Date` statement (which sets the system date) is not part of it: `Date = x` is error 451.
- A **string** given to `DateValue` or `TimeValue` is read by the let-coercion to `Date` (**MS-VBAL §5.5.1.2.4**), in the Gregorian calendar whatever
  `Calendar` is.

## 6.1.2.4.1 Public Functions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.4.1** Public Functions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e6a03763-6801-405f-a55c-9b3c9f27bc7a).

|§|Member|Notes|
|---|---|---|
|6.1.2.4.1.1|[DateAdd](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4fd0361f-289b-4395-9623-beae29e39ffc)|One month after January 31 is the last day of February.|
|6.1.2.4.1.2|[DateDiff](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/0c1b8433-c74f-4f23-8517-098256b0abf1)|Counts the boundaries of the interval: December 31 to January 1 is a year.|
|6.1.2.4.1.3|[DatePart](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/8936e45a-9902-42c3-a905-7c24b3e53d5d)||
|6.1.2.4.1.4|[DateSerial](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/051aa28f-0ea7-40a5-95ab-8428ac682826)|A two-digit year: 0-29 are 2000-2029, 30-99 are 1930-1999; Hijri 0-99 are 1400-1499.|
|6.1.2.4.1.5|[DateValue](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e61ba5e5-f7c1-4799-9c19-9f33d924fe2f)||
|6.1.2.4.1.6|[Day](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/b257b1de-accf-465a-beb4-483316d45ab4)|`Null` of `Null`.|
|6.1.2.4.1.7|[Hour](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/dea063c7-3357-4c06-abbe-2f22d16a1aca)|`Null` of `Null`.|
|6.1.2.4.1.8|[Minute](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/373bd4db-cb63-4c55-95b6-304b8e5841bc)|`Null` of `Null`.|
|6.1.2.4.1.9|[Month](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/797ca6f3-d6d4-46fa-84d2-0964c713ecf4)|`Null` of `Null`.|
|6.1.2.4.1.10|[Second](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/61c09f9c-9e66-4727-808c-364339437bf8)|`Null` of `Null`.|
|6.1.2.4.1.11|[TimeSerial](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3f2478d7-8abf-4489-b89e-e56eca57d047)|Before midnight is the day before: `TimeSerial(-1, 0, 0)` is 23:00 on December 29, 1899.|
|6.1.2.4.1.12|[TimeValue](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/57993b1f-63b9-4656-988d-f094dfdea777)||
|6.1.2.4.1.13|[Weekday](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/305c3db1-07ba-4e5e-8782-300a604712cb)|Counted from `FirstDayOfWeek` as 1; `Null` of `Null`.|
|6.1.2.4.1.14|[Year](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/79964d2e-97f8-41fd-ad5e-6ac5e4702d5c)|`Null` of `Null`.|


## 6.1.2.4.2 Public Properties

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.4.2** Public Properties](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f934f430-8fe8-4ded-a778-aadbca928b31).

|§|Member|Notes|
|---|---|---|
|6.1.2.4.2.1|[Calendar](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e6436600-0b0d-4932-9b4a-c8999a6628b0)|State of the session; Gregorian until set.|
|6.1.2.4.2.2|[Date/Date$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/53387d55-dd1b-49f2-a2a1-75170476eddc)|`Date` is a name in an expression (**MS-VBAL §3.3.5.2** *reserved-name*), and a type only in a declaration.|
|6.1.2.4.2.3|[Now](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/bb44575f-2504-42af-b39d-9a321cd5e354)||
|6.1.2.4.2.4|[Time/Time$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/bcea9c16-8334-4c1b-8467-aaf016119849)|`Time = ` sets the time of the session's clock, never the machine's.|
|6.1.2.4.2.5|[Timer](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/1b430e82-b37b-474b-83db-5f2303104090)||

---
> ⏮️ [**RD-VBAL §6.1.2.3** Conversion Module](rd-vbal.6.1.2.3.conversion-module.md) | ⏭️ [**RD-VBAL §6.1.2.5** FileSystem](rd-vbal.6.1.2.5.filesystem.md)
