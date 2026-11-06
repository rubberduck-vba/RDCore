# 5.4.2.1 Call Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.1** Call Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f7c864a8-8fce-49dc-8347-30dc749d6576).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[CallStatementNode](../api/RDCore.SDK.Model.AST.Statements.CallStatementNode.html)|`Simple`|A `Call` statement or a bare call.|

See [**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md) and
[**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md).

Without `Call`, blanks then parentheses right after what the statement calls begin its arguments: `F (2)` calls `F` with the parenthesized argument `(2)`, which is passed by value
([**RD-VBAL §5.6.6** Parenthesized Expressions](rd-vbal.5.6.6.parenthesized-expressions.md)). In an expression, the parentheses are the argument list of what is before them: `Call F (2)` calls `F` with `2`, and `F (1) = 2` is an
assignment to `F(1)` ([**RD-VBAL §5.6.13** Index Expressions](rd-vbal.5.6.13.index-expressions.md)).

## Static Semantics

What a call statement calls is a procedure: a `Sub`, a `Function` or a `Property`, of the workspace, of a library or a `Declare`. A variable or a
constant is none, and a call statement written on one is a compile error, with `Call` or without, as it is in **MS-VBA** (recorded in Excel):

|What the statement names|Example|Outcome|
|---|---|---|
|A variable or a constant of the procedure, a parameter included|`Dim x As Long` … `x (1)`, `x`, `Call x`|[`VBC09364`](../diagnostics/vbc09364.md) _Expected Sub, Function, or Property_|
|A variable or a constant of a module, bare or qualified|`Count (1)`, `Globals.Count (1)`, `Call Count`|[`VBC09370`](../diagnostics/vbc09370.md) _Expected procedure, not variable_|
|A variable of an object, without `Call`|`Dim c As New Collection` … `c (1)`|Valid: the statement calls the object's default member.|

A `Variant` is no object: a call statement on one is the error, whatever it holds. A variable hides a procedure of the same name in the procedure that
declares it, so `P (1)` in a procedure that declares `Dim p As Long` is `VBC09364`, though the module has a `Sub P`.

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
