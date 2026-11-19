# 6.1.3.2 Err Class

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.3.2** Err Class](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/7075a4ae-6055-4173-b4b6-e0dab68155e3).

The `Err` class is represented in the language core by the interface
`IStdErrClass`. RD-VBA names the class `ErrObject`.

`ErrObject` is implemented in full: the `RDCore.Runtime` class `ErrObject` implements every member of `IStdErrClass`.


## The Err function

**RD-VBA differs from MS-VBAL:** the error object has two shapes, and RD-VBA implements the MS-VBA one.

|Shape|Described by|The error object is|
|---|---|---|
|Global class module|MS-VBAL|The default instance of a global class module named `Err`.|
|Function|MS-VBA|An instance of a class named `ErrObject`, returned by `Err`: a zero-argument `Function` of the `Information` module.|

The two shapes are indistinguishable from source. A standard module's members are promoted to the project scope, so a
bare `Err` yields the error object in either shape
([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)).

RD-VBA's `Err` is a zero-argument `Function` of the `Information` module, returning an `ErrObject`
([**RD-VBAL §6.1.2.7.1.15** Err](rd-vbal.6.1.2.7.information.md#6127115-err)). It is not a global class module with a
default instance, the shape MS-VBAL describes.

The MS-VBA shape leaves `ErrObject` nameable in an `As` clause, instead of shadowed by its own default instance.

## New ErrObject

The VBA library says the `ErrObject` class can be created, so `New ErrObject` and `Dim e As New ErrObject` compile, in
MS-VBA and in RD-VBA (**MS-VBAL §5.6.8**, **§5.2.3.1.4**). Nothing creates one: the error object is the one `Err`
returns. Creating another raises run-time error -2147221164 _Class not registered_, as **MS-VBA** does (recorded in
Excel), and a variable that was `Nothing` before `Set e = New ErrObject` is `Nothing` after it. A variable declared `As New ErrObject` raises it at each reference that would
create its object. Both forms are reported as [`RDC11028`](../diagnostics/rdc11028.md), a Warning. See
[**RD-VBAL §5.6.8** New Expressions](rd-vbal.5.6.8.new-expressions.md).


## 6.1.3.2.1 Public Subroutines

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.3.2.1** Public Subroutines](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/0d8e11d5-36bf-4bad-9d45-b54f9f473ba4).

|§|Member|Notes|
|---|---|---|
|6.1.3.2.1.1|[Clear](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/8329612a-cea0-4f18-a0bf-5b5f77efbf7e)|Also resets `Erl` to `0`, unless the environment keeps it as MS-VBA does. See [**RD-VBAL §6.1.2.7.1.14** Erl](rd-vbal.6.1.2.7.information.md#6127114-erl).|
|6.1.3.2.1.2|[Raise](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/51d5d48c-8c9c-4a5d-a5f0-855eb28739f1)|Raises an _application error_. See [**RD-VBAL §2.6.3** Runtime Errors](rd-vbal.2.6.3.runtime-errors.md).|


## 6.1.3.2.2 Public Properties

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.3.2.2** Public Properties](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/fbf7d695-f4ad-46e8-8980-e41ffe32ca43).

`StackTrace` is not an MS-VBAL member. It is numbered after the last MS-VBAL member of this section.

|§|Member|Notes|
|---|---|---|
|6.1.3.2.2.1|[Description](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9589efc8-0183-46e9-aa30-980596f9ebda)|An `Err.Raise` given one sets it. An `Err.Raise` given none keeps the one `Err` has while it has an error, as it keeps the other arguments it is not given (MS-VBAL §6.1.3.2.1.2): `Err.Raise 13` after a division by zero is described "Division by zero". Otherwise it is the text VBA gives the number, as the `Error` function returns it, or "Application-defined or object-defined error" for a number VBA defines no error for. `Err.Clear` sets it to an empty `String`.|
|6.1.3.2.2.2|[HelpContext](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e0c1ef21-cfcc-4dd1-814a-4bc037021059)|As **MS-VBA** sets it (recorded in Excel, `oracle\s58` H03-H06): a run-time error, the `Error` statement, an error an automation object raises, and an `Err.Raise` given `0` set it to `1000000` and the error's number. That is `95` for a number VBA defines no error for (`Err.Raise 1000` is `1000095`, and so is an RD-VBA error of its own, `50001` on), and `440` for one outside `0` to `65535` (`vbObjectError + 1` is `1000440`). An `Err.Raise` given one sets that. An `Err.Raise` given none keeps the one `Err` has while it has an error, as it keeps the other arguments it isn't given (MS-VBAL §6.1.3.2.1.2), and sets the error's otherwise. `Err.Clear` sets `0`.|
|6.1.3.2.2.3|[HelpFile](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/60444e0c-65ce-4566-ab73-50b5c0c2077d)|Empty unless the program sets it. See [§6.1.3.2.2.3](#613223-helpfile).|
|6.1.3.2.2.4|[LastDllError](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/54c13521-19f1-46aa-a34d-4cd5733102f8)||
|6.1.3.2.2.5|[Number](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/af123ad2-997f-4180-9d99-11d529757bf4)||
|6.1.3.2.2.6|[Source](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a282720f-a558-4918-b5cb-6aa7114c0d47)|See [§6.1.3.2.2.6](#613226-source).|
|6.1.3.2.2.7|`StackTrace`|RD-VBA addition. See [§6.1.3.2.2.7](#613227-stacktrace).|

### 6.1.3.2.2.3 HelpFile

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.3.2.2.3** HelpFile](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/60444e0c-65ce-4566-ab73-50b5c0c2077d).

**MS-VBAL §6.1.3.2.1.2** leaves the help file of an error raised without one to the implementation. **MS-VBA** gives it
the path of its own help file. After a run-time error, MS-VBA's `Err.HelpFile` is
`C:\Program Files\Common Files\Microsoft Shared\VBA\VBA7.1\1033\VbLR6.chm`, and its `Err.HelpContext` is `1000440`
(recorded in Excel, `oracle\s62` X12, where the error is -2147221164).

**RD-VBA** gives an empty string. `Err.HelpFile` is `""` unless the program sets it, with the property itself or with the
`HelpFile` argument of `Err.Raise`, and `Err.Clear` empties it. The help system that file belongs to is MS-VBA's own:
RD-VBA has none, opens no help for an error, and has no file to name. A path to MS-VBA's file would name a file that is
not there on a machine without Office.

`Err.HelpContext` is set as MS-VBA sets it (see the table above), so a program that reads it gets the same number.

### 6.1.3.2.2.6 Source

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.3.2.2.6** Source](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a282720f-a558-4918-b5cb-6aa7114c0d47).

An error's `Source` is the name of the project whose code runs (**MS-VBAL §6.1.3.2.1.2**: "the programmatic ID of the
current Visual Basic project"), unless `Err.Raise` gives it one. Recorded in Excel (`oracle\s58` H06, `s61` H07-H12),
where the project is `VBAProject`:

|What raises the error|`Err.Source`|
|---|---|
|A run-time error (`1 / 0`), an `Error` statement, an automation object (a `Dictionary`'s 457), in a standard module or a class|The project's name, whatever `Err.Source` was before: one assigned, one an `Err.Raise` gave|
|`Err.Raise` with a source|That source|
|`Err.Raise` with none, while `Err` has one (`Err.Raise 5, "lib"`, then `Err.Raise 6`)|The one `Err` has: `lib`|
|`Err.Raise` with none, and `Err` none (after `Err.Clear`)|The project's name|

`Err.Clear` empties it. The project's name is the one the project file gives it, or its folder's
([**RD-VBAL §2.2.3**](rd-vbal.2.2.3.projectfile.md)). The shell's project is named `VBAProject`, as MS-VBA names a new
project; its one module is `Program`.
The `rdcore/session/execute` result reports the same source for the error a run ends with
([**RD-VBAL §2.0.2** Client/Server Capabilities](rd-vbal.2.0.2.client-server-capabilities.md)).

### 6.1.3.2.2.7 StackTrace

`ErrObject.StackTrace` is an RD-VBA addition to the MS-VBAL `Err` class, and the only member RD-VBA adds to it.

The MS-VBA error object identifies what an error was, not the call chain it was raised in. `StackTrace` reports that
call chain.

`StackTrace` is a read-only property. It lists the activations of the call stack the current error was raised on,
innermost activation first.

#### Runtime Semantics

The stack trace is captured when the error is raised, rather than derived when it is read, because by the time a
handler reads it the activations it names have been unwound. It is captured at the interpreter's error-interception
point, the one place every run-time error passes through ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).

The activation the error was raised in is located at the statement or the expression that faulted. Each of its
callers is located at the call it was suspended at: the start of the call expression, or of the call statement, that
was running when the error was raised. In `Debug.Print Outer(Inner(0))`, an error raised in `Inner` locates its caller
at `Inner(0)`, and one raised in `Outer` locates it at `Outer(Inner(0))`.

Each activation also lists the `GoSub` statements it had not `Return`ed from when the error was raised, innermost
first: its GoSub Resumption List at that moment ([**RD-VBAL §5.4.2.14** GoSub Statement](rd-vbal.5.4.2.14.gosub-statement.md)).
An activation in no `GoSub` lists none.

The value is a `String` with one line per activation, innermost first, separated by `vbCrLf`. A line names the
procedure, then the line and column of its location, then a `from` and the line and column of each `GoSub`:

```text
  at Inner (12:5) from (8:5)
  at Outer (3:5)
```

|Condition|`StackTrace`|
|---|---|
|No error is current.|Empty.|
|An error was made current by setting `Number`, rather than by raising one.|Empty.|

#### Implementation

`ErrObject.StackTrace` is implemented through the
[VBStackTrace](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Errors.VBStackTrace.html) type, one
[VBStackTraceFrame](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Errors.VBStackTraceFrame.html) per activation. The current error's stack trace is held on
`ISessionErrorState.StackTrace`, and is built from
[ICallStack](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Abstract.Execution.ICallStack.html)`.Frames`.
