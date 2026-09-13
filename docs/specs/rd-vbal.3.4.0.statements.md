# 3.4.0 Statements

A _statement_ is an executable unit inside a member's body. Every statement node derives
[StatementNode](../api/RDCore.SDK.Model.AST.Abstract.StatementNode.html), which implements
[IExecutableNode](../api/RDCore.SDK.Model.AST.Abstract.IExecutableNode.html) — its `Inputs` are the
expressions evaluated immediately before the statement executes. A block statement's nested statements
are held in a [StatementBlock](../api/RDCore.SDK.Model.AST.Statements.StatementBlock.html), independent
of `Inputs`.

> [!NOTE]
> This page catalogs the AST shape the _parser_ produces — it does not describe static or runtime
> semantics (coercion, error conditions, control flow). Those belong to the resolver and the
> interpreter, and are out of scope here.


---
## 3.4.1 Block Statements

Every block statement follows the same shape: its header condition(s)/expression(s) are named
properties, and its nested statements are a `Body: StatementBlock` — a deliberate consistency across
the whole family (MS-VBAL §5.4.2's block-statement group), rather than an undifferentiated children
list.

|Statement|Node type(s)|MS-VBAL|
|---|---|---|
|`If...Then...ElseIf...Else...End If`|[IfBlockStatementNode](../api/RDCore.SDK.Model.AST.Statements.IfBlockStatementNode.html), [ElseIfBlockStatementNode](../api/RDCore.SDK.Model.AST.Statements.ElseIfBlockStatementNode.html), [ElseBlockStatementNode](../api/RDCore.SDK.Model.AST.Statements.ElseBlockStatementNode.html)|§5.4.2.8|
|`While...Wend`|[WhileWendStatementNode](../api/RDCore.SDK.Model.AST.Statements.WhileWendStatementNode.html)|§5.4.2.2|
|`For...Next`|[ForStatementNode](../api/RDCore.SDK.Model.AST.Statements.ForStatementNode.html)|§5.4.2.3|
|`For Each...Next`|[ForEachStatementNode](../api/RDCore.SDK.Model.AST.Statements.ForEachStatementNode.html)|§5.4.2.4|
|`Do...Loop` (5 header shapes: bare, `Do While`/`Loop`, `Do Until`/`Loop`, `Do`/`Loop While`, `Do`/`Loop Until`)|[DoLoopStatementNode](../api/RDCore.SDK.Model.AST.Statements.DoLoopStatementNode.html), [DoWhileLoopStatementNode](../api/RDCore.SDK.Model.AST.Statements.DoWhileLoopStatementNode.html), [DoUntilLoopStatementNode](../api/RDCore.SDK.Model.AST.Statements.DoUntilLoopStatementNode.html), [DoLoopWhileStatementNode](../api/RDCore.SDK.Model.AST.Statements.DoLoopWhileStatementNode.html), [DoLoopUntilStatementNode](../api/RDCore.SDK.Model.AST.Statements.DoLoopUntilStatementNode.html)|§5.4.2.6|
|`Select Case...End Select`|[SelectCaseStatementNode](../api/RDCore.SDK.Model.AST.Statements.SelectCaseStatementNode.html), [CaseExpressionStatementNode](../api/RDCore.SDK.Model.AST.Statements.CaseExpressionStatementNode.html), [CaseElseClauseStatementNode](../api/RDCore.SDK.Model.AST.Statements.CaseElseClauseStatementNode.html)|§5.4.2.10|
|`With...End With`|[WithStatementNode](../api/RDCore.SDK.Model.AST.Statements.WithStatementNode.html)|§5.4.2.21|
|Single-line `If...Then...Else`|[InlineIfStatementNode](../api/RDCore.SDK.Model.AST.Statements.InlineIfStatementNode.html) — `ThenBody`/`ElseBody` may hold several colon-separated statements instead of a full `block`|§5.4.2.9|

Each `Case` line's comma-separated conditions are its own small hierarchy under the abstract
[CaseRangeClauseNode](../api/RDCore.SDK.Model.AST.Statements.CaseRangeClauseNode.html): a single value
([CaseValueRangeClauseNode](../api/RDCore.SDK.Model.AST.Statements.CaseValueRangeClauseNode.html), e.g.
`Case 5`), a comparison
([CaseComparisonRangeClauseNode](../api/RDCore.SDK.Model.AST.Statements.CaseComparisonRangeClauseNode.html),
e.g. `Case Is > 5`), or an inclusive range
([CaseToRangeClauseNode](../api/RDCore.SDK.Model.AST.Statements.CaseToRangeClauseNode.html), e.g.
`Case 1 To 10`) — all three independently, per MS-VBAL §5.4.2.10.

> [!TIP]
> A bare line-number target in either branch (`If x Then 100`) is not modeled as its own shape — MS-VBAL
> specifies it as equivalent to a `GoTo` statement targeting that line, so the parser synthesizes a real
> [GoToStatementNode](../api/RDCore.SDK.Model.AST.Statements.GoToStatementNode.html) as that branch's
> (typically only) statement instead.


---
## 3.4.2 Simple Statements

|Statement|Node type|MS-VBAL|
|---|---|---|
|`Call` / bare call|[CallStatementNode](../api/RDCore.SDK.Model.AST.Statements.CallStatementNode.html)|§5.4.2.1|
|`Let` assignment (`[Let] lExpression = expression`)|[AssignmentStatementNode](../api/RDCore.SDK.Model.AST.Statements.AssignmentStatementNode.html) (`Kind`: `ImplicitLet`/`ExplicitLet`)|§5.4.3.8|
|`Set` assignment|`AssignmentStatementNode` (`Kind`: `Set`)|§5.4.3.9|
|`ReDim` [Preserve]|[RedimDeclarationNode](../api/RDCore.SDK.Model.AST.Declarations.RedimDeclarationNode.html) — modeled as a declaration, not a statement, since it declares/resizes storage|§5.4.3.3|
|`Erase`|[KeywordStatementNode](../api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html) (`Token`: `Erase`)|§5.4.3.4|
|`Name...As`|`KeywordStatementNode` (`Token`: `Name`)|— (not a MS-VBAL-numbered statement)|
|`RaiseEvent`|`KeywordStatementNode` (`Token`: `RaiseEvent`)|§5.4.2.20|
|`Stop`|`KeywordStatementNode` (`Token`: `Stop`)|§5.4.2.11|
|`End`|`KeywordStatementNode` (`Token`: `End`)|— (not a MS-VBAL-numbered statement)|
|`Exit Do`/`Exit For`/`Exit Sub`/`Exit Function`/`Exit Property`|`KeywordStatementNode` (`Token`: `ExitDo`/`ExitFor`/`ExitSub`/`ExitFunction`/`ExitProperty`)|§5.4.2.7/.5/.17/.18/.19|
|`GoTo`|[GoToStatementNode](../api/RDCore.SDK.Model.AST.Statements.GoToStatementNode.html)|§5.4.2.12|
|`GoSub`|[GoSubStatementNode](../api/RDCore.SDK.Model.AST.Statements.GoSubStatementNode.html)|§5.4.2.14|
|`Return`|[ReturnStatementNode](../api/RDCore.SDK.Model.AST.Statements.ReturnStatementNode.html)|§5.4.2.15|
|`On Error GoTo <label>`|[OnErrorGoToStatementNode](../api/RDCore.SDK.Model.AST.Statements.OnErrorGoToStatementNode.html)|§5.4.4.1|
|`On Error Resume Next`|[OnErrorResumeStatementNode](../api/RDCore.SDK.Model.AST.Statements.OnErrorResumeStatementNode.html) — the same grammar rule as `On Error GoTo`, disambiguated by which keyword follows|§5.4.4.1|
|Bare `Resume`, `Resume <label>`|[ResumeStatementNode](../api/RDCore.SDK.Model.AST.Statements.ResumeStatementNode.html) (`LabelExpression` nullable)|§5.4.4.2|
|`Resume Next`|[ResumeNextStatementNode](../api/RDCore.SDK.Model.AST.Statements.ResumeNextStatementNode.html) — its own node, not `ResumeStatementNode` with a "Next" label|§5.4.4.2|
|`Error #`|[ErrorStatementNode](../api/RDCore.SDK.Model.AST.Statements.ErrorStatementNode.html)|§5.4.4.3|

`Assignment`'s `Target` is always an `lExpression` — see [RD-VBAL §3.0.2](rd-vbal.3.0.syntax-tree.html)
for the shared expression family it draws from (member access, index, dictionary access, a bare name).
Statement labels and line numbers themselves are captured separately
([LineLabelNode](../api/RDCore.SDK.Model.AST.Statements.LineLabelNode.html)/
[LineNumberNode](../api/RDCore.SDK.Model.AST.Statements.LineNumberNode.html)) — a `GoTo`/`GoSub`'s own
target is just an expression naming or numbering one, with no static link between the two.

> [!NOTE]
> Computed `On...GoTo`/`On...GoSub` (§5.4.2.13/.16) have no node type at all yet — not yet requested.

> [!NOTE]
> `Mid`/`Mid$`/`LSet`/`RSet` (§5.4.3.5/.6/.7) are assignment-shaped statements with no node type yet —
> unblocked now that a real `lExpression` exists as their left-hand side, but not yet built.


---
## 3.4.3 File Statements

MS-VBAL groups file I/O under one umbrella, §5.4.5 File Statements.

|Statement|Node type|MS-VBAL|
|---|---|---|
|`Open`|[OpenStatementNode](../api/RDCore.SDK.Model.AST.Statements.OpenStatementNode.html) — `Mode`/`Access`/`Lock` are keyword choices ([VBFileMode](../api/RDCore.SDK.Model.AST.Statements.VBFileMode.html), [VBFileAccessMode](../api/RDCore.SDK.Model.AST.Statements.VBFileAccessMode.html), [VBFileLockMode](../api/RDCore.SDK.Model.AST.Statements.VBFileLockMode.html)), not expressions|§5.4.5.1|
|`Close`, `Reset`|`KeywordStatementNode` (`Token`: `Close`/`Reset`)|§5.4.5.2|
|`Seek`|`KeywordStatementNode` (`Token`: `Seek`)|§5.4.5.3|
|`Lock`, `Unlock`|`KeywordStatementNode` (`Token`: `Lock`/`Unlock`)|§5.4.5.4/.5|
|`Line Input #`|`KeywordStatementNode` (`Token`: `LineInput`)|§5.4.5.6|
|`Width #`|`KeywordStatementNode` (`Token`: `Width`)|§5.4.5.7|
|`Print #`, `Debug.Print`|[PrintStatementNode](../api/RDCore.SDK.Model.AST.Statements.PrintStatementNode.html) (`Token`: `Print`), [ObjectPrintExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.ObjectPrintExpressionNode.html) for the unqualified object-relative form|§5.4.5.8|
|`Write #`|`PrintStatementNode` (`Token`: `Write`)|§5.4.5.9|
|`Input #`|`KeywordStatementNode` (`Token`: `Input`)|§5.4.5.10|
|`Put #`|`KeywordStatementNode` (`Token`: `Put`)|§5.4.5.11|
|`Get #`|`KeywordStatementNode` (`Token`: `Get`)|§5.4.5.12|

`PrintStatementNode`/`ObjectPrintExpressionNode` share an output-list shape:
[PrintOutputItemNode](../api/RDCore.SDK.Model.AST.Expressions.PrintOutputItemNode.html) (a value and/or
a `;`/`,` separator), [PrintSpcClauseNode](../api/RDCore.SDK.Model.AST.Expressions.PrintSpcClauseNode.html),
[PrintTabClauseNode](../api/RDCore.SDK.Model.AST.Expressions.PrintTabClauseNode.html).

> [!TIP]
> A value and its trailing separator are always two separate, alternating `PrintOutputItemNode`s (one
> value-only, one separator-only) — MS-VBAL's `outputItem` grammar rule admits combining them into one
> node, but the generated parser never actually takes that alternative. Any consumer of `Items` should
> walk the alternating list rather than assume `(value, separator)` pairs.

> [!NOTE]
> The `?` shorthand for `Print` has **no lexer/grammar token at all** in the current grammar — adding it
> is a lexer-level change, riskier and different in kind from AST/listener work, and is deliberately
> deferred.


---
> ⏮️ [**RD-VBAL §3.3** Operators](rd-vbal.3.3.0.operators.html) | ⏭️ [**RD-VBAL §4.0** Program Structure](rd-vbal.4.0.program-structure.html)
