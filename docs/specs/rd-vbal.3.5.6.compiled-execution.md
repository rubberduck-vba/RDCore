# 3.5.6 Compiled Execution

A procedure can run as IL instead of in the interpreter ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)). The setting
`Environment.ExecutionEngine` decides it ([**RD-VBAL §2.3.3** Configuration](rd-vbal.2.3.3.configuration.md#build-configuration)):

- `Compiled`, the default, compiles a procedure the first time it is called, when compiled code can do enough of it. Any other procedure runs in the
  interpreter.
- `Interpreter` runs every procedure in the interpreter.

A program does the same thing in either engine: what it prints, the errors it raises, and where it raises them.

## When a procedure is compiled

Any `Sub`, `Function` or `Property` procedure can be compiled, in a standard module or in a class. Compiled code holds the procedure's scalar parameters,
passed `ByVal` or `ByRef`, its scalar variables, and the result of a function that returns a scalar. The scalars are `Byte`, `Integer`, `Long`, `LongLong`,
`Boolean`, `Currency`, `Single`, `Double` and `Date`. An enumeration is a `Long` (MS-VBAL §5.2.3.4): a variable, a parameter, a field or a function
result declared as one is held as a `Long`. Everything else the procedure has stays in its frame, as in the interpreter: `Me`, a `ParamArray`, an
object, a `String`, a `Variant`, an array and a record.

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

### Variant function results

The result of a function of the `Variant` type, or declared without a type, stays in its frame like a `Variant` variable. Compiled code assigns it and
reads it there whenever it holds a value of a scalar type, as above.

A `Let` of what a call returns to a `Variant` variable or to the function's own `Variant` result (`V = F()`, `V = C.Item(K)`) is compiled whatever the
call returns. Compiled code makes the call, then assigns its result as the interpreter assigns the value of the statement's expression:

- a value of any type is assigned as it is (MS-VBAL §5.5.1.2.12): `Empty` and `Null` stay `Empty` and `Null`, a `String` and an error value keep their
  type, and an array is copied;
- for an object, its default member's value is assigned. An object with no default member raises error 438 at that statement, and the procedure's error handler gets
  the error. The call is not made again.

What the call returned is let go of when its statement is over, as in the interpreter. An object only the result held is destroyed there, and its
`Class_Terminate` runs before the next statement. A function of the `Variant` type called as a statement (`F 1`) is compiled too, and its result is let
go of the same way.

A `Variant` result assigned to a variable of a scalar type (`N = F()`) is the interpreter's, and so is a `Set` of a function's `Variant` result.

### Strings

Compiled code reads and writes a `String` parameter or variable where its frame keeps it, too, unless it is a fixed-length `String`, which pads or truncates
what it is assigned. A `Let` of a literal, of another `String` variable, or of `String`s joined by `&` (or by `+`, of two `String`s) assigns the text it is
(MS-VBAL §5.6.9.4, §5.6.5), and two such `String`s are compared under the module's `Option Compare` (MS-VBAL §5.6.9.5), as the interpreter compares them.
`vbNullString` reads as the zero-length string, and a copy of it is still `vbNullString`. Compiled code also reads, as such `String`s, `Err.Description`
and `Err.Source` (MS-VBAL §6.1.3.2.2.1, §6.1.3.2.2.6), where the session keeps the error, and a `String` public variable of an object (see Objects,
below). A `String` that ends with a lone byte (`ChrB(65)`) is the interpreter's to join and compare: compiled code hands it each statement that reads
one. So is a call of a library function that takes or returns a `String`, such as `Len(S)` or `CStr(I)`.

Compiled code calls a procedure of a standard module with a `String` parameter as the interpreter does (MS-VBAL §5.3.1.11). Given by value, the
parameter gets the text of the `String`s given (`vbNullString` stays `vbNullString`). Given by reference, it is a second name for the `String`
variable given. Either way the callee holds its own frame, which the interpreter makes. A `Variant` or a number given to a `String` parameter, a constant,
a fixed-length `String`, a field and an element of an array are the interpreter's to give, and it makes the call.

### Fields of `Me`

In a procedure of a class, compiled code reads and writes a field of `Me` of a scalar type where the object keeps it, so the member's code and a caller
outside the class (`C.Total = 10`) see the same field. A field passed `ByRef` is passed as the field itself: what the callee assigns its parameter, it
assigns the field. Each object has its own fields, and a member called on one reads and writes that object's.

Compiled code also reads and writes, where the interpreter keeps it, a variable of a scalar type that outlives the activation (MS-VBAL §2.3): a field of a
standard module, the procedure's own module or another, which the session keeps; and a `Static` variable of the procedure, which the session keeps in a
standard module, and the object in a class, each object its own, as in MS-VBA. What another procedure, the interpreter, a debugger or `CopyMemory` writes
there, compiled code reads, and the other way around, from one call and one run to the next. A procedure with a `Static` is always called with its frame:
the call that first needs the `Static` gives it its storage, and so does the first call after `End`, which resets every `Static` of the program. A `For`
whose counter is such a variable is the interpreter's.

### Objects

Compiled code reads the reference held by an object parameter or variable (declared `As Object`, or as a class or an interface) where its frame keeps it.
It reads the reference held by an object field of `Me` or of a standard module, or by an object `Static`, where the interpreter keeps it, as it does a
scalar one. It compares two such references with `Is`, or one with `Nothing`, and tests one with `TypeOf`, as the interpreter does (MS-VBAL §5.6.9.7,
§5.6.7). `TypeOf` of `Nothing` is the interpreter's, which raises error 91. So is a variable or field declared `As New`, which the interpreter makes when
it is read.

Compiled code does a `Set` of such a variable or field, or of the result of a function that returns an object, to another such variable or field, to `Me` or to `Nothing`
(MS-VBAL §5.4.3.9). It Set-coerces the object to the declared type of what it is assigned to, stores it, and counts the reference as the interpreter does
(MS-VBAL §5.3.1.10), so the object lives exactly as long as in the interpreter. The interpreter runs these `Set`s on its own, and compiled code reads the
variable again after each:

- one whose object does not Set-coerce, an object of another class: the interpreter raises the error there;
- one that lets go of the last reference to an object: the interpreter runs its `Class_Terminate` there.

The interpreter runs these `Set`s as a call that compiled code makes (see What compiled code does itself, below), and compiled code reads every variable
again after each:

- one of a `WithEvents` field: the interpreter detaches its handlers from the object it held and attaches them to the one it is given;
- one of a parameter passed `ByRef`, or to anything else, such as `New`, a call or a member of an object.

Compiled code also calls a member of such a variable declared as a class or an interface (`C.Add 1`, `N = C.Total()`). It reads a `Property Get` of it
(`N = C.Count`, `If C.IsEmpty Then`) the same way, as a call of a function (MS-VBAL §5.3.1.7). Each time, it finds the member the
object's class has for it, as the interpreter does (MS-VBAL §5.3.1.9): the member itself, or, for a variable declared as an interface, the member the
object's class implements it with. It then calls that member as the interpreter calls it, with the object as `Me`, so the object lives at least as long as
the call. A call on `Nothing` is the interpreter's, which raises error 91, and so is a call of a variable declared `Object` or `Variant`, which is
late-bound.

Compiled code calls a member of the default instance of a class (MS-VBAL §5.2.4.1.2, `Board.Grow 1`, `N = Board.Size`) the same way, on the object
the default instance holds. A default instance that holds no object yet, or holds none since a `Set` of it to `Nothing`, is the interpreter's: it makes
the object, and runs its `Class_Initialize`, as it does for any variable declared `As New` (MS-VBAL §5.2.3.1.4). Compiled code calls the members of the
object it made.

Compiled code reads a public variable of a scalar type of an object variable declared as a class, of the default instance of a class, or of the
object of a `With` block (`N = C.Size`, `.X + 1`) where the object keeps it (MS-VBAL §5.6.12). Each time, it finds the variable the object's class
has for it, as the interpreter does. It reads a `String` public variable the same way (`S = C.Name`), unless it is a fixed-length `String`. A read of a
variable of `Nothing` is the interpreter's, which raises error 91, and so is a read of a variable given arguments, which index it.

Compiled code gives an object to a procedure or a member it calls as the interpreter does (MS-VBAL §5.3.1.11):

- By value, the parameter gets the object held by a variable or field of the procedure, by `Me`, or `Nothing`, Set-coerced to the parameter's declared
  type. The parameter holds and counts the reference while the callee runs, so the object lives at least as long as the call.
- By reference, the parameter is a second name for an object variable of the procedure of exactly the parameter's type. Whatever the callee `Set`s it to,
  the variable holds after the call.

What a function or a `Property Get` returns is given by value the same way (`N = Doubled(S.Pos)`), in a statement that calls inside an expression (see
Calls inside an expression, below). A field given by reference, a `WithEvents` variable, a member or `New` given as an argument, and a call given as one
in any other statement are the interpreter's to give, and it makes the call. A call that gives an object goes through the interpreter's binding of the
callee, never straight into the callee's compiled code.

When the member is compiled too, compiled code calls the member's compiled code directly. A member that keeps nothing in its frame but `Me` and values of
scalar types runs without a frame, as a compiled procedure called directly does. Its activation still holds `Me`, so an object is let go of, and its
`Class_Terminate` runs, exactly when the interpreter would do it. The activation gets the frame the interpreter would have made as soon as anything needs
it: an error, a statement the interpreter runs, another object's `Class_Terminate`. Under a debugger, every member gets its frame when it is called.

Compiled code remembers where it last found an object's fields, and looks for them again for another object. An object keeps the same place for each
field for as long as it lives, and a procedure is compiled again when its class is edited. What compiled code remembers never keeps an object alive: once
nothing else holds the object, it is gone.

### Records

Compiled code reads and writes the fields of a record where the record is kept: a record parameter or variable of the procedure, where its frame keeps
it, and a record field of `Me` (`this`, of `Private this As TState`) or of a standard module, where the interpreter keeps it. It finds the record each
time it reads or writes one of its fields, so after the interpreter assigns the whole record (`A = B`, or `LSet A = B`) it reads the record as assigned. A
field of a record that is itself a record (`P.Inner.X`) is read and written in place.

- A field of a scalar type is read and written as a variable of that type, and a `Variant` field as a `Variant` (see Variants, above).
- An object field is compared with `Is`, tested with `TypeOf`, and Set as an object variable is (see Objects, above): to an object variable or field,
  to another object field of a record, to `Me` or to `Nothing`. An object variable, or the result of a function that returns an object
  (`Set Owner = this.Owner`, in a `Property Get`), is Set to the object a field holds. Each object field holds its object in a place of its own, where
  the reference is counted, so the object lives exactly as long as in the interpreter. The interpreter runs on its own the same `Set`s of a field as of
  a variable: one whose object does not Set-coerce, and one that lets go of the last reference to an object.
- A member of the object an object field holds is called, and a scalar public variable of it read, as a member of an object variable is (see Objects,
  above): `this.Random.Maybe(Sometimes)`, `If this.Player.Kind = Human Then`.

The interpreter does the rest on its own: a `String` field, an array in a record, a field declared `As New`, and a field passed to a procedure, which
the interpreter binds to the field itself when it is passed `ByRef`.

### Members of objects and `With` blocks

A `Let` of a member of an object declared as a class is compiled (MS-VBAL §5.4.3.8). The object can be held by one of the procedure's object variables
or fields (`P.X = 1`), be `Me` (`Me.X = 1`), or be the object of the enclosing `With` block (`.X = 1`). Compiled code evaluates the value, then the
interpreter assigns it, as it assigns the value of any `Let`. The interpreter evaluates the object after the value, then finds the member on the object:

- a public field is assigned the value, let-coerced to the field's declared type;
- a `Property Let` is called with the value, let-coerced to the type of its last parameter.

So a call that gives the value runs first, even when it `Set`s the variable the object is read from to another object: the new object's member is
assigned. A member of `Nothing` raises error 91 after the value is evaluated. Compiled code writes every variable into the frame before the assignment and
reads them back after it, as around a call, because a `Property Let` runs code of its own. An error the assignment raises goes to the procedure's error
handler, and the statement is not run again. What the statement held while it ran is let go of when it is over, as in the interpreter.

Compiled code also calls a member of the object of a `With` block (`.Add 1`, `N = .Total()`, `If .IsSunken Then`) as it calls a member of an object
variable (see Objects, above), inside an expression too (see Calls inside an expression, below). The `With` statement and its `End With` are the
interpreter's: it evaluates the object and holds it for the block (MS-VBAL §5.4.2.21), and lets go of it at the `End With`, or when the procedure is left.
Compiled code reads the object where the interpreter holds it, each time it needs it, so a jump out of the block and back in finds the same object. A block whose `With` statement did not run (jumped into), or whose object raised an error that
`On Error Resume Next` went past, has no object, and neither does a block of `Nothing`: a member of it raises error 91, as in the interpreter.

The interpreter keeps the rest:

- a member of an object declared `Object` or `Variant`, which is late-bound;
- a `With` block whose object is a record;
- a `Set` of a member, and a `Set` inside a `With` block;
- a `String` assigned to a member.

### Arrays

Compiled code handles an array parameter or local array variable of the procedure, except a `Static` array, a `ParamArray` and an array declared
`As New`. An array field of `Me` or of a module, and an array in a record, are the interpreter's.

- `LBound` and `UBound` read the array's bounds, whatever its element type (**MS-VBAL §3.3.5.2**).
- It finds an element in the array the frame holds each time it reads or writes one (**MS-VBAL §5.6.13**), so after a `ReDim` it finds the element in
  the new array. Each subscript is converted to a `Long`. The array has up to three dimensions.
- An element of an array of a scalar type is read and written as a variable of that type, and an element of an array of `Variant`s as a `Variant`.
  An element of an array of objects can be compared with `Is` and tested with `TypeOf`.
- The interpreter runs a `ReDim` of an array of a scalar type whose bounds compiled code computes, and an `Erase` of such an array, for compiled code.
  An error either raises goes to the procedure's error handler, as an error of a call does.

The interpreter does the rest on its own:

- A subscript outside its dimension, which raises error 9 at that statement, and `LBound` or `UBound` of a dimension the array does not have.
- A `Set` of an element of an array of objects.
- A `ReDim` or an `Erase` of an array of `Variant`s or of objects, which can release objects.
- An element passed `ByRef`: the array cannot be resized while the callee holds its element.
- An element of an array of `String`s, of records or of an enumeration.

### Calls inside an expression

Compiled code calls a function inside an expression, such as `X = F(A) + 1`, `If F(A) > 0 Then` or `G(F(A))`, when the statement is one of these:

- a `Let` of a scalar variable of the procedure or of the function's result;
- a call statement;
- the condition of an `If`, an `ElseIf` or a loop;
- the start, the end or the step of a `For` (`For X = P.Left To P.Left + P.Width - 1`), which are evaluated once, in that order, before the counter is
  given the start (MS-VBAL §5.4.2.3). Each is the value it had when it was evaluated: `For I = A To Bump(A)` starts at what `A` held before the call.

A function given no argument and called by its name alone is called this way too, and so is a `Property Get` of one of the procedure's object
variables (`X = C.Count + 1`), of the default instance of a class, or of the object of a `With` block (`X = .Count + 1`). The function itself may be
compiled or not: one the interpreter runs is called as the interpreter calls it.

Compiled code also calls a member of what such a function or `Property Get` returns, when it is declared as a class (`Y = S.Pos.Y`,
`N = S.Pos.Twice + 1`, `Make().Pos.Bump`). It also reads a scalar public variable of it, and gives it to an object parameter by value (see Objects,
above). The call that returns the object is made once. The statement holds the object until it is over, and then lets go of it, as the interpreter does:
if nothing else holds it, its `Class_Terminate` runs there (MS-VBAL §5.3.1.10). An object something else holds lives on. The statement is over:

- after its assignment or its call;
- for an `If`, an `ElseIf` or a loop, once its condition is evaluated, before the jump the condition decides;
- for a `For`, once the counter is given the start, before the first test of the counter.

A member of a call that returns `Nothing` is the interpreter's, which raises error 91, and so is a member of what a call returns as `Object` or
`Variant`, which is late-bound.

Every operand of such a statement must be one compiled code keeps as it evaluated it: a literal, a constant, a scalar variable of the procedure, the
function's result variable, a scalar public variable of an object (see Objects, above), `Err.Number`, a call, or an operation of these. The
interpreter runs a statement with any other operand on its own, as described below: a `Variant`, a `String`, an object, a field or an element of an
array. Such an operand would be read a second time after a call that may have changed it.

A statement can read a variable that a call in it may change: one it also passes `ByRef` to the call, such as `X = A + Bump(A)`; a `ByRef` parameter,
while it passes another `ByRef` parameter to a call, since the two may be the same variable of the caller; or a variable of a module or a public
variable of an object, which any call may change. Compiled code runs such a statement when every operation in it is of
type `Byte`, `Boolean`, `Integer`, `Long`, `LongLong` or `Currency`, and the variable is an operand, not an argument of a call. It reads the variable
where it is written, as MS-VBA does ([**RD-VBAL §5.6.9.2**](rd-vbal.5.6.9.2.simple-data-operators.md#when-an-operand-is-read)). The interpreter runs
any other such statement on its own: one that computes a `Single`, a `Double` or a `Date`, and one that gives the variable to a call as an argument.

When compiled code hands the activation to the interpreter after such a call, it also gives the interpreter what it already evaluated of the statement:
the result of each call it made, and each variable as it read it. This happens when an operation of the result fails, when the program is to stop, when
the program waited in the callee, and when the code was edited meanwhile. The interpreter evaluates the rest of the statement with those values, so:

- no call is made twice;
- an operand keeps the value it was read as, even when a debugger assigned the variable while the program waited in the callee;
- after an edit made while the program waited in the callee, the statement finishes as it was, and the activation goes on after it in the edited code.
  When the call is made by the condition of an `If`, an `ElseIf` or a loop, the edit is not applied yet, as in the interpreter.

An error the statement raises after the call goes to the handler as any error does. A `Resume` runs the statement again, calls included. An object a
call of the statement returned is let go of before the handler's first statement, as the interpreter lets go of it. When no handler of the procedure
takes the error, it is let go of as the procedure is left, before the caller goes on with the error.

### What compiled code does itself

Compiled code does these itself:

|Kind|What compiled code does|
|---|---|
|Expressions|Literals, `String` literals among them, constants, the procedure's parameters and variables, its `Variant`s and `String`s, the scalar and object fields of `Me` and of standard modules, the scalar, `Variant` and object fields of its records (see Records, above), the procedure's scalar and object `Static`s, the elements of its arrays of scalars and of `Variant`s, `LBound` and `UBound` of its arrays (see Arrays, above), the function's own result variable, a `Variant` one included, the scalar and `String` public variables of an object variable, of an object field of its records, of the default instance of a class or of the object of a `With` block (see Objects, above), `Err.Number`, `Err.Description` and `Err.Source`, and parentheses.|
|Operators|`+`, `-`, `*`, `/`, `\`, `Mod`, the comparisons, `And`, `Or`, `Xor`, `Eqv`, `Imp`, `Not`, and negation, on operands of scalar types and on `Variant`s that hold one; `&`, `+` and the comparisons of two `String`s; `Is` of the procedure's object variables, object fields, the elements of its arrays of objects, and `Nothing`, and `TypeOf` of one of its object variables or fields or of an element of one of its arrays of objects.|
|Statements|`Let` of a variable, of a `Variant`, of a `String`, of an element of an array of scalars or of `Variant`s, of a scalar field of `Me` or of a standard module, of a scalar or `Variant` field of one of its records, of a scalar `Static`, of the function's result, a `Variant` one included, or of a member of an object declared as a class, the object of a `With` block included (see Members of objects and `With` blocks, above); `Set` of an object variable, field or `Static` (not `WithEvents`), or of a function's object result, to an object variable or field, `Me` or `Nothing`, and `Set` of an object field of one of its records to any of these or to another such field, and of an object variable or a function's object result to such a field; `ReDim` and `Erase` of arrays of scalars, which the interpreter runs for compiled code (see Arrays, above).|
|Calls|A call statement with arguments, a function's result assigned whole, or a function's result assigned to a `Variant` (see Variant function results, above) or to a member of an object, of a procedure of a standard module, or of a member of one of the procedure's own object variables or object fields declared as a class or interface, of an object field of one of its records, of the default instance of a class, or of the object of a `With` block declared as a class, when every parameter is of a scalar type (`Sub` or `Function`, and a member's `Property Get`, whose value is read); a parameter may also be an object (see Objects, above), and, of a procedure of a standard module, a `String`. A `ByRef` argument is a variable of the procedure of the parameter's type, or a value that is no variable, which the callee is given a copy of. A function or `Property Get` so called can also be called inside an expression (see Calls inside an expression, below).|
|Control flow|`If`, `ElseIf` and `Else`; `Do` and `Loop` with `While` or `Until`; `While` … `Wend`; `For` … `Next` with a counter that is a variable of the procedure, not a `Boolean`, and not passed `ByRef`; the `Next` of a `For` with a `Variant` counter, whose `For` statement the interpreter runs, since it decides the type the loop counts in; `Select Case` of a scalar selector; `Exit Do`, `Exit For`, `Exit Sub`, `Exit Function` and `Exit Property`; `GoSub` and `Return`.|
|Errors|`On Error GoTo` a label, `On Error Resume Next`, `On Error GoTo 0` and `On Error GoTo -1`.|

The interpreter runs any other statement on its own, inside the compiled procedure. Examples are a `GoTo`; `On … GoTo` and `On … GoSub`; `Resume`,
`Resume Next`, `Resume` a label and the `Error` statement (see Error handling, below); a statement that uses something that stays in the frame (a
fixed-length `String`, an object other than as above, a field or a `Static` that is neither a scalar nor an object); a call of a member of anything other than one of the procedure's object variables and fields, a default instance or the object of a `With` block (`Err.HelpContext` is one), and a member of what a call returns in any statement but one that calls inside an expression (see Calls inside an expression, above); an element of an array that compiled code does not handle (see Arrays, above); and a `For` whose counter or bounds
compiled code does not do. Compiled code first writes its variables into the frame. The interpreter runs the statement, and compiled code reads
the variables back and goes on wherever the statement left the procedure. A statement the interpreter runs this way behaves exactly as it does in the
interpreter, errors included.

The interpreter runs a call statement that compiled code does not make (`Err.Clear`, `C.Add 1` with `C` a `Collection`, a call given a late-bound
argument), and a `Set` it does not do (see Objects, above), as a call that compiled code makes: compiled code writes its variables into the
frame, the interpreter runs the statement, and compiled code reads the variables back after it, as after any call it makes. An error the statement raises
reaches the activation's handler as an error a callee raises does (see Error handling, below). Such a statement never keeps a procedure in the
interpreter, and `rdc compilation` does not list it as something the interpreter runs. Inside a `With` block, the interpreter runs it on its own, as
above.

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
the block that uses its object other than as compiled code does (see Members of objects and `With` blocks, above); a `For Each` and its `Next`; and a
`Select Case` and its `Case`s when the selector is no scalar, such as a `String` or a `Variant`, or is a scalar compiled code cannot evaluate, such as `Len(S)` with `S` a `String`. Compiled code does the rest of the block. What a block holds is let go of where the interpreter lets go of it, so a `Class_Terminate` runs at the
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

Compiled code also hands the activation to the interpreter when the program is to stop or a debugger attaches: the interpreter then breaks, or runs the activation under the debugger.
Compiled code checks for either at the back edge of each loop, after each call it makes, and after each instruction the interpreter runs for it. A break asked for
while a call or such an instruction ran is answered before the next instruction, where the interpreter answers it. A procedure called while the program
is to stop runs in the interpreter, which breaks before its first statement.

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
|`CompiledActivation`|What compiled code is given: its parameters, the cells of its `ByRef` variables, its calls, the poll, the hand-over to the interpreter with what compiled code evaluated of the instruction, and `Recover`, which has the interpreter run one instruction.|
|`PayloadOperations`|The operations compiled code and the interpreter share.|
|`VariantOperations`|What compiled code does with a `Variant`: reads and writes it in the frame, and runs its operations through the interpreter's own code.|
|`ObjectOperations`|What compiled code does with an object variable: reads the reference it holds in the frame, and compares references with `Is`.|
|`StringOperations`|What compiled code does with a `String`: reads and writes its text in the frame, and joins two texts.|
|`Extended`|The floating-point arithmetic of compiled code: in `Double` where the result is exact, and in the 80-bit format otherwise. The interpreter computes the same operations with `ExtendedIntermediates`.|

The IL calls only members of `RDCore.Runtime.Compiled`, and a test checks it.

---
> ⏮️ [**RD-VBAL §3.5.5** Placement and Licensing](rd-vbal.3.5.5.placement-and-licensing.md) | ⏭️ [**RD-VBAL §4.0** Program Structure and Organization](rd-vbal.4.0.program-structure.md)
