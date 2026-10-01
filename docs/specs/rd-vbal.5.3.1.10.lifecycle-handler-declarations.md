# 5.3.1.10 Lifecycle Handler Declarations

This section corresponds to [**MS-VBAL §5.3.1.10** Lifecycle Handler Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/62bbe63e-379c-4dc0-8648-7d9050a2f396).

## The `Class` interface

Every class module implicitly implements the `Class` interface, which has two members: `Initialize` and `Terminate`. A class handles them the way it handles the member of any interface it implements, with a procedure named `InterfaceName_MemberName`: `Class_Initialize` and `Class_Terminate`.

The interface is implicit. The class has no `Implements Class` directive, the interface is not among the class type's `SuperTypes`, and no name refers to it. It is the `ImplicitInterfaces` of the class module symbol, not its `ImplementedInterfaces`.

A handler is never called by name. Raising an event dispatches the interface member to whatever the object's class implements it with ([**MS-VBAL §5.3.1.9**](rd-vbal.5.3.1.9.implemented-name-declarations.md)), with the object as the target. A class that does not handle an event raises nothing.

## `Initialize`

`Class_Initialize` runs once for each object created, before a reference to the object is returned by the operation that created it:

|Creation|Status|
|---|---|
|The `New` operator|Implemented|
|The first reference to a variable declared `As New`, and to a predeclared default instance|Implemented|
|The `CreateObject` function|Not implemented|

An error the handler leaves unhandled is the error of the creating operation.

## `Terminate`

`Class_Terminate` runs when an object loses its last reference, while the object is still whole. It runs at most once per object. A handler that gives the object a reference again prevents its destruction; the object is destroyed, without `Terminate` running a second time, when it next loses its last one.

A reference is held by the variable that stores it. It is released when the variable is assigned another object or `Nothing`, and when the procedure that declared the variable returns. A `Static` variable, a module-level variable, a field of an object, an array element and a `Variant` do not release the object they hold when their own lifetime ends, and an object that is held only by one of them is not terminated before the session ends. An object that no variable ever held is not terminated either.

## Automatic instantiation

A variable declared `As New`, and the predeclared instance of a class module whose `VB_PredeclaredId` attribute is `True`, is never `Nothing` when it is referred to: the reference creates the object. This is also so after the variable is set to `Nothing`, so that `Is Nothing` of such a variable is never `True`, and a member call on it is never error 91.

See [**RD-VBAL §2.4.2** Non-intrinsic Types](rd-vbal.2.4.2.non-intrinsic-types.md).

---
> ⏮️ [**RD-VBAL §5.3.1.9** Implemented Name Declarations](rd-vbal.5.3.1.9.implemented-name-declarations.md) | ⏭️ [**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)
