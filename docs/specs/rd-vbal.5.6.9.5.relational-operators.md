# 5.6.9.5 Relational Operators

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.9.5** Relational Operators](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f8acd631-55c1-4199-bc1e-022aaab6d9c8).

## Runtime Semantics

Relational operators are simple data operators: they validate their operands through
`OperatorRuntimeSemantics.LetCoerceNonNullOperand`
([**RD-VBAL §5.6.9.2** Simple Data Operators](rd-vbal.5.6.9.2.simple-data-operators.md)).

Relational operators compare in the effective type, and yield a
[VBBooleanValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Intrinsic.VBBooleanValue.html). With a `Variant` operand, it is held in a `Variant`
([An expression over a Variant](#an-expression-over-a-variant)).

|Effective type|Compared in|
|---|---|
|Integral|The effective type's own representation: for example `Long` as a 32-bit `int`, and `LongLong` as a 64-bit `long`.|
|`Boolean`|A 64-bit integer (`long`).|
|Fixed-point (`Currency`, `Decimal`)|`decimal`|
|`String`|The data of the strings, in the module's comparison mode. See [String comparison](#string-comparison).|
|`Error`|The error codes, as two `Error` values are compared: `CVErr(5) < CVErr(6)` is `True`. An `Error` is compared as it is, and is never let-coerced to an `Error`, which no let-coercion makes. An `Error` and any other value is a type mismatch (13), as the table has it and **MS-VBA** raises it (recorded in Excel).|

|Condition|Run-time error|
|---|---|
|An operand is `NaN`.|6 (Overflow)|

Run-time error 6 is `VBRuntimeErrorId.Overflow`.

The range checks of a `For` loop use the relational operators
`RDCore.Runtime.Semantics.Operators.Relational.BinaryGtRelationalOperatorRuntimeSemantics` and
`BinaryLtRelationalOperatorRuntimeSemantics`, not a raw numeric comparison
([**RD-VBAL §5.4.2.3** For Statement](rd-vbal.5.4.2.3.for-statement.md)).

### Variant String/Numeric comparison

RD-VBA implements the `Variant` String/Numeric comparison exception of **MS-VBAL §5.6.9.5** as run-time behaviour.

The exception applies when both relational operands are `Variant`, one originally holding a `String` value and the
other a numeric, `Boolean` or `Date` value:

- The other operand is always considered less than the `String` operand, regardless of their actual values.
- RD-VBA never attempts to coerce the `String` operand to a number. Depending on its content, that coercion would
  fail, or succeed incorrectly.

`BinaryRelationalOperatorRuntimeSemantics` handles the exception as a short-circuit:

1. It detects the `Variant` String/Numeric case before normal effective-type determination and coercion run.
2. It reduces the case to a synthetic `Integer` rank for each operand (table below).
3. The relational operator's ordinary `Integer` evaluation branch compares the synthetic ranks.

|Operand|Synthetic `Integer` rank|
|---|---|
|The `Variant` holding a number, a `Boolean` or a `Date`|`0`|
|The `String`-holding `Variant`|`1`|

**RD-VBA differs from MS-VBAL §5.6.9.5,** which ranks only a numeric value type (**MS-VBAL §5.5.1.2.1**). MS-VBA ranks a
`Boolean` and a `Date` the same way, and RD-VBA does what MS-VBA does. With `a` holding `"40"`, `a < b` is `False` for `b`
a `Variant` `False`, and `a = b` is `False` for `b` a `Variant` `True`. With `a` holding `"1/1/1990"`, `a < d` is `False`
for `d` a `Variant` date of 1/1/2000 (observed in Excel).

The short-circuit runs before normal coercion because normal coercion would otherwise try, and fail, to coerce the
`String` to a number. Because the case is reduced to synthetic ranks, the `Variant` String/Numeric case needs no
separate evaluation path.

The comparison is legitimate, and almost never meant: the analysis states it as a fact of the operation
(`ComparisonOperatorSemanticFlags.IsVariantStringNumericException`), and RDCore.Diagnostics reports it as
[`RDC11011`](../diagnostics/rdc11011.md), a warning.

### Variant compared with a String

RD-VBA compares a `Variant` holding a number, a `Date` or a `Boolean` with a `String` operand that is not a `Variant` (a
variable declared `As String`, or a string literal) **as text**, as MS-VBA does:

- The effective value type is `String`.
- The `Variant`'s value is Let-coerced to `String`, and the two `String` values are compared
  ([String comparison](#string-comparison)).
- No error is raised, whatever the `String` holds.

**RD-VBA differs from MS-VBAL §5.6.9.5.** Its effective-value-type table gives a numeric operand and a `String` operand
the numeric type, so the `String` is Let-coerced to a number, which raises run-time error 13 (Type mismatch) when it is
not one. Its one exception covers two `Variant` operands only (above).

MS-VBA compares the text instead, as [Microsoft Learn documents it](https://learn.microsoft.com/en-us/office/vba/language/reference/user-interface-help/comparison-operators):
"One expression is a **String** and the other is any **Variant** except a **Null**: perform a string comparison". These
results were measured in Excel:

|`Variant` holds|`String`|Comparison|MS-VBA|MS-VBAL §5.6.9.5|
|---|---|---|---|---|
|`0@` (`Currency`)|`"."`|`=`|`False`|Type mismatch (13)|
|`5@`|`"."`|`>`|`True`, as `"5"` comes after `"."`|Type mismatch (13)|
|`10@`|`"9"`|`>`|`False`, as `"10"` comes before `"9"`|`True`|
|`1.5`|`"1.5"`|`=`|`True`|`True`|
|`10`|`""`|`>`|`True`|Type mismatch (13)|
|`#1/2/2020#`|`"x"`|`=`|`False`|Type mismatch (13)|
|`True`|`"x"`|`=`|`False`|Type mismatch (13)|

When neither operand is a `Variant` (a `Currency` and a `String` variable, say), the table applies, and MS-VBA raises the
type mismatch too. A `Variant` holding an `Error` keeps the table's behaviour, which nothing has measured otherwise.

RD-VBA runs the code as MS-VBA does. The analysis states the departure as a fact of the operation
(`ComparisonOperatorSemanticFlags.IsVariantComparedWithStringAsText`), and RDCore.Diagnostics reports it as
[`RDC11013`](../diagnostics/rdc11013.md), a warning that stands in for the type mismatch the specification raises.

### A Variant String compared with a number

The other way around: one operand is a `Variant` holding a `String`, and the other a number, a `Date` or a `Boolean` that
is not a `Variant` (a variable declared with the type, a literal). RD-VBA compares them as MS-VBA does:

- When the `String` Let-coerces to the effective value type the table gives, the table applies: `"3" < 5` is `True`,
  and so are `"&H3" < 5`, `"(3)" < 0` and `"$3" < 5`. A date's text compared with a `Date` is read as a `Date`
  ([below](#a-string-compared-with-a-date)), and `"True" = True` is `True`.
- When the coercion is a type mismatch, nothing is raised, and the `String` ranks above the other operand, whatever its
  value, as it does when both operands are `Variant` values ([above](#variant-stringnumeric-comparison)): `"abc" > 5` is
  `True`, `"abc" = 5` is `False`, and `5 < "abc"` is `True`.
- Any other error of the coercion is raised. A number's text too large for a `Byte`, an `Integer` or a `Long` is no
  error: the two are compared as `Double` values ([below](#a-string-compared-with-a-byte-an-integer-or-a-long)), and
  `"40000" > 2` is `True` with an `Integer` `2` (`oracle\s56` H01, H06).

**RD-VBA differs from MS-VBAL §5.6.9.5.** Its table makes the `String`'s coercion to the effective type a type mismatch
(13), and so does Microsoft Learn's
[Comparison operators](https://learn.microsoft.com/en-us/office/vba/language/reference/user-interface-help/comparison-operators)
page, for a `Variant` string that can't be converted to a number. These results were measured in Excel (`oracle\s48` H,
I, K, L):

|`Variant` holds|Other operand|Comparison|MS-VBA|MS-VBAL §5.6.9.5|
|---|---|---|---|---|
|`"abc"`|`5`|`<`, `>`, `=`|`False`, `True`, `False`|Type mismatch (13)|
|`""`|`5`|`<`|`False`|Type mismatch (13)|
|`"1/2/1900"`|`5`, `34`|`<`|`False`, `False` (the text of a date is no number)|Type mismatch (13)|
|`"1/2/1900"`|a `Date` of `5`|`<`|`True`, as `1/2/1900` is day 3|`True`|
|`"abc"`|a `Date`, a `Boolean`, a `Currency`, a `Byte`|`>`|`True`|Type mismatch (13)|
|`"3"`|`5`, an `Integer`, a `Date`, a `Boolean`, a `Currency`|`<`|`True`|`True`|

A `String` declared as one is not a `Variant`: `s < 5`, with `s` holding `"abc"`, raises the type mismatch in MS-VBA as
the table says, and in RD-VBA. The analysis states the departure as a fact of the operation
(`ComparisonOperatorSemanticFlags.IsVariantStringThatIsNoValueRanked`), and RDCore.Diagnostics reports it as
[`RDC11020`](../diagnostics/rdc11020.md), a warning that stands in for the type mismatch the specification raises.

### A String compared with a Byte, an Integer or a Long

RD-VBA compares a `String` with a `Byte`, an `Integer` or a `Long` **as `Double` values**, as MS-VBA does. This holds whether the
`String` is a `Variant`'s or one declared `As String`: the effective value type is `Double`, and `"2.5" = 2` is `False`.

**RD-VBA differs from MS-VBAL §5.6.9.5.** Its table gives the two the integral type, which Let-coerces `"2.5"` to `2` and
makes `"2.5" = 2` `True`. [Microsoft Learn](https://learn.microsoft.com/en-us/office/vba/language/reference/user-interface-help/comparison-operators)
calls the comparison "numeric" and names no type. These results were measured in Excel (`oracle\s56` G01-G12):

|`String`|Other operand|Comparison|MS-VBA|MS-VBAL §5.6.9.5|
|---|---|---|---|---|
|a `Variant` `"2.5"`|`2`, `CLng(2)`, an `Integer`, a `Byte` of `2`|`=`|`False`|`True`|
|a `Variant` `"2.5"`|`2`|`>`|`True`|`False`|
|a `String` `"2.5"`|`2`|`=`|`False`|`True`|
|a `Variant` `"2.5"`|`2.5`, a `Double`, a `Currency` of `2.5`|`=`|`True`|`True`|
|a `Variant` `"2.5"`|`True`|`=`|`True`, compared as `Boolean` values, as the table says|`True`|
|a `Variant` or a `String` `"2.00001"`|a `Currency` of `2`|`=`|`True`, compared as `Currency` values, to 4 decimals, as the table says|`True`|
|a `Variant` `"9007199254740993"`|a `Decimal` of `9007199254740992`|`=`|`False`, compared as `Decimal` values, as the table says|`False`|

A `Currency` and a `Decimal` keep the table's types (`oracle\s56` I02-I04).

A `Variant` `String` compared with a `Single` or a `LongLong` is compared as `Double` values too (`oracle\s56` H02-H04):

|`String`|Other operand|Comparison|MS-VBA|MS-VBAL §5.6.9.5|
|---|---|---|---|---|
|a `Variant` `"0.1"`|a `Single` of `0.1`|`=`|`False`|`True`|
|a `String` `"0.1"`|a `Single` of `0.1`|`=`|`True`, compared as `Single` values, as the table says|`True`|
|a `Variant` `"2.5"`|a `LongLong` of `2`|`=`, `>`|`False`, `True`|`True`, `False`|
|a `Variant` `"9007199254740993"`|a `LongLong` of `9007199254740992`|`=`|`True`, as the two are the same `Double`|`False`|

A `String` declared as one, compared with a `LongLong`, is not measured, and keeps the table's type.

The analysis states the `String` operand of the operation (`StringOperandFact`): its value Let-coerced to a `Double` and to the table's type, the
error each Let-coercion raises, and the number when it is known. The analyzer reports the comparison where the operator can answer otherwise than in
the table's type:
- a `String` it does not know;
- a `String` that is a number the table's type does not hold as the `Double` does (`"2.5"`, or `"40000"` with an `Integer`), where the answers differ.
  It compares them for the number, when it is known, and otherwise for the numbers next to the `String`'s value. Only there can they differ: `"2.5"` is
  the table's `2`, and `"2.5" < n` is `2 < n` for every integer, where `"2.5" > n` is not `2 > n` for `n = 2`.

It does not report a known `String` that is not a number, a type mismatch (13) either way. RDCore.Diagnostics reports it as
[`RDC11027`](../diagnostics/rdc11027.md), a warning.

### An expression over a Variant

An operator with a `Variant` operand is declared `Variant`, and what it evaluates to is a `Variant`'s value:

|Operators|MS-VBAL|
|---|---|
|The arithmetic operators, `&`|**§5.6.9.3**, **§5.6.9.4**|
|The relational operators, `Like`|**§5.6.9.5**, **§5.6.9.6**|
|The logical operators, `Not` included|**§5.6.9.8**|
|A pair of parentheses|**§5.6.6**|

`Is` is declared `Boolean` (**MS-VBAL §5.6.9.7**). So `v + 1`, `v * 1`, `-v`, `v & ""`, `(v + 1)`, `v Or 255`, `Not v` and
`v < 255` are compared as `v` itself is ([above](#variant-compared-with-a-string)). With `a` holding `"40"` and `v` holding `0`,
`a < (v Or 255)` and `a < (a < 255)` are `False`: both operands are `Variant` values, and the `String` ranks above the other
([above](#variant-stringnumeric-comparison); observed in Excel).
`If`, `Select Case`, `Do While` and `IIf` decide on these values the same way. With `v` holding `1`,
`(v + 1) = "2.0"` is `False`, compared as text, and `CInt(v) = "1.0"` is `True`, as `CInt` returns an `Integer`. These were
measured in Excel (`oracle\s56` G02, G09, G10), and a `Select Case` over such an expression compares its `Case` values the same way
([**RD-VBAL §5.4.2.10**](rd-vbal.5.4.2.10.select-case-statement.md)).

### A String compared with a Date

The table gives a `String` and a `Date` the effective type `Date`. The operands of a `Date` effective type are compared by
their serial, a `Double`. A `String` operand is first Let-coerced to `Date`, which reads the text of a date as one, and is
then compared by its serial: `"1/2/1900"` compared with a `Date` of `5` compares day 3 with day 5 (`oracle\s48` K04).

### String comparison

Each of the six operators compares two `String` operands as
`StringComparisonRulesExtensions.Compare` orders them, against `0`.
`Compare` is the one place a `String` order is defined, and `Select Case` compares through the same operators. It
compares in the comparison mode of the module ([**RD-VBAL §5.2.1.1** Option Compare Directive](rd-vbal.5.2.1.option-directives.md#5211-option-compare-directive)),
and sees every byte of a string's data, a trailing byte included
([**RD-VBAL §2.4.1.2** VBStringType](rd-vbal.2.4.1.intrinsic-types.md#2412-vbstringtype)).

|Mode|Two `String`s compare by|
|---|---|
|Binary|Their data: the code units, in order, then a trailing byte. A string whose code units begin another's, followed by more, is greater. Between the same code units, no trailing byte is less than any, and two trailing bytes compare by value.|
|Text|Their characters regardless of case, according to the environment's regional settings. A trailing byte is not text: it only breaks a tie between two strings that are equal regardless of case, the same way.|

**RD-VBA differs from MS-VBAL §5.6.9.5,** which compares "each byte of the implementation-specific representation of the string data". Binary
mode compares the code units of that data rather than its bytes one by one. The two find the same strings equal, and
order alike any two strings whose characters are all below `U+0100`. Where they differ, RD-VBA's representation is
little-endian: `"Ł"` (`41 01`) is greater than `"B"` (`42 00`) by code unit, and would be less byte by byte.

`Like` matches characters against a pattern of characters, and a trailing byte is not one
([**RD-VBAL §5.6.9.6** Like Operator](rd-vbal.5.6.9.6.like-operator.md)).

## 5.6.9.5.1 = Operator

This section corresponds to [**MS-VBAL §5.6.9.5.1** = Operator](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/34660fe0-2ce9-4526-b0c3-00613c9fa19e).

> [!NOTE]
> Reserved. This section has no content yet.

## 5.6.9.5.2 <> Operator

This section corresponds to [**MS-VBAL §5.6.9.5.2** <> Operator](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a5a4bd2c-a89d-4e88-a634-40a8bca8d458).

> [!NOTE]
> Reserved. This section has no content yet.

## 5.6.9.5.3 < Operator

This section corresponds to [**MS-VBAL §5.6.9.5.3** < Operator](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6b25b2c5-a96f-4e20-b77e-d328a13c66cd).

> [!NOTE]
> Reserved. This section has no content yet.

## 5.6.9.5.4 > Operator

This section corresponds to [**MS-VBAL §5.6.9.5.4** > Operator](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/c648bb41-d1b0-4462-9da1-a37c251521a4).

> [!NOTE]
> Reserved. This section has no content yet.

## 5.6.9.5.5 <= Operator

This section corresponds to [**MS-VBAL §5.6.9.5.5** <= Operator](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/66dfebc2-a5e1-43fd-965f-75334e04f5ec).

> [!NOTE]
> Reserved. This section has no content yet.

## 5.6.9.5.6 >= Operator

This section corresponds to [**MS-VBAL §5.6.9.5.6** >= Operator](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/0720f474-42ca-429c-ac1d-09f2453128a6).

> [!NOTE]
> Reserved. This section has no content yet.
