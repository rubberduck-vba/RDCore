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

A procedure's header written once in each branch of a `#If` block, around one body and one `End`, is one procedure, which **MS-VBA** compiles with the
header of the live branch (recorded in Excel). The common case is the `PtrSafe` pattern:

```vba
#If VBA7 Then
Private Function Address(ByVal p As LongPtr) As LongPtr
#Else
Private Function Address(ByVal p As Long) As Long
#End If
    Address = p
End Function
```

How RD-VBA reads it:

- **One branch's header.** The parse is of every branch, whatever the constants are. It reads the branch whose header is of the kind of the statement
  that closes the procedure, or the first branch when none is.
- **The alternatives.** Each other branch is read on its own, and its header is an alternative of the procedure
  ([MemberDeclarationNode](../api/RDCore.SDK.Model.AST.Declarations.MemberDeclarationNode.html)`.HeaderAlternatives`).
- **The body.** What a branch writes after its header belongs to the body, in source order.
- **Before the header.** What a branch writes before its header - a `Declare`, a variable, a whole procedure - is the module's, where it is written, as
  in any `#If` block. A branch's header is the procedure it leaves open.
- **Which compiles.** It is decided by the environment host, which knows the constants: see below.
- **In MS-VBA:**
  - The live branch's header is the one compiled, and the live branch's statements are the ones run.
  - A live `Sub` header closed by `End Function` is "Expected End Sub".
  - A header written only in a dead branch leaves its body "Invalid outside procedure".

### The live declaration

The language server fuses a declaration written once in each branch of a `#If` block into one symbol. The symbol is the first branch's, with every site in
`Definitions` and what each other branch declares in `Alternatives`. The cases are:

- a procedure written whole in each branch;
- a procedure header split across branches;
- a `Declare` with and without `PtrSafe`;
- a field of a `Type` that each branch declares.

Each alternative travels beside the symbol to the environment host, and the module's directives travel with them (`DefineSymbolsParams.PrecompilerTriviaJson`).
The host evaluates the directives with the session's constants (`PrecompilerLiveBranchEvaluator`), as **MS-VBA** compiles a module (recorded in Excel):

|What the module writes|What the host defines|
|---|---|
|A declaration in each branch|The one of the live branch, its signature included; each site of `Definitions` marked live or dead.|
|A declaration in dead branches only|Nothing, nor anything its body declares: a call of it with an argument list is "Sub or Function not defined" (`VBC09331`), as in **MS-VBA**.|
|A procedure header in dead branches only, around a body that is not|"Invalid outside procedure" (`VBC00054`), at the first statement of the body, as in **MS-VBA**.|
|A field of a `Type` in each branch, or in a dead branch only|The type with the fields of its live branches.|
|A procedure written whole in each branch|The live branch's procedure, whose body is the live branch's: the dead copy is not loaded.|

> [!NOTE]
> **Not implemented.**
> - "Expected End Sub", which **MS-VBA** reports of a live header of another kind than its closing statement, is not reported yet.
> - The language server's own analysis - completion, diagnostics, navigation - still reads a fused symbol as its first branch declares it.

> [!NOTE]
> **Not implemented.** A `#If` that splits a block statement's header inside a body (an `If`, a `Select Case`, a loop or a `With` written once per branch)
> is unparseable by the RD-VBA grammar. Such a statement reports located `VBC` diagnostics (`VBC00001`), which a client can anchor to a source range.

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
