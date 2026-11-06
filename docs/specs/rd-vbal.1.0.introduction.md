# 1.0 Introduction

This specification describes the **RDCore Language Platform and SDK**. The platform includes an implementation of the VBA programming language, herein called **RD-VBA**.

RD-VBA is derived from [**MS-VBAL**](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d5418146-0bd2-45eb-9c7a-fd9502722c74), the specification for **Microsoft Visual Basic for Applications** (**MS-VBA**). RD-VBA is entirely independent from the historical host environment of MS-VBA, and aims to achieve and maintain full compatibility with it.

## 1.0.1 RDCore

**RDCore**™ is a *Language Server* (LSP) platform. It is a work in progress, under active development.

Ultimately, the RDCore deliverables are the following six components:

|Deliverable|Kind|Description|
|---|---|---|
|`rdc.exe`|LSP client CLI application|A configurable and extensible RD-VBA *environment host*.|
|`RDCore.LanguageServer.exe`|LSP server application|The platform's *orchestrator* language server.|
|`RDCore.ParseServer.exe`|Satellite LSP server application|The platform's parser. It is owned and coordinated by the main language server, `RDCore.LanguageServer.exe`.|
|`RDCore.Diagnostics.exe`|Core platform extension|Issues *diagnostics* asynchronously to the main language server. See [**RD-VBAL §1.1.4** Core Diagnostics](rd-vbal.1.1.4.core-diagnostics.md).|
|`RDCore.Runtime.dll`|Library|An implementation of all the RD-VBA runtime semantics and mechanics, including an implementation of the VBA Standard Library.|
|`RDCore.SDK.dll`|Library|Exposes the RDCore abstractions, and encapsulates the base RD-VBA *language core* implementation.|

The RDCore platform shall provide, with the `rdc.exe` CLI client, the ability to *compose, host, analyze, run, and debug* any RD-VBA application. See [**RD-VBAL §2.0** RD-VBA Computational Environment](rd-vbal.2.0.computational-environment.md) and [**RD-VBAL §2.3** Application Host](rd-vbal.2.3.application-host.md).

A fully realized RDCore platform could run RD-VBA CI/CD pipelines, and integrate with enterprise software development lifecycles. See [**RD-VBAL §4.0** Program Structure and Organization](rd-vbal.4.0.program-structure.md).

## 1.0.2 RD-VBA

The implementation of the platform's *language core*, RD-VBA, is a work in progress. Ultimately, RD-VBA:

- **aims for strict compliance with the MS-VBAL specifications.** Strict compliance is intended to ensure behavioural compatibility with existing VBA semantics.
- **elevates VBA into a modern, extensible, and fully open-sourced language platform.** RD-VBA separates the VBA language definition from its original 1993 implementation.
- **makes implicit language behaviour explicit.** RD-VBA exposes semantic rules, evaluation steps, call stacks, and error conditions as *observable facts*.

The principles that govern these objectives are described in [**RD-VBAL §1.1** Design and Extension Philosophy](rd-vbal.1.1.philosophy.md).

---
> ⏮️ [**RD-VBAL** Table of Contents](rd-vbal.md) | ⏭️ [**RD-VBAL §1.1** Design and Extension Philosophy](rd-vbal.1.1.philosophy.md)
