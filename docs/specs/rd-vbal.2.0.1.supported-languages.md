# 2.0.1 Supported Languages

An RD-VBA _environment host_ may configure language-level _restrictions_ or _extensions_, depending on the _capabilities_ of the _host application_.

A language is identified by a _language code_. The following list is _prioritized_:

|Language code|Definition|
|---|---|
|`VBA`|The _Visual Basic for Applications_ language as per the **MS-VBAL** language specification.|
|`VB6`|Largely the same language definition as `VBA`, without the restrictions around attribute semantics, and with a limited set of additional semantics.|
|`VBX`|_Extended RD-VBA_. An _environment host_ that signals support for this language code may support semantics that would be _illegal_ in `VB6` or `VBA`.|
|`VBS`|A _diminished_ language specification that removes `Option Explicit` and _declared types_, forcing the use of _duck-typing_ using implicit `Variant` declarations.|
|`BASIC`|A _diminished_ language specification that removes _procedure scopes_ (see [BASIC](#basic) below).|

`VBA` is deemed a subset of the `VB6` language (see [**RD-VBAL §3.1.1** Attributes](rd-vbal.3.1.1.attributes.md)).

The list is not intended to be exhaustive: additional _dialects_ may be supported by different RD-VBA _hosts_.

## BASIC

The `BASIC` language code removes _procedure scopes_, and restricts the language as follows:

|Construct|`BASIC` restriction|
|---|---|
|Comments|`REM` is the only comment form. This makes _annotations_ unavailable.|
|Control flow|_Line numbers_ and `GoSub`/`Return` are required for control flow.|
|Loops|`Do...Loop` and `Do...While` constructs are undefined, forcing the use of `While...Wend` constructs.|

This list of restrictions is not complete.

## Scope of the SDK

> The _scope_ of the **RDCore SDK** minimally covers `VBA`, _then_ `VB6`, _then_ `VBX`, and so on.

The LSP paradigm shift, where an RD-VBA project physically exists in the file system (see [**RD-VBAL §2.0** RD-VBA Computational Environment](rd-vbal.2.0.computational-environment.md)), alone brings RD-VBA much closer to how VB6 already works.

---
> ⏮️ [**RD-VBAL §2.0** RD-VBA Computational Environment](rd-vbal.2.0.computational-environment.md) | ⏭️ [**RD-VBAL §2.0.2** Client/Server Capabilities](rd-vbal.2.0.2.client-server-capabilities.md)
