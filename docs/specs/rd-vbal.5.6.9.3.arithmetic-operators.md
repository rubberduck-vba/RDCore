# 5.6.9.3 Arithmetic Operators

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.9.3** Arithmetic Operators](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e070115f-8d40-40cf-ac6d-ab18b9c6c906).

## Runtime Semantics

Arithmetic operators are simple data operators
([**RD-VBAL §5.6.9.2** Simple Data Operators](rd-vbal.5.6.9.2.simple-data-operators.md)):

- They validate their operands through `OperatorRuntimeSemantics.LetCoerceNonNullOperand`.
- An integral or fixed-point result is computed in the effective type's own representation, never through a `Double`
  intermediate: `Long` in a 32-bit `int`, `Currency` and `Decimal` in `decimal`.
- A `Single`, `Double` or `Date` result of operands of declared types is computed in the 80-bit extended format, as
  [extended intermediates](#extended-intermediates) describes.

Arithmetic runs in a _checked_ context. An integral or fixed-point result that does not fit the effective type raises
`Overflow` rather than wrapping or silently narrowing. A floating-point value is an overflow where it is stored as a
`Single` or `Double` and that is not finite: `s = s * 10` raises 6 for a `Single` of `3E+38`. So is a result that is
no number (NaN) of operands that are numbers: infinity less infinity. An operation of an infinity is otherwise
IEEE-754's, with no error: infinity plus one is infinity, and an infinity compares as one.

|Condition|Run-time error|
|---|---|
|An integral or fixed-point result does not fit the effective type.|6 — Overflow|
|A floating-point value stored as a `Single` or `Double` is past its range.|6 — Overflow|
|A floating-point result of operands that are numbers is NaN.|6 — Overflow|
|A floating-point number other than zero is divided by zero.|11 — Division by zero|

The error carries the value IEEE-754 arithmetic gave the operation, and a `Let` assignment of it to a `Single` or
`Double` variable assigns it before raising the error: after `s = s * 10`, `s` is infinite
([**RD-VBAL §5.4.3.8**](rd-vbal.5.4.3.8.let-statement.md#a-floating-point-value-that-overflowed)). MS-VBAL
§5.6.9.3.5 says this of a division of operands declared `Double`; MS-VBA does it for every floating-point
operation, and RD-VBA follows MS-VBA.

### Extended intermediates

MS-VBAL §5.6.9.3 lets an implementation "perform floating point operations with a higher-precision than the effective
value type (such as an 'extended' or 'long double' type)". MS-VBA does: it computes them as an x87 floating-point unit
does, in 80 bits - a 64-bit significand and a 15-bit exponent - and RD-VBA computes them the same way
([Float80](../api/RDCore.SDK.Model.Values.Numerics.Float80.html)). An expression of `Single`, `Double` and `Date`
operations whose operands have declared types is computed operation after operation in that format, a `Single`'s
included, and rounded where its value is stored. So:

- `(d * 10) / 10` is `1E+308` for a `Double` of `1E+308`, with no overflow: the intermediate `1E+309` is a value.
- `0.1 + 0.2 - 0.3` is `2.77555756156289E-17`, where an evaluation in `Double` gives `5.55111512312578E-17`.
- `s + 1 - s` is `1` for a `Single` of `16777216`, which no `Single` arithmetic gives.

Where the value goes decides what it is rounded to:

|The value is…|It is…|
|---|---|
|Let-coerced to a declared numeric type: a variable, an element, a field, a parameter, a function's result, a `For` bound, a subscript, a `ReDim` bound, a condition, an operand of `\`, `Mod`, `^`, `And` and the other logical operators, `CLng` and the other conversions to a number.|Converted to that type from its 80 bits: `e = s + 1` assigns `16777217` to a `Double`, and `2.5 + 2^-60` is `3` as a `Long`.|
|Held as itself: by a `Variant`, written as a `String` (`CStr` included), made a `Decimal`, the selector of a `Select Case`.|Stored as the type of the expression first: a `Single` `s + 1` held by a `Variant` is `16777216`.|
|Compared.|Rounded to the precision of the comparison's effective type, in the extended range: `d * 10 > 0` is `True`, and `1 + 2^-60 > 1` is `False`.|
|Given to `Abs`, `Int`, `Fix`, `Sgn`.|Computed on as it is.|
|Given to `CDbl`, or `CSng`.|Rounded to a `Double` - an infinity past its range, an overflow pending - and, by `CSng`, to the precision of a `Single`.|

An invalid operation and a division by zero do not stop the evaluation: the exception is pending until the value is
stored, and the error raised then. `(1 / z) - (1 / z)` assigns a NaN and raises 11, for `z` zero - a division by zero
is raised before an invalid operation or an overflow.

An operation with an operand declared `Variant` is MS-VBA's `Variant` arithmetic, which rounds each result to its type
(below): a typed operand of it is stored as its own type for it.

When an operand is declared `Variant`, an overflowing result is evaluated again in a wider type instead
([**RD-VBAL §5.0** Semantics](rd-vbal.5.0.semantics.md)); a result no `Double` holds is still 6. For an infinity and a
NaN, MS-VBA's `Variant` arithmetic differs from typed arithmetic, and RD-VBA follows it:

|`Variant` operation|Typed operation|
|---|---|
|A `Single` result that is not finite is a `Double`, with no error: a `Variant` holding a `Single` infinity, plus one, is a `Double` infinity.|A `Single` result of finite operands that is not finite is 6.|
|A `Double` result that is not finite is 6, an infinity's or a NaN's included, and assigns nothing.|A `Double` of an infinity is IEEE-754's, with no error: infinity plus one is infinity.|

An infinity and a NaN are values of `Single`, `Double` and `Variant`: they are copied between them, and assigned,
with no error. Converted to any other type, compared (a NaN), or given a sign by `Sgn` (a NaN), they are 6.

Run-time error 6 is [VBRuntimeErrorId](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorId.html)`.Overflow`. For division
by zero, see [**RD-VBAL §5.6.9.3.6** \ Operator and Mod Operator](#56936--operator-and-mod-operator).

## 5.6.9.3.1 Unary - Operator

This section corresponds to [**MS-VBAL §5.6.9.3.1** Unary - Operator](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/181ac001-7156-4fed-a518-fb90b828ec59).

> [!NOTE]
> Reserved. This section has no content yet.

## 5.6.9.3.2 + Operator

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.9.3.2** + Operator](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/91621d4b-3da3-4fe9-9581-adda857efe05).

🧩 RD-VBA explicitly adds runtime semantics for a unary `+` operator, as a language core extension
([**RD-VBAL §1.1.2** Language Core Extensions](rd-vbal.1.1.2.language-core-extensions.md)).

👉 A `For` loop's `ForNext` instruction adds `step` to the counter through the addition operator,
`RDCore.Runtime.Semantics.Operators.Arithmetic.BinaryAdditionOperatorRuntimeSemantics`. The step addition is therefore
an overflow-checked operation, not a plain CLR addition
([**RD-VBAL §5.4.2.3** For Statement](rd-vbal.5.4.2.3.for-statement.md)).

## 5.6.9.3.3 Binary - Operator

This section corresponds to [**MS-VBAL §5.6.9.3.3** Binary - Operator](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/c966fa50-f49f-44ab-a8f8-f2691fffe048).

> [!NOTE]
> Reserved. This section has no content yet.

## 5.6.9.3.4 * Operator

This section corresponds to [**MS-VBAL §5.6.9.3.4** * Operator](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/917bed61-f19f-4e7d-9f93-2e44d4ec7432).

> [!NOTE]
> Reserved. This section has no content yet.

## 5.6.9.3.5 / Operator

This section corresponds to [**MS-VBAL §5.6.9.3.5** / Operator](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/8628f21c-3f02-42b1-908c-201bd7ffe1b5).

> [!NOTE]
> Reserved. This section has no content yet.

## 5.6.9.3.6 \ Operator and Mod Operator

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.9.3.6** \ Operator and Mod Operator](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/09fdf266-a9d5-4ecb-b7d8-ffd1361cb349).

### Static Semantics

> [!NOTE]
> **Not implemented.** The `Mod` operator token has no mapped static semantics rule. The static semantics of a `Mod`
> expression defer to [VBUnknownType](../api/RDCore.SDK.Model.Types.VBUnknownType.html)
> ([**RD-VBAL §5.0** Semantics](rd-vbal.5.0.semantics.md)).

### Runtime Semantics

|Condition|Run-time error|
|---|---|
|An integral division or `Mod` by zero.|11 — Division by zero|

Run-time error 11 is `VBRuntimeErrorId.DivisionByZero`.

## 5.6.9.3.7 ^ Operator

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.9.3.7** ^ Operator](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/c3f526af-9d58-47f9-a6be-22ab81e8747a).

The `^` operator is the sole exception to computation in the effective type
([**RD-VBAL §5.6.9.2** Simple Data Operators](rd-vbal.5.6.9.2.simple-data-operators.md#computation-in-the-effective-type)):
its effective type is always `Double`. The `^` operator is evaluated as IEEE-754 exponentiation, except where that
gives no finite number:

|Condition|Run-time error|
|---|---|
|`0` to a negative power.|5 — Invalid procedure call or argument|
|A negative number to a fractional power (`(-8) ^ (1 / 3)`), which has no real result.|5 — Invalid procedure call or argument|
|A result past the range of a `Double` (`10 ^ 400`).|6 — Overflow|

A `Variant` operand doesn't change this: `Double` has nothing wider. `0 ^ 0` is `1`.

---
> ⏮️ [**RD-VBAL §5.6.9.2** Simple Data Operators](rd-vbal.5.6.9.2.simple-data-operators.md) | ⏭️ [**RD-VBAL §5.6.9.4** & Operator](rd-vbal.5.6.9.4.ampersand-operator.md)
