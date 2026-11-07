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

### Variants

Compiled code reads and writes a `Variant` parameter or variable where its frame keeps it, whenever the `Variant` holds a value of a scalar type. An
operation with a `Variant` operand is worked out for the type of the value the `Variant` holds when it runs, as the interpreter works it out (MS-VBAL
§5.6.9), so `V + 1` adds two `Integer`s while `V` holds an `Integer`, and two `Double`s once it holds a `Double`. A `Variant` assigned a scalar holds it as
its own type (MS-VBAL §5.5.1.2.12). When the `Variant` holds anything else (`Empty`, `Null`, a `String`, an object, an array), or the operation would
overflow its type, the interpreter runs that statement on its own and compiled code goes on after it. That is where the interpreter widens an `Integer`
that overflows to a `Long`, and where a `Variant` that held an object lets go of it. A `Variant` holding a `Single` or a `Double` assigned to a variable
of an integer type is the interpreter's too, whole or not: it rounds the value (MS-VBAL §5.5.1.2.1).

When a procedure is compiled, the compiler also guesses the type each of its `Variant`s holds. The guess comes from what the procedure assigns the
`Variant` and from the type a `For` loop of a `Variant` counter counts in. For `S = 0#` and then `S = S + I` with `I` an `Integer`, it guesses `Double`. An
operation on `Variant`s that hold the types they are guessed to hold is then compiled as an operation on those types, using the same operations as the
interpreter (`PayloadOperations`). The `Next` of a `For` loop of a `Variant` counter, and a condition such as `S > 100`, are compiled the same way. Each
time it runs, compiled code first checks the type the `Variant` holds. When it does not hold the guessed type, the operation is worked out as above, for
the type it does hold. That is the case for an `Empty` `Variant` before its first assignment, a `Variant` that came to hold a `String` or `Null`, and a sum
that has overflowed to a wider type. A wrong guess makes the operation slower; its result is the same.

### Strings

Compiled code reads and writes a `String` parameter or variable where its frame keeps it, too, unless it is a fixed-length `String`, which pads or truncates
what it is assigned. A `Let` of a literal, of another `String` variable, or of `String`s joined by `&` (or by `+`, of two `String`s) assigns the text it is
(MS-VBAL §5.6.9.4, §5.6.5), and two such `String`s are compared under the module's `Option Compare` (MS-VBAL §5.6.9.5), as the interpreter compares them.
`vbNullString` reads as the zero-length string, and a copy of it is still `vbNullString`. A `String` that ends with a lone byte (`ChrB(65)`) is the
interpreter's to join and compare: compiled code hands it each statement that reads one. So is a call of a library function that takes or returns a
`String`, such as `Len(S)` or `CStr(I)`.

Compiled code calls a procedure of a standard module with a `String` parameter as the interpreter does (MS-VBAL §5.3.1.11). Given by value, the
parameter gets the text of the `String`s given (`vbNullString` stays `vbNullString`). Given by reference, it is a second name for the `String`
variable given. Either way the callee holds its own frame, which the interpreter makes. A `Variant` or a number given to a `String` parameter, a constant,
a fixed-length `String`, a field and an element of an array are the interpreter's to give, and it makes the call.

### Fields of `Me`

In a procedure of a class, compiled code reads and writes a field of `Me` of a scalar type where the object keeps it, so the member's code and a caller
outside the class (`C.Total = 10`) see the same field. A field passed `ByRef` is passed as the field itself: what the callee assigns its parameter, it
assigns the field. Each object has its own fields, and a member called on one reads and writes that object's.

### Objects

Compiled code reads the reference held by an object parameter or variable (declared `As Object`, or as a class or an interface) where its frame keeps it.
It compares two such references with `Is`, or one with `Nothing`, and tests one with `TypeOf`, as the interpreter does (MS-VBAL §5.6.9.7, §5.6.7). A
`Set` of the variable is the interpreter's, and compiled code reads the variable again after it. `TypeOf` of `Nothing` is also the interpreter's, which
raises error 91. So is a variable declared `As New`, which the interpreter makes when it is read.

Compiled code also calls a member of such a variable declared as a class or an interface (`C.Add 1`, `N = C.Total()`). Each time, it finds the member the
object's class has for it, as the interpreter does (MS-VBAL §5.3.1.9): the member itself, or, for a variable declared as an interface, the member the
object's class implements it with. It then calls that member as the interpreter calls it, with the object as `Me`, so the object lives at least as long as
the call. A call on `Nothing` is the interpreter's, which raises error 91, and so is a call of a variable declared `Object` or `Variant`, which is
late-bound.

When the member is compiled too, compiled code calls the member's compiled code directly. A member that keeps nothing in its frame but `Me` and values of
scalar types runs without a frame, as a compiled procedure called directly does. Its activation still holds `Me`, so an object is let go of, and its
`Class_Terminate` runs, exactly when the interpreter would do it. The activation gets the frame the interpreter would have made as soon as anything needs
it: an error, a statement the interpreter runs, another object's `Class_Terminate`. Under a debugger, every member gets its frame when it is called.

Compiled code remembers where it last found an object's fields, and looks for them again for another object. An object keeps the same place for each
field for as long as it lives, and a procedure is compiled again when its class is edited. What compiled code remembers never keeps an object alive: once
nothing else holds the object, it is gone.

### What compiled code does itself

Compiled code does these itself:

|Kind|What compiled code does|
|---|---|
|Expressions|Literals, `String` literals among them, constants, the procedure's parameters and variables, its `Variant`s and `String`s, the scalar fields of `Me`, the function's own result variable, and parentheses.|
|Operators|`+`, `-`, `*`, `/`, `\`, `Mod`, the comparisons, `And`, `Or`, `Xor`, `Eqv`, `Imp`, `Not`, and negation, on operands of scalar types and on `Variant`s that hold one; `&`, `+` and the comparisons of two `String`s; `Is` of the procedure's object variables and `Nothing`, and `TypeOf` of one of its object variables.|
|Statements|`Let` of a variable, of a `Variant`, of a `String`, of a scalar field of `Me`, or of the function's result.|
|Calls|A call statement with arguments, or a function's result assigned whole, of a procedure of a standard module, or of a member of one of the procedure's own object variables declared as a class or interface, when every parameter is of a scalar type (`Sub` or `Function`); of a procedure of a standard module, a parameter may also be a `String`. A `ByRef` argument is a variable of the procedure of the parameter's type, or a value that is no variable, which the callee is given a copy of.|
|Control flow|`If`, `ElseIf` and `Else`; `Do` and `Loop` with `While` or `Until`; `While` … `Wend`; `For` … `Next` with a counter that is a variable of the procedure, not a `Boolean`, and not passed `ByRef`; the `Next` of a `For` with a `Variant` counter, whose `For` statement the interpreter runs, since it decides the type the loop counts in; `Select Case` of a scalar selector; `Exit Do`, `Exit For`, `Exit Sub`, `Exit Function` and `Exit Property`; `GoSub` and `Return`.|
|Errors|`On Error GoTo` a label, `On Error Resume Next`, `On Error GoTo 0` and `On Error GoTo -1`.|

The interpreter runs any other statement on its own, inside the compiled procedure. Examples are a `GoTo`; `On … GoTo` and `On … GoSub`; `Resume`,
`Resume Next`, `Resume` a label and the `Error` statement (see Error handling, below); a statement that uses something that stays in the frame (a
fixed-length `String`, an object, a field of `Me` that is not of a scalar type, a `Static`); a call of a member of anything other than one of the procedure's object variables (`Err.Number` is one); an element of an array; and a `For` whose counter or bounds
compiled code does not do. Compiled code first writes its variables into the frame. The interpreter runs the statement, and compiled code reads
the variables back and goes on wherever the statement left the procedure. A statement the interpreter runs this way behaves exactly as it does in the
interpreter, errors included.

A variable that such a statement gives an address, with `VarPtr` or as an argument passed `ByRef` to a `Declare`d procedure, is read and written at that
address from then on. Bytes a debugger writes at that address while the program waits are the variable's, and compiled code reads them when it goes on. If
compiled code cannot read a variable back from the frame, or write it there, the interpreter runs the rest of the activation, with the variables as the frame
has them.

A `Select Case` of a scalar selector evaluates the selector once, and compares it with the values of each `Case` in order, as the interpreter does
([**RD-VBAL §5.4.2.10**](rd-vbal.5.4.2.10.select-case-statement.md)): each value converted to the selector's type, except an integer compared with a `Single`
selector, which is compared at its exact value. When a value cannot be converted exactly, compiled code hands the activation to the interpreter at that
`Case`, as it does when an operation declines (below). A `Long` too large for an `Integer` selector is one; so is every `Double` compared with a `Single`
selector, `4#` included, since only a `Single` converts exactly to a `Double` and not the other way. The interpreter runs a `Case` whose value is no scalar,
such as a `String`, on its own, with the selector compiled code evaluated. When compiled code declines the selector itself as it evaluates it (an operation
that declines, such as `D \ 1` with `D` a `Double`), the interpreter runs the rest of the activation, from the `Select Case`.

The interpreter also runs the statements of a block whose state it keeps in the frame, each on its own: a `With` and its `End With`, and each statement in
the block that uses its object; a `For Each` and its `Next`; and a `Select Case` and its `Case`s when the selector is no scalar, such as a `String` or a
`Variant`, or is a scalar compiled code cannot evaluate, such as `Len(S)` with `S` a `String`. Compiled code does the rest of the block. What a block holds is let go of where the interpreter lets go of it, so a `Class_Terminate` runs at the
same place in either engine: the object of a `With` at its `End With` (MS-VBAL §5.4.2.21), and what a `For Each` reads where the program leaves the loop,
by its `Next`, an `Exit For`, a jump or a `Return`.

These keep the whole procedure in the interpreter:

- a procedure in which compiled code would do nothing itself, because the interpreter runs every statement that does something;
- a procedure with a loop the interpreter would run whole: the interpreter runs a statement in the loop (a `For Each` is one), and compiled code does
  nothing at each pass of it, no assignment, call, condition or `Case`, no `Loop While` or `Loop Until`, no `Next` of a `For`. The loop is the innermost
  one around the statement: what an outer loop or a loop beside it does is no gain for this one. Around each such statement compiled code would only write
  its variables into the frame and read them back, which costs more than it saves.

A procedure whose frame holds something compiled code does not hold is always called with its frame, through the interpreter's call, never through the
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
  values its operands hold. Each calls the interpreter's own code for it. A `Variant` read as the type it is guessed to hold fails when it holds another,
  and a `Variant` written as one fails when it holds an object.
- `ObjectOperations`: the reference an object variable holds, read where its frame keeps it, and `Is` of two of them or of one and `Nothing`. A
  variable whose reference compiled code cannot read fails.
- `StringOperations`: the text a `String` variable holds, read where its frame keeps it, two texts joined, and a text written as a `Let` assigns it. A
  `String` that ends with a lone byte fails its read. Compiled code compares two texts with the interpreter's own comparison of the module's mode.

When an operation fails or declines, compiled code hands the activation to the interpreter at that instruction. It writes every variable back into its
cell, the function result into its variable, the state of each `For` loop whose `For` statement ran, and the selector of each `Select Case` whose `Select`
ran, into the frame. A loop that ended keeps the end and the step it last had, as it does in the interpreter: a `Resume` can go back into it, and its `For`
statement can fail when it runs again. The interpreter then evaluates the instruction as it always does, so the error raised, its message and its location
are the interpreter's. The rest of the activation runs in the interpreter.

A `Variant` that holds what compiled code does not evaluate is the exception: the interpreter runs that one statement, whether it assigns, tests a
condition or ends a loop, and compiled code goes on after it, as described above.

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
- the activation goes on inside a `For` loop whose state the frame does not have: a `Resume` to a label inside a loop whose `For` statement never ran.

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
|`ObjectOperations`|What compiled code does with an object variable: reads the reference it holds in the frame, and compares references with `Is`.|
|`StringOperations`|What compiled code does with a `String`: reads and writes its text in the frame, and joins two texts.|
|`Extended`|The floating-point arithmetic of compiled code: in `Double` where the result is exact, and in the 80-bit format otherwise. The interpreter computes the same operations with `ExtendedIntermediates`.|

The IL calls only members of `RDCore.Runtime.Compiled`, and a test checks it.

---
> ⏮️ [**RD-VBAL §3.5.5** Placement and Licensing](rd-vbal.3.5.5.placement-and-licensing.md) | ⏭️ [**RD-VBAL §4.0** Program Structure and Organization](rd-vbal.4.0.program-structure.md)
