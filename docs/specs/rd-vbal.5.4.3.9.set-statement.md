# 5.4.3.9 Set Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.3.9** Set Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f343de03-5100-41f0-8197-93546c4fc21f).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[AssignmentStatementNode](../api/RDCore.SDK.Model.AST.Statements.AssignmentStatementNode.html) (`Kind`: `Set`)|`Simple`|The same node type as `Let`, `LSet` and `RSet`, distinguished by `Kind`.|

See [**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md).

## Static Semantics

The target is a variable, a property or an unbound member: **MS-VBAL §5.4.3.9** makes the statement invalid when it
"is classified as something other than a variable, property or unbound member". A constant is
[`VBC09346`](../diagnostics/vbc09346.md) (`AssignmentToConstantNotPermitted`).

A function named as the target outside its own body - or a property of a module that has no `Property Set` - is a call of
it, as it is for a `Let` statement ([**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md)), and what
it returns is Set-assigned through its default member. As in MS-VBA (recorded in Excel), one that takes an argument is
[`VBC09339`](../diagnostics/vbc09339.md) _Argument not optional_ - before what its type is (`Set Left = Nothing`,
`Set Left$ = Nothing`) - and one whose declared type is neither a `Variant` nor an object is
[`VBC09345`](../diagnostics/vbc09345.md) _Invalid use of property_ (`Set F = Nothing` of a `Function F() As Long`), as is
`Set Err = Nothing`: MS-VBA's library declares `Err` a property. What passes is called when it runs, and what returns no
object is error `424` (`Set Command = Nothing`, `Set Now = Nothing`).

The default instance variable of a predeclared class is a valid target of a `Set` assignment, whatever is assigned to it,
as it is in **MS-VBA**.
[**MS-VBAL §5.2.4.1.2** Default Instance Variables Static Semantics](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/189fb41b-cc3a-4999-a6d2-ba89f72d2870)
says it is invalid; see
[**RD-VBAL §5.2.4.1.2**](rd-vbal.5.2.4.class-module-declarations.md#52412-default-instance-variables-static-semantics).

|Statement, for a predeclared class `Widget`|Result|
|---|---|
|`Set Widget = New Widget`|Valid. The default instance holds the new object.|
|`Set Widget = Nothing`|Valid. The default instance lets go of its object, and the next reference to `Widget` creates another.|
|A `Set` assignment to a local variable or field that is itself named `Widget`|Valid. The local variable or field hides the default instance of class `Widget`, and is an ordinary `Set` target.|
|`Set Sheet1 = …`, the default instance of an extensible class (a document module)|Compile error [`VBC09345`](../diagnostics/vbc09345.md) _Invalid use of property_, whatever is assigned.|
|`Set Debug = …`|Compile error [`VBC00001`](../diagnostics/vbc00001.md) _Syntax error_.|

See [**RD-VBAL §3.1.1** Attributes](rd-vbal.3.1.1.attributes.md) (`VB_PredeclaredId`, `VB_Extensible`).

## Runtime Semantics

The executor dispatches a Set-assignment statement to its statement runtime semantics: Set-assignment is one of the
statement kinds the `Simple` statement provider handles
([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).

1. The value expression is evaluated.
2. The value is Set-coerced
   ([**MS-VBAL §5.5.2.2** Runtime semantics](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/bdd2d85a-3236-4381-b289-002bf0bf8ffd);
   [**RD-VBAL §5.5.2.2** Runtime semantics](rd-vbal.5.5.2.2.runtime-semantics.md)).
3. The Set-coerced value is assigned to the target.

Two things happen around the assignment of an object to a variable.

- **References.** The variable holds a reference to the object it is given, and the object it held loses one. An object that loses its last reference is destroyed, and its `Class_Terminate` handler runs
  ([**RD-VBAL §5.3.1.10**](rd-vbal.5.3.1.10.lifecycle-handler-declarations.md)).
- **`WithEvents`.** When the variable is declared `WithEvents`
  ([**RD-VBAL §5.2.3.1.2**](rd-vbal.5.2.3.module-declarations.md)), its event handlers are detached from the object it holds before the assignment, and attached to the object it is given after it. They handle the events that object raises with `RaiseEvent`
  ([**RD-VBAL §5.4.2.20**](rd-vbal.5.4.2.20.raiseevent-statement.md)); an object that is destroyed handles no more.

`Set obj.Member = value` assigns a member as `Let` does ([**RD-VBAL §5.4.3.8**](rd-vbal.5.4.3.8.let-statement.md)), through the
`Property Set` or the public variable, and with the same two things around it: the references and the `WithEvents` handlers are those of
the object the variable belongs to.

The `Set` statement performs Set-coercion through the same direct entry point that
`RDCore.Runtime.Semantics.Statements.WithStatementRuntimeSemantics` uses for its own `With`-target coercion, not
through the operator pipeline. Set-coercion does not select a strategy per destination type, so it does not need
the operator pipeline. Let-assignment, by contrast, reuses the operator pipeline; see
[**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md).

## Implementation

|Type or member|Role|
|---|---|
|`RDCore.Runtime.Semantics.Statements.StatementRuntimeSemanticsProvider`|Dispatches an `AssignmentStatementNode` whose `Kind` is `Set`.|
|[ISetCoercionRuntimeSemantics](../api/RDCore.SDK.Runtime.Abstract.ISetCoercionRuntimeSemantics.html)`.EvaluateSetCoercion`|The direct Set-coercion entry point, shared with `WithStatementRuntimeSemantics`.|

---
> ⏮️ [**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md) | ⏭️ [**RD-VBAL §5.4.4** Error Handling Statements](rd-vbal.5.4.4.error-handling-statements.md)
