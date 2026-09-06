# 3.2.0 Literal Expressions

[VBLiteralExpression](../api/RDCore.SDK.Model.AST.Expressions.VBLiteralExpression.html) (**MS-VBAL §5.6.5**) represents a value that is statically resolved to a [VBTypedValue](../api/RDCore.SDK.Model.Values.Abstract.VBTypedValue.html).

The parser resolves the literal's _declared type_ from the source token: a `LiteralExpressionNode` already carries a fully-typed [VBTypedValue](../api/RDCore.SDK.Model.Values.Abstract.VBTypedValue.html), including the effect of any _type-declaration character_.


---
## 3.2.0.1 Numeric Literal Types

The declared type of a numeric literal follows **MS-VBAL §3.3.2**:

1. An explicit _type-declaration character_ suffix, if present, forces the type:

   |Suffix|Declared type|
   |---|---|
   |`%`|`Integer`|
   |`&`|`Long`|
   |`^`|`LongLong`|
   |`!`|`Single`|
   |`#`|`Double`|
   |`@`|`Currency`|

2. Otherwise, an **integer literal** (no fractional part, no exponent — decimal, `&H…` hexadecimal, or
   `&O…` octal) takes the smallest of `Integer`, `Long`, `Double` that can hold its value.

3. Otherwise, a **floating-point literal** (fractional part or exponent) is `Double`.

> [!NOTE]
> `LongLong` is only produced by the `^` suffix — an unsuffixed integer literal that exceeds `Long`
> range widens to `Double`, never `LongLong`. String and `$`-suffixed identifiers are covered by the
> declared-type rules for `String`, not here.


---
## 3.2.0.2 Static Symbols

The _environment host_ defines a number of [_static symbols_](../api/RDCore.SDK.Model.Symbols.Abstract.StaticSymbol.html) that are globally defined, on top of the global [IStdConstantsModule](../api/IStdConstantsModule.html): 

|Type|Value|Literal (token)|
|[VBBooleanType](../api/RDCore.SDK.Model.Types.VBBooleanType.html)|[VBBooleanValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBBooleanValue.html)|`True`,`False`|
|[VBStringType](../api/RDCore.SDK.Model.Types.VBStringType.html)|[VBStringValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBStringValue.html)|`VBEmptyString`|
|[VBNullType](../api/RDCore.SDK.Model.Types.VBNullType.html)|[VBNullValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBNullValue.html)|`Null`|
|[VBVariantType](../api/RDCore.SDK.Model.Types.VBVariantType.html)|[VBEmptyValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBEmptyValue.html)|`Empty`|
|[VBObjectType](../api/RDCore.SDK.Model.Types.VBObjectType.html)|[VBNothingValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBNothingValue.html)|`Nothing`|


---
## 3.2.0.2.1 Instance Expressions - "Me"
> [!NOTE]
> **MS-VBAL §5.6.11** describes _instance expressions_ as _values_ with the _declared type_ defined by the class module containing the _enclosing procedure_, statically invalid within a procedural ("standard") module. At run-time, it represents the _current instance_ of the type defined by the enclosing class module and has this type as its _value type_.

It would be aligned with the specification to implement this "expression" not as such, but rather as a simple runtime artifact: the _current object_ is a common concept in many programming languages (often expressed with the token `this`). The implementation could be as simple as having the runtime context inject a `VBObjectValue` presenting the _default interface_ of the enclosing class type - pushing it to the _stack frame_ of _instance member calls_ as it would any _parameter_.

> [!TIP]
> In other words, we can get this one "for free" by having the runtime inject an implicit `Me` (`ByVal`) parameter to all _instance member calls_, pointed at the _current object_.


---
> ⏮️ [**RD-VBAL §3.1** Attributes and Directives](rd-vbal.3.2.attributes-directives.html) | ⏭️ [**RD-VBAL §3.3** Operators](rd-vbal.3.3.0.operators.html)
