# 5.6.9.5 Relational Operators

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.9.5** Relational Operators](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f8acd631-55c1-4199-bc1e-022aaab6d9c8).

## Runtime Semantics

Relational operators are simple data operators: they validate their operands through
`OperatorRuntimeSemantics.LetCoerceNonNullOperand`
([**RD-VBAL §5.6.9.2** Simple Data Operators](rd-vbal.5.6.9.2.simple-data-operators.md)).

Relational operators compare in the effective type, and yield a
[VBBooleanValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBBooleanValue.html).

|Effective type|Compared in|
|---|---|
|Integral|The effective type's own representation: for example `Long` as a 32-bit `int`, and `LongLong` as a 64-bit `long`.|
|`Boolean`|A 64-bit integer (`long`).|
|Fixed-point (`Currency`, `Decimal`)|`decimal`|
|`String`|The data of the strings, in the module's comparison mode. See [String comparison](#string-comparison).|

|Condition|Run-time error|
|---|---|
|An operand is `NaN`.|6 — Overflow|

Run-time error 6 is [VBRuntimeErrorId](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorId.html)`.Overflow`.

👉 The range checks of a `For` loop use the relational operators
`RDCore.Runtime.Semantics.Operators.Relational.BinaryGtRelationalOperatorRuntimeSemantics` and
`BinaryLtRelationalOperatorRuntimeSemantics`, not a raw numeric comparison
([**RD-VBAL §5.4.2.3** For Statement](rd-vbal.5.4.2.3.for-statement.md)).

### Variant String/Numeric comparison

RD-VBA implements the `Variant` String/Numeric comparison exception of **MS-VBAL §5.6.9.5** as run-time behaviour.

The exception applies when both relational operands are `Variant`, one originally holding a `String` value and the
other a numeric value:

- The numeric operand is always considered less than the `String` operand, regardless of their actual values.
- RD-VBA never attempts to coerce the `String` operand to a number. Depending on its content, that coercion would
  fail, or succeed incorrectly.

`BinaryRelationalOperatorRuntimeSemantics` handles the exception as a short-circuit:

1. It detects the `Variant` String/Numeric case before normal effective-type determination and coercion run.
2. It reduces the case to a synthetic `Integer` rank for each operand (table below).
3. The relational operator's ordinary `Integer` evaluation branch compares the synthetic ranks.

|Operand|Synthetic `Integer` rank|
|---|---|
|The numeric-holding `Variant`|`0`|
|The `String`-holding `Variant`|`1`|

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

🎯 This departs from **MS-VBAL §5.6.9.5**. Its effective-value-type table gives a numeric operand and a `String` operand
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

### String comparison

Each of the six operators compares two `String` operands as
[StringComparisonRules](../api/RDCore.SDK.Runtime.StringComparisonRules.html)`.Compare` orders them, against `0`.
`Compare` is the one place a `String` order is defined, and `Select Case` compares through the same operators. It
compares in the comparison mode of the module ([**RD-VBAL §5.2.1.1** Option Compare Directive](rd-vbal.5.2.1.option-directives.md#5211-option-compare-directive)),
and sees every byte of a string's data, a trailing byte included
([**RD-VBAL §2.4.1.2** VBStringType](rd-vbal.2.4.1.intrinsic-types.md#2412-vbstringtype)).

|Mode|Two `String`s compare by|
|---|---|
|Binary|Their data: the code units, in order, then a trailing byte. A string whose code units begin another's, followed by more, is greater. Between the same code units, no trailing byte is less than any, and two trailing bytes compare by value.|
|Text|Their characters regardless of case, according to the environment's regional settings. A trailing byte is not text: it only breaks a tie between two strings that are equal regardless of case, the same way.|

🎯 **MS-VBAL §5.6.9.5** compares "each byte of the implementation-specific representation of the string data". Binary
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

---
> ⏮️ [**RD-VBAL §5.6.9.4** & Operator](rd-vbal.5.6.9.4.ampersand-operator.md) | ⏭️ [**RD-VBAL §5.6.9.6** Like Operator](rd-vbal.5.6.9.6.like-operator.md)
