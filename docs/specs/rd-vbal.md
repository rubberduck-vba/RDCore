# [RD-VBAL]: VBA Language Platform Specification
---

## Table of Contents

- 1. [Introduction](rd-vbal.1.0.introduction.html)
  - 1.1. [Philosophy](rd-vbal.1.1.philosophy.html)
- 2. [RD-VBA Computational Environment](rd-vbal.2.0.computational-environment.html)
  - 2.1. [Implicit Storage](rd-vbal.2.1.implicit-storage.html)
  - 2.2. [Project Structure](rd-vbal.2.2.rdproj-structure.html)
  - 2.3. [Application Host](rd-vbal.2.3.application-host.html)
  - 2.4. [Static Types](rd-vbal.2.4.static-types.html)
  - 2.5. [Runtime Values](rd-vbal.2.5.runtime-values.html)
- 3. [Abstract Syntax Tree](rd-vbal.3.0.syntax-tree.html)
  - 3.1. [Attributes and Directives](rd-vbal.3.1.attributes-directives.html)
  - 3.2. [Literal Expressions](rd-vbal.3.2.literals.html)
  - 3.3. [Operators](rd-vbal.3.3.0.operators.html)
  - 3.4. [Statements]🚧
  - 3.5. [Instructions]🚧
- 4. [Program Structure](rd-vbal.4.0.program-structure.html)
- 5. [Semantics](rd-vbal.5.0.semantics.html)
- 6. [Standard Library](rd-vbal.6.0.standard-library.html)

> [!NOTE]
> **RD-VBA** is an implementation of the **MS-VBAL specification** that is independent from its historical **MS-VBA** runtime host. **RD-VBAL** is the name of the specification/documentation of the _language server platform_, which _includes_ the **RD-VBA** _language core_ but is wider than the sole language specification.
> **The formalization of _RD-VBAL_ is a work in progress**.  

This platform specification presents a similar _technical prose_ style as its inspirational _Open Spec_ source material.

---

## Intellectual Property Rights Notice for Open Specifications Documentation

> [!IMPORTANT]
> **This documentation IS NOT A REVISION of the MS-VBAL specification**.

The publisher of the **RDCore** platform project and of _this present documentation_, is claiming the rights described in the following paragraph of the _Intellectual Property Rights Notice for Open Specifications Documentation_ section (emphasis added):

> **Copyrights**. This documentation [MS-VBAL] is covered by Microsoft copyrights. Regardless of any other terms that are contained in the terms of use for the Microsoft website that hosts this documentation, you can **make copies of it in order to develop implementations of the technologies that are described** in this documentation [MS-VBAL] and can distribute portions of it in your implementations that use these technologies **or in your documentation as necessary to properly document the implementation**. **You can also distribute in your implementation**, with or without modification, any schemas, IDLs, or code samples that are included in the documentation. **This permission also applies to** any documents that are referenced in the Open Specifications documentation.
- [MS-VBAL: VBA Language Specification](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d5418146-0bd2-45eb-9c7a-fd9502722c74#intellectual-property-rights-notice-for-open-specifications-documentation)


✅ **Challenge: Accepted**.

---

## Revisions

|Date|Version|Description|
|---|---|---|
|2026-06-25|1.0|Initial public version|
| | | |

