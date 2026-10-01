# 2.4.2 Non-intrinsic Types

_Non-intrinsic types_ are additional RD-VBA internal data types. They are not exposed to, or directly usable in, _workspace source code_. They complete the model of the system.

The non-intrinsic types are:

|Type|Section|
|---|---|
|[VBStdModuleType](../api/RDCore.SDK.Model.Types.Complex.VBStdModuleType.html)|[§2.4.2.1](#2421-vbstdmoduletype)|
|[VBClassType](../api/RDCore.SDK.Model.Types.Complex.VBClassType.html)|[§2.4.2.2](#2422-vbclasstype)|
|[VBCollectionType](../api/RDCore.SDK.Model.Types.Complex.VBCollectionType.html)|[§2.4.2.3](#2423-vbcollectiontype)|
|[VBEnumType](../api/RDCore.SDK.Model.Types.Complex.VBEnumType.html)||
|[VBProjectType](../api/RDCore.SDK.Model.Types.Abstract.VBProjectType.html)|[§2.4.2.4](#2424-vbprojecttype)|
|[VBUnknownType](../api/RDCore.SDK.Model.Types.VBUnknownType.html)|[§2.4.2.5](#2425-vbunknowntype)|
|[VBVoidType](../api/RDCore.SDK.Model.Types.Complex.VBVoidType.html)|[§2.4.2.6](#2426-vbvoidtype)|

## 2.4.2.1 VBStdModuleType

`VBStdModuleType` represents a _standard module_. A standard module is functionally equivalent to a managed `static class`.

A standard module is defined by workspace source code, or imported from a _referenced library_.

## 2.4.2.2 VBClassType

`VBClassType` represents an _object type_ defined by workspace source code in a _class module_.

`VBClassType` implements [IVBMemberOwnerType](../api/RDCore.SDK.Model.Types.Abstract.IVBMemberOwnerType.html). Through this interface, it exposes an _immutable array_ of [VBTypeMemberSymbol](../api/RDCore.SDK.Model.Symbols.Abstract.VBTypeMemberSymbol.html), where each element describes a _member_ of the class type.

### 2.4.2.2.1 SuperTypes

Class modules defined in workspace source code have no means to _inherit_ another class module, in the Object-Oriented Programming sense of inheritance; see also [**RD-VBAL §5.2.4** Class Module Declarations](rd-vbal.5.2.4.class-module-declarations.md).

All VBA classes nevertheless implicitly implement a `Class` interface that exposes the `Initialize` and `Terminate` events:

|`Class` member|Raised upon|
|---|---|
|`Initialize`|Instantiation of an instance (_object_) of a given class type|
|`Terminate`|Destruction of an instance (_object_) of a given class type|

The class has no `Implements` directive for it, but the interface is included in the `SuperTypes` array like any interface the class implements, its members having an implementation of their own (`DefaultImplementation`); it cannot be referred to by name. See [**RD-VBAL §5.3.1.10** Lifecycle Handler Declarations](rd-vbal.5.3.1.10.lifecycle-handler-declarations.md).

If a class module specifies any `Implements` directives ([**MS-VBAL §5.2.4.2** Implements Directive](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/da526020-9b41-44a6-a5f3-47a7ac255a9e)), the interfaces specified by those directives are included in the class type's `SuperTypes` array.

#### 2.4.2.2.1.1 Extensible ("Document") Modules

_Extensible class modules_ (also called "document" modules) have host-defined interfaces in their `SuperTypes` array. These interfaces require information that is unavailable to the RD-VBA host without a library reference to the library that defines these types.

For example, RD-VBA code that depends on the Microsoft Excel type library requires the Microsoft Excel type library in order to correctly resolve the members and expressions inside extensible (document) class modules.

> [!WARNING]
> Extensible modules cannot specify any `Implements` directives. RD-VBA must explicitly and statically deny `Implements` directives in extensible modules.

MS-VBA does not strictly enforce this rule. In MS-VBA, an `Implements` directive in an extensible module can cause host application instabilities, source project corruption, and host application crashes.

Such a directive should be detected statically, as an ordinary _compile-time_ error.

### 2.4.2.2.2 Default Member

Members of a class can carry a `VB_UserMemId` attribute (see [**RD-VBAL §3.1.1** Attributes](rd-vbal.3.1.1.attributes.md)). A `VB_UserMemId` attribute can specify a number of flags that modify the behavior of the member.

A class can have exactly one member with a `VB_UserMemId` attribute value of `0`. That member is the class type's _default member_.

The default member of a class type can be implicitly invoked through _let-coercion_, yielding the _data value_ of the object; see [**RD-VBAL §5.5.1.2** Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md). When the default member also yields an object data type, the implicit invocation recurses as needed; see [**RD-VBAL §5.6.2** Expression Evaluation](rd-vbal.5.6.2.expression-evaluation.md).

## 2.4.2.3 VBCollectionType

> 🧩 _Object value_ types (inheriting [VBObjectValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBObjectValue.html)) that represent an instance of a collection type should implement [IEnumerableObject](../api/RDCore.SDK.Model.Types.Abstract.IEnumerableObject.html).

`VBCollectionType` is a subclass of `VBClassType` that exposes a `NewEnum` member.

A `VBCollectionType` can be efficiently iterated using a `For Each...Next` loop structure; see [**RD-VBAL §5.4.2.4** For Each Statement](rd-vbal.5.4.2.4.for-each-statement.md).

> [!TIP]
> `For Each...Next` enumeration is intended to be used with collections containing objects. Performance-related _diagnostics_ should be issued when a `VBCollectionType` is accessed by index within the body of a `For...Next` loop; see [**RD-VBAL §5.4.2.3** For Statement](rd-vbal.5.4.2.3.for-statement.md).

### Data Members

MS-VBAL sometimes refers to the _elements_ (or _items_) of a collection as "data members". The term appears in error messages, e.g. [VBR00461](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorId.html) `MethodOrDataMemberNotFound`.

RD-VBAL discards the "data member" terminology for collection elements, because it is confusing. In RD-VBA, a "member" is always a direct child symbol of a module, user-defined type, or enum.

Regardless of the error message content, RD-VBA must still raise the MS-VBA equivalent error code (e.g. VBR00461 `MethodOrDataMemberNotFound`) in the relevant contexts; see [**RD-VBAL §2.6.3** Runtime Errors](rd-vbal.2.6.3.runtime-errors.md).

## 2.4.2.4 VBProjectType

The symbol at the top of the _abstract syntax tree_ (AST) of an entire VBA project is of a type inherited from `VBProjectType`.

The types inherited from `VBProjectType` correspond to the _project types_ defined in [**MS-VBAL §4.1** Projects](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4cd406c7-1ade-4522-8d7b-933183059ac7); see also [**RD-VBAL §4.0** Program Structure and Organization](rd-vbal.4.0.program-structure.md).

|Project type|Section|
|---|---|
|[VBSourceProjectType](../api/RDCore.SDK.Model.Types.Complex.VBSourceProjectType.html)|[§2.4.2.4.1](#24241-vbsourceprojecttype)|
|[VBLibraryProjectType](../api/RDCore.SDK.Model.Types.Complex.VBLibraryProjectType.html)|[§2.4.2.4.2](#24242-vblibraryprojecttype)|
|[VBHostProjectType](../api/RDCore.SDK.Model.Types.Complex.VBHostProjectType.html)|[§2.4.2.4.3](#24243-vbhostprojecttype)|

### 2.4.2.4.1 VBSourceProjectType

`VBSourceProjectType` corresponds to the RD-VBA _workspace folder_ (`.rdproj`) content; see [**RD-VBAL §2.2** RDPROJ Structure](rd-vbal.2.2.rdproj-structure.md).

`VBSourceProjectType` represents the workspace source code of a VBA project.

### 2.4.2.4.2 VBLibraryProjectType

`VBLibraryProjectType` corresponds to a _referenced library_. A library project is referenced by a `VBSourceProjectType` (_source project_).

A `VBLibraryProjectType` project is defined in an implementation-defined manner. It exposes the types and members of the library to RD-VBA source code through the means available to any other VBA source code.

Through these means, workspace source code manipulates the library's objects and members as if they were defined in VBA source code. The library itself may or may not have been compiled from VBA source code.

### 2.4.2.4.3 VBHostProjectType

A `VBHostProjectType` project can be introduced into the _RD-VBA environment_ by the _host_ (rdc.exe) in an implementation-defined manner.

Additional workspace source code may be added to a host project if it is _open_ ("an open host project"). Workspace source code is added to an open host project by agents other than the host application.

> 👉 An [RDCoreReference](../api/RDCore.SDK.Workspace.RDCoreReference.html) for a reference to a `VBHostProjectType` project must have the "unremovable" flag set; see [**RD-VBAL §2.2.3** ProjectFile](rd-vbal.2.2.3.projectfile.md).

## 2.4.2.5 VBUnknownType

`VBUnknownType` is a special data type that represents an _unresolved type_. It is the fallback data type used when type resolution semantics fail to identify a valid data type for a given value.

> [!WARNING]
> An unknown type represents a _compile-time binding failure_, and should raise compile error [VBC09311](../api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) `UserDefinedTypeNotDefined`; see [**RD-VBAL §2.6.2** Semantic Compilation Errors](rd-vbal.2.6.2.semantic-compilation-errors.md).

The `UserDefinedTypeNotDefined` wording can be confusing when it is raised for an unknown type. Verbose diagnostic messages should clarify its meaning in that case.

## 2.4.2.6 VBVoidType

`VBVoidType` is a special data type that represents the absence of return value semantics.

`VBVoidType` is the data type returned by `Sub`, `Property Let`, and `Property Set` procedures; see [**RD-VBAL §5.3.1.6** Subroutine and Function Declarations](rd-vbal.5.3.1.6.subroutine-and-function-declarations.md) and [**RD-VBAL §5.3.1.7** Property Declarations](rd-vbal.5.3.1.7.property-declarations.md).

> 👉 The internal representation of `VBVoidType` is a managed `Int32` value (0), because the intent is to ultimately support `HRESULT` interoperability.

---
> ⏮️ [**RD-VBAL §2.4.1** Intrinsic Types](rd-vbal.2.4.1.intrinsic-types.md) | ⏭️ [**RD-VBAL §2.4.3** Meta and Advanced Types](rd-vbal.2.4.3.meta-and-advanced-types.md)
