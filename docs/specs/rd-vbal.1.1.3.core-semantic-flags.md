# 1.1.3 Core Semantic Flags

The *language core* features an *analytical pipeline* that attaches detailed *semantic flags* to *abstract syntax tree* (AST) nodes. [ComparisonOperatorSemanticFlags](../api/RDCore.SDK.Semantics.Flags.ComparisonOperatorSemanticFlags.html) is an example of core semantic flags. The pipeline is described in [**RD-VBAL §5.0** Semantics](rd-vbal.5.0.semantics.md).

Core semantic flags are **spec-driven**. They are designed to describe the semantic reality of an operation, **without any restraint or judgement**: semantic flags are pure *facts*, not *opinions*.

A semantic flag typically exists because the specified semantics contain a branch or condition:

|Specified semantics|Semantic flag|
|---|---|
|The *effective type* of an operation evaluates a [VBNullType](../api/RDCore.SDK.Model.Types.VBNullType.html) differently than a [VBNumericType](../api/RDCore.SDK.Model.Types.Abstract.VBNumericType.html).|The semantic flags for that operation should reflect the `NullEffectiveType` flag.|
|The specifications mention a `NaN` operand.|There should be a `HasNaNOperand` semantic flag.|

> 👉 **Do** create new core semantic flags as needed to accurately reflect the semantic reality of an operation.
>
> ❌ **Do not** create new core semantic flags that no specified (RD-VBAL) semantics justify.

## Extensions

> 🧩 **Extensions can, and should,** enrich a semantic context well beyond the responsibilities of the language core. They do so with *diagnostics* issued by *analyzers* that can inspect the complete semantic reality of the application.

Semantic flags aim to expose all the facts of the application's semantic reality. Diagnostics are described in [**RD-VBAL §1.1.4** Core Diagnostics](rd-vbal.1.1.4.core-diagnostics.md).

## Semantic Flags Specified Elsewhere

|Condition|Semantic flag|See|
|---|---|---|
|A project reference shadows a `VBA` library definition.|The shadowing should be detected in the semantic layer and reported through semantic flags, so that RDCore.Diagnostics can issue *shadowed declaration* diagnostics.|[**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)|
|An implicit `ReDim` declaration.|A later analysis pass raises a semantic flag at the site.|[**RD-VBAL §5.4.3.3** ReDim Statement](rd-vbal.5.4.3.3.redim-statement.md)|
|A deferred parameter inferred as `Variant`, from array or heterogeneous arguments.|Should issue semantic flags as appropriate, to signal the case to any listening language-level extensions.|[**RD-VBAL §2.4.4** Deferred Types](rd-vbal.2.4.4.deferred-types.md)|
|A class module, predeclared or not, with or without state of its own.|[ClassModuleSemanticFlags](../api/RDCore.SDK.Semantics.Flags.ClassModuleSemanticFlags.html) (`DefaultInstance`, `Stateful`), on the module's semantic model.|[**RD-VBAL §5.2.4** Class Module Declarations](rd-vbal.5.2.4.class-module-declarations.md#52412-default-instance-variables-static-semantics)|
