# [RD-VBAL]: VBA Language Platform Specification

> [!NOTE]
> Cette section n'est disponible qu'en anglais.    
> _This section is only available in English_.

This specification describes the **RDCore Language Platform and SDK**; its inspirational source material is the [**MS-VBAL**](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d5418146-0bd2-45eb-9c7a-fd9502722c74) Open Specification. See [**RD-VBAL §1.0** Introduction](rd-vbal.1.0.introduction.md).

**RD-VBA** is an implementation of the **MS-VBAL specification** that is independent from its historical **MS-VBA** runtime host. **RD-VBAL** is the name of the specification and documentation of the _language server platform_. It _includes_ the **RD-VBA** _language core_, but covers more than the language specification alone.

**The formalization of _RD-VBAL_ is a work in progress**.  

This specification follows the technical prose style of its inspirational Open Spec source material.

---
## Table of Contents

- 1.0 [Introduction](rd-vbal.1.0.introduction.md)
  - 1.1 [Design and Extension Philosophy](rd-vbal.1.1.philosophy.md)
    - 1.1.1 [Platform Extensions](rd-vbal.1.1.1.platform-extensions.md)
    - 1.1.2 [Language Core Extensions](rd-vbal.1.1.2.language-core-extensions.md)
    - 1.1.3 [Core Semantic Flags](rd-vbal.1.1.3.core-semantic-flags.md)
    - 1.1.4 [Core Diagnostics](rd-vbal.1.1.4.core-diagnostics.md)
    - 1.1.5 [Extension Manifest](rd-vbal.1.1.5.extension-manifest.md)
    - 1.1.6 [Capabilities Provider](rd-vbal.1.1.6.capabilities-provider.md)
- 2.0 [RD-VBA Computational Environment](rd-vbal.2.0.computational-environment.md)
  - 2.0.1 [Supported Languages](rd-vbal.2.0.1.supported-languages.md)
  - 2.0.2 [Client/Server Capabilities](rd-vbal.2.0.2.client-server-capabilities.md)
  - 2.1 [Implicit Storage](rd-vbal.2.1.implicit-storage.md)
  - 2.2 [RDPROJ Structure](rd-vbal.2.2.rdproj-structure.md)
    - 2.2.1 [Conventions](rd-vbal.2.2.1.conventions.md)
    - 2.2.2 [WorkspaceFile](rd-vbal.2.2.2.workspacefile.md)
    - 2.2.3 [ProjectFile](rd-vbal.2.2.3.projectfile.md)
  - 2.3 [Application Host](rd-vbal.2.3.application-host.md)
    - 2.3.1 [Composition Root](rd-vbal.2.3.1.composition-root.md)
    - 2.3.2 [Mode / State](rd-vbal.2.3.2.mode-state.md)
  - 2.4 [Static Types](rd-vbal.2.4.static-types.md)
    - 2.4.1 [Intrinsic Types](rd-vbal.2.4.1.intrinsic-types.md)
    - 2.4.2 [Non-intrinsic Types](rd-vbal.2.4.2.non-intrinsic-types.md)
    - 2.4.3 [Meta and Advanced Types](rd-vbal.2.4.3.meta-and-advanced-types.md)
    - 2.4.4 [Deferred Types](rd-vbal.2.4.4.deferred-types.md)
  - 2.5 [Runtime Values](rd-vbal.2.5.runtime-values.md)
    - 2.5.1 [Runtime Entities](rd-vbal.2.5.1.runtime-entities.md)
    - 2.5.2 [VBTypedValue](rd-vbal.2.5.2.vbtypedvalue.md)
  - 2.6 [Diagnostics](rd-vbal.2.6.diagnostics.md)
    - 2.6.1 [Syntax Errors](rd-vbal.2.6.1.syntax-errors.md)
    - 2.6.2 [Semantic Compilation Errors](rd-vbal.2.6.2.semantic-compilation-errors.md)
    - 2.6.3 [Runtime Errors](rd-vbal.2.6.3.runtime-errors.md)
    - 2.6.4 [Rubberduck Core Diagnostics](rd-vbal.2.6.4.rubberduck-core-diagnostics.md)
    - 2.6.5 [Diagnostics Pipeline](rd-vbal.2.6.5.diagnostics-pipeline.md)
- 3.0 [Abstract Syntax Tree](rd-vbal.3.0.syntax-tree.md)
  - 3.0.1 [Token Semantics](rd-vbal.3.0.1.token-semantics.md)
  - 3.0.2 [Node Types](rd-vbal.3.0.2.node-types.md)
  - 3.0.3 [Binding Contexts](rd-vbal.3.0.3.binding-contexts.md)
  - 3.1 [Attributes and Directives](rd-vbal.3.1.attributes-directives.md)
    - 3.1.1 [Attributes](rd-vbal.3.1.1.attributes.md)
  - 3.2.0 [Literal Expressions](rd-vbal.3.2.0.literals.md)
  - 3.3.0 [Operator Expressions](rd-vbal.3.3.0.operators.md)
    - 3.3.1 [Unary Operators](rd-vbal.3.3.1.unary-operators.md)
    - 3.3.2 [Arithmetic Operators](rd-vbal.3.3.2.arithmetic-operators.md) (*reserved*)
    - 3.3.3 [Logical (Bitwise) Operators](rd-vbal.3.3.3.logical-operators.md) (*reserved*)
    - 3.3.4 [Relational (Comparison) Operators](rd-vbal.3.3.4.relational-operators.md) (*reserved*)
  - 3.4.0 [Statements](rd-vbal.3.4.0.statements.md)
    - 3.4.1 [Block Statements](rd-vbal.3.4.1.block-statements.md)
    - 3.4.2 [Simple Statements](rd-vbal.3.4.2.simple-statements.md)
    - 3.4.3 [File Statements](rd-vbal.3.4.3.file-statements.md)
  - 3.5.0 [Instructions](rd-vbal.3.5.0.instructions.md)
    - 3.5.1 [InstructionList](rd-vbal.3.5.1.instructionlist.md)
    - 3.5.2 [Instruction](rd-vbal.3.5.2.instruction.md)
    - 3.5.3 [Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)
    - 3.5.4 [Execution](rd-vbal.3.5.4.execution.md)
    - 3.5.5 [Placement and Licensing](rd-vbal.3.5.5.placement-and-licensing.md)
- 4.0 [Program Structure and Organization](rd-vbal.4.0.program-structure.md)
  - 4.1 [VBIDE Synchronization](rd-vbal.4.1.vbide-synchronization.md)
- 5.0 [Semantics](rd-vbal.5.0.semantics.md)
  - 5.1 [Module Body Structure](rd-vbal.5.1.module-body-structure.md) (*reserved*)
  - 5.2 [Module Declaration Section Structure](rd-vbal.5.2.module-declaration-section-structure.md) (*reserved*)
    - 5.2.1 [Option Directives](rd-vbal.5.2.1.option-directives.md)
    - 5.2.2 [Implicit Definition Directives](rd-vbal.5.2.2.implicit-definition-directives.md)
    - 5.2.3 [Module Declarations](rd-vbal.5.2.3.module-declarations.md)
    - 5.2.4 [Class Module Declarations](rd-vbal.5.2.4.class-module-declarations.md)
  - 5.3 [Module Code Section Structure](rd-vbal.5.3.module-code-section-structure.md) (*reserved*)
    - 5.3.1 [Procedure Declarations](rd-vbal.5.3.1.procedure-declarations.md) (*reserved*)
  - 5.4 [Procedure Bodies and Statements](rd-vbal.5.4.procedure-bodies-and-statements.md)
    - 5.4.1 [Statement Blocks](rd-vbal.5.4.1.statement-blocks.md) (*reserved*)
    - 5.4.2 [Control Statements](rd-vbal.5.4.2.control-statements.md)
    - 5.4.3 [Data Manipulation Statements](rd-vbal.5.4.3.data-manipulation-statements.md)
    - 5.4.4 [Error Handling Statements](rd-vbal.5.4.4.error-handling-statements.md)
    - 5.4.5 [File Statements](rd-vbal.5.4.5.file-statements.md)
    - 5.4.6 [Graphics Statements](rd-vbal.5.4.6.graphics-statements.md)
  - 5.5 [Implicit coercion](rd-vbal.5.5.implicit-coercion.md)
    - 5.5.1 [Let-coercion](rd-vbal.5.5.1.let-coercion.md)
    - 5.5.2 [Set-coercion](rd-vbal.5.5.2.set-coercion.md) (*reserved*)
  - 5.6 [Expressions](rd-vbal.5.6.expressions.md) (*reserved*)
    - 5.6.1 [Expression Classifications](rd-vbal.5.6.1.expression-classifications.md) (*reserved*)
    - 5.6.2 [Expression Evaluation](rd-vbal.5.6.2.expression-evaluation.md)
    - 5.6.3 [Member Resolution](rd-vbal.5.6.3.member-resolution.md) (*reserved*)
    - 5.6.4 [Expression Binding Contexts](rd-vbal.5.6.4.expression-binding-contexts.md) (*reserved*)
    - 5.6.5 [Literal Expressions](rd-vbal.5.6.5.literal-expressions.md) (*reserved*)
    - 5.6.6 [Parenthesized Expressions](rd-vbal.5.6.6.parenthesized-expressions.md)
    - 5.6.7 [TypeOf...Is Expressions](rd-vbal.5.6.7.typeof-is-expressions.md)
    - 5.6.8 [New Expressions](rd-vbal.5.6.8.new-expressions.md)
    - 5.6.9 [Operator Expressions](rd-vbal.5.6.9.operator-expressions.md)
    - 5.6.10 [Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md)
    - 5.6.11 [Instance Expressions](rd-vbal.5.6.11.instance-expressions.md)
    - 5.6.12 [Member Access Expressions](rd-vbal.5.6.12.member-access-expressions.md)
    - 5.6.13 [Index Expressions](rd-vbal.5.6.13.index-expressions.md)
    - 5.6.14 [Dictionary Access Expressions](rd-vbal.5.6.14.dictionary-access-expressions.md)
    - 5.6.15 [With Expressions](rd-vbal.5.6.15.with-expressions.md)
    - 5.6.16 [Constrained Expressions](rd-vbal.5.6.16.constrained-expressions.md)
- 6.0 [Standard Library](rd-vbal.6.0.standard-library.md)
  - 6.1 [VBA Project](rd-vbal.6.1.vba-project.md)
    - 6.1.1 [Predefined Enums](rd-vbal.6.1.1.predefined-enums.md)
    - 6.1.2 [Predefined Procedural Modules](rd-vbal.6.1.2.predefined-procedural-modules.md)
    - 6.1.3 [Predefined Class Modules](rd-vbal.6.1.3.predefined-class-modules.md)
  - 6.2 [VBScript Regular Expressions](rd-vbal.6.2.vbscript-regexp.md)

---
## Conventions

### Section numbers

|Sections|Numbering|
|---|---|
|Chapters 1–4; the overview sections 5.0 and 6.0; section 6.2|RD-VBAL's own numbering.|
|Chapters 5 and 6, except 5.0, 6.0 and 6.2|Mirrors MS-VBAL: RD-VBAL §N is about MS-VBAL §N.|
|An RD-VBA addition that is a variant of an MS-VBAL member|A child number of that member. `GetAllJsonSettings` is [§6.1.2.8.1.7.1](rd-vbal.6.1.2.8.interaction.md), a variant of `GetAllSettings` (§6.1.2.8.1.7).|
|An RD-VBA addition with no MS-VBAL counterpart|The next number after the last MS-VBAL sibling. Several such additions are numbered in alphabetical order. `Erl` is [§6.1.2.7.1.14](rd-vbal.6.1.2.7.information.md) and the `Err` function is §6.1.2.7.1.15 (Information module); `ErrObject.StackTrace` is [§6.1.3.2.2.7](rd-vbal.6.1.3.2.err-class.md) (Err class properties).|
|A section number without a prefix|An RD-VBAL section. An MS-VBAL section is always written with the `MS-VBAL` prefix, as in **MS-VBAL §5.4.2.3**.|

### Notes

|Note|Meaning|
|---|---|
|Reserved. This section has no content yet.|The section only reserves its number. In chapters 5 and 6, a reserved page still names the MS-VBAL section it corresponds to. When the content lives on another page, a `See …` line after the note links that page.|
|**Not implemented.** …|A limitation: what RD-VBA does not implement, and what happens instead when that is known.|

---
## Intellectual Property Rights Notice for Open Specifications Documentation

> [!IMPORTANT]
> **This documentation IS NOT A REVISION of the MS-VBAL specification**.

The publisher of the **RDCore** platform project and of _this present documentation_, is claiming the rights described in the following paragraph of the _Intellectual Property Rights Notice for Open Specifications Documentation_ section (emphasis added):

> **Copyrights**. This documentation [MS-VBAL] is covered by Microsoft copyrights. Regardless of any other terms that are contained in the terms of use for the Microsoft website that hosts this documentation, you can **make copies of it in order to develop implementations of the technologies that are described** in this documentation [MS-VBAL] and can distribute portions of it in your implementations that use these technologies **or in your documentation as necessary to properly document the implementation**. **You can also distribute in your implementation**, with or without modification, any schemas, IDLs, or code samples that are included in the documentation. **This permission also applies to** any documents that are referenced in the Open Specifications documentation.
- [MS-VBAL: VBA Language Specification](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d5418146-0bd2-45eb-9c7a-fd9502722c74#intellectual-property-rights-notice-for-open-specifications-documentation)


**Challenge: Accepted**.
