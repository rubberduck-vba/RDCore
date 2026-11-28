# 2.5.2.1.3 User-Defined Type (UDT) Values

An instance of a UDT is a [VBUserDefinedTypeValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Intrinsic.VBUserDefinedTypeValue.html).

The _data type_ of a UDT value is defined by the UDT declaration of its _declared type_; see [**RD-VBAL §5.2.3** Module Declarations](rd-vbal.5.2.3.module-declarations.md).

## Location Identity

Like an _object value_, the underlying value of a UDT value is a unique addressable ID: a location in the heap.

A UDT has location identity. A UDT is never copied by value at the level of its underlying value (its addressable ID).

> UDT values **must** be passed by reference (`ByRef`). See [**RD-VBAL §5.3.1.5** Parameter Lists](rd-vbal.5.3.1.5.parameter-lists.md) and [**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md).

## Field Store

A UDT value is a view of its **field store**, a [RecordBlock](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Intrinsic.RecordBlock.html): the storage of one record, laid out in **declaration order** by the type's [RecordShape](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Intrinsic.RecordShape.html).

|Field|Held in the block|
|---|---|
|`Byte`, `Integer`, `Boolean`, `Long`, `LongLong`, `LongPtr`, `Single`, `Double`, `Currency`, `Date`, an `Enum`|As bytes, read and written through the field's [RecordBytesCell](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Intrinsic.RecordBytesCell.html).|
|A nested UDT|Inline: its bytes and slots are the outer record's own, and the nested UDT value is a view of the same block.|
|Any other field: a `String` (fixed-length or not), an object, a `Variant`, an array, a `Decimal`|In a reference slot, as a value in a cell of its own.|

A UDT value carries its field store for the same reason an array value carries its element block ([**RD-VBAL §2.5.2.1.2** Array Values](rd-vbal.2.5.2.1.2.array-values.md)): a UDT's real data is its fields, which a scalar managed value has nowhere to hold.

The declaration order of the field store is normative rather than incidental. It is:

- the order [**MS-VBAL §5.4.5.11** Put Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/46eeacb8-7a06-4ec8-9736-eea42de4eeca) writes a record in (see [**RD-VBAL §5.4.5.11** Put Statement](rd-vbal.5.4.5.11.put-statement.md));
- the order [VBUserDefinedTypeLayout](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBUserDefinedTypeLayout.html) assigns field offsets in.

Each field starts at its declared type's own default value, because VBA gives a UDT no initializer: a field held as bytes starts as zero bytes.

A UDT field is not an addressable symbol the way a variable is: a `Let` assignment to a UDT field writes the field's cell. See [**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md).

## Storage

Like an array, a UDT value is **location-identified**. Three pieces preserve a UDT value through storage:

|Piece|Role|
|---|---|
|[VBRuntimeUserDefinedTypeValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Runtime.VBRuntimeUserDefinedTypeValue.html) boxing|Boxes a UDT value when it is stored, into the handle a symbol's storage allocation reserves.|
|[VBUserDefinedType](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBUserDefinedType.html)`.CreateValue(IBindingHandle)` unboxing|Unboxes the stored UDT value back out unchanged, rather than rebuilding one with default fields.|
|The `VBUserDefinedTypeValue` copy constructor|Makes every `with`-derived copy another view of the same field store, at the same offsets: a field written through one is written through the other.|

Three kinds of value are location-identified this way: an array, a `Variant`, and a UDT (`VBRuntimeUserDefinedTypeValue`).

Reconstructing a UDT value from a handle, instead of unboxing it, would return a UDT with default fields, whatever had been assigned to it.

`VBRuntimeUserDefinedTypeValue` is a plain (non-`record`) wrapper, for the same reason [VBRuntimeArrayValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Runtime.VBRuntimeArrayValue.html) is: structural equality would recurse into the value's own equality through the boxed value.

### Copies

The copy an assignment makes (`DataCopy`) gets a field store of its own: the record's bytes copied, and each reference field a copy in a cell of its own, deep through an array and what a `Variant` holds. A nested UDT is inline, so the copy is deep through it. This is because VBA copies a UDT on assignment; see [**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md).

## Sizes

A UDT has **two sizes**: an in-memory size and an unpadded size. [**MS-VBAL §6.1.2.11** Strings](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f87cc0fb-f0ca-4875-9691-34d80e2933b0) distinguishes the two sizes where it defines `Len` and `LenB` ([**MS-VBAL §6.1.2.11.1.22** Len / LenB](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a4d9985a-e2cb-489c-a20a-5da09d77a59e)).

|Size|Reported by|MS-VBAL definition|Layout|
|---|---|---|---|
|In-memory|`LenB`; `VBUserDefinedTypeValue.Size`|`LenB` returns "the in-memory size, including any implementation-specific padding between elements".|Each field is aligned to its own natural boundary. The type is padded up to the strictest boundary any field asked for.|
|Unpadded|`Len`|`Len` "returns the size as it will be written to the file".|As MS-VBA reports it: the same memory widths added up with no padding at all, a fixed-length `String` counted as its characters. Not what `Put` writes: a variable-length `String` counts as its pointer.|

`Strings.Len` and `Strings.LenB` make both sizes of a UDT observable to a program; see [**RD-VBAL §6.1.2.11** Strings](rd-vbal.6.1.2.11.strings.md). `Put` and `Get` are described in [**RD-VBAL §5.4.5.11** Put Statement](rd-vbal.5.4.5.11.put-statement.md) and [**RD-VBAL §5.4.5.12** Get Statement](rd-vbal.5.4.5.12.get-statement.md).

### In-Memory Layout

**MS-VBAL** calls the UDT in-memory padding "implementation-specific". RD-VBA chooses MS-VBA's padding, because a program measures a UDT's in-memory size with `LenB` and relies on it.

`VBUserDefinedTypeLayout` represents the in-memory (`LenB`) size of a UDT:

- It holds MS-VBA's own natural alignment (padding) rule in one place.
- It holds each field's offset.
- `VBUserDefinedTypeValue.Size` reports the in-memory size of a UDT: its `VBUserDefinedTypeLayout` size.

### Memory Widths and File Widths

> Memory widths of UDT fields are not file widths.

|Field|In memory (`LenB`)|`Len`|In a record (`Put`)|
|---|---|---|---|
|Variable-length `String`|A pointer|A pointer|Its characters|
|Fixed-length `String`|Unicode|Its characters|ANSI|

The MS-VBAL warning that "Len might not be able to determine the actual number of storage bytes required when used with variable-length strings in user-defined data types" is about a record whose members include a variable-length `String`: MS-VBA's `Len`, and RD-VBA's, count the pointer and not the characters a `Put` writes.

### Pointer Width

The pointer width of a UDT layout is a parameter of `VBUserDefinedTypeLayout`, and it defaults to 32-bit:

- On a 64-bit host, a pointer field (a variable-length `String`, an object, a resizable array) and a `LongPtr` field are 8 bytes, and a `Variant` field is 24 instead of 16, as MS-VBA lays them out (recorded in Excel, 64-bit).
- `Len` and `LenB` lay a record out for the environment's own pointer width, and so does a record's memory image
  (`LSet`, `VarPtr`).

> [!NOTE]
> **Not implemented.** `VBUserDefinedTypeValue.Size` lays a UDT out for a 32-bit pointer.
