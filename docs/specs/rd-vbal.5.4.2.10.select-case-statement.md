# 5.4.2.10 Select Case Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.10** Select Case Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/94a2f0fe-bdbe-4f5d-b3f4-bbf339b0ac65).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[SelectCaseStatementNode](../api/RDCore.SDK.Model.AST.Statements.SelectCaseStatementNode.html)|`Select`|The `Select Case` opener. Its `End` is right past the block.|
|[CaseExpressionStatementNode](../api/RDCore.SDK.Model.AST.Statements.CaseExpressionStatementNode.html)|`ConditionalBranch`|A `Case` header. Its `Matching` names the offset of its `Select` opener. Its `Else` is the offset to go to on no match: the next header in the chain, the first instruction of the `Case Else` body, or right past the whole construct.|
|[CaseElseClauseStatementNode](../api/RDCore.SDK.Model.AST.Statements.CaseElseClauseStatementNode.html)|—|`Case Else` needs no condition.|
|— (synthesized)|`Jump`|The trailing jump at the end of each branch's body. `Node` is `null`; `Target` is right past the whole construct.|

Each `Case` line's comma-separated conditions form their own node hierarchy under the abstract
[CaseRangeClauseNode](../api/RDCore.SDK.Model.AST.Statements.CaseRangeClauseNode.html). The three condition forms
of **MS-VBAL §5.4.2.10** are modelled independently, as three node types:

|Condition form|Example|AST node|
|---|---|---|
|Single value|`Case 5`|[CaseValueRangeClauseNode](../api/RDCore.SDK.Model.AST.Statements.CaseValueRangeClauseNode.html)|
|Comparison|`Case Is > 5`|[CaseComparisonRangeClauseNode](../api/RDCore.SDK.Model.AST.Statements.CaseComparisonRangeClauseNode.html)|
|Inclusive range|`Case 1 To 10`|[CaseToRangeClauseNode](../api/RDCore.SDK.Model.AST.Statements.CaseToRangeClauseNode.html)|

See [**RD-VBAL §3.4.1** Block Statements](rd-vbal.3.4.1.block-statements.md) and
[**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md).

## Runtime Semantics

### Select

1. Evaluate the header expression (the selector) once.
2. Store the selector on the activation, keyed by the `Select` instruction's own offset.
3. Fall through into the body, to the first `Case` header.

**MS-VBAL §5.4.2.10** says "the select-expression is immediately evaluated": the selector is evaluated once, ahead
of every case-clause. RD-VBA passes the evaluated selector to every clause comparison, in accordance with this.

### Case Header

The executor dispatches `ConditionalBranch` separately for a `Case` header: a `Case` header has
[Instruction](../api/RDCore.SDK.Semantics.Instructions.Instruction.html)`.Matching` set instead of a Boolean
condition.

1. Read the enclosing `Select`'s stored selector, through the header's `Matching` offset.
2. If the selector is a `Variant` that holds `Null`, go directly to the header's `Else` offset without evaluating any
   range clause. Every `Case` header does the same, so control reaches `Case Else`.
3. Otherwise, match the header's range clauses against the selector, as a comparison or logical expression, with
   the value of each clause converted to the declared type of the selector (see
   [The Value of a Clause Is Converted to the Selector's Type](#the-value-of-a-clause-is-converted-to-the-selectors-type)).
   A `Variant` selector is compared as the `Variant` it is.
4. On a match, fall through into the branch's body. Otherwise, go to the header's `Else` offset: the next header in
   the chain, the first instruction of the `Case Else` body, or right past the whole construct.
5. After the branch's body runs, the synthesized trailing `Jump` goes right past the whole construct.

**MS-VBAL §5.4.2.10**: "If select-expression is the data value Null, only the case-else-clause is executed". A
`Null` can reach `Select Case` only through a `Variant` selector: a local declared `Long` can never hold `Null`.

### Range Clause Matching

A `Case` header's range clauses are matched as a comparison or logical expression, the way
**MS-VBAL §5.4.2.10** phrases its own runtime semantics:

|Range clause|Example|Matched as|
|---|---|---|
|Value|`Case 5`|`selector = 5`|
|Comparison|`Case Is > 5`|`selector > 5`. The clause's own, already-normalized comparison-operator token selects the relational operator strategy directly.|
|`To`|`Case 1 To 10`|`(selector >= 1) And (selector <= 10)`|

Each comparison is evaluated through the same operator strategies every other expression uses
(`BinaryRelationalOperatorRuntimeSemantics`, `BinaryAndLogicalOperatorRuntimeSemantics`;
[**RD-VBAL §5.6.9.5** Relational Operators](rd-vbal.5.6.9.5.relational-operators.md),
[**RD-VBAL §5.6.9.8** Logical Operators](rd-vbal.5.6.9.8.logical-operators.md)).

The already-evaluated selector is passed to each clause comparison as an operand value. It is not re-evaluated per
clause.

### The Value of a Clause Is Converted to the Selector's Type

Before it is compared, the value of each range clause (each end of a `To` clause) is let-coerced to the declared type of
the selector, and the two are compared in that type. This is what MS-VBA does (Excel, `oracle\s46` F15a-g and
`oracle\s48`). Neither **MS-VBAL §5.4.2.10** nor Microsoft Learn's `Select Case` page says so.

|Selector|Clause|MS-VBA, and RD-VBA|The expression of the two|
|---|---|---|---|
|`n As Long` = `2`|`Case 1.7`|matches: `1.7` is the `Long` `2`|`n = 1.7` is `False`|
|`CInt(5)`|`Case Is < 5.4`|no match: `5.4` is the `Integer` `5`|`5 < 5.4` is `True`|
|`b As Boolean` = `False`|`Case Is < 1`|no match: `1` is `True`, and `False < True` is `False`|`b < 1` is `True`|
|`s As String` = `"1.0"`|`Case 1`|no match: `1` is `"1"`|`s = 1` is `True`|
|`s As String` = `"10"`|`Case Is < 5`|matches: `"10" < "5"` as text|`s < 5` is `False`|
|`n As Long` = `2`|`Case 1.5 To 2.4`|matches: both ends are the `Long` `2`|`False`|
|an `Enum` value of `5`|`Case 4.6`|matches: an `Enum` is a `Long`|`False`|

- The declared type is the selector expression's: `Select Case n * 1` with `n As Long` converts to `Long`, and
  `Select Case n + 0.5` to `Double`.
- A fixed-length `String` selector converts to `String`: `Case "ab"` does not match a `String * 5` holding `"ab"`,
  which is `"ab   "`.
- A `Single` or `Double` selector converts a value too: with `s As Single` = `0.1`, `Case 0.1` matches, and with
  `d As Double` = `0.1`, `Case CSng(0.1)` does not (`oracle\s48` M01-M07). An integer is the exception: a `Single`
  selector compares it at its exact value, as MS-VBA loads it on the x87 without rounding it to a `Single` (see
  [**RD-VBAL §5.6.9.3**](rd-vbal.5.6.9.3.arithmetic-operators.md)). With `s` = `16777216`, `Case 16777217` does not
  match, and `Case Is < 16777217` does (`oracle\s48` M02, `oracle\s42` D22).
- A value the conversion cannot make raises the conversion's error, at the `Case`: `Case "abc"` raises 13 for a
  `Long` selector, `Case 40000` raises 6 for an `Integer`, `Case Null` raises 94. Under `On Error Resume Next`, execution
  goes on in that `Case`'s body, as it does in MS-VBA.
- A selector declared `Variant` is compared with the value as it is, as in an expression: with `v` holding the `Long`
  `2`, `Case 1.7` does not match, and with `h` holding `100`, `Case "100"` matches and `Case Is > "9"` does not (a
  `Variant` number compared with a `String` is compared as text; [**RD-VBAL §5.6.9.5**](rd-vbal.5.6.9.5.relational-operators.md)).

**MS-VBAL §5.4.2.10** says only that a single value "is compared with the value of select-expression", so `Case 1.7`
has nothing to depart from. For the two other forms it states the expression that is evaluated:
`select-expression comparison-operator expression` for `Case Is`, and an `And` of two such comparisons for `To`. Where
the conversion makes a `Case Is` clause a different comparison from that expression, RDCore.Diagnostics reports
[`RDC11014`](../diagnostics/rdc11014.md), a warning that stands in for the comparison the specification states.

The runtime states it as a `CaseClauseStatementFact` with `CaseClauseSemanticFlags.ComparedInSelectorType`: the
conversion is stated when the relational operator's effective type for the two
([**RD-VBAL §5.6.9.5**](rd-vbal.5.6.9.5.relational-operators.md)) is not the selector's type. With `n As Long`,
`Case Is < 5` is not stated: an `Integer` compared with a `Long` is compared as a `Long` either way.

## Implementation

- `ProcedureExecutor` dispatches `Select`, and the `ConditionalBranch` of a `Case` header.
- The selector is stored through `CallStackFrame.SetBlockState` and read back through
  [ICallStackFrame](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html)`.TryGetBlockState`, keyed by
  the opener's offset. A single block-state value is enough for the selector. There is no separate closer
  instruction to pop the stored state on exit
  ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).
- A `Case` header's `Matching` is the same field a `ForNext` instruction uses for its back-reference to its opener
  ([**RD-VBAL §5.4.2.3** For Statement](rd-vbal.5.4.2.3.for-statement.md)).
- `Select Case` needs no synthesized closer: leaving the last branch, or `Case Else`, already reaches the offset
  that the construct's own `End`/`Else` chaining designates
  ([**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)).
- `ProcedureExecutor.ExecuteCaseHeader`'s `Null`-selector short-circuit unwraps a `Variant` selector before testing
  it for `Null`. The clauses are matched against the selector as it was evaluated, the `Variant` included.
- `CaseMatchEvaluator` converts the value of a clause to the selector's declared type through the
  `ILetCoercionRuntimeSemanticsProvider`, with `ConversionSite.CaseTest`. A selector evaluated on payloads (IR lowering
  S1.6) is converted to the payload's type when the conversion is exact; anything else takes the path above. A
  selector declared `Variant` is never evaluated on payloads, since the payload would be of the `Variant`'s subtype.
- `RDCore.Runtime.Execution.CaseMatchEvaluator` matches the range clauses against the selector. It calls the specific
  operator strategy directly rather than going through `OperatorRuntimeSemanticsProvider`'s token dispatch, because
  the range clause already determines the operator strategy.
- `CaseMatchEvaluator` passes the clause's own operand expression as the location-bearing node of each operator
  call, never a fabricated node standing in for the already-evaluated selector.
- This relies on the node parameter of `OperatorRuntimeSemantics<TContext,TFlags>` being typed
  [ExpressionNode](../api/RDCore.SDK.Model.AST.Abstract.ExpressionNode.html)
  ([**RD-VBAL §5.6.9.2** Simple Data Operators](rd-vbal.5.6.9.2.simple-data-operators.md)).

---
> ⏮️ [**RD-VBAL §5.4.2.9** Single-line If Statement](rd-vbal.5.4.2.9.single-line-if-statement.md) | ⏭️ [**RD-VBAL §5.4.2.11** Stop Statement](rd-vbal.5.4.2.11.stop-statement.md)
