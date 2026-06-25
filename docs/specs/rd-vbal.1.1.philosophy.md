# 1.1 Design and Extension Philosophy

RD-VBA is not a _reinterpretation_ of VBA - it is an effort to ***fully realize* its specification**.  

The _language core_ shall remain **strictly compatible** with MS-VBAL specifications, but it shall **not** be treated as a _fossilized language_, either.  


---
## 1.1.1 Platform Extensions
>🧩 RDCore operates on a **capability-driven host model**, where extended features may or may not be available depending on the execution environment. Extensions must be resilient to partial capability availability.

The _platform_ is intended to be massively extended through first and third party extensions whose capabiliites are negociated with the _RD-VBA environment host_.  

> [!NOTE]
> In the **RDCore** ecosystem, the default _RD-VBA environment host_ is `rdc.exe`. Other LSP clients may eventually join the family, but would be packaged separately from the **RDCore language platform**.


---
## 1.1.2 Language Core Extensions

Where the specification defines implicit behaviors, RD-VBA may choose to make these behaviors explicit, provided that:

- The underlying semantics remain unchanged;
- Exiting code continues to behave identically;
- The added explicitness improves clarity, diagnostics, or tooling.

This includes features such as:

- The comment annotations syntax from the _legacy Rubberduck VBIDE add-in_;
- The introduction of an [explicit coercion operator](../api/RDCore.SDK.Semantics.Static.Operators.BinaryLetCoerceOperatorStaticSemantics.html) for semantic disambiguation;
- The explicit addition of runtime semantics for a unary '+' operator;

These additions do not *alter* the language - they *reveal* it.  
>   ✅ **A valid _language core_ extension makes explicit what was implicit.**  
>   ❌ An invalid _language core_ extension introduces new semantics.  

Examples of **valid** _language core_ extensions include:
- Introducing a new `Option Strict` _directive_ to _restrict_ existing static and/or runtime semantics by issuing error diagnostics or new, explicitly specified compile-time errors.
  - ...provided that no new _tokens_ are introduced without also introducing the native capability to output them as _comment annotations_, keeping the output strictly compliant with MS-VBAL.
- Leveraging the obiquitously permitted late-binding and _duck-typing_ to introduce and surface inherent _deferred types_ to _design-time_ symbols, enabling LSP-level enhanced capabilities around auto-completion lists, notably.

Examples of **invalid** _language core_ extensions include:

- Exposing symbols for _descriptors_ and other internal meta-types that could introduce _reflection semantics_ to the language;
- Building on MS-VBAL explicitly specifying every `Variant` input in the _standard library_ as _expressions to be evaluated_ to introduce _deferred execution semantics_ to the language (i.e. leveraging meta-types to introduce new semantics);
  - This would effectively make _functions_ a first-class RD-VBA _runtime entity_ that can be passed around as _values_ - this should not be done without a careful and thorough consideration of the implications on the rest of the semantic model.

This however, does not make them invalid extensions in the _RDCore ecosystem_: each and every single one of these could be great [_platform-level_ extensions](#111-platform-extensions).


### 1.1.3 Core Semantic Flags

The _language core_ features an _analytical pipeline_ that attaches detailed _semantic flags_ to _abstract syntax tree_ (AST) nodes. See for example [`ComparisonOperatorSemanticFlags`](../api/RDCore.SDK.Semantics.Flags.ComparisonOperatorSemanticFlags.html).

These flags are **spec-driven** and designed to _describe the semantic reality_ of an operation, **without any restraint or judgement**.

The existence of a semantic flag is typically motivated by the presence of a branch or condition in the specified semantics: if the _effective type_ of an operation evaluates a `VBNullType` differently than a `VBNumericType`, then the semantic flags for that operation should reflect the `NullEffectiveType` flag, and if the specifications mention something about a `NaN` operand, then there should be a `HasNaNOperand` semantic flag to reflect this fact.

> 👉 **DO** create new _core semantic flags_ as needed to accurately reflect the semantic reality of an operation.  
> ❌ **DO NOT** create new _core semantic flags_ that no specified (RD-VBAL) semantics justify.  

> 🧩 **Extensions CAN** (and should) enrich a semantic context _well beyond_ the responsibilities of the _language core_, with _diagnostics_ issued by _analyzers_ that can inspect the complete semantic reality of the application: semantic flags aim to _expose all the facts_ of this reality.  


### 1.1.4 Core Diagnostics

> 🧩 **RDCore.Diagnostics** is the _core platform extension_ responsible for issuing all _language core diagnostics_. Additional extensions (first or third party) may provide additional/advanced diagnostics to the LSP _orchestration layer_.

The schema of a _diagnostic_ is specified by [LSP 3.17 § Diagnostic](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#diagnostic), and includes a **Code** that the **RDCore** platform specifies as follows:

|Format|Description|
|---|---|
|`VBC00000`|**Visual Basic Compilation** error diagnostics|
|`VBR00000`|**Visual Basic Runtime** error diagnostics|
|`VBA00000`|**Visual Basic Application** error diagnostics|
|`RDC00000`|**RDCore** _core diagnostics_|
|`RDX00000`|**RDCore** _extension diagnostics_|

The `00000` numeric portion of a diagnostic code depends on its nature:
- Errors issued from the _parser_ are normally _syntax errors_ reporting a [VBCompileErrorId](../api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) with a numeric value **between 42 and 999**;
- Errors issued from the _static semantics analysis pass_ are _compilation errors_ reporting a [VBCompileErrorId](../api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) with a numeric value **greater than or equal to 9300**;
- Errors issued from a _language core extension_ that are _compilation errors_, reporting a [VBCompileErrorId](../api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) with a numeric value **between 8000 and 9299**; 
- Errors issued from the _runtime semantics analysis pass_ are _runtime errors_ reporting a [VBRuntimeErrorId](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorId.html) with a numeric value _corresponding to its MS-VBAL specified error code_;
- Errors raised from _workspace source code_ have an unspecified `VBA00000` diagnostic code that is defined by the _custom error code_ raised in the _workspace program_;
- Any other _language core diagnostics_ issued from **RDCore.Diagnostics** are to be documented under a `RDC00000` code;
- Any other first-party _platform extension diagnostics_ issued from any other extension, shall be documented under a `RDX00000` code in the documentation pertaining to that extension.
- Third-party _patform extension diagnostics_ may be reported under an available `RDX` code, or a reasonably publisher-specific 3-letter prefix.

Such a clean separation of _application-defined_ errors and of specified _semantic runtime errors_ is compliant with MS-VBAL; it is the MS-VBA implementation that weakly separates them using a `vbObjectError` constant, but the proper use of this constant was historically misunderstood and the distinction between run-time and application errors doesn't really exist. **RDCore** removes the need for that constant by internally representing them as different error metadata types, which clarifies their origin before they even reach the runtime. 

The description of compilation and run-time errors shall match _exactly_ with the corresponding **MS-VBA** descriptions. However, all **RD-VBA** trace messages can include an optional _verbose_ message that the platform shall use to further explain the reason behind any error being raised, independent of (or _supplemental to_) any other _verbose_ content including but not limited to execution _stack traces_.

> [!WARNING]
> A third-party extension that issues _already-documented first-party diagnostic codes_ may be **required to modify** its codes before it can be distributed through the platform's official distribution channels. This ensures that all diagnostic codes published under the platform remain **traceable and searchable online**.


---
## 1.1.5 Extension Manifest

**RDCore** extensions are _discovered_ by the _environment host_ during the _composition_ of the host environment. The _extension manifest_ is a small text file in a human-readable JSON serialized format that describes the extension to the platform.

By default, the _environment host_ may restrict capabilities request by **unsigned or untrusted extensions**. Users may override these defaults through configuration or development modes;

> [!IMPORTANT]
> Extension packages **must** include a **signed manifest** that binds metadata to the distributed artifacts (the _extension server_ executable).


### 1.1.5.1 Schema

```javascript
{
    "Name": "string",
    "Title", "string",
    "Version", "version-string",
    "Publisher", "string",
    "PublisherUrl", "url-string",
    "Description", "string",
    "Signature", "string"
}
```

|Attribute|Description|
|---|---|
|Name|The file name of the extension executable (.exe) located in the same folder as the manifest file.|
|Title|The _friendly name_, or _title_ of the extension; **must** match the name of the folder it is located in.|
|Version|A _semantic version_ string minimally identifying the **Major.Minor.Build** version of the extension; **must** match the assembly file version of the extension executable (.exe) file.|
|Publisher|The name of the publisher (copyright holder) of the extension.|
|PublisherUrl|A reasonably short website URL provided by the publisher.|
|Description|A short description of the extension.|
|Signature|The `SHA512` file-hash signature of the extension executable (.exe) file; **must** match _exactly_ with the file-hash of the discovered extension executable (.exe). |


### 1.1.5.2 Validation

An extension manifest discoverd under the platform's `./extensions` folder must be validated by the host before the extension can be authorized to execute. This validation occurs in layers and helps ensure only _trusted extensions_ are allowed to run.

- If _platform extension configuration_ is **NOT** explicitly listing the `Title` of the extension as `Allowed`, a `NotAllowed` validation flag is issued;
  - The _environment host_ may prompt to allow discovered extensions that don't appear in the platform extension configuration; whether the host prompts to allow, or automatically blocks non-configured extensions is dependent on the host implementation.
- If _platform extension configuration_ is explicitly listing the `Title` of the extension as `Blocked`, a `Blocked` validation flag is issued;
- If the `Title` in the extension manifest mismatches the folder location it was discovered in, a `LocationMismatch` validation flag is issued;
- If the extension executable (.exe) _named in the manifest_ is not found in the same folder as the manifest file, a `FileNotFound` validation flag is issued;
- If the `SHA512` file-hash signature in the manifest mismatches the `SHA512` file-hash signature of the extension executable (.exe), a `SignatureMismatch` validation flag is issued;

See [Validation Flags](../api/RDCore.SDK.Extensibility.ExtensionValidationFlags.html) for more details.

If the validation result is `NoFlags`, the host may proceed to configure a _client host_ for this extension server, start its executable process, and initiate the LSP connection handshake and capabilities exchange.

> [!TIP]
> In order to facilitate building _platform extensions_, the `rdc.exe` host may be configured to allow unsigned extension builds using a `--unsafe-dev-mode` command-line flag.


---
## 1.1.6 Capabilities Provider

An _environment host_ must implement [IExtensionCapabilityProvider](../api/RDCore.SDK.Extensibility.IExtensionCapabilityProvider.html) and provide all available extension capabilities. 

This interface allows a host to determine whether certain extension capabilities should be enabled or not, through mechanisms that are implementation-dependent and **may include** but are in no way restricted to, the **validation of an active subscription** to said extended capabilities.

All platform extensions must _gracefully_ handle being denied their extended capabilities by the _capability provider_. This includes:
- TRACE output acknowledging the denied capabilities;
- Responding to a LSP `shutdown` request and cleanly terminating upon a LSP `exit` notification from the _environment host_;

An abstract server application in the SDK should already handle these lifecycle events correctly, without needing any further configuration.

If an extension _successfully_ registers **any** capability, its process continues to run and may handle a reduced set of LSP requests and notifications; otherwise the _environment host_ requests the termination of the extension server process.


---
> ⏮️ [**RD-VBAL §1.0** Introduction](rd-vbal.1.0.introduction.html) | ⏭️ [**RD-VBAL §2.0** Computational Environment](rd-vbal.2.0.computational-environment.html)  

