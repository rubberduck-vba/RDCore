# 5.2.2 Implicit Definition Directives

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.2** Implicit Definition Directives](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/8865edf3-62ab-4eb7-aa13-c628aef9ccc2).

_Directives_ include the `Def<Type>` _implicit definition_ statements
([**RD-VBAL §3.1** Attributes and Directives](rd-vbal.3.1.attributes-directives.md)). Each one configures implicit
definitions for one data type:

|Directive|Configures implicit definitions for|Environment|
|---|---|---|
|`DefBool`|[VBBooleanType](../api/RDCore.SDK.Model.Types.VBBooleanType.html)|—|
|`DefByte`|[VBByteType](../api/RDCore.SDK.Model.Types.VBByteType.html)|—|
|`DefInt`|[VBIntegerType](../api/RDCore.SDK.Model.Types.VBIntegerType.html)|—|
|`DefLng`|[VBLongType](../api/RDCore.SDK.Model.Types.VBLongType.html)|—|
|`DefLngLng`|[VBLongLongType](../api/RDCore.SDK.Model.Types.VBLongLongType.html)|64-bit|
|`DefLngPtr`|[VBLongPtrType_x86](../api/RDCore.SDK.Model.Types.VBLongPtrType_x86.html)|32-bit|
|`DefLngPtr`|[VBLongPtrType_x64](../api/RDCore.SDK.Model.Types.VBLongPtrType_x64.html)|64-bit|
|`DefCur`|[VBCurrencyType](../api/RDCore.SDK.Model.Types.VBCurrencyType.html)|—|
|`DefSng`|[VBSingleType](../api/RDCore.SDK.Model.Types.VBSingleType.html)|—|
|`DefDbl`|[VBDoubleType](../api/RDCore.SDK.Model.Types.VBDoubleType.html)|—|
|`DefDate`|[VBDateType](../api/RDCore.SDK.Model.Types.VBDateType.html)|—|
|`DefStr`|[VBStringType](../api/RDCore.SDK.Model.Types.VBStringType.html)|—|
|`DefObj`|[VBObjectType](../api/RDCore.SDK.Model.Types.VBObjectType.html)|—|
|`DefVar`|[VBVariantType](../api/RDCore.SDK.Model.Types.VBVariantType.html)|—|
