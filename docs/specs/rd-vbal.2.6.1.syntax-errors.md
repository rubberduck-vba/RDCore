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

An argument list that ends with an omitted argument - `Total(1, )`, `Report 1,` - is a syntax error (`VBC00001`), at the omitted argument, for an `Optional` parameter too: **MS-VBAL §5.6.13.1** makes the last positional argument of a list a required one, and MS-VBA refuses it (recorded in Excel). The RD-VBA grammar reads the list, and the declaration pass refuses it. An omitted argument before another is valid (`Total(1, , 3)`), and so is a list of no argument at all (`Total()`).

A logical line joins at most 25 physical lines: 24 line-continuations in a row, and the 25th is a syntax error (`VBC00001`), "Too many line continuations", at its underscore, once for the line. **MS-VBAL §3.2.1** leaves the length of a line to the implementation, and this is MS-VBA's limit (MS Learn, and recorded in Excel). Two logical lines of 20 line-continuations each are valid: the limit is of one line.

## Conditional Compilation

A directive is a logical line (**MS-VBAL §3.2.2**, §3.4): the physical lines a line-continuation joins to a line that starts with `#` are the directive's, and are
no code of the module. `#Const Debugging = _` followed by `    1` is one `#Const`, and so is a `#If` or a `#ElseIf`, or a comment after a directive, that
continues over lines, and the `#` of a directive may stand apart from its keyword (`#  If`, `# _` followed by `If 1 Then`). A line that starts with `#` and
continues another line is not a directive: `d = _` followed by `    #1/2/2000#` assigns a date literal. **MS-VBA** reads them so (recorded in Excel).

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
|[`VBC00056`](../diagnostics/vbc00056.md)|An #ElseIf, #Else, or #EndIf must be preceded by an #If clause|a `#End If` ends no `#If` block|
|[`VBC00057`](../diagnostics/vbc00057.md)|An #Else clause must be preceded by an #If|a `#Else` belongs to no block, or is the second of its block|
|[`VBC00058`](../diagnostics/vbc00058.md)|An #ElseIf must be preceded by an #If or #ElseIf and followed by an #Else or #EndIf|a `#ElseIf` belongs to no block, or comes after its `#Else`|
|[`VBC00059`](../diagnostics/vbc00059.md)|You must terminate the #If block with an #EndIf|a `#If` block is not ended|

## Directives That Are Wrong

A directive is checked on its own logical line against the rule of the RD-VBA grammar for it. One that is not well formed is no directive, as in
**MS-VBA** (recorded in Excel): an `#If` with no `Then`, one whose expression is no expression, one with code after its `Then`, an unknown keyword (`#Iff`),
and an `#End If` with text after it, a comment written with `Rem` too. An `#End If` after such an `#If` ends no block, and so is `VBC00056`; a block whose
`#End If` is not well formed is not ended, and so is `VBC00059`. `#EndIf`, written as one word, is an `#End If`.

No whitespace is required around the condition of an `#If` or an `#ElseIf`, where a parenthesis separates it from the keywords: `#If(VBA7) Then`,
`#If (Win64)Then` and `#ElseIf(Mac) Then` are well formed, as in **MS-VBA**. A `#Const` name is any identifier, an accented one too (`#Const Été = 1`).

A directive line that is not well formed is the syntax error `VBC00001`, with **MS-VBA**'s message, at the token **MS-VBA** puts the caret on:

|Directive|Message|At|
|---|---|---|
|`#If VBA7`|Expected: Then|the end of the line|
|`#If VBA7 Foo Then`|Expected: Then|`Foo`|
|`#If X = Then`|Expected: expression|`Then`|
|`#If VBA7 Then Private Const A = 1`|Expected: end of statement|`Private`|
|`#Else junk`, `#End If junk`|Expected: end of statement|`junk`|
|`#Iff X Then`, `#`|Expected: If or Else or ElseIf or End or EndIf or Const|the word after `#`, or where it would be|
|`#Const X`, `#Const X As Long = 1`|Expected: =|the end of the line, `As`|
|`#Const = 1`|Expected: identifier|`=`|
|`#Const X =`|Expected: expression|the end of the line|
|`#Const X = 1 junk`|Expected: end of statement|`junk`|

**MS-VBA** reports the first error of a module, and finds the errors of the blocks' structure (`VBC00056`–`VBC00059`) before those of the lines; RD-VBA
reports every one of them. **MS-VBA** puts the caret of `VBC00059` on the module's last line, and RD-VBA reports it at the `#If` that is not ended.

---
> ⏮️ [**RD-VBAL §2.6** Diagnostics](rd-vbal.2.6.diagnostics.md) | ⏭️ [**RD-VBAL §2.6.2** Semantic Compilation Errors](rd-vbal.2.6.2.semantic-compilation-errors.md)
