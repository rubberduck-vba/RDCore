# 6.0 Standard Library

This chapter describes the **RD-VBA** implementation of the VBA standard library,
[**MS-VBAL §6** VBA Standard Library](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/c645c903-9bd4-4849-8735-3136e867536a).

The `VBA` project consists of a set of classes, functions, `Enum` types and constants that together form VBA's
_standard library_ ([**RD-VBAL §6.1** VBA Project](rd-vbal.6.1.vba-project.md)).

🎯 The **RDCore** platform must implement the VBA standard library. The _environment host_ shall inject the standard
library's symbols into all `VBA` projects, and those symbols shall carry the appropriate _return type_ metadata.

The SDK defines the interfaces for the _internal representation_ of each standard-library module
([RDCore.SDK.Runtime.Abstract.StdLib](../api/RDCore.SDK.Runtime.Abstract.StdLib.html)). The environment host exposes
the symbols provided by the standard library to the _workspace_.

A session is composed from several _symbol providers_: configuration flags, AST declarations, reflected referenced
libraries, and the environment host's own runtime and standard library
([**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md)).

The following standard-library topics are described with their module or class:

|Topic|Described in|
|---|---|
|The `Err` function, and the shape of the error object it returns|[**RD-VBAL §6.1.3.2** Err Class](rd-vbal.6.1.3.2.err-class.md)|
|🧩 `ErrObject.StackTrace`|[**RD-VBAL §6.1.3.2.2.7** StackTrace](rd-vbal.6.1.3.2.err-class.md#613227-stacktrace)|
|🧩 `Information.Erl`|[**RD-VBAL §6.1.2.7.1.14** Erl](rd-vbal.6.1.2.7.information.md#6127114-erl)|
|`Strings.Len` / `Strings.LenB`|[**RD-VBAL §6.1.2.11.1.22** Len / LenB](rd-vbal.6.1.2.11.strings.md#61211122-len--lenb)|
|The VBScript RegExp 5.5 class modules|[**RD-VBAL §6.2** VBScript Regular Expressions](rd-vbal.6.2.vbscript-regexp.md)|


## Standard-Library Calls

Most of the standard library declares `Variant` parameters. A standard-library member's `Variant` parameter accepts
its argument.

An argument of a standard-library call reaches the member as follows:

1. The evaluator coerces the argument to the parameter's declared type on the way in
   ([**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)).
2. The external call carries the argument as an
   [IRuntimeValue](../api/RDCore.SDK.Model.Values.Runtime.IRuntimeValue.html): a runtime value rather than a typed
   value.
3. At external dispatch, the typed value of a `Variant` argument is taken off the runtime variant itself
   ([VBRuntimeVariantValue](../api/RDCore.SDK.Model.Values.Runtime.VBRuntimeVariantValue.html)), not from its
   `BoxedValue`. A `Variant`'s own `BoxedValue` unwraps all the way to the managed value
   ([**RD-VBAL §2.5.2.1.5** Variant Values](rd-vbal.2.5.2.1.5.variant-values.md)).

The declared type of an argument survives external dispatch, because each intrinsic type stores its own exact
managed type: a `short` for `Integer` and an `int` for `Long`
([**RD-VBAL §2.5.2.1.1** Numeric Values](rd-vbal.2.5.2.1.1.numeric-values.md)).

👉 Because each intrinsic stores its exact managed type, `Len` returns "the number of bytes required to store a
variable" ([**RD-VBAL §6.1.2.11.1.22** Len / LenB](rd-vbal.6.1.2.11.strings.md#61211122-len--lenb)).


## 6.0.1 Symbol Injection

The SDK declarations are the standard library's definition. The standard library's symbols are read off those
declarations by [StdLibSymbolReader](../api/RDCore.SDK.Runtime.StdLib.StdLibSymbolReader.html).

A standard-library module's members, their names, their parameters and their return types all come from the
signature an implementation has to satisfy. Each of them is stated once, in that SDK signature.

👉 Because the symbols are read off the SDK signatures, a symbol the _workspace_ resolves cannot describe a member the
runtime does not have.

### Return Types

A standard-library declaration states its return type in its own signature:

|Declaration result type|Return type of the member|
|---|---|
|[RuntimeSemanticsEvaluationResult&lt;TValue&gt;](../api/RDCore.SDK.Runtime.Shared.RuntimeSemanticsEvaluationResult-1.html)|Stated: `TValue` names the value the member produces.|
|[RuntimeSemanticsEvaluationResult](../api/RDCore.SDK.Runtime.Shared.RuntimeSemanticsEvaluationResult.html) (non-generic)|None. This is what makes the member a `Sub`.|

### Attributes

An attribute states what a declaration's signature cannot express, and is used only where it applies.

|Attribute|Carries|
|---|---|
|[StdLibModuleAttribute](../api/RDCore.SDK.Runtime.Abstract.StdLib.StdLibModuleAttribute.html)|Marks a declaration as a standard-library module, and may name it.|
|[StdLibClassAttribute](../api/RDCore.SDK.Runtime.Abstract.StdLib.StdLibClassAttribute.html)|Marks a declaration as a standard-library class, and may name it.|
|[StdLibEnumAttribute](../api/RDCore.SDK.Runtime.Abstract.StdLib.StdLibEnumAttribute.html)|Marks a declaration as a standard-library enum, and may name it. `FormShowConstants` is a name no naming convention recovers, so the attribute states it.|
|[StdLibMemberAttribute](../api/RDCore.SDK.Runtime.Abstract.StdLib.StdLibMemberAttribute.html)|Any of: a member name no naming convention recovers (for example `Hex` beside `Hex$`); an accessor kind ([StdLibMemberKind](../api/RDCore.SDK.Runtime.Abstract.StdLib.StdLibMemberKind.html)); a return type that is a _class_ or an _enum_ rather than an intrinsic type.|
|[StdLibArrayAttribute](../api/RDCore.SDK.Runtime.Abstract.StdLib.StdLibArrayAttribute.html)|The element type of an array parameter (`ValueArray() As Double`), which `VBResizableArrayValue` alone reads as `Variant()`. It is refused on anything but a required `ByVal` array parameter.|

### Naming Conventions

A standard-library name that follows a naming convention needs no attribute:

|SDK declaration|Standard-library name|
|---|---|
|Interface [IStdInformationModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdInformationModule.html)|Module `Information`|
|Enum [VBDayOfWeek](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBDayOfWeek.html)|Enum `VbDayOfWeek`|
|Enum member `VBSunday`|Constant `vbSunday`|

### No Reference, No Opt-In

Nothing references the standard library, and nothing opts into it. The standard-library modules a project gets are
the SDK declarations that carry a marker attribute.

A project has the standard library's symbols whether or not its `.rdproj` mentions the library
([**RD-VBAL §2.2.3** ProjectFile](rd-vbal.2.2.3.projectfile.md)).

`rdcore/host/symbols/define` resolves a declared type name against the standard library's own types as well as the
intrinsic types ([**RD-VBAL §2.0.2** Client/Server Capabilities](rd-vbal.2.0.2.client-server-capabilities.md)).

### Library Name

The standard library is a project of its own, named `VBA` in every language
([StdLibSymbolProvider.LibraryName](../api/RDCore.SDK.Runtime.StdLib.StdLibSymbolProvider.html)): it is what a project-qualified reference to the library names
(`VBA.Strings.LenB`, `VBA.LenB`; [**MS-VBAL §5.6.12**](rd-vbal.5.6.12.member-access-expressions.md)), and what the library's members say they belong to
(`SymbolProperties.Library`).

VB6 loads the very same `VBA` library. The `VB` library of VB6 is another thing: the runtime library of its ActiveX controls (`VB.Form`, `VB.TextBox`...),
which the platform does not model.

> [!NOTE]
> **Not implemented.** The library is the same whole in every language ([SupportedLanguage](../api/RDCore.SDK.Workspace.SupportedLanguage.html)). Where a
> language has more or fewer members than another - VB6 has `VBA`-only members the platform does not hide from it, and a BASIC has no library of its own to
> speak of - the intent is for the members to say which languages they belong to, with an attribute on the declaration, and not for the library to change
> its name.

### Pointer Width

`CLngPtr` needs the one thing a standard-library declaration cannot state: its `LongPtr` return type depends on the
pointer width. `LongPtr` is a different type in each pointer width
([**RD-VBAL §2.4.1** Intrinsic Types](rd-vbal.2.4.1.intrinsic-types.md);
[**RD-VBAL §6.1.2.3.1.10** CLngPtr](rd-vbal.6.1.2.3.conversion-module.md#6123110-clngptr)).

The pointer width belongs to the environment. The environment host passes
[StdLibSymbolProvider](../api/RDCore.SDK.Runtime.StdLib.StdLibSymbolProvider.html) the pointer width of its own
runtime profile
([IRuntimeEnvironmentProfile](../api/RDCore.SDK.Runtime.Abstract.Execution.IRuntimeEnvironmentProfile.html)).

---
> ⏮️ [**RD-VBAL §5.6.16** Constrained Expressions](rd-vbal.5.6.16.constrained-expressions.md) | ⏭️ [**RD-VBAL §6.1** VBA Project](rd-vbal.6.1.vba-project.md)
