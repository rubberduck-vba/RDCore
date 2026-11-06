# 2.3.1 Composition Root

The _environment host_ is the composition root of an **RD-VBA** application. It is responsible for:

|Responsibility|See also|
|---|---|
|Composing an **RD-VBA** application from its _references_, _instructions_, and _symbols_.|[**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md)|
|Configuring the implicit storage of the _host VBA environment_.|[**RD-VBAL §2.1** Implicit Storage](rd-vbal.2.1.implicit-storage.md)|
|Loading any _application settings_ and additional _workspace resources_ into the runtime environment.|[**RD-VBAL §2.1** Implicit Storage](rd-vbal.2.1.implicit-storage.md)|

## Startup sequence

1. The environment host composes the application, as above.
2. The environment host resolves an _entry point_.
3. The environment host initiates an _execution session_ ([**RD-VBAL §2.3.1.1** Execution Session](rd-vbal.2.3.1.1.execution-session.md)).
4. The environment host constructs an [ICallStackFrame](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html) and pushes it to the _evaluation engine_.
5. The evaluation engine sequentially evaluates each instruction in the pushed frame ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).

## Symbol composition

A session is composed from several _symbol providers_, each an
[ISymbolProvider](../api/RDCore.SDK.Runtime.Abstract.Execution.ISymbolProvider.html): configuration flags, AST
declarations, reflected referenced libraries, and the environment host's own runtime and standard library
([**RD-VBAL §6.0** Standard Library](rd-vbal.6.0.standard-library.md)).

The composition root defines each `Symbol` a provider yields:

|Context|Each provided `Symbol` is defined into|
|---|---|
|Static|The semantic layer.|
|Runtime|The session symbol table, through `ISessionSymbols.TryDefine`.|

See [**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md) for `ISymbolProvider` and the session symbol table.

## Expression evaluator wiring

`RuntimeExpressionEvaluator.ProcedureInvoker` and `RuntimeExpressionEvaluator.LetCoercionProvider` are settable
properties, not constructor parameters. They are assigned after every other collaborator is composed.

The evaluator exists before its invoker is built, because `RuntimeProcedureInvoker` needs a `ProcedureExecutor`,
built from a `StatementRuntimeSemanticsProvider`, which is itself built from the same `RuntimeExpressionEvaluator`.
See [**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)
for procedure invocation.

---
## In this section

|§|Title|
|---|---|
|2.3.1.1|[Execution Session](rd-vbal.2.3.1.1.execution-session.md)|
|2.3.1.2|[Session Services](rd-vbal.2.3.1.2.session-services.md)|
|2.3.1.3|[Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)|
