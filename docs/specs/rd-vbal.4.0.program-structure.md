# 4.0 RD-VBA Program Structure and Organization
> [!NOTE]
> This documentation is incomplete at this time.

A _RD-VBA Environment_ is organized into a number of _workspace source files_ and _host-defined projects_.

In _design mode_, the _host environment_ maintains a _static execution context_ that is an _execution context_ containing the _symbols_ provided by or defined in:
- _static symbols_ defined by the _environment host_ itself;
- the _workspace source code_;
- all _referenced libraries_;
- any _extension symbol providers_;
- any additional unbound symbols defined by the host (e.g. via _immediate_ commands).

👉 The _static execution context_ retains the state of _immediate_ commands until an `End` command resets the _execution context_ back to its initial state.

✨ A new RD-VBA _source project_ always minimally contains a single _standard (procedural) module_ with a _host-defined_ default name. It is _host-defined_ whether that single module contains any _directives_, annotations, or a templated _entry point_, or whether there are other default/templated modules in a new source project.

A **RD-VBA** project without any modules is valid and still define a symbol for the project, but produces no output and the host cannot exit _design mode_ to run or debug.

While **RD-VBA** is normally hosted in a _standalone hosting environment_ that is self-sufficient, the _environment host_ must ultimately be able to **attach** to an _external process_ that hosts a **MS-VBA** _VBA environment_, and _externally address_ the host memory space to enable automation through COM and .NET interoperability; the external addressing technically allows a `VBVariantValue` to _wrap_ an externally-defined object reference.

As far as _Microsoft Office Automation_ is concerned, this _attach to host process_ feature ultimately positions the **RDCore** platform in a similar technical spot as _Microsoft VSTO_ did, **automating COM from the sidelines** rather than from within the host. The comparison stops here though: _a fully-realized RDCore platform_ could technically run **RD-VBA CI/CD pipelines** and **integrate Enterprise software development lifecycles**.  


## 4.0.1 Application Composition
> [!NOTE]
> This documentation is incomplete at this time.

## 4.0.2 Execution Pipeline and Interpreter
> [!NOTE]
> This documentation is incomplete at this time.

### 4.0.2.1 Call Stack
> [!NOTE]
> This documentation is incomplete at this time.

### 4.0.2.2 Memory Management
> [!NOTE]
> This documentation is incomplete at this time.

### 4.0.2.3 External Calls
> [!NOTE]
> This documentation is incomplete at this time.

### 4.0.3 Application Teardown
> [!NOTE]
> This documentation is incomplete at this time.

### 4.0.4 VBIDE Synchronization

🎯 The **RDCore** platform tooling shall ultimately include a _lightweight VBIDE add-in_ responsible for:
- Exporting a **MS-VBA** _source project_ to a **RD-VBA** _workspace folder_;
- Importing a **RD-VBA** _workspace folder_ into a **MS-VBA** _source project_;
- Launching a **RD-VBA** _environment host_ attached to a specified _host process ID_.

---

> ⏮️ [**RD-VBAL §3.0** Syntax Tree](./rd-vbal.3.0.syntax-tree.html) | ⏭️ [**RD-VBAL §5.0** Semantics](./rd-vbal.5.0.semantics.html)
