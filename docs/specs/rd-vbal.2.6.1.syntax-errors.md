# 2.6.1 Syntax Errors

A **syntax error** is raised while the parser traverses the *concrete syntax tree* (CST). It is a token the grammar cannot place.

|||
|---|---|
|Code family|`VBC`, range `VBC00001`–`VBC00999`|
|Title|_Syntax error_|
|Raised by|the parser (concrete syntax tree)|
|Source metadata|[VBSyntaxErrorInfo](../api/RDCore.SDK.Model.Errors.VBSyntaxErrorInfo.html); its `ErrorId` is a [VBCompileErrorId](../api/RDCore.SDK.Model.Errors.VBCompileErrorId.html)|
|Severity|`Error`|
|Detail|the faulted token and its expected role, on `Diagnostic.data`|

## Syntax and Semantic Compilation Errors

MS-VBAL does not distinguish a compile-time error raised in CST semantics from one raised in AST semantics. RDCore splits CST-semantics (syntax) errors and AST-semantics ([semantic compilation](rd-vbal.2.6.2.semantic-compilation-errors.md)) errors by numeric range only.

Every `VBCompileErrorId` value below 9300 belongs to the parser; `[9300..]` is reserved for semantic compilation errors. Errors issued from the parser are normally syntax errors with a `VBCompileErrorId` value between 42 and 999; see [**RD-VBAL §1.1.4** Core Diagnostics](rd-vbal.1.1.4.core-diagnostics.md).

Conditions that need symbol information (an undefined name, a duplicate declaration) are not syntax errors. They are reported later, as semantic compilation errors.

## Fallback and Dedicated Codes

`VBC00001` is the general syntax-error fallback code: when the input is invalid and no more specific code applies, the parser reports `VBC00001`.

The parser narrows its output: recurring syntax-error shapes are promoted to a dedicated code in the `VBC00042`–`VBC00999` range.

A numeric literal whose value does not fit its type is the `NumericLiteralOverflow` syntax error (`VBC00042`), located at the literal. This includes a literal whose value does not fit its suffix-forced type, a floating-point literal that overflows to infinity, and an unsuffixed radix literal beyond 32 bits; see [**RD-VBAL §3.2.0** Literal Expressions](rd-vbal.3.2.0.literals.md).

A procedure whose blocks do not match - a `Next` with no `For`, a block `If` with no `End If`, and so on - is the syntax error MS-VBA names for it (`VBC00043`–`VBC00053`), at the statement that does not fit, rather than what the grammar expected where it could no longer parse.

## Conditional Compilation

> [!NOTE]
> **Not implemented.** A `#If` that splits a single statement across conditional-compilation branches (`#If` / `#Else` / `#End If`) is unparseable by the RD-VBA grammar. Such a statement reports located `VBC` diagnostics (`VBC00001`), which a client can anchor to a source range.

## Published Codes

|Code|Title|Condition|
|---|---|---|
|[`VBC00001`](../diagnostics/vbc00001.md)|Syntax error|a token the grammar cannot place|
|[`VBC00042`](../diagnostics/vbc00042.md)|Numeric literal overflow|a numeric literal outside the range of its type|
|[`VBC00043`](../diagnostics/vbc00043.md)|Next without For|a `Next` statement ends no `For` loop|
|[`VBC00044`](../diagnostics/vbc00044.md)|For without Next|a `For` loop is not ended before its procedure is|
|[`VBC00045`](../diagnostics/vbc00045.md)|Loop without Do|a `Loop` statement ends no `Do` loop|
|[`VBC00046`](../diagnostics/vbc00046.md)|Do without Loop|a `Do` loop is not ended before its procedure is|
|[`VBC00047`](../diagnostics/vbc00047.md)|End If without block If|an `End If` statement ends no block `If`|
|[`VBC00048`](../diagnostics/vbc00048.md)|Block If without End If|a block `If` is not ended before its procedure is|
|[`VBC00049`](../diagnostics/vbc00049.md)|Else without If|an `Else` statement belongs to no block `If`|
|[`VBC00050`](../diagnostics/vbc00050.md)|Case without Select Case|a `Case` clause belongs to no `Select Case` statement|
|[`VBC00051`](../diagnostics/vbc00051.md)|Select Case without End Select|a `Select Case` statement is not ended before its procedure is|
|[`VBC00052`](../diagnostics/vbc00052.md)|End With without With|an `End With` statement ends no `With` block|
|[`VBC00053`](../diagnostics/vbc00053.md)|Expected End With|a `With` block is not ended before its procedure is|
|[`VBC00054`](../diagnostics/vbc00054.md)|Invalid outside procedure|a statement that runs is written in the declarations section of a module|
|[`VBC00055`](../diagnostics/vbc00055.md)|Only comments may appear after End Sub, End Function, or End Property|a declaration or a statement is written after the first procedure of a module|

---
> ⏮️ [**RD-VBAL §2.6** Diagnostics](rd-vbal.2.6.diagnostics.md) | ⏭️ [**RD-VBAL §2.6.2** Semantic Compilation Errors](rd-vbal.2.6.2.semantic-compilation-errors.md)
