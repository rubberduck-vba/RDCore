# 5.3.1.6 Subroutine and Function Declarations

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.3.1.6** Subroutine and Function Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/b9466587-f1b4-47f5-b34e-3af3398063f4).

## Static Semantics

[VBVoidType](../api/RDCore.SDK.Model.Types.Complex.VBVoidType.html) is the data type returned by `Sub`,
`Property Let` and `Property Set` procedures; see
[**RD-VBAL §2.4.2** Non-intrinsic Types](rd-vbal.2.4.2.non-intrinsic-types.md).

A `Sub`'s own symbol already carries `VBVoidType` as its type. See
[**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md).

## Function Result Variable

`Function` and `Property Get` return values (the function result variable) follow
[**MS-VBAL §5.3.1** Procedure Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/227005ad-78fb-479f-8145-fa3b8b610386).

- Each invocation of a `Function` or `Property Get` gets a fresh function result variable.
- The function result variable is modeled as
  [ICallStackFrame](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html)`.ReturnValue`, a
  single per-activation slot.
- `ICallStackFrame.ReturnValue` is read-only on the SDK interface, like `ICallStackFrame.Pc`; it is mutable on
  the runtime's `CallStackFrame`.
- `ReturnValue` is seeded to the default value of the procedure's declared return type before the procedure
  body runs.

Inside the procedure's own body, a bare reference to the procedure's own name is the function result variable:

|Use of the procedure's own name, from within its own body|Effect|Described in|
|---|---|---|
|A bare reference|Reads the `ReturnValue` slot instead of the general symbol table.|[**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md)|
|A Let-assignment to a bare reference|Assigns the `ReturnValue` slot instead of the general symbol table.|[**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md)|
|A Set-assignment to a bare reference|Set-coerces the object to the declared result type, and assigns the `ReturnValue` slot: the result of a `Variant` function that is `Set` to an object holds the object.|[**RD-VBAL §5.4.3.9** Set Statement](rd-vbal.5.4.3.9.set-statement.md)|

A function that returns an object and never sets its result returns `Nothing`, which is the default value of an object type.
The object a function returns is not released with the activation's locals, whether the result is declared as an object
type or as a `Variant` that holds the object.

## Invocation Result

|Procedure|Result of a successful invocation|
|---|---|
|`Function`, `Property Get`|The value of its own function result variable.|
|`Sub`|[VBVoidValue](../api/RDCore.SDK.Model.Values.VBVoidValue.html)|

`RuntimeProcedureInvoker` reads `frame.ReturnValue` back once `ExitProcedure` is reached, and reports it as the
call's result. See
[**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md).

A `Function` or `Property Get` that declares no parameters is not given the arguments written after it in parentheses:
it is called without them, and they index what it returns. `Items(1)` of a function that returns a `Collection` is
`Items().Item(1)`, as `h.Items(1)` is `h.Items.Item(1)` for a property. The empty list of `F()` is not an argument.

---
> ⏮️ [**RD-VBAL §5.3.1.5** Parameter Lists](rd-vbal.5.3.1.5.parameter-lists.md) | ⏭️ [**RD-VBAL §5.3.1.7** Property Declarations](rd-vbal.5.3.1.7.property-declarations.md)
