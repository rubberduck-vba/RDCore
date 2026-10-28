# 4.0 Program Structure and Organization

> [!NOTE]
> This section is incomplete at this time.

An _RD-VBA Environment_ is organized into a number of _workspace source files_ and _host-defined projects_.

The symbol at the top of the abstract syntax tree (AST) of an entire VBA project is of a type inherited from
[VBProjectType](../api/RDCore.SDK.Model.Types.Abstract.VBProjectType.html). These types correspond to the project
types defined in
[**MS-VBAL §4.1** Projects](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4cd406c7-1ade-4522-8d7b-933183059ac7)
(see [**RD-VBAL §2.4.2** Non-intrinsic Types](rd-vbal.2.4.2.non-intrinsic-types.md)).

## Static Execution Context

In _design mode_ ([**RD-VBAL §2.3.2** Mode / State](rd-vbal.2.3.2.mode-state.md)), the _host environment_ maintains
a _static execution context_. The static execution context is an _execution context_ containing the _symbols_
provided by or defined in the following sources:

|Source|Symbols|
|---|---|
|The _environment host_ itself|The _static symbols_ it defines.|
|The _workspace source code_|The symbols defined in it.|
|All _referenced libraries_|The symbols they provide.|
|Any _extension symbol providers_|The symbols they provide.|
|The host|Any additional unbound symbols it defines, e.g. via _immediate_ commands.|

👉 The static execution context retains the state of _immediate_ commands until an `End` command
([**RD-VBAL §5.4.2.22** End Statement](rd-vbal.5.4.2.22.end-statement.md)) resets the _execution context_ back to
its initial state.

## Source Projects

A new RD-VBA _source project_ always minimally contains a single _standard (procedural) module_. That module has a
_host-defined_ default name.

The following are _host-defined_:

- whether the single module of a new source project contains any _directives_, annotations, or a templated _entry
  point_;
- whether there are other default/templated modules in a new source project.

For an RD-VBA project without any modules:

|A project without any modules|Outcome|
|---|---|
|Validity|The project is valid.|
|Project symbol|The project still defines a symbol for the project.|
|Output|The project produces no output.|
|Running or debugging|The host cannot exit _design mode_ to run or debug.|

## Attaching to a Host Process

RD-VBA is normally hosted in a _standalone hosting environment_ that is self-sufficient.

The _environment host_ must ultimately be able to:

- **attach** to an _external process_ that hosts an **MS-VBA** _VBA environment_;
- _externally address_ the host memory space, to enable automation through COM and .NET interoperability.

In principle, external addressing of the host process memory allows a
[VBVariantValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBVariantValue.html) to _wrap_ an externally-defined
object reference ([**RD-VBAL §2.5.2.1.5** Variant Values](rd-vbal.2.5.2.1.5.variant-values.md)).

> [!NOTE]
> For _Microsoft Office Automation_, the _attach to host process_ feature places the **RDCore** platform in a
> technical position similar to that of _Microsoft VSTO_: automating COM from outside the host rather than from
> within it.
>
> The comparison with _Microsoft VSTO_ extends no further than automating COM from outside the host: a complete
> **RDCore** platform could also run RD-VBA CI/CD pipelines and integrate with enterprise software development
> lifecycles.

The _lightweight VBIDE add-in_ that launches an environment host attached to a host process is specified in
[**RD-VBAL §4.1** VBIDE Synchronization](rd-vbal.4.1.vbide-synchronization.md).

## 4.0.1 Application Composition

> [!NOTE]
> Reserved. This section has no content yet.

## 4.0.2 Execution Pipeline and Interpreter

> [!NOTE]
> Reserved. This section has no content yet.

### 4.0.2.1 Call Stack

> [!NOTE]
> Reserved. This section has no content yet.

### 4.0.2.2 Memory Management

> [!NOTE]
> Reserved. This section has no content yet.

### 4.0.2.3 External Calls

> [!NOTE]
> Reserved. This section has no content yet.

## 4.0.3 Application Teardown

> [!NOTE]
> Reserved. This section has no content yet.

---
> ⏮️ [**RD-VBAL §3.5.6** Compiled Execution](rd-vbal.3.5.6.compiled-execution.md) | ⏭️ [**RD-VBAL §4.1** VBIDE Synchronization](rd-vbal.4.1.vbide-synchronization.md)
