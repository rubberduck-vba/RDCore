# RDCore Diagnostics

Every problem the RDCore platform reports carries a stable **code** and, through the LSP
`codeDescription` field, a link to its page here. See
[RD-VBAL §2.6 Diagnostics](../specs/rd-vbal.2.6.diagnostics.md) for the pull pipeline and the
definition of each code family.

|Family|Prefix|Raised by|
|---|---|---|
|Syntax errors|`VBC` (`VBC00001`–`VBC00999`)|the parser, walking the concrete syntax tree|
|Semantic compilation errors|`VBC` (`VBC09300`+)|the static semantics layer, walking the abstract syntax tree|
|Runtime errors|`VBR` / `VBA`|the runtime semantics layer / a workspace `Err.Raise`|
|Rubberduck Core diagnostics|`RDC`|the `RDCore.Diagnostics` analyzers|

## Stability

A code gets a page here **the moment the platform can emit it** — the documentation grows at the
same rate as the diagnostics. Once published, a code is **not renumbered and not retired**: a
workspace built against an older release must still resolve its diagnostic links. The *content* of a
page may evolve as the ideal set of codes is narrowed down; the code and its abstract meaning do not.

Each page describes the condition in the abstract. The specifics of a particular occurrence — which
token, which literal, which type — travel in the diagnostic's verbose detail, not in the code.

## Published codes

### Syntax errors

|Code|Condition|
|---|---|
|[VBC00001](vbc00001.md)|Syntax error — a token the grammar cannot place|
|[VBC00042](vbc00042.md)|Numeric literal overflow — a literal outside the range of its type|

---
> ⏭️ [**VBC00001** Syntax error](vbc00001.md)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](../specs/rd-vbal.md) | [SDK](/RDCore/api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)
