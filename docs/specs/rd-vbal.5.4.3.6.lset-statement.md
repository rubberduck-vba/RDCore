# 5.4.3.6 LSet Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.3.6** LSet Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6964d2a4-b3e3-497b-bb80-8bc98f0edab9).

`LSet` (**MS-VBAL §5.4.3.6**) and `RSet`
([**MS-VBAL §5.4.3.7** RSet Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/beccdd43-9dad-4bcf-b063-e542869917a1);
[**RD-VBAL §5.4.3.7** RSet Statement](rd-vbal.5.4.3.7.rset-statement.md)) are implemented.

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[AssignmentStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.AssignmentStatementNode.html) (`Kind`: `LSet`)|`Simple`|The same node shape as `Let`, `Set` and `RSet`, which share `AssignmentStatementNode` and are distinguished by its [AssignmentKind](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.AssignmentKind.html) `Kind`.|

See [**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md).

## Static Semantics

MS-VBAL §5.4.3.6 allows `LSet` between any two UDTs. MS-VBA refuses `LSet` onto a record whose type holds a
variable-length `String` member, with "Compile error: Type mismatch" (observed in Excel; see [UDT form](#udt-form)).
RD-VBA runs the statement, and records a variable-length `String` member on either side with a semantic flag.

`FixedAssignmentRuntimeSemantics``.Analyze` sets the
[FixedAssignmentSemanticFlags](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Semantics.Flags.FixedAssignmentSemanticFlags.html) of the statement, the way an operator's `Analyze` sets its own:

|Flag|Set when|
|---|---|
|`StringTarget`|The target is a `String`: the string form.|
|`UserDefinedTypeCopy`|`LSet` between two user-defined types: the byte copy.|
|`SourceHoldsVariableLengthString`|The source of the byte copy has a variable-length `String` member, at any depth.|
|`DestinationHoldsVariableLengthString`|The destination of the byte copy has one.|
|`Failed`|The operation raises a run-time error: the target is neither.|

A diagnostic is the job of an analyzer that reads the flags (`RDCore.Diagnostics`, or any other).

## Runtime Semantics

`LSet` has two forms:

|Form|Operands|Effect|
|---|---|---|
|String form|A `String` target|Fits a value into the width the target already has.|
|UDT form|Two UDT variables|Copies one UDT over another as bytes.|

### String form

`LSet` fits a value into the width the target already has. It takes the target's width from the target's current
value, not from its declared type. Taking the width from the current value makes `LSet` meaningful on a
variable-length `String`, and harmless on one.

`LSet` and `RSet` differ only in which end pads: `LSet` is left-aligned and `RSet` is right-aligned. Both truncate
from the same end.

Both count characters: a trailing byte of the value is not one, and is not part of the result
([**RD-VBAL §2.4.1.2** VBStringType](rd-vbal.2.4.1.intrinsic-types.md#2412-vbstringtype)).

|Value, compared with the target's width|`LSet`|`RSet`|
|---|---|---|
|Shorter|Left-aligned: padded at the end.|Right-aligned: padded at the start.|
|Longer|Truncated.|Truncated from the same end as `LSet`.|

### UDT form

`LSet`'s other form is a byte copy between two UDT variables. `LSet` copies a UDT as bytes because a byte copy
between two UDTs is how VBA emulates a union.

1. The source record is laid out through its own layout
   ([VBUserDefinedTypeLayout](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.VBUserDefinedTypeLayout.html)), for the environment's
   pointer width ([**RD-VBAL §2.5.2.1.3**](rd-vbal.2.5.2.1.3.udt-values.md#pointer-width)).
2. Only the bytes both types have are copied.
3. The copied bytes are read back through the destination's layout, place by place: each field, each field of a
   nested record and each element of a fixed-size array field, at its offset from the start of the record. For
   example, a `Long` is read back as two `Integer`s, and a `Currency` of 1 as a `Byte` array whose first two
   elements are 16 and 39.
4. The rest of the destination keeps what it held, as in MS-VBA (observed in Excel):
   - A place the source's image reaches only part of holds its own bytes, with the source's laid over their start. A
     `Long` given the two bytes of an `Integer` keeps its high two bytes; a `Double` given a `Long` keeps its high four;
     a `String * 4` that holds `"wxyz"`, given an `Integer` of 65, holds `"Axyz"`.
   - A place past the end of the source's image is left as it was.

**RD-VBA differs from MS-VBA:** an object field that the source's image reaches only part of is set to `Nothing`,
because part of an address is the address of no object. MS-VBA keeps the mixed bytes as the field's address.

Each place is written where it is, so a `ByRef` parameter given a field or an element of the destination reads what
`LSet` wrote.

A field of an `Enum` is stored in the image as the `Long` it holds, four bytes, and is read back as one
([**RD-VBAL §5.2.3.4**](rd-vbal.5.2.3.module-declarations.md#5234-enum-declarations)): a field of an `Enum` and a
`Long` field at the same offset copy into each other.

A fixed-length `String` field is stored in the image as its characters, inline, two bytes each: the string's own data
([**RD-VBAL §2.4.1.2** VBStringType](rd-vbal.2.4.1.intrinsic-types.md#2412-vbstringtype)), not a text encoding of it.
Its bytes are copied exactly, so a field that holds a lone surrogate (`U+D800`, `00 D8`) holds it after the copy. A
fixed-length `String` field's width is two bytes per character, so what is read back is always whole characters.

A variable-length `String` member is not part of the byte image:

- In MS-VBA, `LSet` onto a record whose type holds a variable-length `String` member is a compile error: "Type
  mismatch" (observed in Excel).
- In RD-VBA, a variable-length `String` member of a UDT has no byte image.
- The value of a variable-length `String` member is carried across only when the destination has a field of the
  same type at the same offset.

#### Objects

An object field is laid out as the address `ObjPtr` returns for its object
([**RD-VBAL §6.1.2.13**](rd-vbal.6.1.2.13.hidden-module.md#objptr)).

|Source bytes|Destination place|Result|
|---|---|---|
|An object|An object|The object, counted, as `Set B.O = A.O` would give it. The destination's old object is released, and the two records share no cell.|
|An object|A `LongPtr` or a `Long`|The object's `ObjPtr` address.|
|A number|An object|The object at that address, counted; `Nothing` for 0. Any other number raises error 424 (Object required), and nothing of the record is written.|
|A `Variant` that holds an object or an array|A `Variant`|The object, counted, or a copy of the array.|

A place declared as a class takes only an object `Set` would give it: an object of that class, or of a class that
implements it. An object of another class raises error 13 (Type mismatch), and nothing of the record is written. This
holds for an object from the source record and for one found by its address.

**RD-VBA differs from MS-VBA:** `LSet` of an `ObjPtr` address into an object field counts the reference, and
MS-VBA does not. Code that uses this to hold a weak reference (a child that refers back to its parent) keeps both
objects alive in RD-VBA: a parent and a child that refer to each other this way never run `Class_Terminate`.

The layout and the field store of a UDT value are described in
[**RD-VBAL §2.5.2.1.3** User-Defined Type (UDT) Values](rd-vbal.2.5.2.1.3.udt-values.md).

## Implementation

|Type or member|Role|
|---|---|
|`VBUserDefinedTypeImage`|The byte copy of `LSet`'s UDT form (**RDCore.Runtime**).|
|`VBRecordCopyOutcome`|What the copy did: copied, or the error that stopped it (**RDCore.Runtime**).|
|`VBUserDefinedTypeLayout`|The layout a record is laid out through, and read back through (**RDCore.SDK**).|
|`RDCore.Runtime.Semantics.Statements.StatementRuntimeSemanticsProvider`|Dispatches an `AssignmentStatementNode` whose `Kind` is `LSet` or `RSet`; see [**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md).|
