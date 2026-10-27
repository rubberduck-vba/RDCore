# 5.6.12 Member Access Expressions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.12** Member Access Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/af3a4059-3059-4e79-8aa5-685324fb266a).

## Syntax

|Expression|AST node|
|---|---|
|Member access|[MemberAccessExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.MemberAccessExpressionNode.html)|

See [**RD-VBAL §3.0.2** Node Types](rd-vbal.3.0.2.node-types.md).

## Static Semantics

A member access reads the members of a class or user-defined type from the type's declaration, by the type's own
identity. A class whose member is typed as the class itself therefore resolves through any number of chained member
accesses. See [**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md#design-time-resolver-composition).

For a predeclared class `Widget`, `Widget.Size` is a member access on a variable of type `Widget`. See
[**RD-VBAL §3.1.1.6** `VB_PredeclaredId`](rd-vbal.3.1.1.attributes.md#3116-vb_predeclaredid).

## Runtime Semantics

A member of an object reached through an expression declared `Object` or `Variant` is late-bound, and what it returns is a `Variant`, whatever the
member declares. An `Integer` property read through a `Variant` is a `Variant` holding the `Integer`: `vo.P * 7000` is a `Variant` product, which widens to
the `Long` 35000 where an `Integer` product would overflow, as **MS-VBA** has it (recorded in Excel). Early-bound, through a variable declared as the class,
the same product overflows (error 6). An object, an array and a record a late-bound member returns are what they are.

A UDT field is reachable from source code in both directions, read and write:

|Direction|Source|Effect|
|---|---|---|
|Read|A `<member-access-expression>`|Reads the UDT field.|
|Write|A Let statement whose target is a UDT field ([**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md))|Let-coerces the value to the field's own declared type, and writes the field's cell.|

A UDT's fields are held in its field store. See
[**RD-VBAL §2.5.2.1.3** User-Defined Type (UDT) Values](rd-vbal.2.5.2.1.3.udt-values.md).

---
> ⏮️ [**RD-VBAL §5.6.11** Instance Expressions](rd-vbal.5.6.11.instance-expressions.md) | ⏭️ [**RD-VBAL §5.6.13** Index Expressions](rd-vbal.5.6.13.index-expressions.md)
