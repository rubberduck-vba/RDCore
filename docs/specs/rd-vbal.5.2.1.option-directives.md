# 5.2.1 Option Directives

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.1** Option Directives](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/049088bd-acb7-4e33-a875-ab17a891ccda).

_Directives_ include the `Option` statements
([**RD-VBAL §3.1** Attributes and Directives](rd-vbal.3.1.attributes-directives.md)):

|Directive|Description|
|---|---|
|`Option Compare`|Determines the comparison mode for string comparisons (§5.2.1.1).|
|`Option Base`|Determines the base (0 or 1) of implicitly-sized arrays (§5.2.1.2).|
|`Option Explicit`|Implicit declarations become compile-time errors (§5.2.1.3).|
|`Option Private Module`|Determines the _accessibility_ of a module (§5.2.1.4).|

A module's [LexicalScope](../api/RDCore.SDK.Model.Symbols.LexicalScope.html) carries its
[ModuleDirectives](../api/RDCore.SDK.Model.Symbols.ModuleDirectives.html)
([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)):

|Member|Description|
|---|---|
|`Explicit`|Whether the module declares `Option Explicit`.|
|`Compare`|The module's `Option Compare` mode.|
|`Strict`|Reserved for RD-VBA's `'@OptionStrict` annotation. No symbol provider sets it, so it is always `false`, and nothing reads it.|

`ModuleDirectives` is reachable from any scope nested under the module, via
`LexicalScope.EnclosingModuleDirectives()`.

A class module's directives govern its code as a standard module's do: `Option Explicit` makes an undeclared name in
the class a compile error, which keeps the class from loading; `Option Compare` is how its relational operators
compare `String` values, whatever the mode of the module that calls it; and `Option Base` is the lower bound its
arrays take when they state none. The name of a class binds only in the type binding context
([**RD-VBAL §5.6.4** Expression Binding Contexts](rd-vbal.5.6.4.expression-binding-contexts.md)), so the host finds the
module a member belongs to by its name as a value and then as a type
([SessionSymbolsModuleExtensions](../api/RDCore.SDK.Runtime.Abstract.Execution.SessionSymbolsModuleExtensions.html)`.TryResolveModule`),
both when it gives the module's symbol its directives and when a new activation takes its frame's directives from
it.


## 5.2.1.1 Option Compare Directive

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.1.1** Option Compare Directive](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/801de7da-232b-46dd-9695-21167620e078).

The `Option Compare` directive determines the comparison mode for string comparisons: `Text` or `Binary`. The
comparison mode may instead be a _host-defined token_, which dynamically configures the comparison mode.


## 5.2.1.2 Option Base Directive

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.1.2** Option Base Directive](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a4d229ea-e66e-4e3d-bd17-7d7cf2ac9290).

The `Option Base` directive determines the base (0 or 1) of implicitly-sized arrays.

The _lower bound_ of an _uninitialized array value_ depends on the value of the `Option Base` directive
([**RD-VBAL §2.5.2.1.2** Array Values](rd-vbal.2.5.2.1.2.array-values.md)):

|`Option Base`|Lower bound|
|---|---|
|Not specified (the default)|`0`|
|`Option Base 1`|`1`|

Resolving an omitted array lower bound against `Option Base` is the concern of the semantic pass that materializes
the array value, not of the declaration pass
([**RD-VBAL §2.4.1** Intrinsic Types](rd-vbal.2.4.1.intrinsic-types.md)).


## 5.2.1.3 Option Explicit Directive

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.1.3** Option Explicit Directive](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e9253cf2-5139-4abd-8c1d-eaf390805cb8).

Under the `Option Explicit` directive, implicit declarations become compile-time errors.

Whether the enclosing module declares `Option Explicit` is the module-level fact a static semantics rule needs
([**RD-VBAL §5.0** Semantics](rd-vbal.5.0.semantics.md), §5.0.1). The static-semantics layer consumes
`ModuleDirectives` to decide what an _unresolved_ simple name is
([**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md)):

|Module declares `Option Explicit`|An unresolved simple name is|
|---|---|
|No|A deferred [VBUnknownType](../api/RDCore.SDK.Model.Types.VBUnknownType.html).|
|Yes|A **VBC09302** _Variable not defined_ compile-time error: an `Error` carrying `VariableNotDefined` ([VBCompileErrorId](../api/RDCore.SDK.Model.Errors.VBCompileErrorId.html)).|

An implicit declaration by an unqualified `ReDim` target is legal under `Option Explicit`
([**RD-VBAL §5.4.3.3** ReDim Statement](rd-vbal.5.4.3.3.redim-statement.md)).


## 5.2.1.4 Option Private Directive

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.1.4** Option Private Directive](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/41e5d997-61bf-4b9b-b62d-661a33578927).

The `Option Private Module` directive determines the _accessibility_ of a module.

---
> ⏮️ [**RD-VBAL §5.2** Module Declaration Section Structure](rd-vbal.5.2.module-declaration-section-structure.md) | ⏭️ [**RD-VBAL §5.2.2** Implicit Definition Directives](rd-vbal.5.2.2.implicit-definition-directives.md)
