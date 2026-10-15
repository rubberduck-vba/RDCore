# 2.4.1 Intrinsic Types

In RD-VBA, a type is an _intrinsic type_ if MS-VBAL mentions it. Whether a type is an intrinsic type does not depend on its specified semantics, nor on how RD-VBA models it.

[VBIntrinsicType](../api/RDCore.SDK.Model.Types.Abstract.VBIntrinsicType.html) is the type-system abstraction that formalizes this classification; see [**RD-VBAL §2.4** Static Types](rd-vbal.2.4.static-types.md).

## Non-numeric Intrinsic Types

The non-numeric intrinsic types are:

|Type|Notes|
|---|---|
|[VBArrayType](../api/RDCore.SDK.Model.Types.VBArrayType.html)|Array types; see [§2.4.1.3](#2413-vbarraytype).|
|[VBFixedSizeArrayType](../api/RDCore.SDK.Model.Types.VBFixedSizeArrayType.html)|Array type; see [§2.4.1.3](#2413-vbarraytype).|
|[VBResizableArrayType](../api/RDCore.SDK.Model.Types.VBResizableArrayType.html)|Array type; see [§2.4.1.3](#2413-vbarraytype).|
|[VBResizableByteArrayType](../api/RDCore.SDK.Model.Types.VBResizableByteArrayType.html)|Array type; see [§2.4.1.3](#2413-vbarraytype).|
|[VBBooleanType](../api/RDCore.SDK.Model.Types.VBBooleanType.html)||
|[VBDateType](../api/RDCore.SDK.Model.Types.VBDateType.html)||
|[VBEmptyType](../api/RDCore.SDK.Model.Types.VBEmptyType.html)||
|[VBErrorType](../api/RDCore.SDK.Model.Types.VBErrorType.html)||
|[VBLongPtrType_x64](../api/RDCore.SDK.Model.Types.VBLongPtrType_x64.html)|`LongPtr`; see [LongPtr](#longptr).|
|[VBLongPtrType_x86](../api/RDCore.SDK.Model.Types.VBLongPtrType_x86.html)|`LongPtr`; see [LongPtr](#longptr).|
|[VBMissingType](../api/RDCore.SDK.Model.Types.VBMissingType.html)||
|[VBNullType](../api/RDCore.SDK.Model.Types.VBNullType.html)||
|[VBObjectType](../api/RDCore.SDK.Model.Types.VBObjectType.html)||
|[VBStringType](../api/RDCore.SDK.Model.Types.VBStringType.html)|String type; see [§2.4.1.2](#2412-vbstringtype).|
|[VBFixedStringType](../api/RDCore.SDK.Model.Types.VBFixedStringType.html)|String type; see [§2.4.1.2](#2412-vbstringtype).|
|[VBVariantType](../api/RDCore.SDK.Model.Types.VBVariantType.html)||

### LongPtr

`LongPtr` is a different type in each pointer width: `VBLongPtrType_x64` for a 64-bit pointer width, and `VBLongPtrType_x86` for a 32-bit pointer width. The pointer width belongs to the environment; see [**RD-VBAL §6.0** Standard Library](rd-vbal.6.0.standard-library.md).

The data value type [VBLongPtrValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBLongPtrValue.html) has the `VBType` `VBLongPtrType_x64` or `VBLongPtrType_x86`. Which of the two it has depends on the host environment; see [**RD-VBAL §2.5.2.1** Intrinsic Type Values](rd-vbal.2.5.2.1.intrinsic-type-values.md).

## 2.4.1.1 VBNumericType

_Numeric types_ are intrinsic types that represent a _numeric_ value, regardless of the value's representation. [VBNumericType](../api/RDCore.SDK.Model.Types.Abstract.VBNumericType.html) is the type-system abstraction that formalizes this classification.

Every numeric type additionally implements one of three _marker interfaces_:

|Marker interface|Implemented by|
|---|---|
|[IIntegralNumericType](../api/RDCore.SDK.Model.Types.Abstract.IIntegralNumericType.html)|All integer types|
|[IFixedPointNumericType](../api/RDCore.SDK.Model.Types.Abstract.IFixedPointNumericType.html)|All fixed-point numeric types|
|[IFloatingPointNumericType](../api/RDCore.SDK.Model.Types.Abstract.IFloatingPointNumericType.html)|All floating-point numeric types|

The table of _intrinsic numeric data types_ below shows the managed (.NET) minimum and maximum values, for simplicity.

|Type|Interface|Description|MinValue|MaxValue|
|---|---|---|---|---|
|[VBByteType](../api/RDCore.SDK.Model.Types.VBByteType.html)|`IIntegralNumericType`|8-bit `Byte` unsigned integer|`Byte.MinValue` (0)|`Byte.MaxValue` (255)|
|[VBIntegerType](../api/RDCore.SDK.Model.Types.VBIntegerType.html)|`IIntegralNumericType`|16-bit `Integer` signed integer|`Int16.MinValue` (-32,768)|`Int16.MaxValue` (32,767)|
|[VBLongType](../api/RDCore.SDK.Model.Types.VBLongType.html)|`IIntegralNumericType`|32-bit `Long` signed integer|`Int32.MinValue` (-2,147,483,648)|`Int32.MaxValue` (2,147,483,647)|
|[VBLongLongType](../api/RDCore.SDK.Model.Types.VBLongLongType.html)|`IIntegralNumericType`|64-bit `LongLong` signed integer|`Int64.MinValue` (-9,223,372,036,854,775,808)|`Int64.MaxValue` (9,223,372,036,854,775,807)|
|[VBSingleType](../api/RDCore.SDK.Model.Types.VBSingleType.html)|`IFloatingPointNumericType`|32-bit single-precision floating-point|`Single.MinValue`|`Single.MaxValue`|
|[VBDoubleType](../api/RDCore.SDK.Model.Types.VBDoubleType.html)|`IFloatingPointNumericType`|64-bit double-precision floating-point|`Double.MinValue`|`Double.MaxValue`|

Floating-point types also define a `SignificantIntegerDigits`; see [**RD-VBAL §2.5.2.1.1** Numeric Values](rd-vbal.2.5.2.1.1.numeric-values.md).

## 2.4.1.2 VBStringType

_String types_ are internally represented using a UTF-16 standard .NET `System.String` value.

The string types are:

- `VBStringType`
- `VBFixedStringType`

The data of a string is its UTF-16 code units, two bytes each, little-endian. That data can also be an odd number of
bytes: a `Byte()` of any length let-coerces to a `String`
([**RD-VBAL §5.5.1.2.6** Let-coercion to and from resizable Byte()](rd-vbal.5.5.1.2.runtime-semantics.md#55126-let-coercion-to-and-from-resizable-byte)).
A `System.String` cannot hold half a code unit, so the byte left over after the last whole character is held beside
it: the runtime value of such a string is a
[VBRuntimeByteStringValue](../api/RDCore.SDK.Model.Values.Runtime.VBRuntimeByteStringValue.html), its whole characters
and its trailing byte. A string of whole characters keeps the plain runtime value it has always had.
[VBStringValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBStringValue.html) gives the data of either as bytes
(`GetBytes`, `ByteLength`), and makes a `String` of any bytes (`FromBytes`), every byte kept, a lone surrogate
included. Two strings that differ only in a trailing byte are different values.

|Reads a `String` as|Where|
|---|---|
|Its bytes, a trailing byte included|`LenB`; let-coercion to `String` from a `String` (a copy) and to and from `Byte()`; `&`, and `+` between two `String`s; the relational operators; the `MidB` statement, and the `Mid` statement, which keeps a trailing byte it does not replace; a standard-library member's `String` argument.|
|Its whole characters|`Len`; `Print`, `Print #`, `Write #` and `Put`; the numeric, `Boolean` and `Date` conversions; `LSet`, `RSet` and a fixed-length `String`; `Like`; a `Declare`d procedure's `String` argument, and the `String` and `String * n` fields of a record passed to one; a `String` passed to an automation server.|

👉 The debugger shows a `String`'s characters: a trailing byte is not displayed.

## 2.4.1.3 VBArrayType

_Array types_ include the following implementations of `VBArrayType`:

- `VBFixedSizeArrayType`
- `VBResizableArrayType`
- `VBResizableByteArrayType`

An array type does not encode any dimensions.

The _value_ associated with an array type encodes the array dimensions; see [VBArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBArrayValue.html) and its derived types, and [**RD-VBAL §2.5.2.1.2** Array Values](rd-vbal.2.5.2.1.2.array-values.md). On every read after the array value is stored, `VBArrayType.CreateValue(IBindingHandle)` unboxes it unchanged.

### Resizable Byte Arrays

If the _item type_ of a _resizable array value_ is `VBByteType`, the data type of the array is `VBResizableByteArrayType`.

`VBResizableByteArrayType` has specific _let-coercion_ semantics attached, allowing implicit conversion to and from `VBStringType`; see [**RD-VBAL §5.5.1.2** Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md).

### ReDim

|Array value|`ReDim`|
|---|---|
|[VBFixedSizeArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBFixedSizeArrayValue.html)|Illegal with any `VBFixedSizeArrayValue`.|
|[VBResizableArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBResizableArrayValue.html)|May declare a `VBResizableArrayValue`, or redimension an already-declared `VBResizableArrayValue`.|

For the re-dimension of an existing array, the implicit declaration by an unqualified `ReDim` target, and `ReDim` bounds, see [**RD-VBAL §5.4.3.3** ReDim Statement](rd-vbal.5.4.3.3.redim-statement.md).

### Declared Array Types

The _declaration pass_ binds the _declared type_ of an array symbol from the _array-dim clause_ alone ([**MS-VBAL §5.2.3.1.3** Array Dimensions and Bounds](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/7c97ad30-4b76-45d9-9827-7455f98a5503)), before any bound is evaluated:

|Array-dim clause|Example|Declared type|
|---|---|---|
|One or more _bounds_|`Dim g(1 To 3, 0 To 4) As Long`|`VBFixedSizeArrayType`|
|Empty `()` clause|`Dim b() As Long`|`VBResizableArrayType`|
|Trailing `()` on the `As` clause|`Dim b As Long()`|`VBResizableArrayType`|
|Empty `()` clause, or trailing `()` on the `As` clause, with item type `Byte`|`Dim b() As Byte`, `Dim b As Byte()`|`VBResizableByteArrayType` (instead of `VBResizableArrayType`)|

An omitted array _item type_ defaults to `Variant` ([**MS-VBAL §5.2.3.1** Module Variable Declaration Lists](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/0f9113df-fd9c-485a-9583-fdb0e9d68e1b)). This default is applied by a later normalization pass, not by the declaration pass.

The declaration pass keeps each array bound verbatim, as declared. The following are the concern of the semantic pass that materializes the array value ([**RD-VBAL §2.5.2.1.2** Array Values](rd-vbal.2.5.2.1.2.array-values.md)):

- evaluating each bound to a `Long`;
- resolving an omitted _lower bound_ against `Option Base` (see [**RD-VBAL §5.2.1** Option Directives](rd-vbal.5.2.1.option-directives.md)).

---
> ⏮️ [**RD-VBAL §2.4** Static Types](rd-vbal.2.4.static-types.md) | ⏭️ [**RD-VBAL §2.4.2** Non-intrinsic Types](rd-vbal.2.4.2.non-intrinsic-types.md)
