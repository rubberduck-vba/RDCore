# 3.5.0 Instructions

An *instruction* is the unit the interpreter's program counter fetches. A procedure body has one instruction
per executable [StatementNode](../api/RDCore.SDK.Model.AST.Abstract.StatementNode.html), plus the
synthesized instructions that a block statement needs but has no source node for. Instructions are ordered in
the order execution would normally reach them.

[**RD-VBAL §3.4.0** Statements](rd-vbal.3.4.0.statements.md) catalogs the statement tree the parser produces.
This section catalogs the flat, offset-addressable
`InstructionList` that
`InstructionListLowering` produces from
that tree. Executing it is the role that [**RD-VBAL §2.3.1** Composition Root](rd-vbal.2.3.1.composition-root.md)
gives the evaluation engine: "sequentially evaluate each instruction in the frame."

## Why a flat list

`GoTo`, `GoSub`, `On…GoTo`, `On…GoSub` and `Resume` can jump to any statement in the procedure. A recursive
tree walk cannot express an arbitrary jump, which is why statements are lowered to a flat instruction list.

Expressions keep their tree shape, because expressions contain no jumps. Lowering therefore only
flattens the statement tree; it never flattens the expression trees held in each statement's `Inputs`.

---
## In this section

|§|Title|
|---|---|
|3.5.1|[InstructionList](rd-vbal.3.5.1.instructionlist.md)|
|3.5.2|[Instruction](rd-vbal.3.5.2.instruction.md)|
|3.5.3|[Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)|
|3.5.4|[Execution](rd-vbal.3.5.4.execution.md)|
|3.5.5|[Placement and Licensing](rd-vbal.3.5.5.placement-and-licensing.md)|
