# 5.4.2.23 Assert Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.23** Assert Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4deae985-1f0b-4a95-9b69-7c69a91ea7a1).

## Syntax

|AST node|Instruction kind|Notes|
|---|---|---|
|[DebugAssertStatementNode](../api/RDCore.SDK.Model.AST.Statements.DebugAssertStatementNode.html)|`Simple`|Represents `Debug.Assert`.|

## Static Semantics

`Debug` is only ever written as the `Debug.Assert` statement and the `Debug.Print` statement
([**RD-VBAL §5.4.5.8** Print Statement](rd-vbal.5.4.5.8.print-statement.md)). The host's `Debug` object is somewhere
between an object and a keyword: any other use of its name is compile error [`VBC00001`](../diagnostics/vbc00001.md)
_Syntax error_, as it is in **MS-VBA** (recorded in Excel):

|Use of `Debug`|Example|
|---|---|
|The target of a `Set`, a `Let`, or an implicit `Let`|`Set Debug = Nothing`, `Let Debug = 1`, `Debug = 1`|
|A value, an argument, or what an object variable is `Set` to|`v = Debug`, `TypeName(Debug)`, `Set o = Debug`|
|The owner of a member called otherwise than as its statement|`Call Debug.Print("x")`|
|The target of a `With` block|`With Debug` … `.Print "x"` … `End With`|

A variable, a field, or a class of the workspace that is itself named `Debug` is not the host's object, and is not
concerned.

## Runtime Semantics

A failed `Assert` call is a semantic break: it enters `Break` mode
([**RD-VBAL §2.3.2** Mode / State](rd-vbal.2.3.2.mode-state.md)).

In a release build, `Debug.Assert` leaves no instruction at all and its expression is not evaluated, as with `Debug.Print`
([**RD-VBAL §2.3.3** Configuration](rd-vbal.2.3.3.configuration.md#build-configuration)).

---
> ⏮️ [**RD-VBAL §5.4.2.22** End Statement](rd-vbal.5.4.2.22.end-statement.md) | ⏭️ [**RD-VBAL §5.4.3** Data Manipulation Statements](rd-vbal.5.4.3.data-manipulation-statements.md)
