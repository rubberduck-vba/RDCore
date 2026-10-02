# 5.4.3.6 LSet Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.3.6** LSet Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6964d2a4-b3e3-497b-bb80-8bc98f0edab9).

`LSet` (**MS-VBAL §5.4.3.6**) and `RSet`
([**MS-VBAL §5.4.3.7** RSet Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/beccdd43-9dad-4bcf-b063-e542869917a1);
[**RD-VBAL §5.4.3.7** RSet Statement](rd-vbal.5.4.3.7.rset-statement.md)) are implemented.

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[AssignmentStatementNode](../api/RDCore.SDK.Model.AST.Statements.AssignmentStatementNode.html) (`Kind`: `LSet`)|`Simple`|The same node shape as `Let`, `Set` and `RSet`, which share `AssignmentStatementNode` and are distinguished by its [AssignmentKind](../api/RDCore.SDK.Model.AST.Statements.AssignmentKind.html) `Kind`.|

See [**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md).

## Static Semantics

A program written against MS-VBA that uses `LSet` over a record holding a variable-length `String` member relies
on what MS-VBA does with it (see [UDT form](#udt-form)). The statement says so with a semantic flag, and not with an error: it is not wrong.

[FixedAssignmentRuntimeSemantics](../api/RDCore.Runtime.Semantics.Statements.FixedAssignmentRuntimeSemantics.html)`.Analyze` sets the
[FixedAssignmentSemanticFlags](../api/RDCore.SDK.Semantics.Flags.FixedAssignmentSemanticFlags.html) of the statement, the way an operator's `Analyze` sets its own:

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

|Value, compared with the target's width|`LSet`|`RSet`|
|---|---|---|
|Shorter|Left-aligned: padded at the end.|Right-aligned: padded at the start.|
|Longer|Truncated.|Truncated from the same end as `LSet`.|

### UDT form

`LSet`'s other form is a byte copy between two UDT variables. `LSet` copies a UDT as bytes because a byte copy
between two UDTs is how VBA emulates a union.

1. The source record is laid out through its own layout
   ([VBUserDefinedTypeLayout](../api/RDCore.SDK.Model.Types.VBUserDefinedTypeLayout.html)).
2. Only the bytes both types have are copied.
3. The copied bytes are read back through the destination's layout. For example, a `Long` is reinterpreted as two
   `Integer`s.
4. A destination field that the source's image does not reach is set to its declared type's default value, rather
   than being filled with half a value.

A variable-length `String` member is not part of the byte image:

- In MS-VBA, `LSet` over a UDT with a variable-length `String` member copies the pointer, leaving two records
  owning one allocation, which corrupts the process.
- In RD-VBA, a variable-length `String` member of a UDT has no byte image.
- The value of a variable-length `String` member is carried across only when the destination has a field of the
  same type at the same offset.
- `LSet`'s UDT form never reinterprets a reference as a different kind of reference.

The layout and the field store of a UDT value are described in
[**RD-VBAL §2.5.2.1.3** User-Defined Type (UDT) Values](rd-vbal.2.5.2.1.3.udt-values.md).

## Implementation

|Type or member|Role|
|---|---|
|[VBUserDefinedTypeImage](../api/RDCore.SDK.Model.Types.VBUserDefinedTypeImage.html)|The byte copy of `LSet`'s UDT form (**RDCore.SDK**).|
|`VBUserDefinedTypeLayout`|The layout a record is laid out through, and read back through (**RDCore.SDK**).|
|`RDCore.Runtime.Semantics.Statements.StatementRuntimeSemanticsProvider`|Dispatches an `AssignmentStatementNode` whose `Kind` is `LSet` or `RSet`; see [**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md).|

---
> ⏮️ [**RD-VBAL §5.4.3.5** Mid/MidB/Mid$/MidB$ Statement](rd-vbal.5.4.3.5.mid-statement.md) | ⏭️ [**RD-VBAL §5.4.3.7** RSet Statement](rd-vbal.5.4.3.7.rset-statement.md)
