# 2.5.2.1.2 Array Values

An array value is a [VBArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBArrayValue.html), or one of its specialized array values.

## Array Declarations

An _array declaration_ creates an _array value_ of the appropriate _array type_ in the _scope_ of the declaration. Which array value it creates depends on how its _dimensions_ are declared:

|Array declaration|Array value created|
|---|---|
|With dimension specifications|[VBFixedSizeArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBFixedSizeArrayValue.html)|
|Without dimension specifications|[VBResizableArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBResizableArrayValue.html)|
|Resizable, with a `Byte` _item type_|[VBResizableByteArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBResizableByteArrayValue.html)|

The specialized array values exist to simplify pattern-matching in both static and runtime semantics.

> 👉 If no _item type_ is specified in an array declaration, the _declared item type_ of the array is `Variant`.

Array declarations are described in [**RD-VBAL §5.2.3** Module Declarations](rd-vbal.5.2.3.module-declarations.md) and [**RD-VBAL §5.4.3.1** Local Variable Declarations](rd-vbal.5.4.3.1.local-variable-declarations.md).

## Dimensions and Bounds

- An array declaration provides the number of dimensions of the array, and the size of each dimension.
- An array has up to 60 dimensions.
- Each dimension records only its _lower bound_ and _upper bound_. These are the operands of `LBound` and `UBound`.

The value associated with an array type encodes the array dimensions. Evaluating each bound to a `Long`, and resolving an omitted lower bound against `Option Base`, is the concern of whatever allocates the storage of the variable; see [**RD-VBAL §2.4.1** Intrinsic Types](rd-vbal.2.4.1.intrinsic-types.md).

### Declared arrays

What a declaration says of an array travels with the symbol it declares, because the type has no room for it:

- the _kind_ and _element type_ are the type (`VBFixedSizeArrayType`, `VBResizableArrayType`, each of an element type);
- the _bounds_ of a fixed-size array are constant expressions (**MS-VBAL §5.2.3.1.3**) that may name a `Const`, parsed by the declaration pass
  (`ArrayDimensionBound.LowerExpression`, `UpperExpression`) and held on the symbol (`SymbolProperties.ArrayBounds`).

Both reach the environment host in the symbol descriptors (`SymbolDescriptor.Array`, `LocalDescriptor.Array`, `ParameterDescriptor.Array`: whether it is
fixed-size, and the bound expressions), with the element type as the descriptor's type name.

The host allocates the storage of a variable through `IVariableDefaults` (`ISessionSymbols.Defaults`), which the execution pipeline sets:
a fixed-size array starts with the dimensions its bounds reduce to - an omitted lower bound being the `Option Base` of the module - and a resizable one
starts uninitialized, as an array of its declared element type. A procedure's local is allocated with each activation, a field of a class with each object,
and a module-level array when its module's code is loaded, which is when its constants can first be reduced.

> **Not implemented.** A bound that cannot be reduced (a non-constant expression, or an upper bound below the lower) is a compile error that nothing reports
> yet; the array is left uninitialized. The fixed-size array field of a user-defined type carries no bounds.

## Initialization

An array value is _initialized_ when it has any number of dimensions defined. An array value with no dimensions defined is _uninitialized_.

|Bound of an uninitialized array value|Value|
|---|---|
|Upper bound|`-1`|
|Lower bound|The value of the `Option Base` directive: `0` by default, or `1`.|

`Option Base` is described in [**RD-VBAL §5.2.1** Option Directives](rd-vbal.5.2.1.option-directives.md).

## Element Storage

The elements of an array value are held in a single flat block (store):

- The element block is addressed in **column-major order**: the first subscript varies fastest. Column-major order matches an OLE `SAFEARRAY`.
- Column-major order is the traversal order [**MS-VBAL §5.4.2.4.1** Array Enumeration Order](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ce63be3a-aa7a-4f5f-bcf8-a2d266bf7cfc) mandates; see [**RD-VBAL §5.4.2.4** For Each Statement](rd-vbal.5.4.2.4.for-each-statement.md).
- Because of the single flat block, a multi-dimensional array is a contiguous store rather than an array of per-dimension arrays.
- Each element is initialized to the _default value_ of the _declared item type_.
- Element storage is mutable.

> [!NOTE]
> **Not implemented.** The element block is not held in the environment host session's addressable storage. It lives in the value model instead, on the `VBArrayValue` object.

## Location Identity

An array value is **location-identified**, not value-identified.

The entire runtime payload of an intrinsic scalar is the one managed value its binding handle carries. The real data of an array value is its element block, which lives on the `VBArrayValue` object itself.

Storage keeps the array value itself:

1. A symbol's storage allocation is performed by `SymbolAddressTable`, which `ICallStackFrame` and the module/global resolver share; see [**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md).
2. For an array symbol, `SymbolAddressTable` boxes the array value itself, not a derived scalar, into the handle it allocates. It boxes the array value via [VBRuntimeArrayValue](../api/RDCore.SDK.Model.Values.Runtime.VBRuntimeArrayValue.html).
3. On every subsequent read, [VBArrayType](../api/RDCore.SDK.Model.Types.VBArrayType.html)`.CreateValue(`[IBindingHandle](../api/RDCore.SDK.Model.Values.Bindings.IBindingHandle.html)`)` unboxes the array value back out unchanged. It does not attempt to reconstruct an array value from a bare handle.

👉 Array values therefore round-trip through storage. When a plain array-typed variable is read back as an expression ([SimpleNameExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.SimpleNameExpressionNode.html), the ordinary shape of `arr` in `For Each item In arr`), `VBArrayType.CreateValue` unboxes the `VBRuntimeArrayValue` and returns the same instance, with its cells intact. See [**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md).

`VBRuntimeArrayValue` is a plain (non-`record`) wrapper. Boxing the array through a structurally-equatable type would make comparing two array-typed `VBTypedValue`s recurse back into the array's own equality, through the same boxed value.

A UDT value carries its field store as an array value carries its element block, and a `Variant` uses the same boxing pattern for storage; see [**RD-VBAL §2.5.2.1.3** User-Defined Type (UDT) Values](rd-vbal.2.5.2.1.3.udt-values.md) and [**RD-VBAL §2.5.2.1.5** Variant Values](rd-vbal.2.5.2.1.5.variant-values.md).

---
> ⏮️ [**RD-VBAL §2.5.2.1.1** Numeric Values](rd-vbal.2.5.2.1.1.numeric-values.md) | ⏭️ [**RD-VBAL §2.5.2.1.3** User-Defined Type (UDT) Values](rd-vbal.2.5.2.1.3.udt-values.md)
