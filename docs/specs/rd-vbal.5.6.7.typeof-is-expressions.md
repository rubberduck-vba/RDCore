# 5.6.7 TypeOf...Is Expressions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.7** TypeOf...Is Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/22d0220a-65bc-412a-9052-adb02aadb875).

## Syntax

|Expression|AST node|
|---|---|
|`TypeOf <expr> Is <type>`|[TypeOfIsExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.TypeOfIsExpressionNode.html)|

See [**RD-VBAL §3.0.2** Node Types](rd-vbal.3.0.2.node-types.md). An intrinsic type written as the type is a
[BuiltInTypeExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.BuiltInTypeExpressionNode.html), so that it can be
refused where it is written.

## Static semantics

What is tested is declared as a user-defined type, a class, `Object` or `Variant`, written without parentheses; anything
else is [VBC09310](../diagnostics/vbc09310.md) _Type mismatch_ (a `Long`), or [VBC00001](../diagnostics/vbc00001.md)
_Syntax error_ (`TypeOf (o) Is Collection`).

The type is the name of a class, an interface, a user-defined type or an Enum type, bare or qualified, or `Object`.
What MS-VBA refuses there, RD-VBA refuses the same way (recorded in Excel):

|The type is|Example|Error|
|---|---|---|
|An intrinsic type other than `Object`|`TypeOf o Is Long`, `TypeOf o Is Date`|[VBC00001](../diagnostics/vbc00001.md) _Syntax error_|
|No name|`TypeOf o Is Nothing`, `TypeOf o Is Object()`, `TypeOf o Is String * 2`|[VBC00001](../diagnostics/vbc00001.md) _Syntax error_|
|A procedural module, a library's module, or a project|`TypeOf o Is Globals`, `TypeOf o Is Strings`, `TypeOf o Is VBA`|the error of a declared type of that name ([**RD-VBAL §5.6.16.7**](rd-vbal.5.6.16.constrained-expressions.md#56167-type-expressions))|
|Any other name: a variable's, a procedure's, or nothing's|`TypeOf o Is Count`, `TypeOf o Is Widgit`|[VBC09311](../diagnostics/vbc09311.md) _User-defined type not defined_|

## Runtime semantics

|What is tested|The type|Result|
|---|---|---|
|`Nothing`|any|run-time error 91 _Object variable or With block variable not set_|
|An object|its class, or an interface its class implements|`True`|
|An object|`Object`|`True`|
|An object|another class, a user-defined type, an Enum type|`False`|
|A value that is no object: a `Variant` holding a number, a record|any|`False`|

Error 91 for `Nothing` is MS-VBAL §5.6.7's, and MS-VBA's (recorded in Excel): test `o Is Nothing` first.

---
> ⏮️ [**RD-VBAL §5.6.6** Parenthesized Expressions](rd-vbal.5.6.6.parenthesized-expressions.md) | ⏭️ [**RD-VBAL §5.6.8** New Expressions](rd-vbal.5.6.8.new-expressions.md)
