# 2.5.2.1.1 Numeric Values

All numeric data values inherit [VBNumericTypedValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Abstract.VBNumericTypedValue.html). This common base simplifies implementing semantics that involve "_any numeric type_" specifications.

`VBNumericTypedValue` implements [INumericValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Abstract.INumericValue.html), a non-generic base abstraction. `INumericValue` ensures every numeric type minimally has a `double` managed representation.

## Numeric Data Values

A numeric data value can be one of the following:

- [VBByteValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Intrinsic.VBByteValue.html)
- [VBIntegerValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Intrinsic.VBIntegerValue.html)
- [VBLongValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Intrinsic.VBLongValue.html)
- [VBLongLongValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Intrinsic.VBLongLongValue.html)
- [VBSingleValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Intrinsic.VBSingleValue.html)
- [VBDoubleValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Intrinsic.VBDoubleValue.html)
- [VBCurrencyValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Intrinsic.VBCurrencyValue.html)
- [VBDecimalValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Intrinsic.VBDecimalValue.html)

Each intrinsic type stores its own exact managed type: a `short` for `Integer` and an `int` for `Long`, not one integer type for both.

> Because each intrinsic type stores its own managed type, the declared type is preserved across the dispatch of an external call. See [**RD-VBAL §6.1.2.11** Strings](rd-vbal.6.1.2.11.strings.md) (`Len` / `LenB`).

## Static Values

Each numeric value type minimally defines a typed representation of its `MinValue`, `MaxValue` and `Zero`. A numeric value type may define other static values as required by runtime semantics.

## Significant Digits

Floating-point types also define a `SignificantIntegerDigits`. It is used to represent floating-point values correctly as [VBStringValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Intrinsic.VBStringValue.html) in conversions and coercions to `String`; see [**RD-VBAL §5.5.1.2** Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md).

|Value type|VBType|`SignificantIntegerDigits`|
|---|---|---|
|`VBSingleValue`|[VBSingleType](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBSingleType.html)|7|
|`VBDoubleValue`|[VBDoubleType](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBDoubleType.html)|15|

The `SignificantIntegerDigits` constant is declared on the `VBType`: `VBSingleType.SignificantIntegerDigits` and `VBDoubleType.SignificantIntegerDigits`.

## Precompiler Constants

Precompiler constants are interpreted as `Integer` values.

Conditional compilation expressions are described in [**RD-VBAL §5.6.16** Constrained Expressions](rd-vbal.5.6.16.constrained-expressions.md).
