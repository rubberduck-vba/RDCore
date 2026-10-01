# 5.2.3 Module Declarations

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.3** Module Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/b40b8d00-3348-43c1-9cfb-c0eadef565ee).

A standard module's non-`Private` members (an explicit `Public` / `Global` / `Friend`, or an implicit
procedure-like member) are also declared in the project scope. Because of this, a sibling module resolves them
without qualification ([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)).


## 5.2.3.1 Module Variable Declaration Lists

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.3.1** Module Variable Declaration Lists](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/0f9113df-fd9c-485a-9583-fdb0e9d68e1b).

👉 When an array declaration specifies no _item type_, the declared item type of the array is `Variant`
([**RD-VBAL §2.4.1** Intrinsic Types](rd-vbal.2.4.1.intrinsic-types.md);
[**RD-VBAL §2.5.2.1.2** Array Values](rd-vbal.2.5.2.1.2.array-values.md)).

### 5.2.3.1.1 Variable Declarations

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.3.1.1** Variable Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/cf31e152-6b15-4ddd-a415-3257a30b1ac8).

Any variable declared with an `As New` clause is an _automatic instantiation variable_
([**MS-VBAL §2.5.1** Automatic Object Instantiation](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/fef06761-45b9-48c2-825c-b75c28aee9b5)).
Its symbol carries the `AutoInstantiated` property of
[SymbolProperties](../api/RDCore.SDK.Model.Symbols.Abstract.SymbolProperties.html)
([**RD-VBAL §2.5.1** Runtime Entities](rd-vbal.2.5.1.runtime-entities.md);
[**RD-VBAL §3.1.1** Attributes](rd-vbal.3.1.1.attributes.md)).

The default instance variable of a predeclared class is created as if declared `As New`
([**RD-VBAL §5.2.4** Class Module Declarations](rd-vbal.5.2.4.class-module-declarations.md), §5.2.4.1.2).

### 5.2.3.1.2 WithEvents Variable Declarations

This section corresponds to [**MS-VBAL §5.2.3.1.2** WithEvents Variable Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f41f8ec5-7a2d-4797-8ba9-0e3b52113b9e).

A `WithEvents` variable is a variable of a class module whose declared type is a class with events. The symbol of such a variable has the `WithEvents` property (`SymbolProperties.WithEvents`), which the language server sets from the `WithEvents` keyword and sends to the environment host in its descriptor (`SymbolDescriptor.IsWithEvents`). The procedures of the module named `VariableName_EventName` handle that event of the object the variable holds ([**RD-VBAL §5.3.1.8**](rd-vbal.5.3.1.8.event-handler-declarations.md)).

Assigning the variable with `Set` attaches its handlers to the object it is given, and detaches them from the object it held ([**RD-VBAL §5.4.3.9**](rd-vbal.5.4.3.9.set-statement.md)).

> [!NOTE]
> **Not implemented.** The declaration-level validity of `WithEvents` (**MS-VBAL §5.2.3.1.2**) is not checked: the declared type must be a specific class with at least one event, and must not be the class of the module containing the declaration.

### 5.2.3.1.3 Array Dimensions and Bounds

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.3.1.3** Array Dimensions and Bounds](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/7c97ad30-4b76-45d9-9827-7455f98a5503).

The _declaration pass_ binds the _declared type_ of an array symbol from the _array-dim clause_ alone, before any
bound is evaluated ([**RD-VBAL §2.4.1** Intrinsic Types](rd-vbal.2.4.1.intrinsic-types.md)).

An _array declaration_ creates an _array value_ of the appropriate _array type_ in the _scope_ of the declaration
([**RD-VBAL §2.5.2.1.2** Array Values](rd-vbal.2.5.2.1.2.array-values.md)):

|Array-dim clause|Example|Declared type|Array value created|
|---|---|---|---|
|One or more _bounds_ (dimension specifications)|`Dim g(1 To 3, 0 To 4) As Long`|[VBFixedSizeArrayType](../api/RDCore.SDK.Model.Types.VBFixedSizeArrayType.html)|[VBFixedSizeArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBFixedSizeArrayValue.html)|
|An empty `()` clause|`Dim b() As Long`|[VBResizableArrayType](../api/RDCore.SDK.Model.Types.VBResizableArrayType.html)|[VBResizableArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBResizableArrayValue.html)|
|A trailing `()` on the `As` clause|`Dim b As Long()`|`VBResizableArrayType`|`VBResizableArrayValue`|
|An empty `()` clause, or a trailing `()` on the `As` clause, with a `Byte` _item type_|`Dim b() As Byte`|[VBResizableByteArrayType](../api/RDCore.SDK.Model.Types.VBResizableByteArrayType.html)|[VBResizableByteArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBResizableByteArrayValue.html)|

An array has up to 60 dimensions.

Resolving an omitted _lower bound_ against `Option Base` is specified in
[**RD-VBAL §5.2.1** Option Directives](rd-vbal.5.2.1.option-directives.md) (§5.2.1.2).

### 5.2.3.1.4 Variable Type Declarations

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.3.1.4** Variable Type Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d27d3dae-ac46-4113-8927-c7f60d844dbf).

A declared type name binds in the _type binding context_. With the
[CompositeSymbolResolver](../api/RDCore.SDK.Model.Symbols.CompositeSymbolResolver.html), a module's `As SomeType`
binds to a sibling module's `Type` or `Enum`, or to a class, not only to a reserved data-type name
([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)).

**MS-VBAL §5.2.3.1.4**: the type specified in an `As New` clause must be a named class, and must be creatable unless
it is declared in the same project.

> [!NOTE]
> **Not implemented.** The declaration-level validity of `As New` (**MS-VBAL §5.2.3.1.4**) is not modeled.

### 5.2.3.1.5 Implicit Type Determination

This section corresponds to [**MS-VBAL §5.2.3.1.5** Implicit Type Determination](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6df70c22-dc30-45d0-b8a7-9a0fb4f068b3).

> [!NOTE]
> Reserved. This section has no content yet.


## 5.2.3.2 Const Declarations

This section corresponds to [**MS-VBAL §5.2.3.2** Const Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ffa99fa9-28da-4512-85eb-db376cab0711).

> [!NOTE]
> Reserved. This section has no content yet.


## 5.2.3.3 User Defined Type Declarations

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.3.3** User Defined Type Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a5fe374b-eddf-4808-9e73-477914940dba).

**MS-VBAL §5.2.3.3** uses the same accessibility wording as
[**MS-VBAL §5.2.3.4** Enum Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/da1d4885-946f-4937-9487-488143b97f08):
a user-defined type is accessible within the enclosing project, or within the enclosing module.

A public user-defined type declared in a class module reaches the project scope. A class module's other members
need an instance to be reached through, but a declared type needs none ([**RD-VBAL §5.2.3.4** Enum Declarations](#5234-enum-declarations)).

The _data type_ of a UDT value is defined by the UDT declaration of its _declared type_
([**RD-VBAL §2.5.2.1.3** User-Defined Type (UDT) Values](rd-vbal.2.5.2.1.3.udt-values.md)).


## 5.2.3.4 Enum Declarations

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.3.4** Enum Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/da1d4885-946f-4937-9487-488143b97f08).

**MS-VBAL §5.2.3.4** states that "the Enum type *and its Enum members* are accessible within the enclosing project"
/ "within the enclosing module".

An `Enum`'s members are lexically scoped like the Enum type itself: accessible within the enclosing project, or
within the enclosing module.

|Aspect|Enum member|
|---|---|
|Access modifier|An Enum member has no access modifier of its own.|
|Visibility|An Enum member takes its visibility from its Enum's own access modifier.|
|Symbol parent|An enum constant (Enum member) symbol parents to its Enum rather than to a scope.|
|Scope placement|[ScopeTreeBuilder](../api/RDCore.SDK.Model.Symbols.ScopeTreeBuilder.html) resolves an Enum member's scope placement through its Enum ([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)).|

👉 Because Enum members are placed through their Enum, an Enum member such as `vbSunday` is a name on its own,
resolvable without qualification.

A public `Enum` declared in a class module reaches the project scope. A class module's other members need an
instance to be reached through, but a declared type (an Enum or a user-defined type) needs none.


## 5.2.3.5 External Procedure Declaration

This section corresponds to [**MS-VBAL §5.2.3.5** External Procedure Declaration](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/75679e90-7e14-420d-af11-f83ffaf60418).

> [!NOTE]
> Reserved. This section has no content yet.


## 5.2.3.6 Circular Module Dependencies

This section corresponds to [**MS-VBAL §5.2.3.6** Circular Module Dependencies](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4e4256bb-0f8c-4a7e-8355-a0c8e25e37bd).

> [!NOTE]
> Reserved. This section has no content yet.

---
> ⏮️ [**RD-VBAL §5.2.2** Implicit Definition Directives](rd-vbal.5.2.2.implicit-definition-directives.md) | ⏭️ [**RD-VBAL §5.2.4** Class Module Declarations](rd-vbal.5.2.4.class-module-declarations.md)
