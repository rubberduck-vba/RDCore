# 3.4.0 Statements

A _statement_ is an executable unit inside a member's body.

## Statement Nodes

Every statement node derives from [StatementNode](../api/RDCore.SDK.Model.AST.Abstract.StatementNode.html). `StatementNode` derives directly from [SyntaxNode](../api/RDCore.SDK.Model.AST.Abstract.SyntaxNode.html); see [**RD-VBAL §3.0.2** Node Types](rd-vbal.3.0.2.node-types.md).

`StatementNode` implements [IExecutableNode](../api/RDCore.SDK.Model.AST.Abstract.IExecutableNode.html). A statement node's `Inputs`, from `IExecutableNode`, are the expressions evaluated immediately before the statement executes.

A block statement's nested statements are held in a [StatementBlock](../api/RDCore.SDK.Model.AST.Statements.StatementBlock.html). The `StatementBlock` is independent of the statement node's `Inputs`.

## Statement Node Families

This section catalogs the statement AST node families in three groups:

|Group|Section|MS-VBAL|
|---|---|---|
|Block statements|[**RD-VBAL §3.4.1** Block Statements](rd-vbal.3.4.1.block-statements.md)|[**MS-VBAL §5.4.2** Control Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/70d423da-18b4-42d2-9897-9f0b8100786b)|
|Simple statements|[**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md)|**MS-VBAL §5.4.2**, [**MS-VBAL §5.4.3** Data Manipulation Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ee62ca0d-bf15-4679-8d11-6e411b37901b), [**MS-VBAL §5.4.4** Error Handling Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/47b18690-3175-44d9-9de1-31629f0aacc7)|
|File statements|[**RD-VBAL §3.4.3** File Statements](rd-vbal.3.4.3.file-statements.md)|[**MS-VBAL §5.4.5** File Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2fd9c1be-0d9a-4b29-b5ac-c9d51ce483cf)|

Each statement node family is cross-referenced to its MS-VBAL section, and to the page of [**RD-VBAL §5.4** Procedure Bodies and Statements](rd-vbal.5.4.procedure-bodies-and-statements.md) that describes its implementation.

> [!NOTE]
> This section catalogs the AST shape the parser produces. It does not describe the static or runtime semantics of statements (coercion, error conditions, control flow). Those belong to the resolver and the interpreter, and are out of scope here.

## Statements and Instructions

This section catalogs the statement tree the parser produces. [**RD-VBAL §3.5.0** Instructions](rd-vbal.3.5.0.instructions.md) catalogs the flat, offset-addressable [InstructionList](../api/RDCore.SDK.Semantics.Instructions.InstructionList.html) produced from that tree.
