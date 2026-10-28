# 3.5.6 Compiled Execution

A procedure can run as IL instead of in the interpreter ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)). The setting
`Environment.ExecutionEngine` decides it ([**RD-VBAL §2.3.3** Configuration](rd-vbal.2.3.3.configuration.md#build-configuration)):

- `Interpreter`, the default, runs every procedure in the interpreter.
- `Compiled` compiles a procedure the first time it is called, when compiled code can do everything in it. Any other procedure runs in the interpreter.

A program does the same thing in either engine: what it prints, the errors it raises, and where it raises them.

## When a procedure is compiled

A procedure is compiled when it is a `Sub`, a `Function` or a `Property Get` of a standard module and its signature meets these conditions:

- every parameter is a scalar passed `ByVal`;
- every variable is a scalar and none is `Static`;
- a function returns a scalar.

The scalars are `Byte`, `Integer`, `Long`, `LongLong`, `Boolean`, `Currency`, `Single`, `Double` and `Date`.

Its body may contain only these:

|Kind|What compiled code does|
|---|---|
|Expressions|Literals, constants, the procedure's parameters and variables, the function's own result variable, and parentheses.|
|Operators|`+`, `-`, `*`, `/`, `\`, `Mod`, the comparisons, `And`, `Or`, `Xor`, `Eqv`, `Imp`, `Not`, and negation, on operands of scalar types.|
|Statements|`Let` of a variable or of the function's result.|
|Control flow|`If`, `ElseIf` and `Else`; `Do` and `Loop` with `While` or `Until`; `While` … `Wend`; `For` … `Next` with a counter that is a variable of the procedure and not a `Boolean`; `Exit Do`, `Exit For`, `Exit Sub`, `Exit Function` and `Exit Property`.|

Anything else keeps the procedure in the interpreter, for example an error handler, `GoTo`, `GoSub`, `With`, `Select Case` or `For Each`, or a call, a member, an
element of an array, a `String` or a `Variant`. `rdc compilation` lists, for each procedure of a workspace, whether it is compiled, and what keeps it in
the interpreter.

No procedure runs compiled under a debugger or during an analysis.

## The semantics are the interpreter's

Compiled code evaluates an expression exactly as the interpreter evaluates it on payloads (IR lowering S1.2). It calls the same operations, in
`RDCore.Runtime.Compiled`:

- `PayloadOperations`: the integer operations, the comparisons, the logical operators and the exact conversions. Each returns a status: `Ok`, `Overflow`,
  `DivisionByZero` or `Decline`.
- `Extended`: an arithmetic operation of a `Single`, a `Double` or a `Date`, which MS-VBA computes in the x87's 80-bit format and rounds where the value is
  stored (MS-VBAL §5.6.9.3). Compiled code computes the operation in `Double` when the result is exact; that is the value the 80-bit format would compute.
  Otherwise it computes in the 80-bit format.

When an operation fails or declines, compiled code hands the activation to the interpreter at that instruction. It writes every variable back into its
cell, the function result into its variable, and the state of each `For` loop it is in into the frame. The interpreter then evaluates the instruction as it
always does, so the error raised, its message and its location are the interpreter's. The rest of the activation runs in the interpreter.

Compiled code does the same when the program is to stop or a debugger attaches, at the back edges of its loops: the interpreter then breaks, or runs the
activation under the debugger.

## Implementation

|Type|Role|
|---|---|
|`ProcedureAnalysis`|Decides whether a procedure is compiled, and translates its instructions into the payload operations they are made of.|
|`ProcedureEmitter`|Writes one static method per procedure, `Run(CompiledActivation)`, with an IL label for each instruction.|
|`ProcedureCompiler`|Compiles each procedure on its first call, into one collectible assembly for each program image, and keeps the result for as long as the session's scopes are the same.|
|`CompiledActivation`|What compiled code is given: its parameters, the poll, and the hand-over to the interpreter.|
|`PayloadOperations`, `Extended`|The operations compiled code and the interpreter share.|

The IL calls only members of `RDCore.Runtime.Compiled`, and a test checks it.

---
> ⏮️ [**RD-VBAL §3.5.5** Placement and Licensing](rd-vbal.3.5.5.placement-and-licensing.md) | ⏭️ [**RD-VBAL §4.0** Program Structure and Organization](rd-vbal.4.0.program-structure.md)
