# 5.4.3.2 Local Constant Declarations

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.3.2** Local Constant Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/90382d70-261f-468f-92de-0068235c012b).

## Syntax

|Declaration|AST node|Symbol on the procedure's `Locals`|
|---|---|---|
|`Const` (in a procedure body)|[ConstantDeclarationNode](../api/RDCore.SDK.Model.AST.Declarations.ConstantDeclarationNode.html)|[VBLocalConstantSymbol](../api/RDCore.SDK.Model.Symbols.VBLocalConstantSymbol.html)|

`VBProcedureMemberSymbol.Locals` and `VBReturningMemberSymbol.Locals` list every `Dim`, `Static` and `Const`
declared in the procedure body; see
[**RD-VBAL §5.4.3.1** Local Variable Declarations](rd-vbal.5.4.3.1.local-variable-declarations.md).

## Runtime Semantics

In MS-VBAL, a local `Const`'s value is substituted at compile time; it has no run-time address.

RD-VBA does the same: a constant's expression is reduced once, when its module is loaded, and a read of the constant is that value. The expression is evaluated in the scope
where the constant is written, and may name another constant.

## Implementation

`RDCore.Runtime.Execution.RuntimeProcedureInvoker.HoistLocals` hoists a procedure's `Dim` and `Static` locals
only; a local `Const` gets no storage. `RDCore.Runtime.Execution.ModuleLoader` hands the constants of the module, the local ones included, to
`RuntimeExpressionEvaluator.FoldConstants`.

---
> ⏮️ [**RD-VBAL §5.4.3.1** Local Variable Declarations](rd-vbal.5.4.3.1.local-variable-declarations.md) | ⏭️ [**RD-VBAL §5.4.3.3** ReDim Statement](rd-vbal.5.4.3.3.redim-statement.md)
