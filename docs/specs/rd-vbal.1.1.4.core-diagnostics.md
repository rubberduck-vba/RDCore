# 1.1.4 Core Diagnostics

> **RDCore.Diagnostics** is the *core platform extension* responsible for issuing all *language core diagnostics*. Additional first-party or third-party extensions may provide additional or advanced diagnostics to the LSP *orchestration layer*.

A *diagnostics provider* is a platform extension whose manifest advertises the [DiagnoseDocument](../api/RDCore.SDK.Client.DiagnoseDocument.html) capability. RDCore.Diagnostics is the core-bundled diagnostics provider; see [**RD-VBAL §2.6.5** Diagnostics Pipeline](rd-vbal.2.6.5.diagnostics-pipeline.md).

The role of *analyzers* in extensions like RDCore.Diagnostics is to inspect the flags and errors in *semantic contexts*, and issue diagnostics. Semantic flags are described in [**RD-VBAL §1.1.3** Core Semantic Flags](rd-vbal.1.1.3.core-semantic-flags.md).

## Diagnostic Codes

The schema of a *diagnostic* is specified by [LSP 3.17 § Diagnostic](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#diagnostic). It includes a **Code**, whose format the RDCore platform specifies as follows:

|Format|Description|See|
|---|---|---|
|`VBC00000`|**Visual Basic Compilation** error diagnostics|[**RD-VBAL §2.6.1** Syntax Errors](rd-vbal.2.6.1.syntax-errors.md), [**RD-VBAL §2.6.2** Semantic Compilation Errors](rd-vbal.2.6.2.semantic-compilation-errors.md)|
|`VBR00000`|**Visual Basic Runtime** error diagnostics|[**RD-VBAL §2.6.3** Runtime Errors](rd-vbal.2.6.3.runtime-errors.md)|
|`VBA00000`|**Visual Basic Application** error diagnostics|[**RD-VBAL §2.6.3** Runtime Errors](rd-vbal.2.6.3.runtime-errors.md)|
|`RDC00000`|**RDCore** *core diagnostics*|[**RD-VBAL §2.6.4** Rubberduck Core Diagnostics](rd-vbal.2.6.4.rubberduck-core-diagnostics.md)|
|`RDX00000`|**RDCore** *extension diagnostics*, issued by the core extension|The documentation pertaining to the extension|
|`XYZ00000`|A third-party extension's diagnostics, under the prefix it declares ([Declaring a Prefix](#declaring-a-prefix))|The documentation pertaining to the extension|

Every RDCore LSP diagnostic carries a stable code. See [**RD-VBAL §2.6** Diagnostics](rd-vbal.2.6.diagnostics.md).

## Numeric Portion

The `00000` numeric portion of a diagnostic code is a five-digit zero-padded code, e.g. `VBC00001`, `VBR00009`, `RDC01001`. Its value depends on the nature of the diagnostic:

|Origin|Diagnostic|Numeric portion|
|---|---|---|
|The *parser*|Normally a *syntax error*|A [VBCompileErrorId](../api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) value between **42 and 999**.|
|The *static semantics analysis pass*|A *compilation error*|A `VBCompileErrorId` value **greater than or equal to 9300**.|
|A *language core extension*|A *compilation error*|A `VBCompileErrorId` value **between 8000 and 9299**.|
|The *runtime semantics analysis pass*|A *runtime error*|A [VBRuntimeErrorId](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorId.html) value corresponding to its MS-VBAL specified error code.|
|*Workspace source code*|An error raised by the *workspace program*|An unspecified `VBA00000` code, defined by the *custom error code* raised in the workspace program.|
|RDCore.Diagnostics|Any other *language core diagnostic*|Documented under an `RDC00000` code.|
|The core extension|A *platform extension diagnostic*|Documented under an `RDX00000` code, in the documentation pertaining to the extension.|
|A third-party extension|A *platform extension diagnostic*|Any number, under the prefix the extension declares.|

## Declaring a Prefix

An extension declares the prefix of its diagnostic codes with an assembly attribute, [DiagnosticPrefixAttribute](../api/RDCore.SDK.Extensibility.DiagnosticPrefixAttribute.html):

```csharp
[assembly: DiagnosticPrefix("XYZ")]
```

`rdc.exe describe-ext` writes the prefix into the extension's manifest as `DiagnosticPrefix` ([**RD-VBAL §1.1.5** Extension Manifest](rd-vbal.1.1.5.extension-manifest.md)).

The platform manifest (`rdcore.json`) names the *core extension*, the one the platform bundles: `"coreExtension": "RDCore.Diagnostics"`. A platform manifest that does not name one bundles `RDCore.Diagnostics`, which declares `RDC`.

While it assembles the platform, the language server does not start an extension when its prefix is one of the following, whatever the order of the folders, and it logs a warning for each one:

|Prefix|Not started|
|---|---|
|`RDC` or `RDX`|Any extension other than the core extension.|
|`VBC`, `VBR` or `VBA`, the language's own|Every extension.|
|A prefix that two extensions declare|Both, each warning naming the other.|
|Not three letters `A` to `Z`|The extension that declares it.|

At run time, the language server reports the core extension's findings as they are. Of another extension's findings, it keeps only those whose code has the prefix the extension declares, and it reports each one with the extension's title as its `source` and with no link to the RDCore documentation. It drops every other finding, and logs it: a finding of an extension that declares no prefix is always dropped.

A diagnostic gets a link to its page in the RDCore documentation only when its code has one of the platform's own prefixes (`VBC`, `VBR`, `VBA`, `RDC`, `RDX`).

## Run-time and Application Errors

A clean separation of *application-defined* errors from specified *semantic runtime errors* is compliant with MS-VBAL. The MS-VBA implementation weakly separates them using the `vbObjectError` constant. The proper use of this constant has historically been misunderstood, and MS-VBA makes no effective distinction between run-time errors and application errors.

RDCore removes the need for the `vbObjectError` constant by internally representing run-time errors and application errors as different error metadata types: [VBRuntimeErrorInfo](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorInfo.html) and [VBApplicationErrorInfo](../api/RDCore.SDK.Model.Errors.VBApplicationErrorInfo.html). This clarifies their origin before they reach the runtime.

## Error Descriptions

The description of a compilation or run-time error shall be identical to the corresponding MS-VBA description.

All RD-VBA trace messages can include an optional *verbose* message. The platform shall use this verbose message to further explain the reason behind any error being raised. The verbose message is independent of, or *supplemental to*, any other verbose content, including but not limited to execution *stack traces*.
