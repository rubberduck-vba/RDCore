# 3.5.5 Placement and Licensing

The instruction model lives in **RDCore.Runtime.LanguageCore** (GPLv3), and the contracts of an activation in
**RDCore.SDK** (MIT). The interpreter that executes the instructions lives in **RDCore.Runtime** (GPLv3), like the other
RDCore implementations of the session services
([**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md)).

## Types

|Type or member|Assembly|Licence|Role|
|---|---|---|---|
|`InstructionList`|RDCore.Runtime.LanguageCore|GPLv3|A procedure body's flattened instruction list ([**RD-VBAL §3.5.1** InstructionList](rd-vbal.3.5.1.instructionlist.md)).|
|`Instruction`|RDCore.Runtime.LanguageCore|GPLv3|One entry of the list ([**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)).|
|`InstructionKind`|RDCore.Runtime.LanguageCore|GPLv3|An entry's control-flow shape.|
|`InstructionListLowering`|RDCore.Runtime.LanguageCore|GPLv3|Lowering ([**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)).|
|[ICallStackFrame](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html)|RDCore.SDK|MIT|The read-only view of an activation's state.|
|[ForLoopState](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Shared.ForLoopState.html), [ForEachState](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Shared.ForEachState.html), [ErrorHandlerState](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Shared.ErrorHandlerState.html)|RDCore.SDK|MIT|Per-activation state values ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).|
|[IProcedureInvoker](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Abstract.Execution.IProcedureInvoker.html), [CallableBindingHandle](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Bindings.CallableBindingHandle.html)|RDCore.SDK|MIT|The call contract: given a procedure symbol, a resolver, and arguments, run the procedure.|
|[ISymbolResolver](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Abstract.Execution.ISymbolResolver.html)`.TryGetAddress`, `.TryAllocate`|RDCore.SDK|MIT|Address lookup and storage allocation for a symbol ([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)).|
|[VBProcedureMemberSymbol](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.VBProject.VBProcedureMemberSymbol.html)`.Locals`, [VBReturningMemberSymbol](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.Abstract.VBReturningMemberSymbol.html)`.Locals`|RDCore.SDK|MIT|A procedure's `Dim`, `Static` and `Const` locals.|
|[ScopeTreeBuilder](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.ScopeTreeBuilder.html)|RDCore.SDK|MIT|Extracts `Locals` for name resolution.|
|`ProcedureExecutor`, its statement dispatch, and activation state (`CallStackFrame`)|RDCore.Runtime|GPLv3|The interpreter ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).|
|`RuntimeProcedureInvoker`|RDCore.Runtime|GPLv3|The concrete implementation of the call contract: frame setup, `ByVal`/`ByRef` parameter binding, function result values, and the call-depth guard ([**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)).|
|`RuntimeProcedureInvoker.HoistLocals`|RDCore.Runtime|GPLv3|Walks `Locals` for storage ([**RD-VBAL §5.4.3.1** Local Variable Declarations](rd-vbal.5.4.3.1.local-variable-declarations.md)).|
|`CallStackAwareSymbolResolver`, `RuntimeSymbolResolver`|RDCore.Runtime|GPLv3|The only two resolvers that resolve an address for `TryGetAddress` and allocate storage for `TryAllocate`.|

## The instruction model

`InstructionList`, `Instruction`, `InstructionKind` and `InstructionListLowering` live in RDCore.Runtime.LanguageCore
(GPLv3). Lowering is pure: it uses no symbol resolver and no runtime session.

The instruction set implements MS-VBAL, so it belongs to the language core, and a new instruction is a change there.
The flow analyses that follow the instruction list live there too. What they find about a procedure, such as
whether every code path assigns its return value, is on the procedure's semantic model in RDCore.SDK, where an analyzer
reads it.

`ProcedureExecutor`, its statement dispatch, and activation state are in RDCore.Runtime (GPLv3).

## Read-only interface, mutable implementation

The per-activation state is exposed on the SDK interface `ICallStackFrame`, read-only there. Only the executor
mutates it, through the concrete RDCore.Runtime class `CallStackFrame`:

|State|Read through `ICallStackFrame`|Mutated through `CallStackFrame`|
|---|---|---|
|Program counter|`Pc { get; }`|`Pc { get; set; }`|
|Function result|`ReturnValue { get; }`|`ReturnValue { get; set; }`|
|A symbol's address, including a `ByRef` alias|`TryGetAddress`|`PushByRef`|
|Block state|`TryGetBlockState`|`SetBlockState`|
|`For` loop state|`TryGetForLoopState`|`SetForLoopState`|
|`For Each` state|`TryGetForEachState`|`SetForEachState`|
|GoSub Resumption List|`GoSubDepth`|`PushGoSubReturn`, `TryPopGoSubReturn`|
|Error handler|`ErrorHandler { get; }`|`ErrorHandler { get; set; }`|

`ICallStackFrame.ReturnValue` is read-only on the SDK interface, like `ICallStackFrame.Pc`; the RDCore.Runtime
implementation, `CallStackFrame.ReturnValue`, is mutable.

`GoSubDepth` exposes a count of the GoSub Resumption List, not a peek: nothing that could look inside the list is
exposed.

The error-handler state is exposed as a plain property pair, `ErrorHandler { get; }` on `ICallStackFrame` and
`{ get; set; }` on `CallStackFrame`. This is the same shape `Pc` uses, rather than a `TryGetXState`/`SetXState`
pair, because the error-handler state is a single value per activation.

## Name resolution

`ISymbolResolver.TryGetAddress` and `ISymbolResolver.TryAllocate` apply the same read-only SDK-interface /
Runtime-implementation split to name resolution. `CallStackAwareSymbolResolver` and `RuntimeSymbolResolver`
(RDCore.Runtime) are the only two resolvers that resolve an address for `TryGetAddress` and allocate storage for
`TryAllocate`. Every compile-time-only resolver
(`CompositeSymbolResolver`, `ScopeTreeSymbolResolver`, `IntrinsicSymbolResolver`) returns `false` for both.

`ISymbolResolver.TryAllocate` mutates state: it allocates a `Static` local's storage. The other `ICallStackFrame`
and `ISymbolResolver` members listed on this page are read-only. `TryAllocate` is a mutating member of the SDK
interface because allocating session-level storage is not a hook that only the executor needs.

## Locals

`VBProcedureMemberSymbol.Locals` and `VBReturningMemberSymbol.Locals` are in RDCore.SDK (MIT). A `Dim`, `Static`
or `Const` local is carried on its declaring procedure symbol, as `Parameters` are
([**RD-VBAL §2.5.1** Runtime Entities](rd-vbal.2.5.1.runtime-entities.md)).

|Consumer|Assembly|Uses `Locals` for|
|---|---|---|
|`ScopeTreeBuilder`|RDCore.SDK|Name resolution.|
|`RuntimeProcedureInvoker.HoistLocals`|RDCore.Runtime|Storage.|

## Procedure invocation

`IProcedureInvoker` and `CallableBindingHandle` are the call contract: given a procedure symbol, a resolver, and
arguments, run the procedure. They live in RDCore.SDK (MIT).

`RuntimeProcedureInvoker` is the concrete implementation of the call contract: frame setup, `ByVal`/`ByRef` parameter binding,
function result values, and the call-depth guard. It is in RDCore.Runtime (GPLv3).

The `IProcedureInvoker` / `RuntimeProcedureInvoker` split matches the SDK-contract / Runtime-implementation split
that every other execution-engine piece follows. See
[**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md).
