# RDCore™

[Français](./README.md)

## Before we begin.

> New here? Rubberduck was always an open-source initiative.  
> **RDCore honors it with an Open-Core formula**. See [rubberduckvba.ca](https://rubberduckvba.ca) for more information.

This repository contains different projects producing different libraries and executables.

**GPLv3-licensed code depends on MIT-licensed code**, and never the other way around; there's a clear _process boundary_ between all components.

- ⚖️ **MIT** projects belong in two categories:
   - 👉 The _LSP and RD-VBA language core_ (SDK).
   - 🧩 Extensions or otherwise _terminal_ (non-library) projects.
- ⚖️ **GPLv3** projects are protected and _unless explicitly authorized in writing by **9562-7303 Québec inc.**_ (through a commercial agreement), any derived work must be released alongside its source code and licensed under GPLv3.

This arrangement protects both the legacy and current contributors while enabling the future: **the RDCore runtime implementation shall remain in the hands of its open-source community**.

VIVAT CUCUMIS™

---

## Applications

The repository consists of multiple LSP client/server applications:

- **RDCore.LanguageServer** builds `RDCore.LanguageServer.exe`, the component responsible for a _project workspace_ and the back-end for all the LSP 3.17 IDE features, from completion lists to refacorings. You do not need to start a LSP Server app yourself: per the protocol, the LSP Client starts it for you.
- **RDCore.Diagnostics** builds `RDCore.Diagnostics.exe`, a satellite LSP server owned by a **RDCore.LanguageServer** instance, responsible for analyzing the semantic context of everything coming its way.
- **RDCore.Parsing** builds `RDCore.Parsing.exe`, a satellite LSP server owned by a **RDCore.LanguageServer** instance, responsible for the _tokenization_ and _parsing_ of source files into _abstract syntax trees_, made up of nodes defined in the language core SDK library.
- **RDCore.Runtime** builds `RDCore.Runtime.exe`, a satellite LSP server owned by a **RDCore.LanguageServer** instance, holds the concrete implementations that are key components to the interpreter and run-time memory management semantics: **keeping these bits out of MIT territory ensures RD-VBA remains free and open-source for everyone**.
- **RDCore.CLI** builds `rdc.exe`, a console application that dogfoods the SDK and implements a lightweight LSP client.

..and liraries:

- **RDCore.SDK** is RD-VBA in a box: it models, encapsulates, and packages the entire type system and static and run-time semantics of the language into a single, fully documented library.
- **RDCore.Tests** holds the unit test coverage for the entire language core and SDK.


The "language core" refers to a subset of namespaces in the SDK library that define RD-VBA as a language; the SDK itself is wider than the language core, it also defines everything any **RDCore** extension needs to hit the ground running and focus on what it wants to specifically look at.

![RDCore solution projects](./assets/RDCore-solution.png)