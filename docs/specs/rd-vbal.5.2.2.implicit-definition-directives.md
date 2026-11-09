# 5.2.2 Implicit Definition Directives

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.2** Implicit Definition Directives](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/8865edf3-62ab-4eb7-aa13-c628aef9ccc2).

_Directives_ include the `Def<Type>` _implicit definition_ statements
([**RD-VBAL §3.1** Attributes and Directives](rd-vbal.3.1.attributes-directives.md)). Each one configures implicit
definitions for one data type:

|Directive|Configures implicit definitions for|Environment|
|---|---|---|
|`DefBool`|[VBBooleanType](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBBooleanType.html)|None|
|`DefByte`|[VBByteType](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBByteType.html)|None|
|`DefInt`|[VBIntegerType](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBIntegerType.html)|None|
|`DefLng`|[VBLongType](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBLongType.html)|None|
|`DefLngLng`|[VBLongLongType](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBLongLongType.html)|64-bit|
|`DefLngPtr`|[VBLongPtrType_x86](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBLongPtrType_x86.html)|32-bit|
|`DefLngPtr`|[VBLongPtrType_x64](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBLongPtrType_x64.html)|64-bit|
|`DefCur`|[VBCurrencyType](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBCurrencyType.html)|None|
|`DefSng`|[VBSingleType](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBSingleType.html)|None|
|`DefDbl`|[VBDoubleType](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBDoubleType.html)|None|
|`DefDate`|[VBDateType](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBDateType.html)|None|
|`DefStr`|[VBStringType](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBStringType.html)|None|
|`DefObj`|[VBObjectType](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBObjectType.html)|None|
|`DefVar`|[VBVariantType](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBVariantType.html)|None|
