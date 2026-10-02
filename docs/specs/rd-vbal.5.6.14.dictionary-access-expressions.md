# 5.6.14 Dictionary Access Expressions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.14** Dictionary Access Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f20c9ebc-3365-4614-9788-1cd50a504574).

## Syntax

|Expression|AST node|
|---|---|
|Dictionary access|[DictionaryAccessExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.DictionaryAccessExpressionNode.html)|

See [**RD-VBAL §3.0.2** Node Types](rd-vbal.3.0.2.node-types.md).

The token that follows the `!` operator in a dictionary access expression represents a dictionary key, which the LSP
`Key` symbol kind would fit. See [**RD-VBAL §2.5.1** Runtime Entities](rd-vbal.2.5.1.runtime-entities.md#symbol-kind).

## Runtime Semantics

`owner!member` is a call of the default member of `owner` with the name of the member as its string argument
([**RD-VBAL §5.6.13** Index Expressions](rd-vbal.5.6.13.index-expressions.md)): `d!Foo` is `d.Item("Foo")` where `Item`
is the default member of the class of `d`. A `Variant` holding the object is unwrapped first.

|Condition|Run-time error|
|---|---|
|The owner is not an object.|424 — Object required|
|The owner is `Nothing`.|91 — Object variable or With block variable not set|
|The object has no default member to bind the call to.|438 — Object doesn't support this property or method|

A `!member` expression with no owner, inside a `With` block, is a with-expression. See
[**RD-VBAL §5.6.15** With Expressions](rd-vbal.5.6.15.with-expressions.md).

---
> ⏮️ [**RD-VBAL §5.6.13** Index Expressions](rd-vbal.5.6.13.index-expressions.md) | ⏭️ [**RD-VBAL §5.6.15** With Expressions](rd-vbal.5.6.15.with-expressions.md)
