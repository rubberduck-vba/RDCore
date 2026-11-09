# 3.5.1 InstructionList

[**RD-VBAL §3.5.0** Instructions](rd-vbal.3.5.0.instructions.md) models a procedure body as an `InstructionList`
of `Instruction` entries
([**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)).
`InstructionListLowering` produces the
list from the statement tree ([**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)).

An `InstructionList` is built once per procedure body, and is immutable.

## Offsets

`InstructionList.Items` is dense: index `i` is offset `i`.

A program counter can hold any offset from `0` to `Items.Length` inclusive, and every such offset names an entry
of the list. `Items.Length` itself is valid to hold:

|Offset|Meaning|
|---|---|
|`0` … `Items.Length - 1`|The instruction at that index of `Items`.|
|`Items.Length`|The offset a label with nothing after it resolves to. Execution at this offset completes as if it had reached the end of the procedure body ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).|

## Keys

`Labels` and `ByNode` are two keys that address an instruction without walking `Items`:

|Key|Method|Looks up|Used for|
|---|---|---|---|
|`Labels`|`TryGetLabelOffset`|A *line label* or *line number* name → its offset ([**MS-VBAL §5.4.1.1** Statement Labels](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/825de02b-0e13-4783-8527-de14fbb7104f)).|Resolving a jump's target.|
|`ByNode`|`TryGetOffset`|A statement's [SyntaxNodeId](../api/RDCore.SDK.Model.AST.Abstract.SyntaxNodeId.html) → its offset.|The fault-statement identity that a breakpoint or a runtime error anchors to.|

Label names are looked up case-insensitively, and a label is scoped to the whole procedure; see
[**RD-VBAL §5.4.1.1** Statement Labels](rd-vbal.5.4.1.1.statement-labels.md).

A synthesized instruction has no source node, so it is never a value in `ByNode`
([**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)).

## Line numbers

`InstructionList.TryGetLineNumber` answers the line number `Erl` reports when the environment counts
line-number labels (the `LineLabel` setting). See [**RD-VBAL §6.1.2.7** Information](rd-vbal.6.1.2.7.information.md)
for `Erl` and the setting that chooses what it counts.
