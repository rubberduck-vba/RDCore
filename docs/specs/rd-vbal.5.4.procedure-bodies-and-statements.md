# 5.4 Procedure Bodies and Statements

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4** Procedure Bodies and Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/618815bc-c68b-4488-8082-ed1b36fac6d4).

At run time, statements have side effects on program, global, or host environment state
([**RD-VBAL §5.0** Semantics](rd-vbal.5.0.semantics.md)).

## Procedure Bodies

A procedure body executes as an `InstructionList`:
the flat, offset-addressable list of instructions lowered from the body's statement tree.

|Stage|What happens|RD-VBAL|
|---|---|---|
|Parsing|Each statement of the body is a [StatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Abstract.StatementNode.html). A block statement's nested statements are held in a [StatementBlock](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.StatementBlock.html).|[**RD-VBAL §3.4** Statements](rd-vbal.3.4.0.statements.md)|
|Lowering|`InstructionListLowering` flattens the statement tree into an `InstructionList`: one instruction per executable statement, plus the synthesized instructions a block statement needs but has no source node for.|[**RD-VBAL §3.5** Instructions](rd-vbal.3.5.0.instructions.md)|
|Execution|`ProcedureExecutor` drives the activation's program counter through the `InstructionList`: fetch the instruction, decode it by `InstructionKind`, act on the outcome, repeat.|[**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)|
|Invocation|Invoking a procedure pushes a new activation and runs the callee's lowered body through the same executor.|[**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)|

A statement tree cannot express an arbitrary jump, and `GoTo`, `GoSub`, `On…GoTo`, `On…GoSub` and `Resume` can
jump to any statement in the procedure. Lowering therefore flattens the statement tree, but never the expression
trees each statement holds.

## Statement Pages

Each statement page of this section describes one MS-VBAL statement under the headings **Syntax** (the AST node and
the instruction kind it lowers to), **Static Semantics**, **Runtime Semantics** and **Implementation**.

---
## In this section

|§|Title|MS-VBAL|
|---|---|---|
|5.4.1|[Statement Blocks](rd-vbal.5.4.1.statement-blocks.md) (*reserved*)|[§5.4.1](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d3220925-f958-4ae4-a9cc-e529072e156f)|
|5.4.2|[Control Statements](rd-vbal.5.4.2.control-statements.md)|[§5.4.2](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/70d423da-18b4-42d2-9897-9f0b8100786b)|
|5.4.3|[Data Manipulation Statements](rd-vbal.5.4.3.data-manipulation-statements.md)|[§5.4.3](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ee62ca0d-bf15-4679-8d11-6e411b37901b)|
|5.4.4|[Error Handling Statements](rd-vbal.5.4.4.error-handling-statements.md)|[§5.4.4](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/47b18690-3175-44d9-9de1-31629f0aacc7)|
|5.4.5|[File Statements](rd-vbal.5.4.5.file-statements.md)|[§5.4.5](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2fd9c1be-0d9a-4b29-b5ac-c9d51ce483cf)|
|5.4.6|[Graphics Statements](rd-vbal.5.4.6.graphics-statements.md)|RD-VBA addition|
