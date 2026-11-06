# 3.1 Attributes and Directives

_Directives_ are **non-executable statements**. A directive influences the semantics of the module it is located
in, or of the member it refers to.

In the AST, the base node type for directives is
[DirectiveNode](../api/RDCore.SDK.Model.AST.Abstract.DirectiveNode.html), which derives directly from
`SyntaxNode` ([**RD-VBAL §3.0.2** Node Types](rd-vbal.3.0.2.node-types.md)).

## Option Statements

Directives include the `Option` statements:

|Directive|Description|
|---|---|
|`Option Explicit`|Implicit declarations become compile-time errors.|
|`Option Base`|Determines the base (0 or 1) of implicitly-sized arrays.|
|`Option Compare`|Determines the comparison mode for string comparisons: `Text` or `Binary`. The comparison mode may instead be a _host-defined token_, which dynamically configures the comparison mode.|
|`Option Private Module`|Determines the _accessibility_ of a module.|

The semantics of the `Option` statements are described in
[**RD-VBAL §5.2.1** Option Directives](rd-vbal.5.2.1.option-directives.md).

## `Def<Type>` Statements

Directives include the `Def<Type>` _implicit definition_ statements:

|Directive|Configures implicit definitions for|
|---|---|
|`DefBool`|[VBBooleanType](../api/RDCore.SDK.Model.Types.VBBooleanType.html)|
|`DefByte`|[VBByteType](../api/RDCore.SDK.Model.Types.VBByteType.html)|
|`DefInt`|[VBIntegerType](../api/RDCore.SDK.Model.Types.VBIntegerType.html)|
|`DefLng`|[VBLongType](../api/RDCore.SDK.Model.Types.VBLongType.html)|
|`DefLngLng`|[VBLongLongType](../api/RDCore.SDK.Model.Types.VBLongLongType.html), in 64-bit environments|
|`DefLngPtr`|[VBLongPtrType_x86](../api/RDCore.SDK.Model.Types.VBLongPtrType_x86.html) in a 32-bit environment; [VBLongPtrType_x64](../api/RDCore.SDK.Model.Types.VBLongPtrType_x64.html) in a 64-bit environment|
|`DefCur`|[VBCurrencyType](../api/RDCore.SDK.Model.Types.VBCurrencyType.html)|
|`DefSng`|[VBSingleType](../api/RDCore.SDK.Model.Types.VBSingleType.html)|
|`DefDbl`|[VBDoubleType](../api/RDCore.SDK.Model.Types.VBDoubleType.html)|
|`DefDate`|[VBDateType](../api/RDCore.SDK.Model.Types.VBDateType.html)|
|`DefStr`|[VBStringType](../api/RDCore.SDK.Model.Types.VBStringType.html)|
|`DefObj`|[VBObjectType](../api/RDCore.SDK.Model.Types.VBObjectType.html)|
|`DefVar`|[VBVariantType](../api/RDCore.SDK.Model.Types.VBVariantType.html)|

The semantics of the `Def<Type>` statements are described in
[**RD-VBAL §5.2.2** Implicit Definition Directives](rd-vbal.5.2.2.implicit-definition-directives.md).

## Other Directives

Directives also include the `Implements` and `Attribute` statements:

|Directive|Description|Described in|
|---|---|---|
|`Implements`|Specifies that the (class) module _implements_ an _interface class_.|[**RD-VBAL §5.2.4** Class Module Declarations](rd-vbal.5.2.4.class-module-declarations.md)|
|`Attribute`|Specifies flags and modifiers that alter the semantics of a module or member.|[**RD-VBAL §3.1.1** Attributes](rd-vbal.3.1.1.attributes.md)|
