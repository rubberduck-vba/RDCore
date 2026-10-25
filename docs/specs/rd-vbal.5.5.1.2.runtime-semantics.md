# 5.5.1.2 Runtime semantics

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.5.1.2** Runtime semantics](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3e5fb49f-eb20-4562-a6bd-4a26dc5fa733).

_Let-coercion_ is the implicit conversion applied to an operand, or to an assignment RHS, so that its value fits a
required _destination declared type_.

In the operator evaluation pipeline, the validation step let-coerces all non-null operands
(non-[VBNullValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBNullValue.html)) to the determined _effective type_ of
the operation ([**RD-VBAL §5.6.9.2** Simple Data Operators](rd-vbal.5.6.9.2.simple-data-operators.md)).


## Let-coercion provider

Let-coercion is driven by a let-coercion _provider_, `LetCoercionRuntimeSemanticsProvider`. The provider uses
provider/strategy dispatch: it dispatches to a per-destination-type let-coercion _strategy_, selected by the
destination type.

`LetCoercionRuntimeSemanticsProvider.EvaluateLetCoercionSemantics` evaluates a let-coercion in this order:

1. When the source value is a `Variant`, the provider unwraps it before dispatching to a strategy. The unwrap is
   performed once, by the provider, on behalf of every strategy. It is recursive, because a `Variant` may wrap
   another `Variant`.
2. The provider resolves the strategy by walking the base-type chain of the destination
   [VBType](../api/RDCore.SDK.Model.Types.Abstract.VBType.html). One strategy keyed on `VBNumericType` serves every
   concrete numeric type
   ([**RD-VBAL §5.5.1.2.1** Let-coercion between numeric types](#55121-let-coercion-between-numeric-types)).

Every let-coercion strategy casts `frame.SourceValue` directly to its own concrete value type. The provider unwraps a
`Variant` source before dispatch so that the direct cast of every strategy sees the wrapped value rather than the
[VBVariantValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBVariantValue.html).

Strategy dispatch is keyed on the destination type. It selects the right strategy for a `Variant` source as well,
because the `Variant`'s `TypeInfo` mirrors its wrapped value's
([**RD-VBAL §5.5.1.2.12** Let-coercion to Variant](#551212-let-coercion-to-variant)).

A `Null`, `Empty` or `Error` source is the exception: **MS-VBAL §5.5.1.2.9**–**11** key their rules on the source, so
the provider dispatches it to the strategy of its own type. It does so for any destination except a `Variant` and a
fixed-size array, which those rules leave out ("any type except a fixed-size array or `Variant`"). No strategy is
keyed on a fixed-size array, so a `Null`, `Empty` or `Error` source let-coerced to one is the provider's
`TypeMismatch` (13). Static semantics refuses any let-coercion into a fixed-size array
([LetCoercionStaticSemantics](../api/RDCore.SDK.Semantics.Static.LetCoercionStaticSemantics.html)), so this case is
reached only by a direct call of the provider.

`LetCoercionRuntimeSemanticsProvider.Analyze` asks the destination's strategy, which says nothing about a `Null`
source. The provider itself flags a `Null` source whose coercion raises an error as
[ConversionSemanticFlags](../api/RDCore.SDK.Semantics.Flags.ConversionSemanticFlags.html)`.Failed`, whether the `Null`
is the source or the value a `Variant` source holds: `L = Null` and `L = v`, with `v` a `Variant` holding `Null` and
`L` a `Long`, are both a failed coercion. An `Error` source is not flagged `Failed`.


## Coercion frame stack

The let-coercion provider maintains a coercion frame stack (see
[LetCoercionStackFrame](../api/RDCore.SDK.Runtime.Shared.LetCoercionStackFrame.html)).

A _recursive let-coercion_ is a strategy that must coerce through an intermediate type, e.g.
`Date → Double → Integer`. The coercion frame stack detects a recursive let-coercion and reports it as
`OutOfStackSpace` ([VBRuntimeErrorId](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorId.html)) rather than overflowing
the call stack.

A [VBTypeDescValue](../api/RDCore.SDK.Model.Values.Meta.VBTypeDescValue.html) is used in the implementation of
let-coercion, where semantics demand knowledge of a _data type_ where a _value_ is normally required
([**RD-VBAL §2.4.3** Meta and Advanced Types](rd-vbal.2.4.3.meta-and-advanced-types.md)).


## Let-coercion result

Each let-coercion step yields a [LetCoercionResult](../api/RDCore.SDK.Runtime.Shared.LetCoercionResult.html):

|Result|Carries|
|---|---|
|`Success`|The coerced [VBTypedValue](../api/RDCore.SDK.Model.Values.Abstract.VBTypedValue.html).|
|`Error`|A run-time error, such as `TypeMismatch` or `Overflow` (`VBRuntimeErrorId`).|
|`NotApplicable`|—|


## Let-coercion strategies

The let-coercion strategy contract is
[ILetCoercionRuntimeSemantics](../api/RDCore.SDK.Runtime.Abstract.ILetCoercionRuntimeSemantics.html).
`ILetCoercionRuntimeSemantics.EvaluateLetCoercion` and `ILetCoercionRuntimeSemantics.Analyze` take the coerced value's
own [ExpressionNode](../api/RDCore.SDK.Model.AST.Abstract.ExpressionNode.html) rather than a
[VBOperatorExpression](../api/RDCore.SDK.Model.AST.Expressions.VBOperatorExpression.html).

Every let-coercion strategy treats the node parameter as opaque: it reads only the node's identity and location, for
error reporting. No strategy therefore depends on the narrower `VBOperatorExpression` type.

RD-VBA rules out the fabrication of synthetic nodes for let-coercion: a construct that has no operator node of its own
in source passes its own expression
([**RD-VBAL §5.5.1.2.2** Let-coercion to and from Boolean](#55122-let-coercion-to-and-from-boolean)). The node
parameter of the operator runtime semantics follows the same rule
([**RD-VBAL §5.6.9.2** Simple Data Operators](rd-vbal.5.6.9.2.simple-data-operators.md)).


## 5.5.1.2.1 Let-coercion between numeric types

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.5.1.2.1** Let-coercion between numeric types](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/56fb78c3-a0ea-4bf0-8ee4-eb5f89bda4f7).

One let-coercion strategy keyed on [VBNumericType](../api/RDCore.SDK.Model.Types.Abstract.VBNumericType.html),
`VBNumericLetCoercionTypeRuntimeSemantics`, serves every concrete numeric type.

Let-coercion between numeric types validates that the source value is within the destination's representable range.
When the source value is outside that range, it raises `Overflow`. Otherwise:

|Conversion|Result|
|---|---|
|Widening|The value is copied, converted to the destination's representation.|
|Narrowing to a wider-or-equal integral type|The value is copied, converted to the destination's representation.|
|Floating-point or fixed-point value → integral type (narrowing)|The value is rounded to the nearest integer ([**RD-VBAL §5.5.1.2.1.1** Banker's rounding](#551211-bankers-rounding)) before conversion.|
|Integral → floating-point|🎯 A plain widening copy (see the divergence note below).|

A `Date` source let-coerced to a numeric destination reports `ConversionSemanticFlags.DateSerial`
([ConversionSemanticFlags](../api/RDCore.SDK.Semantics.Flags.ConversionSemanticFlags.html)).
`VBNumericLetCoercionTypeRuntimeSemantics.DateSerialFlagsOf` computes the `DateSerial` flag for a `Date` source.

> [!NOTE]
> 🎯 **RD-VBAL diverges from MS-VBAL** in the integral → floating-point block of **MS-VBAL §5.5.1.2.1**.
>
> MS-VBAL specifies that block as a verbatim copy of the preceding (narrowing) block, including the finite-value and
> banker's-rounding checks. No integer value can meet the finite-value and banker's-rounding conditions, and
> integral → floating-point is an unambiguously widening conversion.
>
> RD-VBAL treats integral → floating-point let-coercion as a plain widening copy.

### MS-VBAL divergence principle

**RDCore** implements the MS-VBAL type-coercion rules verbatim, except for the resolved specification errors noted in
this section ([**RD-VBAL §5.0** Semantics](rd-vbal.5.0.semantics.md)).

- Divergences from MS-VBAL caused by obvious copy/paste and transcription errors in the MS specification are resolved
  in favour of the evident intent.
- Anything in MS-VBAL that implicitly depends on the Windows Registry, ActiveX, or MSForms is out of scope for the
  RD-VBA run-time. MS-VBAL requirements with such an implicit dependency are resolved in favour of the evident intent.

### 5.5.1.2.1.1 Banker's rounding

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.5.1.2.1.1** Banker's rounding](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/98152b5a-4d86-4acb-b875-66cb1f49433e).

The rounding used when narrowing a floating-point or fixed-point value to an integral type is round-half-to-even
("banker's rounding").


## 5.5.1.2.2 Let-coercion to and from Boolean

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.5.1.2.2** Let-coercion to and from Boolean](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3a9f5227-5fd5-4240-949a-51ffc32e71a9).

A `Date` source let-coerced to a `Boolean` destination reports `ConversionSemanticFlags.DateSerial`.
`VBBooleanLetCoercionRuntimeSemantics` reuses `VBNumericLetCoercionTypeRuntimeSemantics.DateSerialFlagsOf` for it
([**RD-VBAL §5.5.1.2.1** Let-coercion between numeric types](#55121-let-coercion-between-numeric-types)).

A condition passes its own expression through to the `Boolean` let-coercion strategy, with no synthetic node standing
in for an operator that is not present in source
([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).


## 5.5.1.2.3 Let-coercion to and from Date

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.5.1.2.3** Let-coercion to and from Date](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/8a4a9201-4e7f-4856-b9fc-5927d2879723).

`VBDateLetCoercionRuntimeSemantics` runs only when `Date` is the destination type.

A number is a `Date` when it is strictly between -657435 and 2958466: a time of day from 100-01-01 to 9999-12-31
(`VBDateType.IsValidSerial`). A number past either end, and a `Date` operation whose result is past it
(`#12/31/9999# + 1`), is run-time error 6, _Overflow_ (MS-VBA, recorded in Excel).

> 👉 A `Date` source let-coerced to a numeric or `Boolean` destination is handled by the numeric or `Boolean`
> strategy, which reports its `DateSerial` flag
> ([**RD-VBAL §5.5.1.2.1** Let-coercion between numeric types](#55121-let-coercion-between-numeric-types),
> [**RD-VBAL §5.5.1.2.2** Let-coercion to and from Boolean](#55122-let-coercion-to-and-from-boolean)).


## 5.5.1.2.4 Let-coercion to and from String

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.5.1.2.4** Let-coercion to and from String](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/00113388-401b-41c2-8107-dc3fc0485554).

Floating-point types define a `SignificantIntegerDigits` constant, declared on the `VBType`. It is used to represent
floating-point values correctly as [VBStringValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBStringValue.html) in
conversions and coercions to `String` ([**RD-VBAL §2.5.2.1.1** Numeric Values](rd-vbal.2.5.2.1.1.numeric-values.md)):

|Value type|VBType|Significant digits (`SignificantIntegerDigits`)|
|---|---|---|
|[VBSingleValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBSingleValue.html)|[VBSingleType](../api/RDCore.SDK.Model.Types.VBSingleType.html)|7|
|[VBDoubleValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBDoubleValue.html)|[VBDoubleType](../api/RDCore.SDK.Model.Types.VBDoubleType.html)|15|

A floating-point zero keeps its sign, as in MS-VBA (recorded in Excel): a negative zero is written `-0`, as
`Round(-0.5)` gives ([**RD-VBAL §6.1.2.10** Math](rd-vbal.6.1.2.10.math.md)).

A number with no integer part is written in scientific notation once its leading zeros and its digits, after the
point, are more than its type's significant digits: `0.0123456` but `1.234567E-02` for a `Single`, and
`0.001234567890123` but `1.2345678901234E-03` for a `Double`. The exponent is written with two digits at least:
`1.234568E+07`, `1E+100` (MS-VBA, recorded in Excel).

A `Currency` or a `Decimal` is written with every digit it has, trailing zeros removed: `922337203685477.5807`,
`0.1234567890123456789`. MS-VBAL §5.5.1.2.4 caps any number at 15 digits; MS-VBA does not cap a fixed-point
number - through `CStr`, `&`, `Str` and `Format` alike (recorded in Excel) - and RD-VBA does what MS-VBA does.

A `String` source is let-coerced to "a copy of the source string": its runtime value, every byte of its data, a
trailing byte included ([**RD-VBAL §2.4.1.2** VBStringType](rd-vbal.2.4.1.intrinsic-types.md#2412-vbstringtype)). A
copy of `vbNullString` is `vbNullString`. An assignment `u = s`, a `ByVal` argument, a parenthesized `(s)` and
`CStr(s)` all copy it this way.

### Regional settings

**MS-VBAL §5.5.1.2.4** writes a number "using the host-defined regional decimal symbol", and a date in the regional
Short Date and Long Time formats, and it reads a `String` as a number or a date "according to the host-defined regional
settings". RD-VBA has one set of regional settings for both directions, as MS-VBA has the machine's: the **platform's**,
[IRuntimeEnvironmentProfile](../api/RDCore.SDK.Runtime.Abstract.Execution.IRuntimeEnvironmentProfile.html)`.Regional`
([RegionalSettings](../api/RDCore.SDK.Runtime.RegionalSettings.html)), bound from the `Environment.Regional` section of `appsettings.json`
([**RD-VBAL §2.1** Implicit Storage](rd-vbal.2.1.implicit-storage.md)). They are en-US unless it says otherwise, whatever the machine's culture and
whatever the session's `Lcid`, so that a program writes `2.5` and reads `"1,000"` the same wherever it runs, and what `CStr` writes, `CDbl` and `CDate`
read back.

|Setting|en-US (the default)|
|---|---|
|`DecimalSymbol`|`.`|
|`DigitGroupingSymbol`|`,`|
|`CurrencySymbol`|`$`|
|`TwoDigitYearMax`|`2049`: `30` is 2030, `50` is 1950|
|`ShortDatePattern`|`M/d/yyyy`|
|`LongTimePattern`|`h:mm:ss tt`|
|`AMDesignator`, `PMDesignator`|`AM`, `PM`|

The patterns are .NET custom date and time format strings, and a date is written in the Gregorian calendar, with `/` and `:`
for the date and time separators: `1/30/2026 2:00:00 PM` in en-US's, as MS-VBA writes it on an en-US machine.

The culture the session's `Lcid` selects (`Environment.Culture`) is no regional settings: it decides how text compares
(`Option Compare Text`, `Like`) and what `Format` writes given a format
([**RD-VBAL §6.1.2.11** Strings Module](rd-vbal.6.1.2.11.strings.md)), never how a value is let-coerced to or from
`String`. A let-coercion never reads the culture of the thread the interpreter runs on either.

Every let-coercion to `String` writes in the regional settings: an assignment, `&`, an argument, `Print`, `LSet`,
`RSet`, `Open`'s path, the `Mid` statement, `Join`, `Split`'s delimiter, `Format` without a format, and `CStr`
([**RD-VBAL §6.1.2.3** Conversion Module](rd-vbal.6.1.2.3.conversion-module.md)), and `Str` for a date. `Str` writes a number
with a `.` whatever the regional settings, and `Write #` writes fixed spellings ([**RD-VBAL §5.4.5.9** Write Statement](rd-vbal.5.4.5.9.write-statement.md)).

### Reading a String as a number or a date

A `String` let-coerced to a number, a `Boolean` or a `Date` is read by the same regional settings.

What is read is what MS-VBA reads (recorded in Excel), which is more than §5.5.1.2.4's grammar says:

- a number may have a sign before it or after it (`1-`), or parentheses around it (`(1)`), never two of them; the currency symbol before it or after it,
  the sign on either side of it (`-$1`, `$-1`); the digit grouping symbol anywhere after its first digit, ignored (`1,000.5,0` is 1000.5); and an exponent,
  `e` or `d`, after all of it;
- a hexadecimal or octal literal is a number too (`&H10`, `&O7`), with no sign and no type character, and what it is depends on the type it is converted
  to: to an `Integer`, up to 16 bits are the `Integer` they are (`&HFFFF` is -1); to a `Long`, up to 32 bits are the `Long` they are; to anything else, up to
  32 bits are the `Long` they are (`&HFFFF` is 65535, `&HFFFFFFFF` is -1), and more are the number they are;
- a date and a time are read by the grammar of a date literal, `date-or-time` (**MS-VBAL §3.3.3**), without its `#` delimiters: which number is the month
  is decided by which can be, a comma is a date separator, a dot a time separator (`1.5` is 1:05), and a date that says no year is in the current one.

> [!NOTE]
> **Not implemented.** A date is read by the rules of `date-or-time` whatever `ShortDatePattern` says: month first, with
> `/`, `-` or `,` between its parts. Under regional settings whose Short Date is `dd.MM.yyyy`, `CStr` writes
> `15.01.2020`, which does not read back as a date. How MS-VBA reads a day-first date is not recorded yet.

👉 What the debugger displays is not a let-coercion: it writes a number and a date the way the VBA editor writes a
literal, whatever the culture.

👉 The `DateTime` module reads the culture of the thread the interpreter runs on, whatever `Lcid` is, for
`vbUseSystemDayOfWeek` and `vbUseSystem`: `Weekday`, `DatePart` and `DateDiff` take the first day of the week from it,
and `DatePart` the first week of the year
([**RD-VBAL §6.1.2.4** DateTime Module](rd-vbal.6.1.2.4.datetime-module.md)).


## 5.5.1.2.5 Let-coercion to String \* length (fixed-length strings)

This section corresponds to [**MS-VBAL §5.5.1.2.5** Let-coercion to String * length (fixed-length strings)](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/442a2cdd-118a-4c0b-99d1-9b494633fabb).

> [!NOTE]
> Reserved. This section has no content yet.


## 5.5.1.2.6 Let-coercion to and from resizable Byte()

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.5.1.2.6** Let-coercion to and from resizable Byte()](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/acf31907-ebaf-4830-9892-bfabe6fe1416).

[VBResizableByteArrayType](../api/RDCore.SDK.Model.Types.VBResizableByteArrayType.html) has its own let-coercion
semantics, which allow implicit conversion to and from
[VBStringType](../api/RDCore.SDK.Model.Types.VBStringType.html)
([**RD-VBAL §2.4.1** Intrinsic Types](rd-vbal.2.4.1.intrinsic-types.md)).

Both directions reinterpret the string's own data, "the implementation-defined binary format used to store String
data" ([**RD-VBAL §2.4.1.2** VBStringType](rd-vbal.2.4.1.intrinsic-types.md#2412-vbstringtype)), and no text encoding
or code page takes part:

|Source|Destination|Result|
|---|---|---|
|`Byte()`|`String`|Every byte of the array, the elements in order: two bytes to a character, and a trailing byte when the array has an odd number of elements. A pair of bytes that is a lone surrogate is kept as one: `00 D8` is `U+D800`. An uninitialized array is a 0-length string.|
|`String`|`Byte()`|Every byte of the string's data: an odd string gives an odd number of elements, and `U+D800` gives `00 D8`.|

👉 RD-VBA's format can hold any number of bytes, so the "?" **MS-VBAL §5.5.1.2.6** substitutes for data that cannot
be represented, and the trailing bytes it discards, never arise: every byte of the array is kept.


## 5.5.1.2.7 Let-coercion to and from non-Byte arrays

This section corresponds to [**MS-VBAL §5.5.1.2.7** Let-coercion to and from non-Byte arrays](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/38315eed-8ea8-4e2a-b91c-25a96910407a).

> [!NOTE]
> Reserved. This section has no content yet.


## 5.5.1.2.8 Let-coercion to and from a UDT

This section corresponds to [**MS-VBAL §5.5.1.2.8** Let-coercion to and from a UDT](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d5727cbc-068f-45f0-8119-7e7f13db9d54).

> [!NOTE]
> Reserved. This section has no content yet.


## 5.5.1.2.9 Let-coercion to and from Error

This section corresponds to [**MS-VBAL §5.5.1.2.9** Let-coercion to and from Error](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a7ac9943-2198-46cf-9fb6-a3dd5e029e44).

> [!NOTE]
> Reserved. This section has no content yet.


## 5.5.1.2.10 Let-coercion from Null

This section corresponds to [**MS-VBAL §5.5.1.2.10** Let-coercion from Null](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/deab39e3-8dbf-4bbe-b3f5-5aa12c542fe6).

> [!NOTE]
> Reserved. This section has no content yet.


## 5.5.1.2.11 Let-coercion from Empty

This section corresponds to [**MS-VBAL §5.5.1.2.11** Let-coercion from Empty](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e2637723-3010-449d-bead-9f17533dc105).

> [!NOTE]
> Reserved. This section has no content yet.


## 5.5.1.2.12 Let-coercion to Variant

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.5.1.2.12** Let-coercion to Variant](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2ae5553a-6515-4967-9b91-e06b527b137f): `Variant` let-coercion and storage.

Any value except a class or `Nothing` let-coerces to `Variant` as a copy. A value let-coerced to `Variant` is wrapped
in a `VBVariantValue` ([**RD-VBAL §2.5.2.1.5** Variant Values](rd-vbal.2.5.2.1.5.variant-values.md)).

### Type information

A `VBVariantValue`'s own `TypeInfo` mirrors its wrapped value's `TypeInfo`, so that ordinary destination-type
dispatch, in let-coercion and in operator and effective-type determination, picks the same strategy it would for the
unwrapped value.

- The runtime (C#) instance of a let-coerced `Variant` value remains a `VBVariantValue`, not the wrapped value's
  concrete type.
- A `VBVariantValue` wraps the whole value, whether or not that value is a scalar.
- A `Variant` may wrap another `Variant`.

### Storage

Storage holds a `VBVariantValue` as a
[VBRuntimeVariantValue](../api/RDCore.SDK.Model.Values.Runtime.VBRuntimeVariantValue.html) box, and reads it back as a
`VBVariantValue`. The box holds the wrapped `VBTypedValue` of the `Variant` itself.

The `VBRuntimeVariantValue` box follows the same pattern a
[VBArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBArrayValue.html) uses via
[VBRuntimeArrayValue](../api/RDCore.SDK.Model.Values.Runtime.VBRuntimeArrayValue.html)
([**RD-VBAL §2.5.2.1.2** Array Values](rd-vbal.2.5.2.1.2.array-values.md)).

> 👉 A `Variant` read back from a variable, array element, or field carries the exact value that was stored in it,
> rather than a new, unrelated `Empty`.

### Unwrapping

Because `TypeInfo` mirrors the wrapped value:

- Any code that short-circuits on a `TypeInfo` match must unwrap a `VBVariantValue` first.
- Any code that pattern-matches a `VBTypedValue` operand against a concrete value type directly must unwrap a
  `VBVariantValue` first.
- Unwrapping a `VBVariantValue` must be recursive, because a `Variant` may wrap another `Variant`.

Code that does not unwrap a `VBVariantValue` sees the box instead of the value.

|Site|`Variant` handling|See|
|---|---|---|
|`LetCoercionRuntimeSemanticsProvider.EvaluateLetCoercionSemantics`|Unwraps a `VBVariantValue` source once, in the provider, for every let-coercion.|[Let-coercion provider](#let-coercion-provider)|
|`OperatorRuntimeSemantics.LetCoerceNonNullOperand`|Does not take its "already the right type" short-circuit for a `Variant` operand: the operand is let-coerced through the provider, which unwraps it.|[**RD-VBAL §5.6.9.2** Simple Data Operators](rd-vbal.5.6.9.2.simple-data-operators.md)|
|`SetCoercionRuntimeSemantics`|Unwraps a `VBVariantValue` before its own object pattern-match.|[**RD-VBAL §5.5.2.2** Runtime semantics](rd-vbal.5.5.2.2.runtime-semantics.md)|
|`RuntimeExpressionEvaluator.EvaluateIndex`|Unwraps a `VBVariantValue` before matching a wrapped array.|[**RD-VBAL §5.6.13** Index Expressions](rd-vbal.5.6.13.index-expressions.md)|
|`ProcedureExecutor.ExecuteForEachOpener`|Unwraps a `VBVariantValue` before matching a wrapped array.|[**RD-VBAL §5.4.2.4** For Each Statement](rd-vbal.5.4.2.4.for-each-statement.md)|
|`BinaryConcatOperatorRuntimeSemantics.IsByteArray`|Unwraps a `VBVariantValue` before matching a wrapped array.|[**RD-VBAL §5.6.9.4** & Operator](rd-vbal.5.6.9.4.ampersand-operator.md)|


## 5.5.1.2.13 Let-coercion to and from a class or Object or Nothing

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.5.1.2.13** Let-coercion to and from a class or Object or Nothing](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9b8fc7b4-8329-497d-b2dd-2d0fa2b7b48c).

The default member of a class type can be implicitly invoked through let-coercion, yielding the _data value_ of the
object ([**RD-VBAL §2.4.2** Non-intrinsic Types](rd-vbal.2.4.2.non-intrinsic-types.md);
[**RD-VBAL §5.6.2** Expression Evaluation](rd-vbal.5.6.2.expression-evaluation.md)).

---
> ⏮️ [**RD-VBAL §5.5.1.1** Static semantics](rd-vbal.5.5.1.1.static-semantics.md) | ⏭️ [**RD-VBAL §5.5.2** Set-coercion](rd-vbal.5.5.2.set-coercion.md)
