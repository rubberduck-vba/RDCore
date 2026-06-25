# 2.4. RD-VBA Type System - Static Types
> [!NOTE]
> This specification may be incomplete at this time.

The _type system_ begins with `VBType` and the _abstractions_ that formalize the implicit classifications in MS-VBAL:

- [VBIntrinsicType](../api/RDCore.SDK.Model.Types.Abstract.VBIntrinsicType.html)
- [VBNumericType](../api/RDCore.SDK.Model.Types.Abstract.VBNumericType.html)
- [IIntegralNumericType](../api/RDCore.SDK.Model.Types.Abstract.IIntegralNumericType.html)
- [IFixedPointNumericType](../api/RDCore.SDK.Model.Types.Abstract.IFixedPointNumericType.html)
- [IFloatingPointNumericType](../api/RDCore.SDK.Model.Types.Abstract.IFloatingPointNumericType.html)

These abstract types and interfaces are very useful for pattern-matching types and values in both static and runtime semantics implemantations.

All representations of a _data type_ inherit the `VBType` class, often indirectly through other abstract types that semantically specialize it.

## 2.4.1 VBIntrinsicType

In RD-VBA, a type is an _intrinsic type_ if MS-VBAL mentions it, irrespective of its specified semantics or modelization.

The non-numeric _intrinsic types_ are:
- [VBArrayType](../api/RDCore.SDK.Model.Types.VBArrayType.html)
- [VBFixedSizeArrayType](../api/RDCore.SDK.Model.Types.VBFixedSizeArrayType.html)
- [VBResizableArrayType](../api/RDCore.SDK.Model.Types.VBResizableArrayType.html)
- [VBResizableByteArrayType](../api/RDCore.SDK.Model.Types.VBResizableByteArrayType.html)
- [VBBooleanType](../api/RDCore.SDK.Model.Types.VBBooleanType.html)
- [VBDateType](../api/RDCore.SDK.Model.Types.VBDateType.html)
- [VBEmptyType](../api/RDCore.SDK.Model.Types.VBEmptyType.html)
- [VBErrorType](../api/RDCore.SDK.Model.Types.VBErrorType.html)
- [VBLongPtr_x64](../api/RDCore.SDK.Model.Types.VBLongPtrType_x64.html)
- [VBLongPtr_x86](../api/RDCore.SDK.Model.Types.VBLongPtrType_x86.html)
- [VBMissingType](../api/RDCore.SDK.Model.Types.VBMissingType.html)
- [VBNullType](../api/RDCore.SDK.Model.Types.VBNullType.html)
- [VBObjectType](../api/RDCore.SDK.Model.Types.VBObjectType.html)
- [VBStringType](../api/RDCore.SDK.Model.Types.VBStringType.html)
- [VBFixedStringType](../api/RDCore.SDK.Model.Types.VBFixedStringType.html)
- [VBVariantType](../api/RDCore.SDK.Model.Types.VBVariantType.html)

## 2.4.1.1 VBNumericType

_Numeric types_ are _intrinsic types_ that represent a _numeric_ value, regardless of its representation. All numeric types additionally implement one of the following _marker interfaces_:
- [IIntegralNumericType](../api/RDCore.SDK.Model.Types.Abstract.IIntegralNumericType.html) for all integer types;
- [IFixedPointNumericType](../api/RDCore.SDK.Model.Types.Abstract.IFixedPointNumericType.html) for all fixed-point numeric types;
- [IFloatingPointNumericType](../api/RDCore.SDK.Model.Types.Abstract.IFloatingPointNumericType.html) for all floating-point numeric types.

The _intrinsic numeric data types_ are the following (showing the managed/.NET min/max values for simplicity):

| Type | Interface | Description | MinValue | MaxValue |
| --- | --- | --- | --- | --- |
| [VBBypeType](../api/RDCore.SDK.Model.Types.VBByteType.html)|`IIntegralNumericType`|8-bit `Byte` unsigned integer|`Byte.MinValue` (0)|`Byte.MaxValue` (255)|
| [VBIntegerType](../api/RDCore.SDK.Model.Types.VBIntegerType.html)|`IIntegralNumericType`|16-bit `Integer` signed integer|`Int16.MinValue` (-32,768)|`Int16.MaxValue` (32,767)|
| [VBLongType](../api/RDCore.SDK.Model.Types.VBLongType.html)|`IIntegralNumericType`|32-bit `Long` signed integer|`Int32.MinValue` (-2,147,483,648)|`Int32.MaxValue` (2,147,486,647)|
| [VBLongLongType](../api/RDCore.SDK.Model.Types.VBLongLongType.html)|`IIntegralNumericType`|64-bit `LongLong` signed integer|`Int64.MinValue` (-9,223,372,036,854,775,808)|`Int64.MaxValue` (9,223,372,036,854,775,807)|
| [VBSingleType](../api/RDCore.SDK.Model.Types.VBSingleType.html)|`IFloatingPointNumericType`|32-bit single-precision floating-point|`Single.MinValue`|`Single.MaxValue`|
| [VBDoubleType](../api/RDCore.SDK.Model.Types.VBDoubleType.html)|`IFloatingPointNumericType`|64-bit double-precision floating-point|`Double.MinValue`|`Double.MaxValue`|

## 2.4.1.2 VBStringType

_String types_ are internally represented using a `UTF-16` standard .NET `System.String` value.

- [VBStringType](../api/RDCore.SDK.Model.Types.VBStringType.html)
- [VBFixedStringType](../api/RDCore.SDK.Model.Types.VBFixedStringType.html)

## 2.4.1.3 VBArrayType

_Array types_ include the following implementations:

- [VBFixedSizeArrayType](../api/RDCore.SDK.Model.Types.VBFixedSizeArrayType.html)
- [VBResizableArrayType](../api/RDCore.SDK.Model.Types.VBResizableArrayType.html)
- [VBResizableByteArrayType](../api/RDCore.SDK.Model.Types.VBResizableByteArrayType.html)

Notes:  
- `ReDim` is illegal to use with any `VBFixedSizeArrayValue`, but may be use to declare or _redimension_ an already-declared `VBResizableArrayValue`.  
- If the _item type_ of a _resizable array value_ is `VBByteType`, the data type of the array is `VBResizableByteArrayType`. This array type has specific _let-coercion_ semantics attached, allowing implicit conversion to and from `VBStringType`.
- The _type_ itself does not encode any dimensions; but the associated _value_ type does. See [VBArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBArrayValue.html) and its derived types.


## 2.4.2 Non-intrinsic Types

These data types are additional RD-VBA internal data types that are **not exposed** to (or directly usable in) _workspace source code_ but help complete the modelization of the system.

They include:
- [VBStdModuleType]
- [VBClassType]
- [VBCollectionType]
- [VBEnumType]
- [VBProjectType]
- [VBUnknownType]
- [VBVoidType]

### 2.4.2.1 VBStdModuleType

Represents a _standard module_, which is functionally equivalent to a managed `static class`. Defined by _workspace source code_ or imported from a _referenced library_.

### 2.4.2.2 VBClassType

Represents an _object type_ defined by _workspace source code_ in a _class module_. This type implements [IVBMemberOwnerType](../api/RDCore.SDK.Model.Types.Abstract.IVBMemberOwnerType.html), which makes it expose an _immutable array_ of [VBTypeMemberSymbol](../api/RDCore.SDK.Model.Symbols.Abstract.VBTypeMemberSymbol.html) where each element describes a _member_ of the class type.

#### 2.4.2.2.1 SuperTypes

While class modules defined in the _workspace source code_ have no means to _inherit_ another class module in the _Object-Oriented Programming_ sense of "Inheritance", all VBA classes still inherit a _base class_ that exposes `Initialize` and `Terminate` internal events, respectively fired by the _host_ upon instantiation and destruction of an instance (_object_) of a given class type.

If a class module specifies any `Implements` directives, the interfaces specified by such directives are included in this array.

#### 2.4.2.2.1.1 Extensible ("Document") Modules
> ℹ️ _extensible class modules_ have host-defined interfaces in their `SuperTypes` array that require information that is **unavailable to the RD-VBA host without a library reference to the library that defines these types**. In other words RD-VBA code that depends on for example the _Microsoft Excel_ type library, **requires** the _Microsoft Excel_ type library to correctly resolve the members and expressions inside such class modules.

> [!WARNING]
> Extensible modules **cannot** specify any `Implements` directives. This specification is not strictly enforced in MS-VBA, which can cause host application instabilities, source project corruption, and host application crashes for what should be statically caught early on as normal _compile-time_ errors; **RD-VBA** must _explicitly_, _statically_ deny `Implements` directives in these modules.


#### 2.4.2.2.2 Default Member

Members of a class can contain a `VB_UserMemId` attribute that can specify a number of flags that modify the behavior. A class can have _exactly one_ member with a `VB_UserMemId` attribute value of `0`, making that member the class type's _default member_.

The _default member_ of a class type can be implicitly invoked through _let-coercion_, yielding the _data value_ of the object, recursively as needed if the _default member_ also yields an object data type.

### 2.4.2.3 VBCollectionType
> 🧩 _Object value_ types (inheriting [VBObjectValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBObjectValue.html)) representing _an instance of a collection type_ should implement [IEnumerableObject](../api/RDCore.SDK.Model.Types.Abstract.IEnumerableObject.html).

A subclass of [VBClassType](../api/RDCore.SDK.Model.Types.VBClassType.html) that exposes a `NewEnum` member and can be _efficiently_ iterated using a `For Each...Next` loop structure.

> [!TIP]
> `For Each...Next` enumeration is intended to be used with _collections containing objects_. Performance-related _diagnostics_ should be issued when a `VBCollectionType` is being _accessed by index_ within the body of a `For...Next` loop.

> [!NOTE]
> The VBA language specification (MS-VBAL) sometimes refers to the _elements_ (or _items_) of a collection as "data members". This term sometimes appears in error messages (see [VBR00461](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorId.html) `MethodOrDataMemberNotFound`), but is **objectively confusing terminology** that RD-VBAL is discarding: in RD-VBA a "member" is always a _direct child symbol of a module, user-defined type, or enum_. Regardless of the message content, RD-VBA must still raise the MS-VBA equivalent error code in the relevant contexts.

### 2.4.2.4 VBProjectType

The symbol at the top of the _abstract syntax tree_ (AST) of an entire VBA project is of a type inherited from `VBProjectType`. These correspond to the _project types_ defined in MS-VBAL§4.1:

- [VBHostProjectType](../api/RDCore.SDK.Model.Types.Complex.VBHostProjectType.html)
- [VBLibraryProjectType](../api/RDCore.SDK.Model.Types.Complex.VBLibraryProjectType.html)
- [VBSourceProjectType](../api/RDCore.SDK.Model.Types.Complex.VBSourceProjectType.html)

#### 2.4.2.4.1 VBSourceProject

This type corresponds to the RD-VBA _workspace folder_ (.rdproj) content, representing the _workspace source code_ of a VBA project.

#### 2.4.2.4.2 VBLibraryProject

This type corresponds to a _referenced library_, referenced by a `VBSourceProjectType` (_source project_). A project of this type is defined in an implementation-defined manner and exposes the types and members of the library to RD-VBA source code through the means available to any other VBA source code, making it _feel_ as though _workspace source code_ is manipulating objects and members defined in VBA source code - but the library itself may or may not have been compiled from such.

#### 2.4.2.4.3 VBHostProjectType

This type of project can be introduced into the _RD-VBA environment_ by the _host_ (rdc.exe) in an implementation-defined manner. Additional _workspace source code_ may be added to this project if it is _open_ ("an _open_ host project"), by agents _other than the host application_.

> 👉 `RDCoreReference` for references of this type must have the "unremovable" flag set.


### 2.4.2.5 VBUnknownType

A special data type that represents an _unresolved type_. This is the **fallback data type** used when type resolution semantics fail to identity a valid data type for a given value.

> [!WARNING]
> Unknown types represent a **compile-time binding failure** and should raise error [RDC009311](../api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) `UserDefinedTypeNotDefined` (which is arguably a confusing wording; _verbose_ messages should help clarify the meaning).

### 2.4.2.6 VBVoidType

A special data type that represents _the absence of return value semantics_; this is the data type returned by `Sub`, `Property Let`, and `Property Set` procedures.

> 👉 The internal representation of this type being a managed `Int32` value (0), because the intent is to ultimately support `HRESULT` interoperability.


## 2.4.3 Meta and Advanced Types
These data types are _introspective_ types that can _reflectively_ describe the type system itself, and as such **MUST NOT BE EXPOSED** directly to RD-VBA source code: doing so would BREAK THE LANGUAGE on a fundamental level.  

> 🧩 They DO, however, provide **extremely powerful** _language-level extension_ possibilities.

These types include:

- [VBTypeDesc](../api/RDCore.SDK.Model.Types.Meta.VBTypeDesc.html)
- [VBMemberDesc](../api/RDCore.SDK.Model.Types.Meta.VBMemberDesc.html)
- [VBParameterDesc](../api/RDCore.SDK.Model.Types.Meta.VBParameterDesc.html)


### 2.4.3.1 VBTypeDesc
Represents (describes) a [VBType](../api/RDCore.SDK.Model.Types.VBType.html) within the type system.

👉 A _value_ (see [VBTypeDescValue](../api/RDCore.SDK.Model.Values.Meta.VBDeferredTypeDescValue.html)) of this meta-type is notably used in the implementation of the `Is` relational operator and of _let-coercion_, where semantics demand knowledge of a _data type_ where a _value_ is normally required.

None of the other descriptor types are currently in use, but _language core_ or future _language extension_ features may eventually use them as needed.

### 2.4.3.2 VBMemberDesc

An _abstract_ descriptor that represents (describes) any [VBTypeMemberSymbol](../api/RDCore.SDK.Model.Symbols.Abstract.VBTypeMemberSymbol.html).

#### 2.4.3.2.1 VBProcedureMemberDesc

A descriptor that represents (describes) any [VBProcedureMemberSymbol](../api/RDCore.SDK.Model.Symbols.VBProject.VBProcedureMemberSymbol.html).

#### 2.4.3.2.2 VBPropertyLetMemberDesc

A descriptor that represents (describes) a [VBProprertyLetMemberSymbol](../api/RDCore.SDK.Model.Symbols.VBProject.VBPropertyLetMemberSymbol.html).

#### 2.4.3.2.3 VBPropertySetMemberDesc

A descriptor that represents (describes) a [VBProprertySetMemberSymbol](../api/RDCore.SDK.Model.Symbols.VBProject.VBPropertySetMemberSymbol.html).

#### 2.4.3.2.4 VBReturningMemberDesc

An _abstract_ descriptor that represents (describes) any [VBReturningMemberSymbol](../api/RDCore.SDK.Model.Symbols.Abstract.VBReturningMemberSymbol.html).

##### 2.4.3.2.4.1 VBFunctionProcedureDesc

A descriptor that represents (describes) a [VBFunctionMemberSymbol](../api/RDCore.SDK.Model.Symbols.VBProject.VBFunctionMemberSymbol.html).

##### 2.4.3.2.4.2 VBPropertyGetProcedureDesc

A descriptor that represents (describes) a [VBFunctionMemberSymbol](../api/RDCore.SDK.Model.Symbols.VBProject.VBFunctionMemberSymbol.html).

### 2.4.3.3 VBParameterDesc

A descriptor that represents (describes) a [VBParameterSymbol](../api/RDCore.SDK.Model.Symbols.VBProject.VBParameterSymbol.html).


## 2.4.4 Deferred Types
The RD-VBA type system defines a category of _deferred_ data types. These types are **not currently in use**, but their presence intends to **prepare the ground** for supporting _IntelliSense_ and other features in _late-bound_ and other cases where MS-VBA would fail to resolve a valid compile-time data type.

A _deferred type_ is a valid RD-VBA _data type_ representing an _undefined_ type that may statically be one of the following data types:
- [VBVariant](../api/RDCore.SDK.Model.Types.VBVariantType.html)
- [VBObject](../api/RDCore.SDK.Model.Types.VBObjectType.html)

An _undefined_ type is a _workspace-defined_ data type that does not have any associated _source code_.  


_**Deferred names**_  

- The name of a _deferred type_ is a _candidate name_ that can change depending on how the _deferred type_ is being used.
- The name of a _deferred member_ is the _identifier name_ identifying it in the _workspace source code_. Any given two symbols should be resolved to the same _deferred member_ if the resolved _qualifying module_ is the same for both symbols. Without a qualifier, the deferred symbol is deemed to be an _undeclared local variable_ as per MS-VBAL scoping rules, meaning if a global-scope _deferred symbol_ with the **same identifier name** exists, then the _undeclared local variable_ should resolve to the global-scope _deferred symbol_.
- The name of a _deferred parameter_ is `Arg` followed by its 1-based position in the _deferred member signature_, in sequence such that the first parameter is named `Arg1`, the second is named `Arg2`, and so on. If a call site supplies a _named argument_ that is not a defined _deferred paraemter_, then a _deferred parameter_ with that name is defined at the end of the _deferred member signature_ (multiple such named parameters would then be materialized in alphabetical order), and deemed `Optional`.


> [!TIP]
> Deferred types cannot implement [IVBMemberOwnerType](../api/RDCore.SDK.Model.Types.Abstract.IVBMemberOwnerType.html), because the related symbols are _unbound_. Instead they implement [IVBInferableType](../api/RDCore.SDK.Model.Types.Complex.IVBInferableType.html), exposing an immutable hashset of _candidate types_ that would be legal to materialize the type with.

### 2.4.4.1 VBDeferredModuleType

Represents a _deferred_ [VBStdModuleType](../api/RDCore.SDK.Model.Types.Complex.VBStdModuleType.html).

A _deferred member_ is presumed to belong to a _deferred module_ unless it is qualified, in which case it is added to the resolved module type's `IVBMemberOwnerType.DeferredMembers` array if the resolved module has a _bound symbol_. If the symbol is _unbound_, then it's a named _deferred module_. Only **ONE** single unnamed _deferred module_ may be _semantically_ (but not _statically_) defined at any given time in a _source project_.

### 2.4.4.2 VBDeferredClassType

Represents a _deferred_ [VBClassType](../api/RDCore.SDK.Model.Types.Complex.VBClassType.html).

👉 Deferred class types cannot be presumed to have a _default instance_ (set by a `VB_PreDeclaredId` module attribute with the value `True`).  

A _deferred class_ is semantically defined when an _unbound member call_ is made against an _object variable_ of a _class type_ that may or may not be defined in the _workspace source code_. The default name of a _deferred class_ is the word `Class` followed by as many numeric digits as needed to make the class name unique in the workspace, in numerical order. In other words the default name of a _deferred class type_ is `Class1`, unless there's already a `Class1` module in the workspace, in which case the default name of the _deferred class type_ is `Class2`, and so on until a unique, non-existing name is found.

> ⚠️ The names of any _deferred type_ defined in _workspace source code_ must be considered as "in use" for all operations involving the naming of a module, including the addition of new (bound) modules in the workspace or project.

### 2.4.4.3 VBDeferredTypeDesc
Represents (describes) a [VBDeferredType](../api/RDCore.SDK.Model.Types.VBType.html) within the type system.

👉 A _value_ (see [VBTypeDescValue](../api/RDCore.SDK.Model.Values.Meta.VBDeferredTypeDescValue.html)) of this meta-type is used in the implementation of the `Is` relational operator, where semantics demand knowledge of a _data type_ where a _value_ is normally required.

None of the other descriptor types are currently in use, but _language core_ or future _language extension_ features may eventually use them as needed. All _deferred_ descriptor types inherit the corresponding _non-deferred_ descriptor type and explicitly _shadow_ (hide) the static `TypeInfo` property (when the descriptor describes a _type_).


#### 2.4.4.4 VBDeferredMemberDesc

A descriptor that represents (describes) any [VBDeferredTypeMemberSymbol](../api/RDCore.SDK.Model.Symbols.VBProject.VBDeferredTypeMemberSymbol.html).

#### 2.4.4.4.1 VBDeferredProcedureMemberDesc

A descriptor that represents (describes) a _non-returning_ [VBDeferredProcedureMemberSymbol](../api/RDCore.SDK.Model.Symbols.VBProject.VBDeferredTypeMemberSymbol.html).

#### 2.4.4.4.2 VBDeferredPropertyLetProcedureDesc

A descriptor that represents (describes) a deferred `Property Let` member.

#### 2.4.4.4.3 VBDeferredPropertySetProcedureDesc

A descriptor that represents (describes) a deferred `Property Set` member.

#### 2.4.4.4.4 VBDeferredFunctionProcedureDesc

A descriptor that represents (describes) a deferred `Function` member.

#### 2.4.4.4.5 VBDeferredPropertyGetProcedureDesc

A descriptor that represents (describes) a deferred `Function` member.

#### 2.4.4.4.6 VBDeferredParameterDesc

A descriptor that represents (describes) a single _parameter_ of a _deferred member_.  

👉 Deferred parameters are inferred from the arguments provided at the call sites of _deferred members_.  

The data type of a _deferred parameter_ depends on how many call sites supply an argument for it, and the data types of these arguments.

- Given only `VBBooleanValue` values, the data type is [VBBooleanType](../api/RDCore.SDK.Model.Types.VBBooleanType.html);
- Given only `VBObjectValue` values, the data type is [VBObjectType](../api/RDCore.SDK.Model.Types.VBObjectType.html);
  - If the _class type_ is resolvable (whether deferred or not), the data type is of that specific _class type_.
- Given any number of `VBStringType` values, the data type is [VBStringType](../api/RDCore.SDK.Model.Types.VBStringType.html);
- Given any number of `VBEnumType` values, the data type is [VBIntegerType](../api/RDCore.SDK.Model.Types.VBIntegerType.html);
- Given any number of `IIntegralNumericType` values, the data type is the **largest** of the _candidate types_;
- Given any number of `IFixedPointNumericType` values, the data type is [VBCurrencyType](../api/RDCore.SDK.Model.Types.VBCurrencyType.html);
- Given any number of `IFloatingPointNumericType` values, the data type is [VBDoubleType](../api/RDCore.SDK.Model.Types.VBDoubleType.html);

All of the above define a deferred `ByVal` parameter that is _passed by value_; the following define a `ByRef` parameter that must be _passed by reference_:

- Given a specific `VBUserDefinedTypeValue`, the data type is that of the specified UDT.
- Given any number of `VBArrayValue` or any mixed-bag heterogenous call site arguments, the data type is `Variant`. This case may not be materializable and should issue _semantic flags_ as appropriate to signal it to any listening language-level extensions.


> ⏮️ [**RD-VBAL §2.3** Application Host](rd-vbal.2.3.application-host.html) | ⏭️ [**RD-VBAL§2.5** Runtime Values](rd-vbal.2.5.runtime-values.html)

