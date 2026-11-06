# 2.5.2.1.5 Variant Values

A `Variant` value is a [VBVariantValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBVariantValue.html). A value let-coerced to `Variant` is wrapped in a `VBVariantValue`; see [**RD-VBAL §5.5.1.2** Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md) ([**MS-VBAL §5.5.1.2.12** Let-coercion to Variant](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2ae5553a-6515-4967-9b91-e06b527b137f)).

A `VBVariantValue` wraps the whole value, which need not be a scalar. A `Variant` may wrap another `Variant`.

## Type Information

A `VBVariantValue`'s own `TypeInfo` mirrors the `TypeInfo` of its wrapped value. Ordinary destination-type dispatch, in let-coercion and in operator and effective-type determination, therefore picks the same strategy it would for the unwrapped value.

The C# instance of a `Variant` value stays a `VBVariantValue`. It is not an instance of the wrapped value's concrete type.

> 👉 Because a `VBVariantValue`'s `TypeInfo` mirrors its wrapped value's, a direct cast or pattern-match on an operand downstream breaks as soon as the operand is a non-default `Variant`: the instance is still the `VBVariantValue`, not the wrapped value.
>
> Any code that short-circuits on a `TypeInfo` match, or pattern-matches a `VBTypedValue` operand against a concrete value type directly, must unwrap a `VBVariantValue` first. The unwrap is recursive, because a `Variant` may wrap another `Variant`. See [**RD-VBAL §5.5.1.2** Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md).

`VBVariantValue` computes its `VarType` on construction: its [VBVarType](../api/RDCore.SDK.Model.Values.Runtime.VBVarType.html) tag, the COM `VARENUM`-compatible tag, via the `VBType`-to-`VBVarType` mapping. See [**RD-VBAL §6.1.1** Predefined Enums](rd-vbal.6.1.1.predefined-enums.md) (`VbVarType`).

## Storage

`VBVariantValue.Value` is computed from `VBVariantValue.Handle`. `Handle` is the single source of truth for a `Variant`'s value.

Storage holds the wrapped `VBTypedValue` of a `Variant` itself inside a [VBRuntimeVariantValue](../api/RDCore.SDK.Model.Values.Runtime.VBRuntimeVariantValue.html) box.

|Step|Behaviour|
|---|---|
|Construction|The `VBVariantValue` constructor binds the new `VBVariantValue` to its own `VBRuntimeVariantValue` box (self-binding on construction).|
|Store|`SymbolAddressTable.FreshBinding` re-boxes the `Variant`'s wrapped value into a fresh `VBRuntimeVariantValue` on every store. See [**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md).|
|Read|The `VBRuntimeVariantValue` box is read back with the wrapped value intact.|

- A `Variant`'s `VBVarType` tag is carried on its `VBRuntimeVariantValue` box. The tag round-trips through storage alongside the value itself.
- `Variant` values use the same boxing pattern for storage as arrays (`VBArrayType.CreateValue`, [VBRuntimeArrayValue](../api/RDCore.SDK.Model.Values.Runtime.VBRuntimeArrayValue.html)); see [**RD-VBAL §2.5.2.1.2** Array Values](rd-vbal.2.5.2.1.2.array-values.md). Three kinds of value are location-identified this way: an array, a `Variant`, and a UDT ([**RD-VBAL §2.5.2.1.3** User-Defined Type (UDT) Values](rd-vbal.2.5.2.1.3.udt-values.md)).
- 👉 A `Variant` value round-trips through storage intact. A `Variant` read back from a variable, array element, or field carries the value that was stored, not a new, unrelated `Empty`.
- A `Variant`'s own `BoxedValue` unwraps all the way to the managed value. See [**RD-VBAL §6.0** Standard Library](rd-vbal.6.0.standard-library.md).

## Interop Representation

RD-VBAL specifies an interop representation for the underlying managed value of a `VBVariantValue`: a managed `struct` type, for eventual interop with actual COM (unmanaged) variant values.

> [!NOTE]
> **Not implemented.** The interop struct (`ValueType` / `ValueAlloc` / `ValuePtr`) is not declared in the SDK. A `VBVariantValue` wraps its value through `VBRuntimeVariantValue` instead; see [Storage](#storage).

The specified internal representation (struct) has three members:

|Member|Type|Description|
|---|---|---|
|`ValueType`|`VBVariantValueType`|A flag that identifies the _variant value type_, such as `Empty`, `Integer`, `Dispatch` or `BString`.|
|`ValueAlloc`|[ScopeKind](../api/RDCore.SDK.Model.Symbols.Abstract.ScopeKind.html)|Gives the host the _allocation scope_ of the value.|
|`ValuePtr`|`long`|A pointer to the value, in the memory space specified by the `ScopeKind` (`ValueAlloc`).|

A `VBVariantValue` is always allocated in the _heap memory_, in the same memory space as _object values_ ([**RD-VBAL §2.5.2.1.4** Object Values](rd-vbal.2.5.2.1.4.object-values.md)).

External addressing of the host process memory makes it possible for a `VBVariantValue` to wrap an externally-defined object reference; see [**RD-VBAL §4.0** Program Structure and Organization](rd-vbal.4.0.program-structure.md).

### Unwrapping

Unwrapping a `VBVariantValue` through the interop struct consists of:

1. Look up its allocated internal struct.
2. Retrieve the struct's `ValuePtr`.
3. Look up the `ValuePtr` value in the appropriate memory space.

Unwrapping yields a scoped `VBTypedValue`. The yielded value may or may not be an immediately usable _intrinsic data type_: it may be another `VBVariantValue`, which requires a new _unwrapping frame_.
