# 5.6.9.4 & Operator

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.9.4** & Operator](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9f072ffc-e943-4fcc-a4d0-f3c7db96abd9).

## Runtime Semantics

The `&` (concatenation) operator is a simple data operator: it validates its operands through
`OperatorRuntimeSemantics.LetCoerceNonNullOperand`
([**RD-VBAL §5.6.9.2** Simple Data Operators](rd-vbal.5.6.9.2.simple-data-operators.md)).

`BinaryConcatOperatorRuntimeSemantics.IsByteArray` unwraps a `Variant`
([VBVariantValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBVariantValue.html)) before matching a wrapped `Byte`
array.

> 👉 `v1 & v2` evaluates `Variant`s holding arrays the same as declared arrays.

See [**RD-VBAL §5.5.1.2.12** Let-coercion to Variant](rd-vbal.5.5.1.2.runtime-semantics.md#551212-let-coercion-to-variant)
for why a `Variant` operand must be unwrapped before it is matched against a concrete value type.

The result is every byte of the left operand's data followed by every byte of the right operand's
([VBStringValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBStringValue.html)`.Concat`). A trailing byte of the left
operand pairs with the first byte of the right one, so two one-element `Byte()` operands concatenate to one character
([**RD-VBAL §2.4.1.2** VBStringType](rd-vbal.2.4.1.intrinsic-types.md#2412-vbstringtype)). `+` between two `String`s is
the same concatenation.

👉 An operand that is a number or a date is let-coerced to `String` in the regional settings of the session's
environment ([**RD-VBAL §5.5.1.2.4** Let-coercion to and from String](rd-vbal.5.5.1.2.runtime-semantics.md#55124-let-coercion-to-and-from-string)).

---
> ⏮️ [**RD-VBAL §5.6.9.3** Arithmetic Operators](rd-vbal.5.6.9.3.arithmetic-operators.md) | ⏭️ [**RD-VBAL §5.6.9.5** Relational Operators](rd-vbal.5.6.9.5.relational-operators.md)
