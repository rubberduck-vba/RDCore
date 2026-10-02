# RDCore Diagnostics

Every problem the RDCore platform reports carries a stable **code** and, through the LSP
`codeDescription` field, a link to its page here. See
[**RD-VBAL §2.6** Diagnostics](../specs/rd-vbal.2.6.diagnostics.md) for the definition of each code
family, and [**RD-VBAL §2.6.5** Diagnostics Pipeline](../specs/rd-vbal.2.6.5.diagnostics-pipeline.md)
for the pull pipeline.

|Family|Prefix|Raised by|
|---|---|---|
|[Syntax errors](../specs/rd-vbal.2.6.1.syntax-errors.md)|`VBC` (`VBC00001`–`VBC00999`)|the parser, walking the concrete syntax tree|
|[Semantic compilation errors](../specs/rd-vbal.2.6.2.semantic-compilation-errors.md)|`VBC` (`VBC09300`+)|the static semantics layer, walking the abstract syntax tree|
|[Runtime errors](../specs/rd-vbal.2.6.3.runtime-errors.md)|`VBR` / `VBA`|the runtime semantics layer (`VBR`) / a workspace `Err.Raise` (`VBA`)|
|[Rubberduck Core diagnostics](../specs/rd-vbal.2.6.4.rubberduck-core-diagnostics.md)|`RDC`|the `RDCore.Diagnostics` analyzers|

## Stability

A code has a page here **as soon as the platform can emit it**.

Once published, a code is **not renumbered and not retired**: a workspace built against an older
release must still resolve its diagnostic links. The *content* of a page may evolve as the ideal set
of codes is narrowed down; the code and its abstract meaning do not.

Each page describes the condition in the abstract. The specifics of a particular occurrence (which
token, which literal, which type) are carried in the diagnostic's verbose detail, not in the code.

## Published codes

### Syntax errors

|Code|Condition|
|---|---|
|[VBC00001](vbc00001.md)|Syntax error — a token the grammar cannot place|
|[VBC00042](vbc00042.md)|Numeric literal overflow — a literal outside the range of its type|

### Semantic compilation errors

|Code|Condition|
|---|---|
|[VBC09309](vbc09309.md)|Label not defined — a jump names a line label or line number the procedure does not define|
|[VBC09312](vbc09312.md)|Exit Do not within Do...Loop — an Exit Do that is not inside a Do loop|
|[VBC09313](vbc09313.md)|Exit For not within For...Next — an Exit For that is not inside a For or For Each loop|
|[VBC09314](vbc09314.md)|Exit Function not allowed in Sub or Property — an Exit Function in a Sub, or a Property Let or Set|
|[VBC09315](vbc09315.md)|Exit Property not allowed in Sub or Function — an Exit Property in a Sub or a Function|
|[VBC09319](vbc09319.md)|Duplicate label definition — a procedure defines the same line label or line number more than once|
|[VBC09320](vbc09320.md)|Inconsistent property accessors — a property's Get/Let/Set do not together describe one valid property|
|[VBC09321](vbc09321.md)|Argument required for Property Let or Property Set — a Let/Set declares no parameters at all|
|[VBC09322](vbc09322.md)|Event not defined — a RaiseEvent names an event the class module does not declare|
|[VBC09323](vbc09323.md)|Wrong number of arguments or invalid argument for event — a RaiseEvent's arguments do not match the event's parameters|
|[VBC09324](vbc09324.md)|Invalid type for WithEvents variable — not a specific class with events, or the class of its own module|
|[VBC09325](vbc09325.md)|Invalid event name — an Event declaration whose name has an underscore|
|[VBC09326](vbc09326.md)|Invalid event handler — a procedure named for an event is not a subroutine with a compatible parameter list|
|[VBC09327](vbc09327.md)|ByVal argument not allowed here — a ByVal argument in an argument list that is not an external procedure's|
|[VBC09328](vbc09328.md)|Invalid Implements directive — the class itself, a repeat, an underscore in a public member, or overlapping interface prefixes|
|[VBC09329](vbc09329.md)|Object module needs to implement all members of its interface — a public member of the interface has no implemented name declaration|
|[VBC09330](vbc09330.md)|Invalid implemented member — an implemented name declaration of another kind, parameters or type than its member|
|[VBC09331](vbc09331.md)|Sub or Function not defined — a statement or a call the language the code is written in does not have|
|[VBC09332](vbc09332.md)|Exit Sub not allowed in Function or Property — an Exit Sub in a Function or a property|
|[VBC09333](vbc09333.md)|Variable required — an expression that is not a variable is where a statement requires one|
|[VBC09334](vbc09334.md)|Access not valid for the file mode — the Access clause of an Open is not one its For mode allows|

### Rubberduck Core diagnostics

|Code|Condition|
|---|---|
|[RDC00101](rdc00101.md)|Implicit declarations enabled — a module that does not state Option Explicit|
|[RDC00302](rdc00302.md)|Obsolete Call statement — a call statement written with the Call keyword|

---
> ⏭️ [**VBC00001** Syntax error](vbc00001.md)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](../specs/rd-vbal.md) | [SDK](/RDCore/api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)
