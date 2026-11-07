# 3.2.0 Literal Expressions

[LiteralExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.LiteralExpressionNode.html) is the AST node for
[**MS-VBAL §5.6.5** Literal Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/13557712-af01-4c82-acfa-01a440a27262)
([**RD-VBAL §5.6.5** Literal Expressions](rd-vbal.5.6.5.literal-expressions.md)). A `LiteralExpressionNode`
represents a value that is statically resolved to a
[VBTypedValue](../api/RDCore.SDK.Model.Values.Abstract.VBTypedValue.html).

The parser resolves a literal's _declared type_ from the source token. A `LiteralExpressionNode` already carries a
fully-typed `VBTypedValue` when the parser produces it. That value includes the effect of any
_type-declaration character_.

## 3.2.0.1 Numeric Literal Types

The declared type of a numeric literal follows
[**MS-VBAL §3.3.2** Number Tokens](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/685ad840-accb-4bdb-8bfd-f3d88498547a).
A numeric literal's type is fixed by the literal's own form (a type-declaration suffix, the presence of a decimal
point or exponent, or a radix prefix) before any context is considered. Because of this, overflow is a property of
the literal alone, and is caught while the syntax tree is built
([VBC00042](../diagnostics/vbc00042.md) _Numeric literal overflow_).

The declared type of a numeric literal is determined as follows:

1. An explicit _type-declaration character_ suffix, if present, forces the literal's declared type. A literal whose
   value does not fit its suffix-forced type is a **syntax error**, `NumericLiteralOverflow` (`VBC00042`), located
   at the literal. For example, the `%` suffix denotes `Integer`, whose maximum is 32767.

   |Suffix|Declared type|
   |---|---|
   |`%`|`Integer`|
   |`&`|`Long`|
   |`^`|`LongLong`|
   |`!`|`Single`|
   |`#`|`Double`|
   |`@`|`Currency`|

2. Otherwise, an **unsuffixed decimal integer literal** (no fractional part, no exponent) takes the smallest of
   `Integer`, `Long`, `Double` that can hold its value.

3. Otherwise, a **floating-point literal** (a literal with a fractional part or an exponent) is `Double`.
   - The exponent letter is one of `[DEde]`.
   - `D` is the legacy double-precision marker. It produces the same value as `E`.
   - A floating-point literal that overflows to infinity is a `NumericLiteralOverflow` syntax error.

4. A **`&H…` hexadecimal or `&O…` octal (radix) literal** is typed by _bit width_, not by the decimal rule (rule 2).
   Its radix digits are interpreted as a **two's-complement** value at the literal's bit width.

### Radix Literals

An unsuffixed `&H…` / `&O…` literal is the narrowest of `Integer` (16-bit) or `Long` (32-bit) that the value's bit
width fits:

|Literal|Declared type|Value|
|---|---|---|
|`&HFFFF`|`Integer`|`-1`|
|`&H8000`|`Integer`|`-32768`|
|`&H10000`|`Long`|`65536`|
|`&HFFFFFFFF`|`Long`|`-1`|

`&HFFFFFFFF` is the largest unsuffixed radix literal. An unsuffixed radix literal beyond 32 bits, such as
`&HFFFFFFFFF`, is a `NumericLiteralOverflow` syntax error. A 64-bit `LongLong` radix literal requires the `^` suffix.

A suffix sets a radix literal's width:

|Suffix|Width|
|---|---|
|`%`|16 bits|
|`&`|32 bits|
|`^`|64 bits|

A radix literal is **never** `Double`.

VBA has no negative literal: `-&H8000` is the unary minus of the `Integer` `-32768`, whose negation no `Integer` holds.
It overflows (6) when it runs, and in a `Const` it is the compile error `Overflow`, as **MS-VBA** has it (recorded in
Excel); so is the least `LongLong` (`-&H8000000000000000^`, `oracle\s58` N09, N11). `-&H8000&` is a `Long`, and `-32768`.

> [!NOTE]
> 👉 **MS-VBA** evaluates a constant expression (**MS-VBAL §5.6.16.1**) when it compiles the code, and negates the least
> `Long` of one as the `Long` it is: `-&H80000000` is `-2147483648`, in a `Const` too, and so are `-(-2147483647 - 1)`,
> `-M` of a `Const M = &H80000000`, and `-CLng(&H80000000)` (`oracle\s58` N07, N08, N12, `s60` N13, N16, N17). `Abs` of
> one is that `Long` too (`s60` A01, A02). The negation, or `Abs`, of a `Long` variable that holds it overflows (6), and so
> does the negation of the least `Integer` or `LongLong` in any expression (`s58` N09-N11, `s60` N15). RD-VBA does what
> MS-VBA does.

> [!NOTE]
> `LongLong` is only produced by the `^` suffix. An unsuffixed integer literal that exceeds `Long` range widens to
> `Double`, never `LongLong`.

`String` and `$`-suffixed identifiers are covered by the declared-type rules for `String`, not by the numeric
literal type rules.

## 3.2.0.2 Static Symbols

The _environment host_ defines a number of globally defined _static symbols_
([StaticSymbol](../api/RDCore.SDK.Model.Symbols.Abstract.StaticSymbol.html)). They are defined on top of the global
[IStdConstantsModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdConstantsModule.html).

|Literal (token)|Type|Value|
|---|---|---|
|`True`, `False`|[VBBooleanType](../api/RDCore.SDK.Model.Types.VBBooleanType.html)|[VBBooleanValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBBooleanValue.html)|
|`VBEmptyString`|[VBStringType](../api/RDCore.SDK.Model.Types.VBStringType.html)|[VBStringValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBStringValue.html)|
|`Null`|[VBNullType](../api/RDCore.SDK.Model.Types.VBNullType.html)|[VBNullValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBNullValue.html)|
|`Empty`|[VBVariantType](../api/RDCore.SDK.Model.Types.VBVariantType.html)|[VBEmptyValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBEmptyValue.html)|
|`Nothing`|[VBObjectType](../api/RDCore.SDK.Model.Types.VBObjectType.html)|[VBNothingValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBNothingValue.html)|

The parser resolves the `Null` literal keyword to a `LiteralExpressionNode` carrying a `VBNullValue`, so a `Null`
value can be written as a literal, for example as a `Select Case` selector
([**RD-VBAL §5.4.2.10** Select Case Statement](rd-vbal.5.4.2.10.select-case-statement.md)).

### 3.2.0.2.1 Instance Expressions

See [**RD-VBAL §5.6.11** Instance Expressions](rd-vbal.5.6.11.instance-expressions.md).
