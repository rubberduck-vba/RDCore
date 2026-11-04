# 6.1.3.2 Err Class

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.3.2** Err Class](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/7075a4ae-6055-4173-b4b6-e0dab68155e3).

The `Err` class is represented in the SDK by the interface
[IStdErrClass](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdErrClass.html). RD-VBA names the class `ErrObject`.

`ErrObject` is implemented in full: the `RDCore.Runtime` class `ErrObject` implements every member of `IStdErrClass`.


## The Err function

🎯 The error object has two shapes. RD-VBA deliberately implements the MS-VBA one:

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

> [!NOTE]
> **Not implemented.** VBA source cannot read or call the error object at run time (`Err.Number`,
> `Err.Raise(...)`), for two reasons:
>
> - The `Err` function of the `Information` module has no runtime implementation: calling it raises the run-time
>   error "Application-defined or object-defined error".
> - Calling a member of an object through a member access is not implemented: a member access reads only a field
>   of a class instance, and any other member reports `InternalError`.


## 6.1.3.2.1 Public Subroutines

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.3.2.1** Public Subroutines](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/0d8e11d5-36bf-4bad-9d45-b54f9f473ba4).

|§|Member|Notes|
|---|---|---|
|6.1.3.2.1.1|[Clear](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/8329612a-cea0-4f18-a0bf-5b5f77efbf7e)||
|6.1.3.2.1.2|[Raise](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/51d5d48c-8c9c-4a5d-a5f0-855eb28739f1)|Raises an _application error_. See [**RD-VBAL §2.6.3** Runtime Errors](rd-vbal.2.6.3.runtime-errors.md).|


## 6.1.3.2.2 Public Properties

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.3.2.2** Public Properties](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/fbf7d695-f4ad-46e8-8980-e41ffe32ca43).

`StackTrace` is not an MS-VBAL member. It is numbered after the last MS-VBAL member of this section.

|§|Member|Notes|
|---|---|---|
|6.1.3.2.2.1|[Description](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9589efc8-0183-46e9-aa30-980596f9ebda)||
|6.1.3.2.2.2|[HelpContext](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e0c1ef21-cfcc-4dd1-814a-4bc037021059)|As **MS-VBA** sets it (recorded in Excel): a run-time error, an `Err.Raise` given none and the `Error` statement set it to `1000000` and the error's number, `95` for a number VBA defines no error for (`Err.Raise 1000` is `1000095`), and `440` for one outside `0` to `65535` (`vbObjectError + 1` is `1000440`). An `Err.Raise` given one sets that, and `Err.Clear` sets `0`.|
|6.1.3.2.2.3|[HelpFile](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/60444e0c-65ce-4566-ab73-50b5c0c2077d)||
|6.1.3.2.2.4|[LastDllError](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/54c13521-19f1-46aa-a34d-4cd5733102f8)||
|6.1.3.2.2.5|[Number](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/af123ad2-997f-4180-9d99-11d529757bf4)||
|6.1.3.2.2.6|[Source](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a282720f-a558-4918-b5cb-6aa7114c0d47)|See [§6.1.3.2.2.6](#613226-source).|
|6.1.3.2.2.7|🧩 `StackTrace`|RD-VBA addition. See [§6.1.3.2.2.7](#613227-stacktrace).|

### 6.1.3.2.2.6 Source

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.3.2.2.6** Source](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a282720f-a558-4918-b5cb-6aa7114c0d47).

`Err.Source` defaults to the project name (**MS-VBAL §6.1.3.2.2.6**). The default is applied where a run-time error is
reported to the host: when the error's source is empty, the `rdcore/session/execute` result reports the project name
as the error's source ([**RD-VBAL §2.0.2** Client/Server Capabilities](rd-vbal.2.0.2.client-server-capabilities.md)).

The session's own `Source`
([ISessionErrorState](../api/RDCore.SDK.Runtime.Abstract.Execution.ISessionErrorState.html)`.Source`) is empty until
the error object sets it (`IStdErrClass.Source`, or `IStdErrClass.Raise` with a source argument). VBA source cannot
reach the error object (see the note under [The Err function](#the-err-function)), so the `rdcore/session/execute`
result always reports the project name as the error's source.

### 6.1.3.2.2.7 StackTrace

🧩 `ErrObject.StackTrace` is an RD-VBA addition to the MS-VBAL `Err` class, and the only member RD-VBA adds to it.

The MS-VBA error object identifies what an error was, not the call chain it was raised in. `StackTrace` reports that
call chain.

`StackTrace` is a read-only property. It lists the activations of the call stack the current error was raised on,
innermost activation first.

#### Runtime Semantics

The stack trace is captured when the error is raised, rather than derived when it is read, because by the time a
handler reads it the activations it names have been unwound. It is captured at the interpreter's error-interception
point, the one place every run-time error passes through ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).

Only the activation the error was raised in carries a location. A caller's activation record does not say where in
itself it is suspended.

|Condition|`StackTrace`|
|---|---|
|No error is current.|Empty.|
|An error was made current by setting `Number`, rather than by raising one.|Empty.|

#### Implementation

`ErrObject.StackTrace` is implemented through the
[VBStackTrace](../api/RDCore.SDK.Model.Errors.VBStackTrace.html) type. The current error's stack trace is held on
`ISessionErrorState.StackTrace`, and is built from
[ICallStack](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStack.html)`.Frames`.

---
> ⏮️ [**RD-VBAL §6.1.3.1** Collection Object](rd-vbal.6.1.3.1.collection-object.md) | ⏭️ [**RD-VBAL §6.1.3.3** Global Class](rd-vbal.6.1.3.3.global-class.md)
