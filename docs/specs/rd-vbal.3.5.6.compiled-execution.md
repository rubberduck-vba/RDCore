# 3.5.6 Compiled Execution

A procedure can run as IL instead of in the interpreter ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)). The setting
`Environment.ExecutionEngine` decides it ([**RD-VBAL §2.3.3** Configuration](rd-vbal.2.3.3.configuration.md#build-configuration)):

- `Interpreter`, the default, runs every procedure in the interpreter.
- `Compiled` compiles a procedure the first time it is called, when compiled code can do everything in it. Any other procedure runs in the interpreter.

A program does the same thing in either engine: what it prints, the errors it raises, and where it raises them.

## When a procedure is compiled

A procedure is compiled when it is a `Sub`, a `Function` or a `Property Get` of a standard module and its signature meets these conditions:

- every parameter is a scalar, passed `ByVal` or `ByRef`, and none is a `ParamArray`;
- every variable is a scalar and none is `Static`;
- a function returns a scalar.

The scalars are `Byte`, `Integer`, `Long`, `LongLong`, `Boolean`, `Currency`, `Single`, `Double` and `Date`.

Its body may contain only these:

|Kind|What compiled code does|
|---|---|
|Expressions|Literals, constants, the procedure's parameters and variables, the function's own result variable, and parentheses.|
|Operators|`+`, `-`, `*`, `/`, `\`, `Mod`, the comparisons, `And`, `Or`, `Xor`, `Eqv`, `Imp`, `Not`, and negation, on operands of scalar types.|
|Statements|`Let` of a variable or of the function's result.|
|Calls|A call statement with arguments, or a function's result assigned whole, of a procedure of a standard module whose parameters are scalars. A `ByRef` argument is a variable of the procedure of the parameter's type, or a value that is no variable, which the callee is given a copy of.|
|Control flow|`If`, `ElseIf` and `Else`; `Do` and `Loop` with `While` or `Until`; `While` … `Wend`; `For` … `Next` with a counter that is a variable of the procedure, not a `Boolean`, and not passed `ByRef`; `Exit Do`, `Exit For`, `Exit Sub`, `Exit Function` and `Exit Property`; `GoSub` and `Return`; `On … GoTo` and `On … GoSub`.|
|Errors|`On Error GoTo` a label, `On Error Resume Next`, `On Error GoTo 0` and `On Error GoTo -1`; `Resume`, `Resume Next` and `Resume` a label; the `Error` statement.|

Anything else keeps the procedure in the interpreter, for example `GoTo`, `With`, `Select Case` or `For Each`, a member (`Err.Number` is one), an
element of an array, a `String` or a `Variant`. `rdc compilation` lists, for each procedure of a workspace, whether it is compiled, and what keeps it in
the interpreter.

No procedure runs compiled during an analysis. Under a debugger, compiled code writes its variables into its frame before each call, so the debugger sees
every activation while the program waits.

## Parameters passed by reference

A `ByRef` parameter is a second name for its caller's variable (MS-VBAL §5.3.1.11) in either engine. Compiled code holds a `ByRef` parameter, and every
variable it passes `ByRef`, in a cell for the whole activation, and the activation's frame holds the same cell. So what the callee writes, the caller reads.
The same is true when either is handed to the interpreter, when a debugger reads or assigns the variable, and when the same variable is passed to two
parameters. A parameter bound to something other than a variable's cell, such as a field or an element of an array, is the interpreter's from the start.

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

## Error handling

A procedure with an `On Error` statement is handed to the interpreter for one instruction at a time (IR lowering S2.4):

- **An instruction that fails.** Compiled code writes its variables into the frame as above. The interpreter runs that instruction alone and gives its error to
  the activation's handler, as it gives any error, so `Err`, `Erl` and the error break of a debugger are the interpreter's. If the handler goes on with the
  activation, compiled code reads its variables back and goes on at the instruction the handler sent it to:
  - the one after the error for `On Error Resume Next`;
  - the handler's label for `On Error GoTo`.
- **An error a callee raises** goes to the handler the same way.
- **The `On Error` statements** set the policy of the activation's frame, as the interpreter does.
- **`Resume`, the `Error` statement, `On … GoTo` and `On … GoSub`** are always run by the interpreter alone, in any procedure. Compiled code goes on where
  the interpreter leaves the activation.
- **`GoSub` and `Return`** use the frame's list of the places to return to. A `Return` with no `GoSub` is error 3, which the interpreter raises.

The rest of the activation runs in the interpreter instead when any of these holds:

- the program waited while the instruction ran;
- a debugger wants the interpreter;
- the code was edited;
- the activation goes on inside a `For` loop whose state the frame doesn't have, such as after an error in the loop's own `For` statement.

`Exit` and the end of the body clear `Err` after an error a handler caught, as in the interpreter (MS-VBAL §6.1.3.2.1).

## Implementation

|Type|Role|
|---|---|
|`ProcedureAnalysis`|Decides whether a procedure is compiled, and translates its instructions into the payload operations they are made of.|
|`ProcedureEmitter`|Writes one static method per procedure, `Run(CompiledActivation)`, with an IL label for each instruction.|
|`ProcedureCompiler`|Compiles each procedure on its first call, into one collectible assembly for each program image, and keeps the result for as long as the session's scopes are the same.|
|`CompiledActivation`|What compiled code is given: its parameters, the cells of its `ByRef` variables, its calls, the poll, the hand-over to the interpreter, and `Recover`, which has the interpreter run one instruction.|
|`PayloadOperations`|The operations compiled code and the interpreter share.|
|`Extended`|The floating-point arithmetic of compiled code: in `Double` where the result is exact, and in the 80-bit format otherwise. The interpreter computes the same operations with `ExtendedIntermediates`.|

The IL calls only members of `RDCore.Runtime.Compiled`, and a test checks it.

---
> ⏮️ [**RD-VBAL §3.5.5** Placement and Licensing](rd-vbal.3.5.5.placement-and-licensing.md) | ⏭️ [**RD-VBAL §4.0** Program Structure and Organization](rd-vbal.4.0.program-structure.md)
