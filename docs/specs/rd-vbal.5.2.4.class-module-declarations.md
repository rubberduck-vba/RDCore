# 5.2.4 Class Module Declarations

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.4** Class Module Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6b025f78-471c-43bc-9d77-79dac281a6f3).

Class modules defined in _workspace source code_ have no means to _inherit_ another class module in the
_Object-Oriented Programming_ sense of inheritance
([**RD-VBAL §2.4.2** Non-intrinsic Types](rd-vbal.2.4.2.non-intrinsic-types.md)).

A public `Enum` and a public user-defined type declared in a class module reach the project scope. A class module's
other members need an instance to be reached through, but a declared type needs none
([**RD-VBAL §5.2.3** Module Declarations](rd-vbal.5.2.3.module-declarations.md), §5.2.3.3 and §5.2.3.4).


## 5.2.4.1 Non-Syntactic Class Characteristics

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.4.1** Non-Syntactic Class Characteristics](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/73a89f41-1c32-460e-a2b5-73a0c1971636).

[VBClassModuleSymbol](../api/RDCore.SDK.Model.Symbols.VBClassModuleSymbol.html).`AutomationKind`
([VBAutomationKind](../api/RDCore.SDK.Model.Symbols.VBAutomationKind.html)) distinguishes an Automation-capable
class module from an `IUnknown`-only class module:

|`AutomationKind`|Class module|
|---|---|
|`Dispatch`|Automation-capable (`VT_DISPATCH`). This is the default.|
|`Unknown`|`IUnknown`-only.|

Every RD-VBA class module has `AutomationKind` `Dispatch` (`VT_DISPATCH`). See
[**RD-VBAL §6.1.1** Predefined Enums](rd-vbal.6.1.1.predefined-enums.md) (§6.1.1.16 `VbVarType`).

### 5.2.4.1.1 Class Accessibility and Instancing

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.4.1.1** Class Accessibility and Instancing](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a09fd48e-abed-4da8-8c4c-a110bf4ef6b6).

The accessibility and instancing of a class module are determined by two module attributes
([**RD-VBAL §3.1.1** Attributes](rd-vbal.3.1.1.attributes.md), §3.1.1.2 and §3.1.1.3):

|Attribute|Determines|Value|
|---|---|---|
|`VB_Exposed`|Whether a class module is visible at all to a _referencing project_.|`False` for _private modules_, `True` for _public modules_.|
|`VB_Creatable`|Whether a class module can be directly instantiated using a `New` (or `CreateObject`) expression from a _referencing project_.|Must be `False` in a **VBA** module. May be `True` in a **VB6** module, for RD-VBA clients that support the **VB6** language.|

A _public module_ may be consumed by a referencing project. Whether a new instance of a public module can be created
outside the _enclosing project_ that defines it depends on the value of its `VB_Creatable` attribute.

A _not-creatable_ class module can only be directly instantiated within the project it is defined in (the
_enclosing project_). An instance of a not-creatable class may be consumed by any referencing project if the class
module is _exposed_.

👉 Together, `VB_Creatable` and `VB_Exposed` determine the _instancing mode_ of a class module:

|Instancing mode|`VB_Exposed`|`VB_Creatable`|
|---|---|---|
|`Private`|`False`|`False`|
|`PublicNotCreatable`|`True`|`False`|
|`PublicCreatable`|`True`|`True`|

The `PublicCreatable` instancing mode is not a legal **VBA** configuration. **RD-VBA** implementations may allow it,
with _semantic flags_ issued, if the _host environment_ is configured to allow building _library projects_.

### 5.2.4.1.2 Default Instance Variables Static Semantics

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.4.1.2** Default Instance Variables Static Semantics](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/189fb41b-cc3a-4999-a6d2-ba89f72d2870).

The `VB_PredeclaredId` attribute ([**RD-VBAL §3.1.1** Attributes](rd-vbal.3.1.1.attributes.md), §3.1.1.6)
determines whether the _environment host_ declares a global _auto-object_ instance of the class with a _predeclared
ID_. The _identifier name_ of that global auto-object is the same as the name of the class module it is a
predeclared instance of.

#### Static Semantics

The static semantics of `VB_PredeclaredId` are modeled:

- A class module with `VB_PredeclaredId = True` has a
  [VBPredeclaredInstanceSymbol](../api/RDCore.SDK.Model.Symbols.VBPredeclaredInstanceSymbol.html): a global
  variable named after the class, whose declared type is that class.
- The predeclared instance variable is created as if declared `As New`. It is an _automatic instantiation variable_
  (`SymbolProperties.AutoInstantiated`,
  [**MS-VBAL §2.5.1** Automatic Object Instantiation](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/fef06761-45b9-48c2-825c-b75c28aee9b5);
  [**RD-VBAL §2.5.1** Runtime Entities](rd-vbal.2.5.1.runtime-entities.md)), like any variable declared with an
  `As New` clause ([**RD-VBAL §5.2.3** Module Declarations](rd-vbal.5.2.3.module-declarations.md), §5.2.3.1.1).
- In the _default binding context_ (`ISymbolResolver.ResolveValue`), a class name binds to the class module's
  predeclared instance variable. A class is a value in that context only through its predeclared instance
  ([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)).
- A class that is not predeclared has no default instance.

👉 Deferred class types cannot be presumed to have a default instance
([**RD-VBAL §2.4.4** Deferred Types](rd-vbal.2.4.4.deferred-types.md)).

It is invalid for the default instance variable to be the target of a `Set` assignment, whatever is assigned to it
(**MS-VBAL §5.2.4.1.2**; see [**RD-VBAL §5.4.3.9** Set Statement](rd-vbal.5.4.3.9.set-statement.md)).

#### Runtime Semantics

In MS-VBA:

- Setting an _auto-object_ (predeclared instance) to `Nothing` destroys its internal state.
- An auto-object reference is re-created as soon as it is referred to, including within an `Is Nothing`
  reference check.

> [!NOTE]
> **Not implemented.** The run-time behavior of the default instance (it is never `Nothing`; it is re-created on
> reference) is not modeled.


## 5.2.4.2 Implements Directive

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.4.2** Implements Directive](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/da526020-9b41-44a6-a5f3-47a7ac255a9e).

The `Implements` directive specifies that the (class) module _implements_ an _interface class_
([**RD-VBAL §3.1** Attributes and Directives](rd-vbal.3.1.attributes-directives.md)).

If a class module specifies any `Implements` directives, the interfaces specified by those directives are included
in the class type's `SuperTypes` array
([VBClassType](../api/RDCore.SDK.Model.Types.Complex.VBClassType.html);
[**RD-VBAL §2.4.2** Non-intrinsic Types](rd-vbal.2.4.2.non-intrinsic-types.md)).

> [!WARNING]
> Extensible ("document") modules cannot specify any `Implements` directives.
>
> MS-VBA does not strictly enforce this rule. In MS-VBA, an `Implements` directive in an extensible module can cause
> host application instabilities, source project corruption, and host application crashes.
>
> **RD-VBA** must explicitly and statically deny `Implements` directives in extensible modules, as a normal
> compile-time error.


## 5.2.4.3 Event Declaration

This section corresponds to [**MS-VBAL §5.2.4.3** Event Declaration](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ff9d44a9-7a89-474e-9546-a1b169d38a26).

An `Event` declaration defines an event member of the class module. It is a `VBEventMemberSymbol`: its name, and its parameters, which describe the arguments a `RaiseEvent` ([**RD-VBAL §5.4.2.20**](rd-vbal.5.4.2.20.raiseevent-statement.md)) must give and the parameter list a handler must have ([**RD-VBAL §5.3.1.8**](rd-vbal.5.3.1.8.event-handler-declarations.md)); it defines no variable. An `Event` without an access modifier is `Public`. `VBClassModuleSymbol.Events` lists them, and `FindEvent` finds one by name, without regard to case.

The event symbols and their parameters are among the member descriptors the environment host receives, so a class's events are known where `RaiseEvent` runs.

> [!NOTE]
> **Not implemented.** The remaining static semantics of **MS-VBAL §5.2.4.3** are not checked: an event name must be unique within the class module, and must not contain an underscore, which is what separates a handler's variable from its event.

---
> ⏮️ [**RD-VBAL §5.2.3** Module Declarations](rd-vbal.5.2.3.module-declarations.md) | ⏭️ [**RD-VBAL §5.3** Module Code Section Structure](rd-vbal.5.3.module-code-section-structure.md)
