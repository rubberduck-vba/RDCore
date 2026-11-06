# 2.4 Static Types

> [!NOTE]
> This specification may be incomplete at this time.

The RD-VBA _type system_ begins with [VBType](../api/RDCore.SDK.Model.Types.Abstract.VBType.html). `VBType` is at the core of RD-VBA _static semantics_; runtime values are described in [**RD-VBAL §2.5** Runtime Values](rd-vbal.2.5.runtime-values.md).

All representations of a _data type_ inherit the `VBType` class. A representation often inherits `VBType` indirectly, through other abstract types that semantically specialize `VBType`.

A type is a reference to its declaration; see [**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md).

## Type Classifications

The type system defines abstractions that formalize the implicit type classifications in MS-VBAL:

|Abstraction|Kind|Classifies|Described in|
|---|---|---|---|
|[VBIntrinsicType](../api/RDCore.SDK.Model.Types.Abstract.VBIntrinsicType.html)|Abstract type|Intrinsic types|[**RD-VBAL §2.4.1** Intrinsic Types](rd-vbal.2.4.1.intrinsic-types.md)|
|[VBNumericType](../api/RDCore.SDK.Model.Types.Abstract.VBNumericType.html)|Abstract type|Numeric types|[**RD-VBAL §2.4.1.1** VBNumericType](rd-vbal.2.4.1.intrinsic-types.md#2411-vbnumerictype)|
|[IIntegralNumericType](../api/RDCore.SDK.Model.Types.Abstract.IIntegralNumericType.html)|Marker interface|Integer types|[**RD-VBAL §2.4.1.1** VBNumericType](rd-vbal.2.4.1.intrinsic-types.md#2411-vbnumerictype)|
|[IFixedPointNumericType](../api/RDCore.SDK.Model.Types.Abstract.IFixedPointNumericType.html)|Marker interface|Fixed-point numeric types|[**RD-VBAL §2.4.1.1** VBNumericType](rd-vbal.2.4.1.intrinsic-types.md#2411-vbnumerictype)|
|[IFloatingPointNumericType](../api/RDCore.SDK.Model.Types.Abstract.IFloatingPointNumericType.html)|Marker interface|Floating-point numeric types|[**RD-VBAL §2.4.1.1** VBNumericType](rd-vbal.2.4.1.intrinsic-types.md#2411-vbnumerictype)|

These abstract types and interfaces support pattern-matching of types and values, in both static-semantics and runtime-semantics implementations.

---
## In this section

|§|Title|
|---|---|
|2.4.1|[Intrinsic Types](rd-vbal.2.4.1.intrinsic-types.md)|
|2.4.2|[Non-intrinsic Types](rd-vbal.2.4.2.non-intrinsic-types.md)|
|2.4.3|[Meta and Advanced Types](rd-vbal.2.4.3.meta-and-advanced-types.md)|
|2.4.4|[Deferred Types](rd-vbal.2.4.4.deferred-types.md)|
