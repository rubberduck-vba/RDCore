# 5.0 Semantics

The role of _semantics_ is to encode the _meaning_ of the language into a set of deterministic rules and specified sequences of operations.

There are two types of _abstract semantics_ explicitly defined in **RDCore.SDK**:
- [StaticSemantics](../api/RDCore.SDK.Semantics.Static.Abstract.IStaticSemantics.html)
- [RuntimeSemantics](../api/RDCore.SDK.Runtime.Abstract.IRuntimeSemantics-2.html)

The _environment host_ may provide additional semantics through external providers (extensions); _static semantics_ are effective in _design-time_ and fully available to the _semantic analysis layer_. 

_Runtime semantics_ are partially available to the _semantic analysis layer_ (for simulated execution pipelines), but generally unavailable in a _static context_.


---
## 5.0.1 Static Semantics
The role of _static semantics_ is to determine a _declared type_ for a given _bound expression_, given the determined static _declared type_ of its inputs.

Static semantics always yield a [StaticSemanticsEvaluationResult](../api/RDCore.SDK.Semantics.Static.Abstract.StaticSemanticsEvaluationResult.html) that represents either:
- a `Success` result encapsulating a [VBType](../api/RDCore.SDK.Model.Types.Abstract.VBType.html);
- an `Error` result encapsulating a [VBCompileErrorInfo](../api/RDCore.SDK.Model.Errors.VBCompileErrorInfo.html).

> 👉 In most error cases, the compile-time error metadata returned is for a [TypeMismatch](../api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) error.


---
## 5.0.2 Runtime Semantics
The role of _runtime semantics_ depends on the type of node being evaluated:
- _Directives_ and _literal_ or _constant expressions_ evaluate to their static / compile-time value;
- _Operators_ evaluate a [VBTypedValue](../api/RDCore.SDK.Model.Values.Abstract.VBTypedValue.html) from their _operands_;
- _Statements_ induce _side-effects_ to _program_, _global_, or _host environment_ state.


### 5.0.2.1 Operator Evaluation
> [!NOTE]
> This section describes the implementation of **MS-VBAL §5.6.9.2 Simple Data Operators**.

The _evaluation pipeline_ of all operators follows a clear sequence:
1. The _effective type_ of the operation is determined, based on the _declared type_ of its _operands_;
2. Validation: all non-[null](../api/RDCore.SDK.Model.Values.Intrinsic.VBNullValue.html) _operands_ are let-coerced to the determined _effective type_ of the operation;
3. Evaluation: a templated method evaluates a result, having the _execution context_ and the validated _operands_ to work with.

The sequence may be aborted at any point to return an _error result_ that encapsulates [VBRuntimeErrorInfo](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorInfo.html) error metadata.


### 5.0.2.2 Statement Evaluation
> [!NOTE]
> The specification of this section is currently a work in progress.


---
## 5.0.3 Semantic Analysis
The _analysis pipeline_ of all operators follows a clear sequence:
1. The _effective type_ of the operation is determined, based on the _declared type_ of its _operands_ and invoking the same methods as runtime semantics;
2. Validation: all non-[null](../api/RDCore.SDK.Model.Values.Intrinsic.VBNullValue.html) _operands_ are let-coerced to the determined _effective type_ of the operation, using the same runtime semantics let-coercion provider as the evaluation pipeline;
3. Semantic evaluation: a templated method evaluates a _semantic result_, having the _execution context_ and the validated _operands_ to work with **but without inducing any side-effects**.

The `Analyze` method then yields a [_builder_](../api/RDCore.SDK.Semantics.Builders.ISemanticContextContributor-2.html) that builds a _semantic context_ for this specific _expression node_ that includes the results of each evaluation step:
- A [DetermineOperatorEffectiveTypeResult](../api/RDCore.SDK.Runtime.Shared.DetermineOperatorEffectiveTypeResult.html) encapsulating the result of the first step;
- A [LetCoercionAnalysisContext](../api/RDCore.SDK.Semantics.Analysis.LetCoercionAnalysisContext.html) encapsulating the aggregated evaluation stack and outcome of all let-coercion operations, with their respective _semantic flags_;
- A [RuntimeSemanticsEvaluationResult](../api/RDCore.SDK.Runtime.Shared.RuntimeSemanticsEvaluationResult.html) encapsulating the result of the operation.

> 👉 The role of the `Analyze` method at this level is simply to report the _semantic facts_ of an operation, that usually cannot be inferred from the operands or _effective type_ alone. **These flags are pure _facts_, not _opinions_**.

> 🧩 The role of _analyzers_ in extensions like **RDCore.Diagnostics** is to inspect the flags and errors in these _semantic contexts, and issue _diagnostics_. While **error** diagnostics are reserved for coded _syntax/compilation_ and _runtime/application_ errors, a **hint** or **suggestion** diagnostic can be as opiniated as needed.

> [!NOTE]
> **Warning** diagnostics should be used carefully, for flagging _potential bugs_ or logical errors causing unexpected or unintended behavior, or perhaps _severe_ performance issues. Always consider the possibility of there being a _treat warnings as errors_ host environment configuration setting: if a diagnostic is not worth _breaking a build over_, then it's not a _warning_. 

**RDCore** implements the MS-VBAL type coercion rules _verbatim_ through _pattern-matching_ against its type system.

---
> ⏮️ [**RD-VBAL §4.0** Program Structure](rd-vbal.4.0.program-structure.html) | ⏭️ [**RD-VBAL §6.0** Standard Library](rd-vbal.6.0.standard-library.html)
