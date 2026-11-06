# 2.4.4 Deferred Types

The RD-VBA type system defines a category of _deferred_ data types.

> [!NOTE]
> **Not implemented.** Deferred types are not in use.

Deferred types address late-bound and other cases where MS-VBA would fail to resolve a valid compile-time data type.

Using late binding and _duck typing_ to introduce and surface inherent deferred types to _design-time_ symbols is a valid _language core extension_. Surfacing deferred types to design-time symbols enables LSP-level enhanced capabilities, notably around auto-completion lists; see [**RD-VBAL §1.1.2** Language Core Extensions](rd-vbal.1.1.2.language-core-extensions.md).

A _deferred type_ is a valid RD-VBA data type representing an _undefined_ type. An undefined type is a _workspace-defined_ data type that does not have any associated source code.

A deferred type may statically be one of the following data types:

- [VBVariantType](../api/RDCore.SDK.Model.Types.VBVariantType.html)
- [VBObjectType](../api/RDCore.SDK.Model.Types.VBObjectType.html)

## Unresolved Names

The static-semantics layer consumes `ModuleDirectives` to decide whether an _unresolved_ simple name is a deferred `VBUnknownType` or a **VBC09302** _Variable not defined_ compile-time error; see [**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md).

RD-VBA defers the actual type guess for an unresolved name to a later type-inference pass ([IVBInferableType](../api/RDCore.SDK.Model.Types.Complex.IVBInferableType.html)), rather than deciding it in the simple-name-expression rule; see [**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md).

## Deferred Names

|Deferred symbol|Name|
|---|---|
|Deferred type|A _candidate name_. The candidate name can change depending on how the deferred type is being used.|
|Deferred member|The _identifier name_ identifying the member in the workspace source code.|
|Deferred parameter|`Arg` followed by its 1-based position in the _deferred member signature_: the first parameter is `Arg1`, the second is `Arg2`, and so on.|

Deferred members resolve as follows:

- Any two symbols should be resolved to the same deferred member if the resolved _qualifying module_ is the same for both symbols.
- Without a qualifier, a deferred symbol is deemed to be an _undeclared local variable_, as per MS-VBAL scoping rules.
- If a global-scope deferred symbol with the same identifier name exists, an unqualified deferred symbol (an undeclared local variable) should resolve to the global-scope deferred symbol ([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)).

Deferred parameters are also defined from _named arguments_:

- If a call site supplies a named argument that is not a defined deferred parameter, a deferred parameter with that name is defined at the end of the deferred member signature.
- Multiple deferred parameters defined from unmatched named arguments are materialized in alphabetical order.
- A deferred parameter defined from an unmatched named argument is deemed `Optional`.

## Member Ownership

Deferred types cannot implement [IVBMemberOwnerType](../api/RDCore.SDK.Model.Types.Abstract.IVBMemberOwnerType.html), because the related symbols are _unbound_.

Deferred types implement `IVBInferableType` instead of `IVBMemberOwnerType`. `IVBInferableType` exposes an immutable hashset of _candidate types_ that would be legal to materialize the deferred type with.

## 2.4.4.1 VBDeferredModuleType

[VBDeferredModuleType](../api/RDCore.SDK.Model.Types.Complex.VBDeferredModuleType.html) represents a deferred [VBStdModuleType](../api/RDCore.SDK.Model.Types.Complex.VBStdModuleType.html).

A deferred member is presumed to belong to a deferred module, unless the deferred member is qualified:

|Deferred member|Owner|
|---|---|
|Unqualified|A deferred module.|
|Qualified; the resolved module has a _bound symbol_|The resolved module type's `IVBMemberOwnerType.DeferredMembers` array.|
|Qualified; the qualifying module symbol is _unbound_|The named deferred module the qualifier denotes.|

Only one unnamed deferred module may be _semantically_ (but not _statically_) defined at any given time in a _source project_.

## 2.4.4.2 VBDeferredClassType

[VBDeferredClassType](../api/RDCore.SDK.Model.Types.Complex.VBDeferredClassType.html) represents a deferred [VBClassType](../api/RDCore.SDK.Model.Types.Complex.VBClassType.html).

Deferred class types cannot be presumed to have a _default instance_. A default instance is set by a `VB_PredeclaredId` module attribute with the value `True`; see [**RD-VBAL §3.1.1** Attributes](rd-vbal.3.1.1.attributes.md) and [**RD-VBAL §5.2.4** Class Module Declarations](rd-vbal.5.2.4.class-module-declarations.md).

A deferred class is semantically defined when an _unbound member call_ is made against an _object variable_ of a class type that may or may not be defined in the workspace source code.

The default name of a deferred class is the word `Class` followed by a number, taken in numerical order until the class name is unique in the workspace: `Class1`, unless a `Class1` module already exists in the workspace, in which case `Class2`, and so on.

> [!WARNING]
> The names of any deferred type defined in workspace source code must be considered "in use" for all operations involving the naming of a module, including the addition of new (bound) modules to the workspace or project.

## 2.4.4.3 VBDeferredTypeDesc

`VBDeferredTypeDesc` represents (describes) a [VBDeferredType](../api/RDCore.SDK.Model.Types.Complex.VBDeferredType.html) within the type system.

A value of the `VBDeferredTypeDesc` meta type, a [VBDeferredTypeDescValue](../api/RDCore.SDK.Model.Values.Meta.VBDeferredTypeDescValue.html), is used in the implementation of the `Is` relational operator; see [**RD-VBAL §5.6.9.7** Is Operator](rd-vbal.5.6.9.7.is-operator.md). The `Is` operator uses a deferred type descriptor value because its semantics demand knowledge of a _data type_ where a _value_ is normally required.

> [!NOTE]
> **Not implemented.** None of the other deferred descriptor types are in use.

All deferred descriptor types inherit the corresponding non-deferred descriptor type ([**RD-VBAL §2.4.3** Meta and Advanced Types](rd-vbal.2.4.3.meta-and-advanced-types.md)). A deferred descriptor type that describes a _type_ explicitly _shadows_ (hides) the static `TypeInfo` property of the non-deferred descriptor type it inherits.

## 2.4.4.4 VBDeferredMemberDesc

[VBDeferredMemberDesc](../api/RDCore.SDK.Model.Types.Meta.VBDeferredMemberDesc.html) is a descriptor that represents (describes) any [VBDeferredTypeMemberSymbol](../api/RDCore.SDK.Model.Symbols.VBProject.VBDeferredTypeMemberSymbol.html).

RD-VBAL groups the following descriptors under `VBDeferredMemberDesc`:

|Section|Descriptor|
|---|---|
|[§2.4.4.4.1](#24441-vbdeferredprocedurememberdesc)|`VBDeferredProcedureMemberDesc`|
|[§2.4.4.4.2](#24442-vbdeferredpropertyletproceduredesc)|`VBDeferredPropertyLetProcedureDesc`|
|[§2.4.4.4.3](#24443-vbdeferredpropertysetproceduredesc)|`VBDeferredPropertySetProcedureDesc`|
|[§2.4.4.4.4](#24444-vbdeferredfunctionproceduredesc)|`VBDeferredFunctionProcedureDesc`|
|[§2.4.4.4.5](#24445-vbdeferredpropertygetproceduredesc)|`VBDeferredPropertyGetProcedureDesc`|
|[§2.4.4.4.6](#24446-vbdeferredparameterdesc)|`VBDeferredParameterDesc`|

### 2.4.4.4.1 VBDeferredProcedureMemberDesc

[VBDeferredProcedureMemberDesc](../api/RDCore.SDK.Model.Types.Meta.VBDeferredProcedureMemberDesc.html) is a descriptor that represents (describes) a _non-returning_ deferred procedure member symbol (a `VBDeferredTypeMemberSymbol`).

### 2.4.4.4.2 VBDeferredPropertyLetProcedureDesc

[VBDeferredPropertyLetProcedureDesc](../api/RDCore.SDK.Model.Types.Meta.VBDeferredPropertyLetProcedureDesc.html) is a descriptor that represents (describes) a deferred `Property Let` member.

### 2.4.4.4.3 VBDeferredPropertySetProcedureDesc

[VBDeferredPropertySetProcedureDesc](../api/RDCore.SDK.Model.Types.Meta.VBDeferredPropertySetProcedureDesc.html) is a descriptor that represents (describes) a deferred `Property Set` member.

### 2.4.4.4.4 VBDeferredFunctionProcedureDesc

[VBDeferredFunctionProcedureDesc](../api/RDCore.SDK.Model.Types.Meta.VBDeferredFunctionProcedureDesc.html) is a descriptor that represents (describes) a deferred `Function` member.

### 2.4.4.4.5 VBDeferredPropertyGetProcedureDesc

[VBDeferredPropertyGetProcedureDesc](../api/RDCore.SDK.Model.Types.Meta.VBDeferredPropertyGetProcedureDesc.html) is a descriptor that represents (describes) a deferred `Property Get` member.

### 2.4.4.4.6 VBDeferredParameterDesc

[VBDeferredParameterDesc](../api/RDCore.SDK.Model.Types.Meta.VBDeferredParameterDesc.html) is a descriptor that represents (describes) a single _parameter_ of a deferred member.

Deferred parameters are inferred from the arguments provided at the call sites of deferred members.

The data type of a deferred parameter depends on how many call sites supply an argument for the parameter. It also depends on the data types of the arguments supplied for it at the call sites:

|Call-site arguments|Deferred parameter data type|Parameter|
|---|---|---|
|Only [VBBooleanValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBBooleanValue.html) arguments|[VBBooleanType](../api/RDCore.SDK.Model.Types.VBBooleanType.html)|`ByVal`|
|Only [VBObjectValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBObjectValue.html) arguments|`VBObjectType`|`ByVal`|
|Only `VBObjectValue` arguments whose class type is resolvable (whether deferred or not)|That specific class type|`ByVal`|
|Any number of `VBStringType` (string) arguments|[VBStringType](../api/RDCore.SDK.Model.Types.VBStringType.html)|`ByVal`|
|Any number of `VBEnumType` (enum) arguments|[VBIntegerType](../api/RDCore.SDK.Model.Types.VBIntegerType.html)|`ByVal`|
|Any number of `IIntegralNumericType` arguments|The largest of the _candidate types_|`ByVal`|
|Any number of `IFixedPointNumericType` arguments|[VBCurrencyType](../api/RDCore.SDK.Model.Types.VBCurrencyType.html)|`ByVal`|
|Any number of `IFloatingPointNumericType` arguments|[VBDoubleType](../api/RDCore.SDK.Model.Types.VBDoubleType.html)|`ByVal`|
|A specific [VBUserDefinedTypeValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBUserDefinedTypeValue.html)|The data type of the specified UDT|`ByRef`|
|Any number of [VBArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBArrayValue.html) arguments|`Variant`|`ByRef`|
|Mixed-bag (heterogeneous) arguments|`Variant`|`ByRef`|

A deferred `ByVal` parameter is _passed by value_. A deferred `ByRef` parameter must be _passed by reference_.

> [!NOTE]
> A deferred parameter inferred as `Variant` (from array or heterogeneous arguments) may not be materializable.

A deferred parameter inferred as `Variant` should issue _semantic flags_ as appropriate, to signal the case to any listening language-level extensions; see [**RD-VBAL §1.1.3** Core Semantic Flags](rd-vbal.1.1.3.core-semantic-flags.md).
