# 5.4.3.7 RSet Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.3.7** RSet Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/beccdd43-9dad-4bcf-b063-e542869917a1).

`RSet` (**MS-VBAL §5.4.3.7**) and `LSet`
([**MS-VBAL §5.4.3.6** LSet Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6964d2a4-b3e3-497b-bb80-8bc98f0edab9);
[**RD-VBAL §5.4.3.6** LSet Statement](rd-vbal.5.4.3.6.lset-statement.md)) are implemented.

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[AssignmentStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.AssignmentStatementNode.html) (`Kind`: `RSet`)|`Simple`|The same node shape as `Let`, `Set` and `LSet`.|

See [**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md).

## Runtime Semantics

`RSet` fits a value into the width the target already has, like `LSet`, but right-aligned. It takes the target's
width from the target's current value, not from its declared type. Taking the width from the current value makes
`RSet` meaningful on a variable-length `String`, and harmless on one.

`RSet` and `LSet` differ only in which end pads, and both truncate from the same end:

|Value, compared with the target's width|`RSet`|
|---|---|
|Shorter|Right-aligned: padded at the start.|
|Longer|Truncated from the same end as `LSet`.|

`RSet` has no UDT form: unlike `LSet`, it does not copy one UDT over another. See
[**RD-VBAL §5.4.3.6** LSet Statement](rd-vbal.5.4.3.6.lset-statement.md).

## Implementation

`RDCore.Runtime.Semantics.Statements.StatementRuntimeSemanticsProvider` dispatches an `AssignmentStatementNode`
whose `Kind` is `LSet` or `RSet`; see [**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md).
