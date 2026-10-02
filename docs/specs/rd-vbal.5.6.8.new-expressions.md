# 5.6.8 New Expressions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.8** New Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/b16c311f-3648-45fc-a382-1e6ddea34191).

## Syntax

|Expression|AST node|
|---|---|
|`New <class>`|[NewExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.NewExpressionNode.html)|

See [**RD-VBAL §3.0.2** Node Types](rd-vbal.3.0.2.node-types.md).

## Static Semantics

The operand of a `NewExpressionNode` binds under the _type binding context_, through
[ISymbolResolver](../api/RDCore.SDK.Runtime.Abstract.Execution.ISymbolResolver.html)`.ResolveType`. See
[**RD-VBAL §3.0.3** Binding Contexts](rd-vbal.3.0.3.binding-contexts.md) and
[**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md).

The positional qualified-type-name rule holds wherever a type name appears: an `As` clause, an `As New` clause, and
the operand of `New`. In `New A.B`, the qualifier `A` is bound as a namespace (`ISymbolResolver.ResolveQualifier`),
and the last part `B` in the type binding context. See
[**RD-VBAL §3.0.3** Binding Contexts](rd-vbal.3.0.3.binding-contexts.md#qualified-type-names).

Whether a class named by `New` is _creatable_ is not a name-lookup concern. It is checked once the name is bound.

A class whose `VB_Creatable` is `False` (the default instancing mode, _Private_, and _Public Not Creatable_) can only be
created by the modules of the project that defines it (**MS-VBAL §5.2.4.1.1**): `New` of one is a type mismatch from any
other project, and valid within its own.

---
> ⏮️ [**RD-VBAL §5.6.7** TypeOf...Is Expressions](rd-vbal.5.6.7.typeof-is-expressions.md) | ⏭️ [**RD-VBAL §5.6.9** Operator Expressions](rd-vbal.5.6.9.operator-expressions.md)
