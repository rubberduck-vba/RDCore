# 5.4.1.1 Statement Labels

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.1.1** Statement Labels](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/825de02b-0e13-4783-8527-de14fbb7104f).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[LineLabelNode](../api/RDCore.SDK.Model.AST.Abstract.LineLabelNode.html)|None|A line label. Its name is a key of [InstructionList](../api/RDCore.SDK.Semantics.Instructions.InstructionList.html)`.Labels`.|
|[LineNumberNode](../api/RDCore.SDK.Model.AST.Abstract.LineNumberNode.html)|None|A line number. Its number is a key of `InstructionList.Labels`.|

Statement labels and line numbers are captured as separate nodes. A jump statement's target is an expression naming
or numbering a label, with no static link to the label node
([**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md)).

A label is defined at the start of a logical line (**MS-VBAL §5.4.1.1**'s `LINE-START`), and may be indented:
whitespace is no token of the syntactic grammar (**MS-VBAL §3.3**), and MS-VBA accepts an indented label. The VBE
moves a label to the first column; a module written in any other editor, or code typed at the BASIC prompt, may
not, and `    Retry:` is the label `Retry` all the same.

## Static Semantics

A procedure declaration must contain exactly one definition of each label value it declares
(**MS-VBAL §5.4.1.1**). A procedure that jumps to a label must contain exactly one definition of that label.

A label belongs to its procedure, not to the block it is written in. A label defined inside a nested block (`If`,
`Do`, `For`, `Select Case`, `With`), however deeply nested, is defined for the whole procedure.

Label names are looked up case-insensitively, like every VBA identifier. Two labels differing only by case, such as
`Top:` and `TOP:`, are the same label.

A label is never a variable. A jump target is looked up among the labels the procedure defines, not among the
symbols in scope.

|Condition|Diagnostic|
|---|---|
|A jump names a line label or line number the procedure does not define.|[VBC09309](../diagnostics/vbc09309.md): Label not defined|
|A label is defined more than once in a procedure, including two labels differing only by case.|[VBC09319](../diagnostics/vbc09319.md): Duplicate label definition|

For a repeated label definition, lowering keeps the first offset the label was defined at; every jump to the label
resolves against that first definition
([**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)).

## Runtime Semantics

A jump's target is resolved at lowering time, not at run time. `InstructionList.Labels` (`TryGetLabelOffset`) maps a
line label or line number name to its offset
([**RD-VBAL §3.5.1** InstructionList](rd-vbal.3.5.1.instructionlist.md)).

A label with nothing after it resolves to offset `Items.Length`. Execution there completes as if it had reached the
end of the procedure body.

A `GoTo` from anywhere in the procedure into the middle of a loop body or an `If` body resolves in the same way as any
other jump ([**RD-VBAL §5.4.2.12** GoTo Statement](rd-vbal.5.4.2.12.goto-statement.md)).

> Jumping into the middle of a loop body has a consequence for the loop's hidden per-activation state. A `Next`
> closer reached without its opener having run in the activation raises run-time error 92
> ([**RD-VBAL §5.4.2.3** For Statement](rd-vbal.5.4.2.3.for-statement.md),
> [**RD-VBAL §5.4.2.4** For Each Statement](rd-vbal.5.4.2.4.for-each-statement.md)).

A named label never sets `Erl`, in either `ErlLineNumbering` mode. Only a label spelled as decimal digits sets `Erl`
([**RD-VBAL §6.1.2.7** Information](rd-vbal.6.1.2.7.information.md)).

## Implementation

`InstructionListLowering` builds the label
table of an `InstructionList` and reports both
diagnostics ([**RD-VBAL §2.6.2** Semantic Compilation Errors](rd-vbal.2.6.2.semantic-compilation-errors.md)).
