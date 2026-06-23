# Introduction

\[[EN](./introduction.html)\] | \[[FR](./introduction.fr.html)\]

**RDCore™** is an **open-source project** engineered and maintained by a private company, aiming to build a modern language platform for _Visual Basic for Applications_ (VBA).  
It ultimately provides a complete semantic model, runtime infrastructure, and extensible tooling surface to analyze, execute, and evolve VBA code **outside of its historical environment**.

## 🚀The idea

Think **Roslyn, but for VBA**.  

RDCore reimagines VBA as a first-class language platform:
- A detailed semantic model
- A runtime decoupled from legacy environments
- A modular architecture built around extensibility
- A foundation for tooling, analysis, and execution

RDCore aims to to provide a sustainable, extensible platform for understanding and evolving the language.

VBA is not just a legacy runtime - it is a _language specification_. RDCore simply treats it as such.


## Architecture

- **The RDCore.SDK library** is licensed under **⚖️MIT**;
- **Everything else** built around it is licensed under **⚖️GPLv3**.

![RDCore solution overview](images/solution-overview.png)

RDCore is composed of:
- **RDCore.SDK** (MIT) defines the _language core_: syntax, symbols, semantic model, type system, etc.
- **RDCore.Runtime** (GPLv3) implements SDK-defined abstractions around runtime semantics, the standard library, etc.
- **Hosts** (GPLv3) include a CLI client (rdc.exe), a LSP server, and other applications that orchestrate execution and interactions.


## ✨What RDCore can makes possible
- Perform deep semantic analysis of VBA code
- Execute VBA code outside the VBIDE
- Build language tooling via the _Language Server Protocol_ (LSP)
- Inspect runtime behavior and semantic facts
- Extend the platform with analyzers and plugins


## 📊Project Status
RDCore is currently in active pre-release development.
- Core architecture: ✅ stable
- Language SDK: ✅ largely defined
- Runtime: 🚧 implementation in progress
- Standard library: 🚧 partially defined
- Parser: 🚧 exists (barely)
- CLI host (rdc.exe): 🚧 exists (barely)
- Public contributions: ❌ not yet opened

---
 ℹ[Home](./index.html) | 🧩[Getting Started](./getting-started.html) | [RD-VBAL](./specs/rd-vbal.html) | [SDK](/api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) | 🌐[rubberduckvba.ca](https://rubberduckvba.ca)

---
