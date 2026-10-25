# 5.4.3.4 Erase Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.3.4** Erase Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f7958382-95a7-47fa-91bd-42262ab9ad32).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[KeywordStatementNode](../api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html) (`Token`: `Erase`)|`Simple`|See [**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md).|

## Static Semantics

The declared type of each `erase-element` must be an array or a `Variant`.

An `erase-element` is "classified as a variable, property, function or unbound member" (**MS-VBAL §5.4.3.4**): a
constant (`Erase vbCrLf`) is [`VBC09333`](../diagnostics/vbc09333.md) (`VariableRequired`).

A function named outside its own body - or a property of a module - is a call of it, and what it returns is what is
erased. As in MS-VBA (recorded in Excel), one that takes an argument is [`VBC09339`](../diagnostics/vbc09339.md)
_Argument not optional_, before what its type is (`Erase Left`, `Erase Left$`), and one whose declared type is not a
`Variant` cannot return an array: [`VBC09307`](../diagnostics/vbc09307.md) _Expected array_ (`Erase F` of a
`Function F() As Long`, `Erase Err`, `Erase Timer`).

## Runtime Semantics

`Erase` is `ReDim`'s opposite: `ReDim` gives a resizable array its dimensions, and `Erase` takes them away
again ([**RD-VBAL §5.4.3.3** ReDim Statement](rd-vbal.5.4.3.3.redim-statement.md)). What it does depends on
which kind of array it was given, because a fixed-size array's bounds are part of its declaration and nothing
at run time may change them.

|Element|Effect|
|---|---|
|A resizable array|Set to an empty array of the same element type. The dimensions and the data are gone.|
|A `Variant` holding an array|The same. The variable keeps storing a `Variant`.|
|A fixed-size array|The dimensions stay. Every element is reset to its element type's default value.|
|A function, or a property of a module|Called: an array it returns is a copy of its own, and nothing else changes; anything else is error 13 (`Erase Now`).|
|Anything else|Runtime error 13, `Type mismatch`.|

An `Erase` statement erases each element of its `erase-list`, in source order.

An `erase-element` that is not a simple name - a member access (`obj.Items`), an element (`v(1)`) - is an expression: the array is read from it, and a resizable array's
emptied replacement is written back through it, as an assignment to it is. A fixed-size array is reset in place.

## Implementation

|Type or member|Role|
|---|---|
|`RDCore.Runtime.Semantics.Statements.ArrayStatementRuntimeSemantics`|Erases each element; see [**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md).|
|[VBResizableArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBResizableArrayValue.html), [VBFixedSizeArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBFixedSizeArrayValue.html)|The two kinds of array the statement tells apart (**RDCore.SDK**).|

---
> ⏮️ [**RD-VBAL §5.4.3.3** ReDim Statement](rd-vbal.5.4.3.3.redim-statement.md) | ⏭️ [**RD-VBAL §5.4.3.5** Mid/MidB/Mid$/MidB$ Statement](rd-vbal.5.4.3.5.mid-statement.md)
