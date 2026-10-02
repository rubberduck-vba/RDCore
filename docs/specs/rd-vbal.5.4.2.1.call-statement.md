# 5.4.2.1 Call Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.1** Call Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f7c864a8-8fce-49dc-8347-30dc749d6576).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[CallStatementNode](../api/RDCore.SDK.Model.AST.Statements.CallStatementNode.html)|`Simple`|A `Call` statement or a bare call.|

See [**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md) and
[**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md).

## Runtime Semantics

A `Call` statement or a bare-call statement whose callee is a bare name (`Foo`, `Foo(...)`, `Call Foo(...)`) invokes
it through a callable binding
([ICallableBindingFactory](../api/RDCore.SDK.Model.Values.Bindings.ICallableBindingFactory.html)):

|Callee|Invoked through|
|---|---|
|A procedure of the workspace|[IProcedureInvoker](../api/RDCore.SDK.Runtime.Abstract.Execution.IProcedureInvoker.html)|
|A member with an external target (a standard-library member)|[IExternalDispatcher](../api/RDCore.SDK.Runtime.Abstract.Execution.IExternalDispatcher.html) ([**RD-VBAL §6.0** Standard Library](rd-vbal.6.0.standard-library.md))|

The unparenthesized argument form of a bare-call statement (`Foo 1, 2`, `p.Grow 2`) and a callee reached through a member access (`obj.Foo`, `Call obj.Foo(1)`) are invoked the
same way: a member of an object, of a module (`Strings.LenB`) or of a project (`VBA.LenB`) is a callee like any other
([**RD-VBAL §5.6.12**](rd-vbal.5.6.12.member-access-expressions.md)).

A bare reference to a `Sub`, `Function` or `Property Get` invokes a procedure the same way
([**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md),
[**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md)).

A callee's run-time error propagates like any other run-time error. The callee's error result
([RuntimeSemanticsEvaluationResult](../api/RDCore.SDK.Runtime.Shared.RuntimeSemanticsEvaluationResult.html)) is
converted back into an `Error` outcome by the caller's `ExecuteCall`.

## Implementation

- A `Simple` instruction is dispatched by its statement type through
  `RDCore.Runtime.Semantics.Statements.IStatementRuntimeSemanticsProvider`
  ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)); `ExecuteCall` handles a `CallStatementNode`.
- `RDCore.Runtime.Execution.RuntimeProcedureInvoker` is the `IProcedureInvoker` implementation.
- `RDCore.Runtime.Execution.RuntimeCallableBindingFactory` is the `ICallableBindingFactory` implementation. A member
  that carries [SymbolProperties](../api/RDCore.SDK.Model.Symbols.Abstract.SymbolProperties.html)`.ExternalTarget`
  is bound to the external dispatcher; any other member is bound to the procedure invoker.

---
> ⏮️ [**RD-VBAL §5.4.2** Control Statements](rd-vbal.5.4.2.control-statements.md) | ⏭️ [**RD-VBAL §5.4.2.2** While Statement](rd-vbal.5.4.2.2.while-statement.md)
