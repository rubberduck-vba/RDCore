# 5.4.2.20 RaiseEvent Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.20** RaiseEvent Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3795fff1-ce8a-40f7-8d2c-b1e2c1a251c4).

## Syntax

|AST node|Instruction kind|Notes|
|---|---|---|
|[KeywordStatementNode](../api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html) (`Token`: `RaiseEvent`)|`Simple`|No dedicated node type; see [**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md). The first input is the event's name, a `SimpleNameExpressionNode` that is not evaluated; the rest are the event arguments.|

## Static Semantics

[StatementStaticSemanticsEvaluator](../api/RDCore.SDK.Semantics.Static.StatementStaticSemanticsEvaluator.html) checks that:

- the event is one the enclosing class module declares ([**RD-VBAL §5.2.4.3**](rd-vbal.5.2.4.class-module-declarations.md)), reported as [VBC09322](../diagnostics/vbc09322.md) otherwise. A standard module declares no events, so a `RaiseEvent` written in one names none;
- the arguments are compatible with the event's parameter list, reported as [VBC09323](../diagnostics/vbc09323.md): there are as many as it requires and no more than it has, and a `ByVal` argument can be Let-coerced to a parameter that is neither a class nor `Object`.

The event name is not a variable and is never resolved as one: it is not an undefined name under `Option Explicit`. Each argument is an expression like any other, and is evaluated by the expression rules.

The grammar of the statement is `event-argument = expression`: an argument cannot be written with `ByVal`. The keyword is valid
only in the argument list of an external procedure's invocation ([**RD-VBAL §5.6.13.1**](rd-vbal.5.6.13.index-expressions.md)),
which a `RaiseEvent` never is, so it is a syntax error ([VBC00001](../diagnostics/vbc00001.md)) at the parser, and not a
compile error. The tree keeps it all the same, as a `ByValArgumentExpressionNode`.

> [!NOTE]
> **Not implemented.** A `ByRef` parameter whose declared type does not exactly match the variable passed to it, and an object argument for a parameter declared as a class, are not checked.

## Runtime Semantics

`RaiseEvent` invokes the procedures that handle the event of the object whose code it is written in, which is `Me`. Which procedures those are is what the object's `WithEvents` variables' assignments have attached ([**RD-VBAL §5.4.3.9**](rd-vbal.5.4.3.9.set-statement.md)): the handlers of each variable that currently holds the object, named `VariableName_EventName` ([**RD-VBAL §5.3.1.8**](rd-vbal.5.3.1.8.event-handler-declarations.md)).

- The handlers run in the order their variables were assigned. Assigning a `WithEvents` variable again moves it to the end of that order, so that the variable assigned most recently is the last to handle an event raised afterwards.
- The arguments are evaluated once, whatever the number of handlers, and passed positionally with the rules of procedure invocation ([**RD-VBAL §5.3.1.11**](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)). A `ByRef` event parameter whose argument is a variable is aliased to it, so what one handler leaves in it is what the next handler starts with, and what the raiser finds in the variable afterwards.
- An event nothing handles, and a source nothing is attached to, is not an error: `RaiseEvent` does nothing.
- An error a handler leaves unhandled stops the invocations, and is the error of the `RaiseEvent` statement.

A `ByRef` parameter whose argument is not a variable (`RaiseEvent Bump(5)`) has no variable to leave a value in, so the raiser
cannot see what a handler did to it. The handlers can: it is given a location of its own for the duration of the statement,
so that what one handler leaves in the parameter is what the next starts with. The location is freed when the statement
ends, and the next `RaiseEvent` starts from its argument again.

---
> ⏮️ [**RD-VBAL §5.4.2.19** Exit Property Statement](rd-vbal.5.4.2.19.exit-property-statement.md) | ⏭️ [**RD-VBAL §5.4.2.21** With Statement](rd-vbal.5.4.2.21.with-statement.md)
