# 5.3.1.2 Static Procedures

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.3.1.2** Static Procedures](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/125068ec-a57e-4296-843b-5d009169de2f).

In MS-VBA, every local variable of a procedure declared `Static` has module extent, not only the ones declared
with an explicit `Static` keyword.

## Syntax

A `Sub`, a `Function` and each property accessor can be declared `Static`
([MemberDeclarationNode](../api/RDCore.SDK.Model.AST.Declarations.MemberDeclarationNode.html)`.IsStatic`).

## Semantics

The language server's symbol builder gives every local variable of a `Static` procedure the same `Static` mark as a variable declared with the `Static` keyword, so
that it has module extent and keeps its value from one call to the next ([**RD-VBAL §5.4.3.1** Local Variable Declarations](rd-vbal.5.4.3.1.local-variable-declarations.md)).
That is every local variable, whatever declared it: a `Dim`, a `ReDim` of an array the procedure declares by it, and a name that is implicitly declared
([**MS-VBAL §5.6.10**](rd-vbal.5.6.10.simple-name-expressions.md)). It is not a parameter, which is a new value at every call, and it is not a constant.

A procedure that is not declared `Static` is not made one by a local variable that is: only that variable keeps its value.

---
> ⏮️ [**RD-VBAL §5.3.1.1** Procedure Scope](rd-vbal.5.3.1.1.procedure-scope.md) | ⏭️ [**RD-VBAL §5.3.1.3** Procedure Names](rd-vbal.5.3.1.3.procedure-names.md)
