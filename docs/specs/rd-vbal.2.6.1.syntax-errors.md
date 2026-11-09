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

## A Module With a Syntax Error

A module that has a syntax error does not run, and neither does the rest of its project. A run is refused with its syntax errors before any of it runs, and a debugger writes them to its error output and ends with exit code 1. The statements around what the parser could not read do not run either: `Debug.Print 1 +` followed by `Debug.Print 2` prints nothing. This applies to every syntax error: `VBC00001` in each of its forms, a line continued past the limit included, `VBC00060` and `VBC00061`.

A syntax error in an excluded branch of a `#If` does not refuse the run: the branch is not compiled (**MS-VBAL §3.4**). The language server still reports it.

**RD-VBA differs from MS-VBA:** a block that an excluded branch opens and does not close refuses the run. In this procedure, the `If` is in an
excluded branch, and nothing closes it:

```vba
#If False Then
    If a Then
#End If
    Debug.Print "after"
End Sub
```

MS-VBA compiles the live code only, and runs it (observed in Excel). The parser reads every branch, so it reads the live statements after the branch into the `If`, and
reports the error where the `If`'s end was expected, at `End Sub`. RD-VBA refuses the run, with "A block opened in a #If branch is not closed in it."
The same goes for a `With`, a loop and a `Select Case`.

The language server goes on analyzing the module as it is written. Only the run is refused.

This includes a module the run does not use. The refusal gives each syntax error of such a module after the module's name, the modules in the order of
their names, before the errors of the module being run. A module edited to a syntax error while a program waits at a stop keeps the program from going
on, and what a debugger evaluates or runs at the stop is refused with the same errors. A module removed from the project does not refuse a run.

**RD-VBA differs from MS-VBA:** MS-VBA compiles only what a run needs (observed in Excel). With Compile On Demand on (the default), that is the
procedures the run reaches and the declarations section of every module: an error in a procedure the run never reaches does not stop the run, and an
error in a procedure it calls stops it at the call, after what ran before it. With Compile On Demand off, that is each whole module the run uses, so an
error in a module the run does not use does not stop the run. RD-VBA refuses the run in all of these cases, before any of it runs: it is stricter than
both modes for an error in a module the run does not use, and stricter than the default for an error in a procedure of a module the run uses.

## Fallback and Dedicated Codes

`VBC00001` is the general syntax-error fallback code: when the input is invalid and no more specific code applies, the parser reports `VBC00001`.

The parser narrows its output: recurring syntax-error shapes are promoted to a dedicated code in the `VBC00042`–`VBC00999` range.

A numeric literal whose value does not fit its type is the `NumericLiteralOverflow` syntax error (`VBC00042`), located at the literal. This includes a literal whose value does not fit its suffix-forced type, a floating-point literal that overflows to infinity, and an unsuffixed radix literal beyond 32 bits; see [**RD-VBAL §3.2.0** Literal Expressions](rd-vbal.3.2.0.literals.md).

A procedure whose blocks do not match - a `Next` with no `For`, a block `If` with no `End If`, and so on - is the syntax error MS-VBA names for it (`VBC00043`–`VBC00053`), at the statement that does not fit, rather than what the grammar expected where it could no longer parse.

A procedure with no `End Sub` (`End Function`, `End Property`) ends where the next procedure's header begins, its `Public`, `Private`, `Friend`, `Global` or `Static` included. It is a syntax error (`VBC00001`) at that header, and the next procedure is read as its own. A word that ends a block where no block around it ends, such as a `Loop` with no `Do` inside a `With`, is read as a statement of its own: the statements after it stay in the procedure, which still ends at its `End Sub`.

> [!NOTE]
> **Not implemented.** **MS-VBA** reports a procedure with no `End Sub` as "Expected End Sub". RD-VBA reports `VBC00001`.

An argument list that ends with an omitted argument - `Total(1, )`, `Report 1,` - is a syntax error (`VBC00001`), at the omitted argument, for an `Optional` parameter too: **MS-VBAL §5.6.13.1** makes the last positional argument of a list a required one, and MS-VBA refuses it (recorded in Excel). The RD-VBA grammar reads the list, and the declaration pass refuses it. An omitted argument before another is valid (`Total(1, , 3)`), and so is a list of no argument at all (`Total()`).

A logical line joins at most 25 physical lines: 24 line-continuations in a row, and the 25th is a syntax error (`VBC00001`), "Too many line continuations", at its underscore, once for the line. **MS-VBAL §3.2.1** leaves the length of a line to the implementation, and this is MS-VBA's limit (MS Learn, and recorded in Excel). Two logical lines of 20 line-continuations each are valid: the limit is of one line.

An expression is nested in at most 2,500 others: one nested deeper is `ExpressionNestedTooDeeply` (`VBC00060`), at the expression that passes the limit,
once for each expression of a statement or of a directive (`#If`, `#ElseIf`, `#Const`, **MS-VBAL §3.4**); the callee and the arguments of a call statement are one expression. An expression is nested in another when it is its operand, its argument, or what its parentheses hold: the first
term of a chain of 2,500 terms joined by `+` is nested in 2,499 additions, and `1` in 2,500 parentheses is nested in 2,500 expressions. It is also nested in
the member access it is the owner of (`a.b`, `a!b`), in the index expression it is the callee of (`a(1)`), and in a `New`, `TypeOf … Is` or `AddressOf`
expression: each of them holds what it is written around as deep as an operator holds its operand. So a fluent chain `b.Add(1).Add(2)…` costs two levels a
link, a member access and an index expression: `b` is nested in 2,500 expressions at 1,250 links, and 1,251 links are `VBC00060`. The arguments of a call
statement are nested in the call, with or without `Call`: `F 1, …` as `Call F(1, …)`. Without `Call`, the parentheses of `F (…)` are not the call's: they
group its one argument, which is a level of its own, so what they hold is nested as in `Call F((…))`, one level deeper than in `Call F(…)`. A named argument (`a:=`) and a `ByVal`
argument are nested in the call and in nothing more. `Debug.Print` takes an output list (**MS-VBAL §5.4.5.8.1**, the list of the `Print` statement of
§5.4.5.8), not an argument list: an item of it, and the expression of its `Spc` or `Tab` clause, are nested in nothing the statement writes around them, as
the expression of an assignment is not. **MS-VBAL** leaves the depth of an expression to the implementation.

A directive's expression is nested as a statement's is, but for its parentheses: the directive pass keeps no node for an expression in parentheses (a
`cc-expression` in parentheses is the expression it holds), so they are not counted, and `#If ((x)) Then` is as deep as `#If x Then`, however many
there are. A directive's expression past the limit has no value, and neither has a `#Const` set to one, nor a constant set to an expression that names
one: no branch of a `#If` that such a value decides is excluded, and the error is `VBC00060`, not one of conditional compilation.

An expression past the limit is kept as text, and isn't analyzed. What is nested in more than 2,500 expressions is an error node of the text it is written
as (`UnbuiltExpressionTriviaNode`), and the name of a member past the limit is kept with its member access: a name in that text is bound to nothing, and is
no reference of what it names. Where a name in that text could refer to a symbol, the references found of the symbol may be incomplete, and say so; a
rename of the symbol is refused, at that name, since it would leave the name there as it was. A variable used only in that text has no use that the
analysis knows of, and can be reported as unused. A module that has an expression past the limit does not run, and neither does the rest of its project:
a run is refused with this error, before any of it runs. The rest of the module is analyzed as it would be, and its text is kept as it is written: a
tool that writes the module back out, such as the indenter, writes the expression as it is.

**RD-VBA differs from MS-VBA:** MS-VBA runs a chain of 2,500 terms, 2,000 nested parentheses and 1,000 nested calls, and crashes the host past the
chain, with no error (observed in Excel: chains of 3,000, 3,500 and 4,000 terms). RD-VBA runs every depth MS-VBA was seen to run, and reports a syntax
error where MS-VBA crashes: an expression nested in more than 2,500 others.

Apart from the language's limit, the RD-VBA parse has one of its own: it reads a module whose rules nest at most **32,000** deep, and stops past it. The
module is then a syntax error (`VBC00001`), "This module is nested too deeply to be read", at its start, and nothing of it is analyzed. A syntax error
found before the parse stopped is reported with it. This is a limit of the implementation, and not of the language: `VBC00060` is the language's error of an
expression nested too deeply, located at it, with the rest of the module read as usual. The ceiling is more than twice as deep as the deepest expression
RD-VBA reads:

|What is nested|Rules a level|Nested deep enough to stop the parse|
|---|---|---|
|Parentheses, a `Spc(`, `Tab(` or `TypeOf` operand, a unary `-`, `Not`|1|about 32,000: far past `VBC00060`'s 2,500|
|A call, however it is written (`Abs(`, `o.F(`, `Call F(G(`, `F G(`, `F(ByVal`, `F(a:=`)|6|about 5,300: 2,500 nested calls are about 15,000 rules deep|
|A chain of operators (`1 + 1 + …`) or of argument lists (`a(1)(2)…`)|none|never, however long|
|A `For` loop|3|about 10,600 nested loops|
|A block `If`, a `With` block|4|about 8,000 nested blocks|
|A `Select Case`|5|about 6,400 nested statements|

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
> is unparseable by the RD-VBA grammar. Such a statement reports located `VBC` diagnostics (`VBC00001`), which a client can anchor to a source range, and
> its project does not run.

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
|[`VBC00060`](../diagnostics/vbc00060.md)|Expression nested too deeply|an expression is nested in more than 2,500 others|
|[`VBC00061`](../diagnostics/vbc00061.md)|Output items not separated|an output item is written right after another, with nothing between them ([**RD-VBAL §5.4.5.8.1**](rd-vbal.5.4.5.8.print-statement.md#54581-output-lists))|

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
