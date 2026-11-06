# 5.4.3 Data Manipulation Statements

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.3** Data Manipulation Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ee62ca0d-bf15-4679-8d11-6e411b37901b).

Each data manipulation statement is listed below with the node the parser produces for it, the instruction kind
it lowers to, and the RD-VBAL page that describes its implementation. The node catalog is in
[**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md); instruction kinds are described in
[**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md).

|Statement|AST node|Instruction kind|RD-VBAL|
|---|---|---|---|
|`Dim`, `Static` (in a procedure body)|[VariableDeclarationNode](../api/RDCore.SDK.Model.AST.Declarations.VariableDeclarationNode.html)|None (a declaration node; the local is hoisted when the procedure is invoked)|[**RD-VBAL §5.4.3.1** Local Variable Declarations](rd-vbal.5.4.3.1.local-variable-declarations.md)|
|`Const` (in a procedure body)|[ConstantDeclarationNode](../api/RDCore.SDK.Model.AST.Declarations.ConstantDeclarationNode.html)|None (a declaration node)|[**RD-VBAL §5.4.3.2** Local Constant Declarations](rd-vbal.5.4.3.2.local-constant-declarations.md)|
|`ReDim`, `ReDim Preserve`|[RedimDeclarationNode](../api/RDCore.SDK.Model.AST.Declarations.RedimDeclarationNode.html)|None (a declaration node)|[**RD-VBAL §5.4.3.3** ReDim Statement](rd-vbal.5.4.3.3.redim-statement.md)|
|`Erase`|[KeywordStatementNode](../api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html) (`Token`: `Erase`)|`Simple`|[**RD-VBAL §5.4.3.4** Erase Statement](rd-vbal.5.4.3.4.erase-statement.md)|
|`Mid`, `Mid$`, `MidB`, `MidB$`|[MidStatementNode](../api/RDCore.SDK.Model.AST.Statements.MidStatementNode.html)|`Simple`|[**RD-VBAL §5.4.3.5** Mid/MidB/Mid$/MidB$ Statement](rd-vbal.5.4.3.5.mid-statement.md)|
|`LSet`|[AssignmentStatementNode](../api/RDCore.SDK.Model.AST.Statements.AssignmentStatementNode.html) (`Kind`: `LSet`)|`Simple`|[**RD-VBAL §5.4.3.6** LSet Statement](rd-vbal.5.4.3.6.lset-statement.md)|
|`RSet`|`AssignmentStatementNode` (`Kind`: `RSet`)|`Simple`|[**RD-VBAL §5.4.3.7** RSet Statement](rd-vbal.5.4.3.7.rset-statement.md)|
|`Let` assignment (`[Let] lExpression = expression`)|`AssignmentStatementNode` (`Kind`: `ImplicitLet` or `ExplicitLet`)|`Simple`|[**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md)|
|`Set` assignment|`AssignmentStatementNode` (`Kind`: `Set`)|`Simple`|[**RD-VBAL §5.4.3.9** Set Statement](rd-vbal.5.4.3.9.set-statement.md)|

`Let`, `Set`, `LSet` and `RSet` share one node type, `AssignmentStatementNode`, distinguished by its
[AssignmentKind](../api/RDCore.SDK.Model.AST.Statements.AssignmentKind.html) `Kind`. A `Simple` instruction's
statement is dispatched by its own type to its statement runtime semantics; see
[**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md).

---
> ⏮️ [**RD-VBAL §5.4.2.23** Assert Statement](rd-vbal.5.4.2.23.assert-statement.md) | ⏭️ [**RD-VBAL §5.4.3.1** Local Variable Declarations](rd-vbal.5.4.3.1.local-variable-declarations.md)
