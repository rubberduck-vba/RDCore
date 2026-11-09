# 2.5.2 VBTypedValue

RD-VBA defines MS-VBAL _data values_ ([**MS-VBAL §2.1** Data Values and Value Types](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/c86480b2-aef2-4488-b177-f55e13cc51f2)) explicitly, as types. RD-VBA data value types inherit from a base [VBTypedValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Abstract.VBTypedValue.html).

`VBTypedValue` is at the core of RD-VBA runtime semantics, as [VBType](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.Abstract.VBType.html) is at the core of static semantics.

## Runtime Payload

The entire runtime payload of an intrinsic scalar is the one managed value its binding handle ([IBindingHandle](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Bindings.IBindingHandle.html)) carries.

Three kinds of value are location-identified instead of value-identified. Their real data does not fit in one scalar managed value, so storage boxes the value itself:

|Value|Storage box|Real data|See|
|---|---|---|---|
|Array|[VBRuntimeArrayValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Runtime.VBRuntimeArrayValue.html)|The element block.|[**RD-VBAL §2.5.2.1.2** Array Values](rd-vbal.2.5.2.1.2.array-values.md)|
|`Variant`|[VBRuntimeVariantValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Runtime.VBRuntimeVariantValue.html)|The wrapped `VBTypedValue`.|[**RD-VBAL §2.5.2.1.5** Variant Values](rd-vbal.2.5.2.1.5.variant-values.md)|
|UDT|[VBRuntimeUserDefinedTypeValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Runtime.VBRuntimeUserDefinedTypeValue.html)|The field store.|[**RD-VBAL §2.5.2.1.3** User-Defined Type (UDT) Values](rd-vbal.2.5.2.1.3.udt-values.md)|

---
## In this section

|§|Title|
|---|---|
|2.5.2.1|[Intrinsic Type Values](rd-vbal.2.5.2.1.intrinsic-type-values.md)|
