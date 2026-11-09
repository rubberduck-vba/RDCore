# 5.4.4.3 Error Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.4.3** Error Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/70ca285b-7f18-4f0a-b0b9-7edcddf30ec4).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[ErrorStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.ErrorStatementNode.html)|`RaiseError`|`Error <number>`.|

See [**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md) and
[**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md).

## Runtime Semantics

`Error <number>` raises a run-time error directly. MS-VBAL specifies that the `Error` statement raises the error
"as if `Err.Raise` were invoked".

1. The number expression is evaluated, and Let-coerced to `Integer`.
2. A run-time error with that number is raised.

The raised error is an application error: a custom run-time error explicitly raised from workspace source code with
`Error` or `Err.Raise`. See [**RD-VBAL §2.6.3** Runtime Errors](rd-vbal.2.6.3.runtime-errors.md).

Like every other run-time error, the raised error passes through the executor's error interception and is catchable
by an error handler; see [**RD-VBAL §5.4.4.1** On Error Statement](rd-vbal.5.4.4.1.on-error-statement.md).

## Implementation

`RDCore.Runtime.Execution.ErrorHandlingEvaluator` Let-coerces the `Error` statement's number expression to
`Integer`. It does so by a direct strategy call, the same way `ConditionEvaluator` forces a condition to `Boolean`
(see [**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).

|Type or member|Role|
|---|---|
|`ErrorHandlingEvaluator`|Evaluates the number expression and Let-coerces it to `Integer`, calling the numeric Let-coercion strategy (`VBNumericLetCoercionTypeRuntimeSemantics`) directly.|
|[VBApplicationErrorInfo](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Errors.VBApplicationErrorInfo.html)|The source metadata of an application error (**RDCore.SDK**).|
