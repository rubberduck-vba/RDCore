# RDCore™

[EN] | [FR](./README.md)

## Before we begin.

> 👋 New here? Rubberduck was always an open-source initiative. **RDCore honors it with an Open-Core formula**.  
> <small>See [rubberduckvba.ca](https://rubberduckvba.ca) for more information.</small>

This repository contains different projects **under active development** producing different libraries and executables, under a relatively simple licensing model:

- **The RDCore.SDK library** is licensed under **⚖️MIT**;
- **Everything else** built around it is licensed under **⚖️GPLv3**.

This arrangement protects both the legacy and current contributors while enabling the future: **The RDCore runtime implementation shall remain open-source**.

👉 We're building a solid _language core_ foundation here, but please note that at the moment the only deliverable is the [documentation site](https://rdcore-sdk.github.io).

---

# 1.0.1 RDCore

**RDCore**™ is an actively evolving _Language Server_ (LSP) platform that is currently a **work in progress**. Ultimately, the RDCore deliverables are:

- 🎯 **rdc.exe**: a configurable and extensible RD-VBA _environment host_ and LSP client CLI application;
- 🎯 **RDCore.LanguageServer.exe**: the platform's "orchestrator" LSP server application;
- 🎯 **RDCore.Parser.exe**: the platform's parser is a satellite LSP server application owned and coordinated by the main language server;
- 🎯 **RDCore.Diagnostics.exe**: a core platform extension asynchronously issuing _diagnostics_ to the main language server;
- 👉 **RDCore.Runtime.dll**: a library containing an implementation for all the RD-VBA runtime semantics and mechanics, _including an implementation of the VBA Standard Library_;
- 🧩 **RDCore.SDK.dll**: a library exposing the RDCore abstractions and encapsulating the base RD-VBA _language core_ implementation.


## ✨ What RDCore could make possible
- **Analyze VBA code** at depths only _LSP analyzers_ can reach
- **Execute** VBA code outside the VBIDE
- **Build dev tools** via the _Language Server Protocol_ (LSP)
- **Inspect runtime** behavior and semantic facts
- **Extend the platform** with analyzers and plugins


## 📊 Project Status
> [!NOTE]
> This section is kept up to date as implementation progresses.

RDCore is currently in active **pre-alpha** development - the **only deliverable for now** consists of its **specification** and **documentation**. 
- Core architecture: ✅ stable
- Language SDK: ✅ largely defined
- Runtime: 🚧 implementation in progress
- Standard library: 🚧 partially defined
- Parser: 🚧 exists (barely)
- CLI host (rdc.exe): 🚧 exists (barely)
- **Public contributions: ❌ not yet opened**


# 1.0.2 RD-VBA

The implementation of the platform's _language core_ is a **work in progress**. Ultimately, RD-VBA:

- 🎯 **aims for strict compliance with the MS-VBAL specifications**, ensuring behavioral compatibility with existing VBA semantics;
- 🧩 **elevates VBA into a modern, extensible, _and fully open-sourced_ language platform** separating the language definition from its original 1993 implementation;
- 👀 **makes implicit language behavior explicit**, exposing semantic rules, evaluation steps, call stacks, and error conditions as _observable facts_.

## Implementation Status
> [!NOTE]
> This section is kept up to date as implementation progresses.

- ✅ Static semantics IMPLEMENTED for all operators  
- ✅ Static semantics IMPLEMENTED for all let-coercions  
- ✅ Runtime semantics IMPLEMENTED for all operators  
- 🚧 Runtime semantics IN PROGRESS for let-coercions  
- 🎯 Runtime semantics TODO for all statements  
- 🎯 Runtime semantics TODO for the standard library  
- 🚧 Evaluation pipeline modelization IN PROGRESS  
- 🚧 Analysis pipeline modelization IN PROGRESS  
- 🚧 Execution pipeline modelization IN PROGRESS  

### Test Coverage
- 🧪 OVERALL test coverage (rdcore.sdk.dll): 17.4 %blocks; **15.0 %lines** | ⚠️ BELOW TARGET (>70%)

The current operator tests run the static semantics through a matrix of [VBIntrinsicType](https://rubberduck-vba.github.io/rdcore/api/RDCore.SDK.Model.Types.Abstract.VBIntrinsicType.html) that exercises most if not all _specified_  input combinations:

![operator static semantics tests](./docs/images/operator-static-semantic-tests.png)  

👉 Missing: tests for any _unspecified_ combinations (if any), and error conditions / type mismatch checks.

---
 V I V A T 🩷 C U C U M I S ™  
 [Home](https://rubberduck-vba.github.io/rdcore)  ℹ️[Introduction](https://rubberduck-vba.github.io/rdcore/introduction.html) | 🧩[Getting Started](https://rubberduck-vba.github.io/rdcore/getting-started.html) | 🎯[RD-VBAL](https://rubberduck-vba.github.io/rdcore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) | 🌐[rubberduckvba.ca](https://rubberduckvba.ca)

---

<p align="center">
<img alt="Logo™ 9562-7303 Québec inc." src="./assets/vector-ducky.svg" style="width:200px; margin-top:72px;" /><br/>
<small>© Copyright <strong>9562-7303 Québec inc.</strong> (2026)<br/>
<em>"Rubberduck" is used as a reference to the legacy open-source project <strong>the same way it has been used publicly since 2015</strong> and without any links or affiliation with any third-party trademark holders of a similar trademark in any jurdisdiction. The corporate logo (blue outline ducky logo), "RDCore", and "VIVAT CUCUMIS" are trademarks claimed by 9562-7303 Québec inc. (pending)
</small>
</p>
