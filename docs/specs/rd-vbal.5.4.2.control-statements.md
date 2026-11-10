# 5.4.2 Control Statements

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2** Control Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/70d423da-18b4-42d2-9897-9f0b8100786b).

A control statement's effect on the flow of execution is carried by the instruction it lowers to. A statement's own
runtime semantics are pure: they evaluate operands and return a result, and never mutate control state. The
`InstructionKind` and the pre-resolved offsets on
`Instruction` let the executor's fetch/decode loop decide
whether to branch ([**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)).

A block statement is kept structured. Each of its headers remains an instruction of its own, and only the control
effects between them (a branch's fall-through-versus-skip choice, a loop's back-edge) are pre-resolved offsets
([**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)).

## Statements

|Statement|AST node(s)|Instruction kind(s)|
|---|---|---|
|[**RD-VBAL §5.4.2.1** Call Statement](rd-vbal.5.4.2.1.call-statement.md)|[CallStatementNode](../api/RDCore.SDK.Model.AST.Statements.CallStatementNode.html)|`Simple`|
|[**RD-VBAL §5.4.2.2** While Statement](rd-vbal.5.4.2.2.while-statement.md)|[WhileWendStatementNode](../api/RDCore.SDK.Model.AST.Statements.WhileWendStatementNode.html)|`ConditionalBranch`, `Jump`|
|[**RD-VBAL §5.4.2.3** For Statement](rd-vbal.5.4.2.3.for-statement.md)|[ForStatementNode](../api/RDCore.SDK.Model.AST.Statements.ForStatementNode.html)|`ForOpener`, `ForNext`|
|[**RD-VBAL §5.4.2.4** For Each Statement](rd-vbal.5.4.2.4.for-each-statement.md)|[ForEachStatementNode](../api/RDCore.SDK.Model.AST.Statements.ForEachStatementNode.html)|`ForEachOpener`, `ForEachNext`|
|[**RD-VBAL §5.4.2.5** Exit For Statement](rd-vbal.5.4.2.5.exit-for-statement.md)|[KeywordStatementNode](../api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html) (`Token`: `"Exit For"`)|`ExitLoop`|
|[**RD-VBAL §5.4.2.6** Do Statement](rd-vbal.5.4.2.6.do-statement.md)|[DoLoopStatementNode](../api/RDCore.SDK.Model.AST.Statements.DoLoopStatementNode.html), [DoWhileLoopStatementNode](../api/RDCore.SDK.Model.AST.Statements.DoWhileLoopStatementNode.html), [DoUntilLoopStatementNode](../api/RDCore.SDK.Model.AST.Statements.DoUntilLoopStatementNode.html), [DoLoopWhileStatementNode](../api/RDCore.SDK.Model.AST.Statements.DoLoopWhileStatementNode.html), [DoLoopUntilStatementNode](../api/RDCore.SDK.Model.AST.Statements.DoLoopUntilStatementNode.html)|`ConditionalBranch`, `LoopBack`, `Jump`|
|[**RD-VBAL §5.4.2.7** Exit Do Statement](rd-vbal.5.4.2.7.exit-do-statement.md)|`KeywordStatementNode` (`Token`: `"Exit Do"`)|`ExitLoop`|
|[**RD-VBAL §5.4.2.8** If Statement](rd-vbal.5.4.2.8.if-statement.md)|[IfBlockStatementNode](../api/RDCore.SDK.Model.AST.Statements.IfBlockStatementNode.html), [ElseIfBlockStatementNode](../api/RDCore.SDK.Model.AST.Statements.ElseIfBlockStatementNode.html), [ElseBlockStatementNode](../api/RDCore.SDK.Model.AST.Statements.ElseBlockStatementNode.html)|`ConditionalBranch`, `Jump`|
|[**RD-VBAL §5.4.2.9** Single-line If Statement](rd-vbal.5.4.2.9.single-line-if-statement.md)|[InlineIfStatementNode](../api/RDCore.SDK.Model.AST.Statements.InlineIfStatementNode.html)|`ConditionalBranch`|
|[**RD-VBAL §5.4.2.10** Select Case Statement](rd-vbal.5.4.2.10.select-case-statement.md)|[SelectCaseStatementNode](../api/RDCore.SDK.Model.AST.Statements.SelectCaseStatementNode.html), [CaseExpressionStatementNode](../api/RDCore.SDK.Model.AST.Statements.CaseExpressionStatementNode.html), [CaseElseClauseStatementNode](../api/RDCore.SDK.Model.AST.Statements.CaseElseClauseStatementNode.html)|`Select`, `ConditionalBranch`, `Jump`|
|[**RD-VBAL §5.4.2.11** Stop Statement](rd-vbal.5.4.2.11.stop-statement.md)|`KeywordStatementNode` (`Token`: `Stop`)|`Break`|
|[**RD-VBAL §5.4.2.12** GoTo Statement](rd-vbal.5.4.2.12.goto-statement.md)|[GoToStatementNode](../api/RDCore.SDK.Model.AST.Statements.GoToStatementNode.html)|`Jump`|
|[**RD-VBAL §5.4.2.13** On...GoTo Statement](rd-vbal.5.4.2.13.on-goto-statement.md)|[OnGoToStatementNode](../api/RDCore.SDK.Model.AST.Statements.OnGoToStatementNode.html)|`JumpTable`|
|[**RD-VBAL §5.4.2.14** GoSub Statement](rd-vbal.5.4.2.14.gosub-statement.md)|[GoSubStatementNode](../api/RDCore.SDK.Model.AST.Statements.GoSubStatementNode.html)|`GoSub`|
|[**RD-VBAL §5.4.2.15** Return Statement](rd-vbal.5.4.2.15.return-statement.md)|[ReturnStatementNode](../api/RDCore.SDK.Model.AST.Statements.ReturnStatementNode.html)|`Return`|
|[**RD-VBAL §5.4.2.16** On...GoSub Statement](rd-vbal.5.4.2.16.on-gosub-statement.md)|[OnGoSubStatementNode](../api/RDCore.SDK.Model.AST.Statements.OnGoSubStatementNode.html)|`GoSubTable`|
|[**RD-VBAL §5.4.2.17** Exit Sub Statement](rd-vbal.5.4.2.17.exit-sub-statement.md)|`KeywordStatementNode` (`Token`: `"Exit Sub"`)|`ExitProcedure`|
|[**RD-VBAL §5.4.2.18** Exit Function Statement](rd-vbal.5.4.2.18.exit-function-statement.md)|`KeywordStatementNode` (`Token`: `"Exit Function"`)|`ExitProcedure`|
|[**RD-VBAL §5.4.2.19** Exit Property Statement](rd-vbal.5.4.2.19.exit-property-statement.md)|`KeywordStatementNode` (`Token`: `"Exit Property"`)|`ExitProcedure`|
|[**RD-VBAL §5.4.2.20** RaiseEvent Statement](rd-vbal.5.4.2.20.raiseevent-statement.md)|`KeywordStatementNode` (`Token`: `RaiseEvent`)|`Simple`|
|[**RD-VBAL §5.4.2.21** With Statement](rd-vbal.5.4.2.21.with-statement.md)|[WithStatementNode](../api/RDCore.SDK.Model.AST.Statements.WithStatementNode.html)|`With`|
|[**RD-VBAL §5.4.2.22** End Statement](rd-vbal.5.4.2.22.end-statement.md)|`KeywordStatementNode` (`Token`: `End`)|`Halt`|
|[**RD-VBAL §5.4.2.23** Assert Statement](rd-vbal.5.4.2.23.assert-statement.md)|None|None|

For a block statement, a `Jump` in the table above is the synthesized trailing jump of a branch, or a loop's
back-edge. See
[**RD-VBAL §3.4.1** Block Statements](rd-vbal.3.4.1.block-statements.md),
[**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md) and
[**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md) for the complete node and instruction catalogues.
