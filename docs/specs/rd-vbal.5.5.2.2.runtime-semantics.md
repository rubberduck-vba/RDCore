# 5.5.2.2 Runtime semantics

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.5.2.2** Runtime semantics](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/bdd2d85a-3236-4381-b289-002bf0bf8ffd).

Set-coercion is evaluated by `SetCoercionRuntimeSemantics`, through
[ISetCoercionRuntimeSemantics](../api/RDCore.SDK.Runtime.Abstract.ISetCoercionRuntimeSemantics.html).

|Construct|Set-coercion entry point|See|
|---|---|---|
|`Set` statement|The same direct entry point that `WithStatementRuntimeSemantics` uses for its own `With`-target coercion, not the operator pipeline.|[**RD-VBAL §5.4.3.9** Set Statement](rd-vbal.5.4.3.9.set-statement.md)|
|Class-valued `With` target|Set-assigned through `ISetCoercionRuntimeSemantics`.|[**RD-VBAL §5.4.2.21** With Statement](rd-vbal.5.4.2.21.with-statement.md)|

Set-coercion does not dispatch to per-destination-type strategies, so it does not need the operator pipeline.
Let-coercion, by contrast, dispatches to a per-destination-type strategy; see
[**RD-VBAL §5.5.1.2** Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md).


## 5.5.2.2.1 Set-coercion to and from a class or Object or Nothing

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.5.2.2.1** Set-coercion to and from a class or Object or Nothing](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/83404cf0-9a0f-49f5-93bd-eb4c473492c5).

`SetCoercionRuntimeSemantics` unwraps a `Variant` that wraps an object before its
[VBObjectValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBObjectValue.html) pattern-match. The unwrap is recursive,
because a `Variant` may wrap another `Variant`.

> A [VBVariantValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBVariantValue.html)'s `TypeInfo` mirrors its wrapped
> value's, but the instance remains a `VBVariantValue`: without the unwrap, the pattern-match would see the box
> instead of the object
> ([**RD-VBAL §5.5.1.2.12** Let-coercion to Variant](rd-vbal.5.5.1.2.runtime-semantics.md#551212-let-coercion-to-variant)).


## 5.5.2.2.2 Set-coercion to and from non-object types

This section corresponds to [**MS-VBAL §5.5.2.2.2** Set-coercion to and from non-object types](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9a2eb890-a7c8-4f10-b86c-3ec77a564294).

> [!NOTE]
> Reserved. This section has no content yet.

---
> ⏮️ [**RD-VBAL §5.5.2.1** Static semantics](rd-vbal.5.5.2.1.static-semantics.md) | ⏭️ [**RD-VBAL §5.6** Expressions](rd-vbal.5.6.expressions.md)
