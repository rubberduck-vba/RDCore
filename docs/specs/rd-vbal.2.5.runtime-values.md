# 2.5. RD-VBA Type System - Runtime
> [!NOTE]
> This specification may be incomplete at this time.

If [VBType](../api/RDCore.SDK.Model.Types.Abstract.VBType.html) is at the core of _static semantics_, then [VBTypedValue](../api/RDCore.SDK.Model.Values.Abstract.VBTypedValue.html) is at the core of _runtime semantics_.


## 2.5.1 Runtime entities

A _runtime entity_ is a simple abstraction that associates a [VBType](../api/RDCore.SDK.Model.Types.Abstract.VBType.html) with a [Symbol](../api/RDCore.SDK.Model.Symbols.Abstract.Symbol.html), which means every RD-VBA _runtime entity_ is addressable with a `Uri` that is unique across the _workspace_.  

## 2.5.1.1 Symbols

A _symbol_ necessarily has a `Uri` and a `Name`, but also a [ScopeKind](../api/RDCore.SDK.Model.Symbols.Abstract.ScopeKind.html) and an _(extended) LSP_ [SymbolKind](../api/RDCore.SDK.Server.ProtocolExtensions.SymbolKindExt.html).

- The `Uri` of a symbol is a _semantic ID_ that uniquely identifies a symbol across an entire _workspace_; see [RDCoreUriNamespaces](../api/RDCore.SDK.Extensibility.RDCoreUriNamespaces.html). The `Uri` is assembled from the _workspace root_ `Uri` and a relative `Uri` that may include a _fragment_. The content of the fragment is implementation-defined, but the relative `Uri` path should be that of the parent module or procedure scope.
- The `Name` of a symbol defined in _workspace source code_ **must** be a valid _identifier name_.
- The `Name` of a _static symbol_ corresponds to its _identifier name_ if it has one. 
- Otherwise (e.g. operators), a _static symbol_ should have a `Name` that _clearly_ isn't a legal VBA name, to avoid any possible confusion.

> [!WARNING]
> While symbol `Uri` may _look_ hierarchical (and they _are_!), they should never be used to _rebuild_ a client-side tree-like structure.

The _scope kind_ of a `Symbol` determines exactly _how_ (and _whether_) it is allocated in memory, and can be one of the following:

|Value|Description|
|---|---|
|Unallocated|A pseudo-scope for pseudo-symbols that aren't allocated in memory, like [VBVoidValue](../api/RDCore.SDK.Model.Values.VBVoidValue.html).|
|Global|[StaticSymbol](../api/RDCore.SDK.Model.Symbols.Abstract.StaticSymbol.html) instances and symbols obtained from referenced libraries, mostly; lives in the _globals_ heap.|
|Local|Procedure level, scoped to the local [ICallStackFrame](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html).|
|Module|Module level; lives in the _workspace statics_ heap.|
|Instance|Instance level; lives in the _object_ heap.|
|External|Allocated externally; lives _out of process_ at a known address.|

The _symbol kind_ of a `Symbol` is as per specified in **LSP 3.17**. The values RD-VBA uses are as follows (see [SymbolKindExt](../api/RDCore.SDK.Server.ProtocolExtensions.SymbolKindExt.html)):

|RD-VBA Value|LSP Equivalence|
|---|---|
|`Module`|`SymbolKind.Module`|
|`Project`|`SymbolKind.Namespace`|
|`Class`|`SymbolKind.Class`|
|`Procedure`|`SymbolKind.Method`|
|`Field`|`SymbolKind.Field`|
|`Enum`|`SymbolKind.Enum`|
|`Interface`|`SymbolKind.Interface`|
|`Function`|`SymbolKind.Function`|
|`Variable`|`SymbolKind.Variable`|
|`Constant`|`SymbolKind.Constant`|
|`StringLiteral`|`SymbolKind.String`|
|`NumberLiteral`|`SymbolKind.Number`|
|`BooleanLiteral`|`SymbolKind.Boolean`|
|`Array`|`SymbolKind.Array`|
|`Object`|`SymbolKind.Object`|
|`Key`|`SymbolKind.Key`|
|`Null`|`SymbolKind.Null`|
|`EnumMember`|`SymbolKind.EnumMember`|
|`UserDefinedType`|`SymbolKind.Struct`|
|`Event`|`SymbolKind.Event`|
|`Operator`|`SymbolKind.Operator`|

> [!TIP]
> LSP standard symbol kinds `File`, `Constructor`, and `TypeParameter` are not used in RD-VBA, and `Namespace` is being repurposed to a different meaning (there is no concept of a _namespace_ in VBA). The `Key` symbol kind may end up being used for the token that follows the `!` operator in _dictionary access expressions_, since it represents, in fact, a _dictionary key_.  

**RD-VBA** additionally defines the following [_extension symbol kinds_](../api/RDCore.SDK.Server.ProtocolExtensions.SymbolKindExt.html):

- Ignored
- Attribute
- Directive
- LineLabel
- DateLiteral
- VariantLiteral
- TypeDescriptor

These extended _symbol kinds_ may or may not be supported by a LSP client (editor), but can help supply more precise _hover tips_ when they are.

## 2.5.2 VBTypedValue

RD-VBA defines MS-VBAL _data values_ explicitly, using types inherited from a base [VBTypedValue](../api/RDCore.SDK.Model.Values.Abstract.VBTypedValue.html).  

### 2.5.2.1 Intrinsic Type Values

The _data values_ of the non-numeric _intrinsic types_ are the following:

|VBTypedValue |VBType |
|---|---|
|[VBArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBArrayValue.html)|[VBArrayType](../api/RDCore.SDK.Model.Types.VBArrayType.html)|
|[VBFixedSizeArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBFixedSizeArrayValue.html)|[VBFixedSizeArrayType](../api/RDCore.SDK.Model.Types.VBFixedSizeArrayType.html)|
|[VBResizableArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBResizableArrayValue.html)|[VBResizableArrayType](../api/RDCore.SDK.Model.Types.VBResizableArrayType.html)|
|[VBResizableByteArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBResizableArrayValue.html)|[VBResizableByteArrayType](../api/RDCore.SDK.Model.Types.VBResizableByteArrayType.html)|
|[VBBooleanValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBBooleanValue.html)|[VBBooleanType](../api/RDCore.SDK.Model.Values.Intrinsic.VBBooleanValue.html)|
|[VBDateValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBDateValue.html)|[VBDateType](../api/RDCore.SDK.Model.Types.VBDateType.html)|
|[VBEmptyValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBEmptyValue.html)|[VBEmptyType](../api/RDCore.SDK.Model.Types.VBEmptyType.html)|
|[VBErrorValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBErrorValue.html)|[VBErrorType](../api/RDCore.SDK.Model.Types.VBErrorType.html)|
|[VBLongPtrValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBLongPtrValue.html)|[VBLongPtr_x64](../api/RDCore.SDK.Model.Types.VBLongPtrType_x64.html) or [VBLongPtr_x86](../api/RDCore.SDK.Model.Types.VBLongPtrType_x86.html) depending on host environment|
|[VBMissingValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBMissingValue.html)|[VBMissingType](../api/RDCore.SDK.Model.Types.VBMissingType.html)|
|[VBNullValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBNullValue.html)|[VBNullType](../api/RDCore.SDK.Model.Types.VBNullType.html)|
|[VBObjectValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBObjectValue.html)|[VBObjectType](../api/RDCore.SDK.Model.Types.VBObjectType.html)|
|[VBStringValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBStringValue.html)|[VBStringType](../api/RDCore.SDK.Model.Types.VBStringType.html)|
|[VBFixedStringValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBFixedStringValue.html)|[VBFixedStringType](../api/RDCore.SDK.Model.Types.VBFixedStringType.html)|
|[VBVariantValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBVariantValue.html)|[VBVariantType](../api/RDCore.SDK.Model.Types.VBVariantType.html)|


#### 2.5.2.1.1 VBNumericTypedValue

All numeric _data values_ inherit [VBNumericTypedValue](../api/RDCore.SDK.Model.Values.Abstract.VBNumericTypedValue.html), which simplifies implementing semantics implicating "_any numeric type_" specifications. This base class implements [INumericType](../api/RDCore.SDK.Model.Values.Abstract.INumericValue.html), a _non-generic_ base abstraction that ensures every numeric type minimally has a `double` managed representation.  

Each numeric type of value minimally defines a typed representation of its `MinValue`, `MaxValue`, and `Zero`; other _static values_ may be defined as required by runtime semantics.  

Floating-point types also define a `SignificantIntegerDigits` that is used for correctly representing these values as [VBStringValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBStringValue.html) in _conversions_ and _coercions_ to `String`:


|Value Type|Significant Digits|
|---|---|
|[VBSingleValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBSingleValue.html)|7|
|[VBDoubleValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBDoubleValue.html)|15|


A numeric _data value_ can be one of the following:
- [VBByteValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBByteValue.html)
- [VBIntegerValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBIntegerValue.html)
- [VBLongValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBLongValue.html)
- [VBLongLongValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBLongLongValue.html)
- [VBSingleValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBSingleValue.html)
- [VBDoubleValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBDoubleValue.html)
- [VBCurrencyValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBCurrencyValue.html)
- [VBDecimalValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBDecimalValue.html)

Additionally, _precompiler constants_ are interpreted as `Integer` values:

- [PrecompilerConstantValue](../api/RDCore.SDK.Model.Values.PrecompilerConstantValue.html)

#### 2.5.2.1.2 Array Values

An _array declaration_ creates an _array value_ of the appropriate _array type_ in the _scope_ of the declaration, depending on how its _dimensions_ are declared:

- An array declaration _with_ dimension specifications creates a `VBFixedSizeArrayValue`;
- An array declaration _without_ dimension specifications creates a `VBResizableArrayValue`;
- A _resizable array declaration_ that specifies a `Byte` _item type_ creates a `VBResizableByteArrayValue`.

The declaration provides the number and size of each dimension (up to **60**). Array value _dimensions_ are initialized with the _default value_ of the declared _item type_ of the array; the specialized array values exist to simplify pattern-matching in both static and runtime semantics.  

> 👉 If no _item type_ is specified in the declaration, then the _declared item type_ of the array is `Variant`.

An _array value_ is considered _initialized_ when it has any number of dimensions defined; if no dimensions are defined, the _array value_ is considered _uninitialized_.

- The _upper bound_ of an _uninitialized array value_ is `-1`.
- The _lower bound_ of an _uninitialized array value_ is dependent on the value of the `Option Base` directive, which is `0` by default but could be set to `1`.

Each dimension of an _array value_ encapsulates a _managed array_ of the underlying _managed type_ of the _declared item type_.  

> 👉 Implementation may optimize certain specific array values for storage and performance. For example 2D arrays may be implemented in a way that optimizes their managed memory layout to avoid unnecessarily iterating individual dimensions for _copy_ operations.


#### 2.5.2.1.3 User-Defined Types (UDT) Values

An instance of a UDT is a [VBUserDefinedTypeValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBUserDefinedTypeValue.html).

The _data type_ of a UDT value is defined by the UDT declaration of its _declared type_.  

> 👉 UDT values **MUST** be passed by reference (`ByRef`).


#### 2.5.2.1.4 Object Values

An instance of a [VBObjectType](../api/RDCore.SDK.Model.Types.VBObjectType.html) is always a [VBObjectValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBObjectValue.html).

The underlying value of an _object value_ is a unique addressable ID.


#### 2.5.2.1.5 Variant Values

A [VBVariantValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBVariantValue.html) has an underlying _managed value_ that is a managed `struct` type intended to eventually interop with actual COM (unmanaged) variant values.

The structure of this internal representation is defined as follows:

- [VBVariantValueType](../api/RDCore.SDK.Model.Values.Intrinsic.VBVariantValueType.html) `ValueType`, a flag that identifies the _variant value type_ (`Empty`, `Integer`, `Dispatch`, `BString`, etc.);
- [ScopeKind](../api/RDCore.SDK.Model.Symbols.Abstract.ScopeKind.html) `ValueAlloc`, giving the host the _allocation scope_ of the value;
- `long ValuePtr`, a pointer to the value in the memory space specified by the `ScopeKind`.

A `VBVariantValue` is always allocated in the _heap memory_, in the same memory space as _object values_.

> [!NOTE]
> The act of "unwrapping" a `VBVariantValue` value consists of looking up its allocated internal struct, retrieving its `ValuePtr`, then looking up that value in the appropriate memory space; this yields a scoped `VBTypedValue` that may or may not be an immediately usable _intrinsic data type_ - it may be also be another `VBVariantValue` requiring a new _unwrapping frame_.


> ⏮️ [**RD-VBAL §2.4** Static Types](rd-vbal.2.4.static-types.html) | ⏮️ [**RD-VBAL §3.0** Syntax Tree](rd-vbal.3.0.syntax-tree.html)

