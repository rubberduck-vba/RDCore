# 3.4.1 Block Statements

A _block statement_ holds nested statements. The block statements belong to [**MS-VBAL §5.4.2** Control Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/70d423da-18b4-42d2-9897-9f0b8100786b); each is cross-referenced below to its MS-VBAL section and to the RD-VBAL page that describes its implementation.

|Statement|Node type(s)|MS-VBAL|RD-VBAL|
|---|---|---|---|
|`If...Then...ElseIf...Else...End If`|[IfBlockStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.IfBlockStatementNode.html), [ElseIfBlockStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.ElseIfBlockStatementNode.html), [ElseBlockStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.ElseBlockStatementNode.html)|[**MS-VBAL §5.4.2.8** If Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/17ff9b37-fbc8-491f-85b2-13c3a379acac)|[**RD-VBAL §5.4.2.8** If Statement](rd-vbal.5.4.2.8.if-statement.md)|
|`While...Wend`|[WhileWendStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.WhileWendStatementNode.html)|[**MS-VBAL §5.4.2.2** While Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4f2f6c46-3c09-4a6d-905b-fe6658405b6f)|[**RD-VBAL §5.4.2.2** While Statement](rd-vbal.5.4.2.2.while-statement.md)|
|`For...Next`|[ForStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.ForStatementNode.html)|[**MS-VBAL §5.4.2.3** For Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/389b1dc4-e608-4ed0-ae64-d88f62f12ea3)|[**RD-VBAL §5.4.2.3** For Statement](rd-vbal.5.4.2.3.for-statement.md)|
|`For Each...Next`|[ForEachStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.ForEachStatementNode.html)|[**MS-VBAL §5.4.2.4** For Each Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/b132463a-fd25-4143-8fc7-a443930e0651)|[**RD-VBAL §5.4.2.4** For Each Statement](rd-vbal.5.4.2.4.for-each-statement.md)|
|`Do...Loop` (five header shapes; see [Do Loop Forms](#do-loop-forms))|[DoLoopStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.DoLoopStatementNode.html), [DoWhileLoopStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.DoWhileLoopStatementNode.html), [DoUntilLoopStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.DoUntilLoopStatementNode.html), [DoLoopWhileStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.DoLoopWhileStatementNode.html), [DoLoopUntilStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.DoLoopUntilStatementNode.html)|[**MS-VBAL §5.4.2.6** Do Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/61d886e0-1768-4032-8bbb-dd3eca7977df)|[**RD-VBAL §5.4.2.6** Do Statement](rd-vbal.5.4.2.6.do-statement.md)|
|`Select Case...End Select`|[SelectCaseStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.SelectCaseStatementNode.html), [CaseExpressionStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.CaseExpressionStatementNode.html), [CaseElseClauseStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.CaseElseClauseStatementNode.html)|[**MS-VBAL §5.4.2.10** Select Case Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/94a2f0fe-bdbe-4f5d-b3f4-bbf339b0ac65)|[**RD-VBAL §5.4.2.10** Select Case Statement](rd-vbal.5.4.2.10.select-case-statement.md)|
|`With...End With`|[WithStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.WithStatementNode.html)|[**MS-VBAL §5.4.2.21** With Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/52caae3d-3ded-436f-a36a-8d5a30c21600)|[**RD-VBAL §5.4.2.21** With Statement](rd-vbal.5.4.2.21.with-statement.md)|
|Single-line `If...Then...Else`|[InlineIfStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.InlineIfStatementNode.html); see [Single-line If](#single-line-if)|[**MS-VBAL §5.4.2.9** Single-line If Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6b4fae50-5e47-4469-970b-a2b5a2b62e7a)|[**RD-VBAL §5.4.2.9** Single-line If Statement](rd-vbal.5.4.2.9.single-line-if-statement.md)|

## Node Shape

Every block statement node follows the same shape:

- its header condition(s) and expression(s) are named properties of the node;
- its nested statements are held in a `Body` property of type [StatementBlock](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.StatementBlock.html).

The uniform `Body: StatementBlock` shape is deliberate. It keeps the whole block-statement family (the block statements of **MS-VBAL §5.4.2**) consistent, rather than giving each node an undifferentiated list of children.

Three nodes hold their nested statements differently:

|Node|Nested statements|
|---|---|
|`InlineIfStatementNode`|One `StatementBlock` per branch: `ThenBody` and `ElseBody`.|
|`CaseExpressionStatementNode`|A `StatementBlock` named `Block`.|
|`SelectCaseStatementNode`|No `StatementBlock` of its own: it holds its `Case` and `Case Else` nodes, and each of those holds one.|

A block statement's `StatementBlock` is independent of the statement node's `Inputs`; see [**RD-VBAL §3.4.0** Statements](rd-vbal.3.4.0.statements.md).

Following MS-VBAL, `Next`, `Loop` and `Wend` have no AST node of their own. The whole construct is one node (`ForStatementNode`, `DoLoopStatementNode`, and so on) with a `Body`; see [**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md).

### Do Loop Forms

`Do...Loop` has five header shapes, each represented by its own node type (**MS-VBAL §5.4.2.6**):

|Header shape|Node type|
|---|---|
|`Do` ... `Loop`|`DoLoopStatementNode`|
|`Do While` ... `Loop`|`DoWhileLoopStatementNode`|
|`Do Until` ... `Loop`|`DoUntilLoopStatementNode`|
|`Do` ... `Loop While`|`DoLoopWhileStatementNode`|
|`Do` ... `Loop Until`|`DoLoopUntilStatementNode`|

## Single-line If

`InlineIfStatementNode`'s `ThenBody` and `ElseBody` may each hold several colon-separated statements instead of a full `block` (**MS-VBAL §5.4.2.9**).

> [!TIP]
> A bare line-number target in either branch (`If x Then 100`) is not modelled as its own AST shape. MS-VBAL specifies such a target as equivalent to a `GoTo` statement targeting that line. The parser therefore synthesizes a [GoToStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.GoToStatementNode.html) as that branch's statement, typically its only one.

## Case Clauses

Each `Case` line's comma-separated conditions form their own node hierarchy under the abstract [CaseRangeClauseNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.CaseRangeClauseNode.html). The three condition forms of **MS-VBAL §5.4.2.10** are modelled independently, as three node types:

|Condition form|Example|Node type|
|---|---|---|
|Single value|`Case 5`|[CaseValueRangeClauseNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.CaseValueRangeClauseNode.html)|
|Comparison|`Case Is > 5`|[CaseComparisonRangeClauseNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.CaseComparisonRangeClauseNode.html)|
|Inclusive range|`Case 1 To 10`|[CaseToRangeClauseNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.CaseToRangeClauseNode.html)|
