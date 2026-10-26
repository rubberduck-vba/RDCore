# 6.1.2.3 Conversion Module

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.3** Conversion Module](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/498c1238-f735-4e3b-810f-e0999b4d5fda).

The `Conversion` module is represented in the SDK by the interface
[IStdConversionModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdConversionModule.html).

The `Conversion` module's declarations follow MS-VBAL's own _Function Declaration_ blocks (**MS-VBAL §6.1.2.3**),
declaration by declaration, including their return types and their `Optional` parameters.

### An object argument

A function that reads the value of its argument reads an object's _simple data value_ (**MS-VBAL §5.5.1.2.13**):
what its default member returns, called with no arguments. That is every function of the module but `CVar`, which
returns its argument, object or not. As in MS-VBA (recorded in Excel), `Nothing` is error 91, an object with no default
member that can be called with no arguments is error 438, and an error its default member raises is the function's.
`Int` and `Fix` answer a `Null` read from an object with `Null`, as they answer `Null`; `Hex`, `Oct` and `Str` raise
error 94 for it, as MS-VBA does, although they answer a `Null` argument with `Null`. The `Math` module reads its
arguments the same way ([**RD-VBAL §6.1.2.10** Math](rd-vbal.6.1.2.10.math.md)).

👉 A default member that returns an object stands for that object's simple data value in turn, as Let-coercing the
object would make it. MS-VBA raises error 51 for it instead, an internal error, which RD-VBA does not raise for a
specified input.

MS-VBA refuses some of these at compile time: a `Nothing` literal ("Invalid use of object"), an object whose default
member takes an argument, such as a `Collection` ("Argument not optional"), and, given to `CInt`, an object whose
default member is declared `As Object` ("Type mismatch") - which `Abs` takes, and raises error 51 for at run time.

`Error` of a number that is no run-time error is "Application-defined or object-defined error", without the period
that ends the specification's sentence (MS-VBA, recorded in Excel).


## 6.1.2.3.1 Public Functions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.3.1** Public Functions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/1951c2a7-7527-4d8d-b4a5-3b212d2442d5).

|§|Member|Notes|
|---|---|---|
|6.1.2.3.1.1|[CBool](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/fb8b7a86-5c7a-4623-807a-9b6551dadfa5)||
|6.1.2.3.1.2|[CByte](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/fd97115a-ea2b-49ba-94eb-ccfbf8701861)||
|6.1.2.3.1.3|[CCur](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ec718b0a-6c8d-4ada-90fa-dbb331516b58)||
|6.1.2.3.1.4|[CDate / CVDate](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/1f287742-e07f-4169-8ce7-5ddfe0f951fb)||
|6.1.2.3.1.5|[CDbl](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/03e8d20d-8356-4080-809c-6f1b563af0d0)||
|6.1.2.3.1.6|[CDec](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/07360f30-1220-4c1f-afc3-8ba7a7dac332)||
|6.1.2.3.1.7|[CInt](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/8d3ac81d-00ba-4cee-bda0-4bf7c32a7fc8)||
|6.1.2.3.1.8|[CLng](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/c5c2f77f-10e8-4a81-8eac-2ea15c13db8b)||
|6.1.2.3.1.9|[CLngLng](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4ca05bda-57b1-4f0f-bdc8-74febb84a1bb)||
|6.1.2.3.1.10|[CLngPtr](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/39e8676c-5beb-4f2f-b7ac-8f4e832c5d62)|See [§6.1.2.3.1.10](#6123110-clngptr).|
|6.1.2.3.1.11|[CSng](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f4161bc8-127a-4ecc-99e8-b6bb964e277d)||
|6.1.2.3.1.12|[CStr](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/0fff7d08-82fc-43bd-bc75-4782cfa2f08d)|A number or a date is written in the regional settings of the session's environment, as let-coercion writes it ([**RD-VBAL §5.5.1.2.4**](rd-vbal.5.5.1.2.runtime-semantics.md#55124-let-coercion-to-and-from-string)). The error code of `Error n` is a whole number, written the same in any culture. A `String` is copied, every byte of its data.|
|6.1.2.3.1.13|[CVar](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6b423db1-cc79-4d28-9c4e-301d7781d41e)||
|6.1.2.3.1.14|[CVErr](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/7a7facef-2f52-4d08-918e-63bf6a9261ed)||
|6.1.2.3.1.15|[Error / Error$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/54992525-5c6f-4768-86ab-b97f3d0e2705)||
|6.1.2.3.1.16|[Fix](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/97f8abc4-fd64-4ae4-9c80-1548db613ec9)||
|6.1.2.3.1.17|[Hex / Hex$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/b5f1801d-2e68-418b-b9db-1803368f012f)|🎯 A negative number is written in the width of its type, as **MS-VBA** writes it, where the specification picks the width by the value: an `Integer` and a `Boolean` in 16 bits (`Hex$(CInt(-32768))` is `8000`), a `Long` in 32 (`Hex$(CLng(-7))` is `FFFFFFF9`), a `LongLong` in 64, a `LongPtr` in its own. Any other type is Let-coerced to a `LongLong` and written in 64 bits: `Hex$(-7.5)` is `FFFFFFFFFFFFFFF8`, and `Hex$(4294967296#)` is `100000000`, with no overflow. A `Variant` is written as what it holds. Recorded in 64-bit Excel; on a 32-bit host, which has no `LongLong`, any other type is Let-coerced to a `Long`, as the specification has it (not verified).|
|6.1.2.3.1.18|[Int](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/db6d8be7-82cd-423c-83cb-3e6eff0a3363)||
|6.1.2.3.1.19|[Oct / Oct$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f8c3018b-7005-4e1d-9c3b-6caea2a2625e)|As `Hex`: `Oct$(CLng(-7))` is `37777777771`.|
|6.1.2.3.1.20|[Str / Str$](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/c4ecaf21-da57-4f88-b5db-0276dd0693de)|A number is written with `.` as the decimal separator, in any culture. A date is "the result of Let-coercing Number to String", in the regional settings of the session's environment, as `CStr` writes it.|
|6.1.2.3.1.21|[Val](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6dff8d10-d573-43ad-8197-ca8cd509acf1)||

### 6.1.2.3.1.10 CLngPtr

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.3.1.10** CLngPtr](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/39e8676c-5beb-4f2f-b7ac-8f4e832c5d62).

`CLngPtr` needs the one thing a standard-library declaration cannot state: its `LongPtr` return type depends on the
pointer width. `LongPtr` is a different type in each pointer width
([**RD-VBAL §2.4.1** Intrinsic Types](rd-vbal.2.4.1.intrinsic-types.md)).

The pointer width belongs to the environment, and is passed to
[StdLibSymbolProvider](../api/RDCore.SDK.Runtime.StdLib.StdLibSymbolProvider.html)
([**RD-VBAL §6.0.1** Symbol Injection](rd-vbal.6.0.standard-library.md#601-symbol-injection)).

---
> ⏮️ [**RD-VBAL §6.1.2.2** Constants Module](rd-vbal.6.1.2.2.constants-module.md) | ⏭️ [**RD-VBAL §6.1.2.4** DateTime Module](rd-vbal.6.1.2.4.datetime-module.md)
