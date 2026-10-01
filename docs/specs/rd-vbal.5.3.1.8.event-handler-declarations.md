# 5.3.1.8 Event Handler Declarations

This section corresponds to [**MS-VBAL §5.3.1.8** Event Handler Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ddbb1c98-db2b-4d32-85f3-362c66fc04e0).

## Static Semantics

A procedure of a class module handles an event when its name is `VariableName_EventName`: `VariableName` is a `WithEvents` variable of the same class module ([**RD-VBAL §5.2.3.1.2**](rd-vbal.5.2.3.module-declarations.md)) and `EventName` an event of the class that is the variable's declared type ([**RD-VBAL §5.2.4.3**](rd-vbal.5.2.4.class-module-declarations.md)).

[VBClassModuleSymbol](../api/RDCore.SDK.Model.Symbols.VBClassModuleSymbol.html) answers both sides of it: `WithEventsVariables` are the variables flagged with the `WithEvents` symbol property, and `FindEventHandler(variable, event)` is the procedure named for the variable and the event. It is the same naming that implements an interface member ([**RD-VBAL §5.3.1.9**](rd-vbal.5.3.1.9.implemented-name-declarations.md)) and a lifecycle handler ([**RD-VBAL §5.3.1.10**](rd-vbal.5.3.1.10.lifecycle-handler-declarations.md)), and the only name a handler has: nothing calls it by name.

A handler is invalid when it is not a subroutine, or when its parameter list is not compatible with the event's:
the same number of parameters, each of the same type and parameter mechanism. The parameters may differ in name, and in
whether the mechanism is written out. [ClassModuleEventSemantics](../api/RDCore.SDK.Semantics.Static.ClassModuleEventSemantics.html)
reports it as [VBC09326](../diagnostics/vbc09326.md). It decides by the name alone: a procedure that only begins with a
`WithEvents` variable's name and an underscore is no handler, and neither is one named for a variable that is not
`WithEvents`; neither is checked.

## Runtime Semantics

A handler is invoked by `RaiseEvent` ([**RD-VBAL §5.4.2.20**](rd-vbal.5.4.2.20.raiseevent-statement.md)) on the object that holds the event's source in the `WithEvents` variable, with that object as its target: its `Me` is the object that owns the variable, not the source.

---
> ⏮️ [**RD-VBAL §5.3.1.7** Property Declarations](rd-vbal.5.3.1.7.property-declarations.md) | ⏭️ [**RD-VBAL §5.3.1.9** Implemented Name Declarations](rd-vbal.5.3.1.9.implemented-name-declarations.md)
