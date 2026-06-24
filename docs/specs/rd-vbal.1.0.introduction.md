# 1.0 Introduction

This specification describes the **RDCore Language Platform and SDK** which includes an _implementation of the VBA programming language_ herein described as **RD-VBA**, *derivative* of the **MS-VBAL** specification for **Microsoft Visual Basic for Applications** (MS-VBA) but **entirely independent** from (but striving to achieve _and maintain_ full compatibility with) its _historical host environment_.

# 1.0.1 RDCore

**RDCore**™ is an actively evolving _Language Server_ (LSP) platform that is currently a **work in progress**. Ultimately, the RDCore deliverables are:

- 🎯 **rdc.exe**: a configurable and extensible RD-VBA _environment host_ and LSP client CLI application;
- 🎯 **RDCore.LanguageServer.exe**: the platform's "orchestrator" LSP server application;
- 🎯 **RDCore.Parser.exe**: the platform's parser is a satellite LSP server application owned and coordinated by the main language server;
- 🎯 **RDCore.Diagnostics.exe**: a core platform extension asynchronously issuing _diagnostics_ to the main language server;
- 👉 **RDCore.Runtime.dll**: a library containing an implementation for all the RD-VBA runtime semantics and mechanics, _including an implementation of the VBA Standard Library_;
- 🧩 **RDCore.SDK.dll**: a library exposing the RDCore abstractions and encapsulating the base RD-VBA _language core_ implementation.


# 1.0.2 RD-VBA

The implementation of the platform's _language core_ is a **work in progress**. Ultimately, RD-VBA:

- 🎯 **aims for strict compliance with the MS-VBAL specifications**, ensuring behavioral compatibility with existing VBA semantics;
- 🧩 **elevates VBA into a modern, extensible, _and fully open-sourced_ language platform** separating the language definition from its original 1993 implementation;
- 👀 **makes implicit language behavior explicit**, exposing semantic rules, evaluation steps, call stacks, and error conditions as _observable facts_.

---

## In this section
- [**RD-VBAL§1.1** Philosophy](./rd-vbal.1.1.philosophy.html)

> ⏭️ [**RD-VBAL §2.0** Computational Environment](./rd-vbal.2.0.computational-environment.html)  

