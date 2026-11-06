# 3.5.6 Compiled Execution

A procedure can run as IL instead of in the interpreter ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)). The setting
`Environment.ExecutionEngine` decides it ([**RD-VBAL §2.3.3** Configuration](rd-vbal.2.3.3.configuration.md#build-configuration)):

- `Interpreter`, the default, runs every procedure in the interpreter.
- `Compiled` compiles a procedure the first time it is called, when compiled code can do enough of it. Any other procedure runs in the interpreter.

A program does the same thing in either engine: what it prints, the errors it raises, and where it raises them.

## When a procedure is compiled

Any `Sub`, `Function` or `Property` procedure can be compiled, in a standard module or in a class. Compiled code holds the procedure's scalar parameters,
passed `ByVal` or `ByRef`, its scalar variables, and the result of a function that returns a scalar. The scalars are `Byte`, `Integer`, `Long`, `LongLong`,
`Boolean`, `Currency`, `Single`, `Double` and `Date`. Everything else the procedure has stays in its frame, as in the interpreter: `Me`, a `ParamArray`,
an object, a `String`, a `Variant`, an array, a record, and a `Static` variable.

Compiled code reads and writes a `Variant` parameter or variable where its frame keeps it, whenever the `Variant` holds a value of a scalar type. An
operation with a `Variant` operand is worked out for the type of the value the `Variant` holds when it runs, as the interpreter works it out (MS-VBAL
§5.6.9), so `V + 1` adds two `Integer`s while `V` holds an `Integer`, and two `Double`s once it holds a `Double`. A `Variant` assigned a scalar holds it as
its own type (MS-VBAL §5.5.1.2.12). When the `Variant` holds anything else (`Empty`, `Null`, a `String`, an object, an array), or the operation would
overflow its type, the interpreter runs that statement on its own and compiled code goes on after it. That is where the interpreter widens an `Integer`
that overflows to a `Long`, and where a `Variant` that held an object lets go of it.

Compiled code does these itself:

|Kind|What compiled code does|
|---|---|
|Expressions|Literals, constants, the procedure's parameters and variables, its `Variant`s, the function's own result variable, and parentheses.|
|Operators|`+`, `-`, `*`, `/`, `\`, `Mod`, the comparisons, `And`, `Or`, `Xor`, `Eqv`, `Imp`, `Not`, and negation, on operands of scalar types and on `Variant`s that hold one.|
|Statements|`Let` of a variable, of a `Variant`, or of the function's result.|
|Calls|A call statement with arguments, or a function's result assigned whole, of a procedure of a standard module whose parameters are scalars (`Sub` or `Function`). A `ByRef` argument is a variable of the procedure of the parameter's type, or a value that is no variable, which the callee is given a copy of.|
|Control flow|`If`, `ElseIf` and `Else`; `Do` and `Loop` with `While` or `Until`; `While` … `Wend`; `For` … `Next` with a counter that is a variable of the procedure, not a `Boolean`, and not passed `ByRef`; the `Next` of a `For` with a `Variant` counter, whose `For` statement the interpreter runs, since it decides the type the loop counts in; `Select Case` of a scalar selector; `Exit Do`, `Exit For`, `Exit Sub`, `Exit Function` and `Exit Property`; `GoSub` and `Return`.|
|Errors|`On Error GoTo` a label, `On Error Resume Next`, `On Error GoTo 0` and `On Error GoTo -1`.|

The interpreter runs any other statement on its own, inside the compiled procedure. Examples are a `GoTo`; `On … GoTo` and `On … GoSub`; `Resume`,
`Resume Next`, `Resume` a label and the `Error` statement (see Error handling, below); a statement that uses something that stays in the frame (a
`String`, an object, a field of `Me`, a `Static`); a call of a member (`Err.Number` is one); an element of an array; and a `For` whose counter or bounds
compiled code doesn't do. Compiled code first writes its variables into the frame. The interpreter runs the statement, and compiled code reads
the variables back and goes on wherever the statement left the procedure. A statement the interpreter runs this way behaves exactly as it does in the
interpreter, errors included.

A variable that such a statement gives an address, with `VarPtr` or as an argument passed `ByRef` to a `Declare`d procedure, is read and written at that
address from then on. Bytes a debugger writes at that address while the program waits are the variable's, and compiled code reads them when it goes on. If
compiled code can't read a variable back from the frame, or write it there, the interpreter runs the rest of the activation, with the variables as the frame
has them.

A `Select Case` of a scalar selector evaluates the selector once, and compares it with the values of each `Case` in order, as the interpreter does
([**RD-VBAL §5.4.2.10**](rd-vbal.5.4.2.10.select-case-statement.md)): each value converted to the selector's type, except an integer compared with a `Single`
selector, which is compared at its exact value. When a value can't be converted exactly, compiled code hands the activation to the interpreter at that
`Case`, as it does when an operation declines (below). A `Long` too large for an `Integer` selector is one; so is every `Double` compared with a `Single`
selector, `4#` included, since only a `Single` converts exactly to a `Double` and not the other way. The interpreter runs a `Case` whose value is no scalar,
such as a `String`, on its own, with the selector compiled code evaluated. When compiled code declines the selector itself as it evaluates it (an operation
that declines, such as `D \ 1` with `D` a `Double`), the interpreter runs the rest of the activation, from the `Select Case`.

The interpreter also runs the statements of a block whose state it keeps in the frame, each on its own: a `With` and its `End With`, and each statement in
the block that uses its object; a `For Each` and its `Next`; and a `Select Case` and its `Case`s when the selector is no scalar, such as a `String` or a
`Variant`, or is a scalar compiled code can't evaluate, such as `Len(S)` with `S` a `String`. Compiled code does the rest of the block. What a block holds is let go of where the interpreter lets go of it, so a `Class_Terminate` runs at the
same place in either engine: the object of a `With` at its `End With` (MS-VBAL §5.4.2.21), and what a `For Each` reads where the program leaves the loop,
by its `Next`, an `Exit For`, a jump or a `Return`.

These keep the whole procedure in the interpreter:

- a procedure in which compiled code would do nothing itself, because the interpreter runs every statement that does something;
- a procedure with a loop the interpreter would run whole: the interpreter runs a statement in the loop (a `For Each` is one), and compiled code does
  nothing at each pass of it, no assignment, call, condition or `Case`, no `Loop While` or `Loop Until`, no `Next` of a `For`. The loop is the innermost
  one around the statement: what an outer loop or a loop beside it does is no gain for this one. Around each such statement compiled code would only write
  its variables into the frame and read them back, which costs more than it saves.

A procedure whose frame holds something compiled code doesn't is always called with its frame, through the interpreter's call, never through the
compiled code of its caller. `rdc compilation` lists, for each procedure of a workspace, whether it is compiled and what keeps it in the interpreter, and,
for a compiled procedure, what the interpreter runs of it.

No procedure runs compiled during an analysis. Under a debugger, compiled code writes its variables into its frame before each call, so the debugger sees
every activation while the program waits.

## Parameters passed by reference

A `ByRef` parameter is a second name for its caller's variable (MS-VBAL §5.3.1.11) in either engine. Compiled code holds a `ByRef` parameter, and every
variable it passes `ByRef`, in a cell for the whole activation, and the activation's frame holds the same cell. So what the callee writes, the caller reads.
The same is true when either is handed to the interpreter, when a debugger reads or assigns the variable, and when the same variable is passed to two
parameters. A parameter bound to something other than a variable's cell, such as a field or an element of an array, is the interpreter's from the start.

## The semantics are the interpreter's

Compiled code evaluates an expression exactly as the interpreter evaluates it on payloads. It calls the same operations, in
`RDCore.Runtime.Compiled`:

- `PayloadOperations`: the integer operations, the comparisons, the logical operators and the exact conversions. Each returns a status: `Ok`, `Overflow`,
  `DivisionByZero` or `Decline`.
- `Extended`: an arithmetic operation of a `Single`, a `Double` or a `Date`, which MS-VBA computes in the x87's 80-bit format and rounds where the value is
  stored (MS-VBAL §5.6.9.3). Compiled code computes the operation in `Double` when the result is exact; that is the value the 80-bit format would compute.
  Otherwise it computes in the 80-bit format.
- `VariantOperations`: a `Variant` read and written where its frame keeps it, and an operation with a `Variant` operand, worked out for the types of the
  values its operands hold. Each calls the interpreter's own code for it.

When an operation fails or declines, compiled code hands the activation to the interpreter at that instruction. It writes every variable back into its
cell, the function result into its variable, the state of each `For` loop whose `For` statement ran, and the selector of each `Select Case` whose `Select`
ran, into the frame. A loop that ended keeps the end and the step it last had, as it does in the interpreter: a `Resume` can go back into it, and its `For`
statement can fail when it runs again. The interpreter then evaluates the instruction as it always does, so the error raised, its message and its location
are the interpreter's. The rest of the activation runs in the interpreter.

Compiled code does the same when the program is to stop or a debugger attaches, at the back edges of its loops: the interpreter then breaks, or runs the
activation under the debugger.

## Error handling

A procedure with an `On Error` statement is handed to the interpreter for one instruction at a time:

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
- the code was edited (while the program waited in a callee, the activation goes on after the call in the edited code, with its handler, its `GoSub` returns
  and its loops where they now are);
- the activation goes on inside a `For` loop whose state the frame doesn't have: a `Resume` to a label inside a loop whose `For` statement never ran.

`Exit` and the end of the body clear `Err` after an error a handler caught, as in the interpreter (MS-VBAL §6.1.3.2.1).

## Implementation

|Type|Role|
|---|---|
|`ProcedureAnalysis`|Decides whether a procedure is compiled, and translates its instructions into the payload operations they are made of.|
|`ProcedureEmitter`|Writes one static method per procedure, `Run(CompiledActivation)`, with an IL label for each instruction.|
|`ProcedureCompiler`|Compiles each procedure on its first call, into one collectible assembly for each program image, and keeps the result for as long as the session's scopes are the same.|
|`CompiledActivation`|What compiled code is given: its parameters, the cells of its `ByRef` variables, its calls, the poll, the hand-over to the interpreter, and `Recover`, which has the interpreter run one instruction.|
|`PayloadOperations`|The operations compiled code and the interpreter share.|
|`VariantOperations`|What compiled code does with a `Variant`: reads and writes it in the frame, and runs its operations through the interpreter's own code.|
|`Extended`|The floating-point arithmetic of compiled code: in `Double` where the result is exact, and in the 80-bit format otherwise. The interpreter computes the same operations with `ExtendedIntermediates`.|

The IL calls only members of `RDCore.Runtime.Compiled`, and a test checks it.

---
> ⏮️ [**RD-VBAL §3.5.5** Placement and Licensing](rd-vbal.3.5.5.placement-and-licensing.md) | ⏭️ [**RD-VBAL §4.0** Program Structure and Organization](rd-vbal.4.0.program-structure.md)
