# 5.0 Semantics

The role of _semantics_ is to encode the _meaning_ of the language into a set of deterministic rules and specified
sequences of operations.

**RDCore.SDK** defines two types of _abstract semantics_:

|Semantics|Defined by|Availability to the _semantic analysis layer_|
|---|---|---|
|_Static semantics_ ([**§5.0.1**](#501-static-semantics))|[IStaticSemantics](../api/RDCore.SDK.Semantics.Static.Abstract.IStaticSemantics.html)|Fully available.|
|_Runtime semantics_ ([**§5.0.2**](#502-runtime-semantics))|[IRuntimeSemantics&lt;,&gt;](../api/RDCore.SDK.Runtime.Abstract.IRuntimeSemantics-2.html)|Partially available, for simulated execution pipelines.|

_Static semantics_ are effective in _design-time_. _Runtime semantics_ are generally unavailable in a _static
context_.

The _environment host_ may provide additional semantics through external providers (extensions; see
[**RD-VBAL §1.1** Design and Extension Philosophy](rd-vbal.1.1.philosophy.md)).

The same layered refinement through _templated methods_ applies to all semantics, both static and runtime
([**RD-VBAL §3.3.0** Operator Expressions](rd-vbal.3.3.0.operators.md)).


## 5.0.1 Static Semantics

The role of _static semantics_ is to determine a _declared type_ for a given _bound expression_, given the
determined static _declared type_ of its inputs.

Static semantics always yield a
[StaticSemanticsEvaluationResult](../api/RDCore.SDK.Semantics.Static.Abstract.StaticSemanticsEvaluationResult.html):

|Result|Encapsulates|
|---|---|
|`Success`|A [VBType](../api/RDCore.SDK.Model.Types.Abstract.VBType.html): the declared type.|
|`Error`|A [VBCompileErrorInfo](../api/RDCore.SDK.Model.Errors.VBCompileErrorInfo.html): compile-time error metadata.|

> 👉 In most static-semantics error cases, the compile-time error metadata returned is for a `TypeMismatch` error
> ([VBCompileErrorId](../api/RDCore.SDK.Model.Errors.VBCompileErrorId.html)).

### Static evaluation context

Every static semantics rule is evaluated against a
[StaticEvaluationContext](../api/RDCore.SDK.Semantics.Static.Abstract.StaticEvaluationContext.html):

|Member|Description|
|---|---|
|`Resolver`|The [ISymbolResolver](../api/RDCore.SDK.Runtime.Abstract.Execution.ISymbolResolver.html).|
|`Scope`|The [LexicalScope](../api/RDCore.SDK.Model.Symbols.LexicalScope.html) the expression is lexically found in. How a lexical scope is resolved is specified in [**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md).|
|`EnclosingWithTargetType`|The declared type of the innermost enclosing `With` block's target expression, or `null` when the expression is not inside any `With` block ([**RD-VBAL §5.6.15** With Expressions](rd-vbal.5.6.15.with-expressions.md)).|

Module-level facts a static semantics rule needs are not parameters of the `StaticEvaluationContext`. They live on
[ModuleDirectives](../api/RDCore.SDK.Model.Symbols.ModuleDirectives.html), reachable from any scope via
`LexicalScope.EnclosingModuleDirectives()`.

The module-level fact a static semantics rule needs is whether the enclosing module declares `Option Explicit`
([**RD-VBAL §5.2.1** Option Directives](rd-vbal.5.2.1.option-directives.md)). `ModuleDirectives` also carries the
module's `Option Compare` mode, and a `Strict` member reserved for RD-VBA's `'@OptionStrict` annotation
([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)). No symbol provider sets `Strict`, so
it is always `false`, and nothing reads it.

Keeping module-level facts on `ModuleDirectives` rather than on `StaticEvaluationContext` keeps the context's shape
stable as the directive surface that MS-VBAL and RD-VBA both define (`Option Compare`, `Attribute` declarations, …)
grows.

### Expression evaluation

Each static semantics rule for a node kind is documented in isolation, in the section that implements its MS-VBAL
counterpart. A node kind's own static semantics rule does not recurse into its own children to produce the
`operandDeclaredTypes` it is given.

[ExpressionStaticSemanticsEvaluator](../api/RDCore.SDK.Semantics.Static.ExpressionStaticSemanticsEvaluator.html) is
the component that recurses into child expressions, given any (possibly deeply nested) expression:

1. It dispatches by the node's own type; for an operator node, it dispatches by the operator's token.
2. It evaluates children first.
3. It short-circuits on the first error.

With `ExpressionStaticSemanticsEvaluator`, an expression such as `Foo.Bar.Baz` or `x + 1` resolves end to end.

|Case|Outcome in `ExpressionStaticSemanticsEvaluator`|
|---|---|
|A node kind with no static semantics rule|Defers to [VBUnknownType](../api/RDCore.SDK.Model.Types.VBUnknownType.html) rather than erroring.|
|An operator token with no mapped rule, such as `Mod` ([**RD-VBAL §5.6.9.3** Arithmetic Operators](rd-vbal.5.6.9.3.arithmetic-operators.md))|Defers to `VBUnknownType` rather than erroring.|

Every type-comparing static-semantics rule in RDCore follows the same convention: an unresolved declared type is
deferred, not flagged (see [VBC09320](../diagnostics/vbc09320.md)).

See [**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md) for the static
semantics of a simple name expression.


## 5.0.2 Runtime Semantics

The role of _runtime semantics_ depends on the type of node being evaluated:

|Node|At runtime|
|---|---|
|_Directives_|Evaluate to their static / compile-time value.|
|_Literal_ or _constant expressions_|Evaluate to their static / compile-time value ([**RD-VBAL §5.6.5** Literal Expressions](rd-vbal.5.6.5.literal-expressions.md)).|
|_Operators_|Evaluate a [VBTypedValue](../api/RDCore.SDK.Model.Values.Abstract.VBTypedValue.html) from their _operands_ ([**RD-VBAL §5.6.9** Operator Expressions](rd-vbal.5.6.9.operator-expressions.md)).|
|_Statements_|Induce _side-effects_ to _program_, _global_, or _host environment_ state ([**RD-VBAL §5.4** Procedure Bodies and Statements](rd-vbal.5.4.procedure-bodies-and-statements.md)).|

🎯 Evaluation returns an evaluation result record,
[RuntimeSemanticsEvaluationResult](../api/RDCore.SDK.Runtime.Shared.RuntimeSemanticsEvaluationResult.html), that
describes and encapsulates either the evaluation result or runtime error metadata
([**RD-VBAL §3.0.3** Binding Contexts](rd-vbal.3.0.3.binding-contexts.md)).

See [**RD-VBAL §5.6.9.2** Simple Data Operators](rd-vbal.5.6.9.2.simple-data-operators.md) for the operator
evaluation pipeline and computation in the effective type.

See [**RD-VBAL §5.6.9.3** Arithmetic Operators](rd-vbal.5.6.9.3.arithmetic-operators.md),
[**RD-VBAL §5.6.9.5** Relational Operators](rd-vbal.5.6.9.5.relational-operators.md) (including the `Variant`
String/Numeric comparison exception) and [**RD-VBAL §5.6.9.8** Logical Operators](rd-vbal.5.6.9.8.logical-operators.md)
for the evaluation of each operator family.

See [**RD-VBAL §5.5.1.2** Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md) for let-coercion, including
numeric let-coercion and `Variant` let-coercion and storage.

See [**RD-VBAL §6.1.1** Predefined Enums](rd-vbal.6.1.1.predefined-enums.md) (§6.1.1.16 `VbVarType`) for the
`VarType` and COM interop shape of a `Variant`.

See [**RD-VBAL §5.4** Procedure Bodies and Statements](rd-vbal.5.4.procedure-bodies-and-statements.md) for statement
evaluation.


## 5.0.3 Semantic Analysis

The _analysis pipeline_ of all operators follows a fixed sequence of three steps:

1. The _effective type_ of the operation is determined, based on the _declared type_ of its _operands_. This step
   invokes the same methods as runtime semantics to determine the effective type.
2. Validation: all non-null operands (non-[VBNullValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBNullValue.html))
   are let-coerced to the determined _effective type_ of the operation. This step uses the same runtime semantics
   let-coercion provider as the evaluation pipeline
   ([**RD-VBAL §5.5.1.2** Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md)).
3. Semantic evaluation: a templated method evaluates a _semantic result_, having the _execution context_ and the
   validated _operands_ to work with, without inducing any side-effects.

The `Analyze` method yields a _builder_,
[ISemanticContextContributor&lt;,&gt;](../api/RDCore.SDK.Semantics.Builders.ISemanticContextContributor-2.html), that
builds a _semantic context_ for the specific _expression node_. The semantic context includes the results of each
analysis step:

|Semantic context member|Encapsulates|
|---|---|
|[DetermineOperatorEffectiveTypeResult](../api/RDCore.SDK.Runtime.Shared.DetermineOperatorEffectiveTypeResult.html)|The result of the first step.|
|[LetCoercionAnalysisContext](../api/RDCore.SDK.Semantics.Analysis.LetCoercionAnalysisContext.html)|The aggregated evaluation stack and outcome of all let-coercion operations, with their respective _semantic flags_.|
|`RuntimeSemanticsEvaluationResult`|The result of the operation.|

The language core features an analytical pipeline that attaches detailed _semantic flags_ to abstract syntax tree
(AST) nodes ([**RD-VBAL §1.1.3** Core Semantic Flags](rd-vbal.1.1.3.core-semantic-flags.md)).

> 👉 The role of the `Analyze` method at this level is to report the _semantic facts_ of an operation. These facts
> usually cannot be inferred from the operands or _effective type_ alone.

Semantic flags are _facts_, not _opinions_.

### The semantic model

What the analysis finds out about a procedure is described by an immutable
[ProcedureSemanticModel](../api/RDCore.SDK.Semantics.ProcedureSemanticModel.html), and that of a module by a
[ModuleSemanticModel](../api/RDCore.SDK.Semantics.ModuleSemanticModel.html). A model is built by the pass that analyzed the code;
it is never written back onto the syntax tree or onto a value.

The first fact a model holds is the _compile errors_ of the _static pass_ (**RD-VBAL §5.0.1**), which is one walk over a
procedure body: `StatementStaticSemanticsEvaluator`. Given the symbols of a workspace it evaluates every expression, and
the coercion of every assignment; with none, `CheckStructure` checks what needs no name resolution:

|Rule|Reported as|
|---|---|
|An `Exit` statement is where it may be (**MS-VBAL §5.4.2.5**, `.7`, `.17`-`.19`).|[VBC09312](../diagnostics/vbc09312.md)–[VBC09315](../diagnostics/vbc09315.md), [VBC09332](../diagnostics/vbc09332.md)|
|A label is defined once (**MS-VBAL §5.4.1.1**).|`DuplicateLabelDefinition`|
|A jump names a label that is defined.|`LabelNotDefined`|
|A statement exists in the language: a bare `Print` is a statement of BASIC only.|`SubOrFunctionNotDefined`|

A module is not valid for having valid procedures: what it declares is checked once for the module, by
`DeclarationStaticSemanticsEvaluator`, and a `ModuleSemanticModel` holds those errors (`DeclarationErrors`) beside the
model of each procedure, so that it is valid only when both are.

|Rule|Reported as|
|---|---|
|A name is declared once in the scope of a module; the accessors of a property are the one declaration of it.|`DuplicateDeclaration`|
|A declared type is a name that resolves to a type (**MS-VBAL §5.6.4**): of a variable, constant, parameter, result or local.|`UserDefinedTypeNotDefined`: _The declared type 'Missing' could not be resolved._|
|What a class module declares about events (**MS-VBAL §5.2.4.3**, `§5.2.3.1.2`, `§5.3.1.8`).|`ClassModuleEventSemantics`|
|What its `Implements` directives require of it (**MS-VBAL §5.2.4.2**, `§5.3.1.9`).|`ImplementsSemantics`|

An unknown type is a type that is not known _yet_; a name that did not resolve is kept as one (`VBUnresolvedType`, which is an
unknown type in every other respect) so that the error can say which. The host defines a module at a time, and a name that
does not resolve while the module that declares it is defined may name a module defined after it, so the rule is asked for
(`DeclarationRules.DeclaredTypes`) only when everything the declaration can see is defined: the name is then resolved again,
and is an error if it still does not.

A statement inside an excluded `#If` branch is not analyzed and defines no label (**MS-VBAL §3.4.2**). Lowering a body to
instructions ([**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)) reports exactly these errors, by calling
`CheckStructure`: the rules are written in one place, and lowering only acts on the outcome (a jump that lands nowhere has no
target; an `Exit` that is not where it may be has no instruction).

### Diagnostics

> 🧩 The role of _analyzers_ in extensions like **RDCore.Diagnostics** is to inspect the flags and errors in
> semantic contexts, and issue _diagnostics_ ([**RD-VBAL §1.1.4** Core Diagnostics](rd-vbal.1.1.4.core-diagnostics.md)).

|Diagnostic|Use|
|---|---|
|**Error**|Reserved for coded _syntax/compilation_ and _runtime/application_ errors.|
|**Warning**|Used carefully: for flagging _potential bugs_ or logical errors causing unexpected or unintended behavior, or _severe_ performance issues.|
|**Hint** or **suggestion**|May be as opinionated as needed.|

A warning severity must account for a _treat warnings as errors_ host environment configuration setting: a
diagnostic that should not break a build is not a _warning_
([**RD-VBAL §2.6** Diagnostics](rd-vbal.2.6.diagnostics.md)).

### Type coercion

**RDCore** implements the MS-VBAL type-coercion rules through _pattern-matching_ against its type system
([**RD-VBAL §5.5** Implicit coercion](rd-vbal.5.5.implicit-coercion.md)).

The rules are implemented verbatim, except for the resolved specification errors noted in
[**RD-VBAL §5.5.1.2** Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md). Divergences from MS-VBAL caused by
obvious copy/paste and transcription errors in the MS specification are resolved in favour of the evident intent.

Anything in MS-VBAL that implicitly depends on the Windows Registry, ActiveX, or MSForms is out of scope for the
RD-VBA run-time; such requirements are also resolved in favour of the evident intent.

---
## In this section

|§|Title|MS-VBAL|
|---|---|---|
|5.1|[Module Body Structure](rd-vbal.5.1.module-body-structure.md) — *reserved*|[§5.1](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/700b6540-33f4-4c9b-a151-cf1ee646e5ee)|
|5.2|[Module Declaration Section Structure](rd-vbal.5.2.module-declaration-section-structure.md) — *reserved*|[§5.2](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/501a2cb4-21a0-4982-9e5d-29fbb1c624f5)|
|5.3|[Module Code Section Structure](rd-vbal.5.3.module-code-section-structure.md) — *reserved*|[§5.3](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/10d7f639-e0e0-4d05-be3a-cff2e542cd35)|
|5.4|[Procedure Bodies and Statements](rd-vbal.5.4.procedure-bodies-and-statements.md)|[§5.4](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/618815bc-c68b-4488-8082-ed1b36fac6d4)|
|5.5|[Implicit coercion](rd-vbal.5.5.implicit-coercion.md)|[§5.5](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/72801139-6d53-4492-ad30-4d4363d6c6f9)|
|5.6|[Expressions](rd-vbal.5.6.expressions.md) — *reserved*|[§5.6](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/65a708dc-e805-442e-8b9c-c02acb6254b2)|

---
> ⏮️ [**RD-VBAL §4.1** VBIDE Synchronization](rd-vbal.4.1.vbide-synchronization.md) | ⏭️ [**RD-VBAL §5.1** Module Body Structure](rd-vbal.5.1.module-body-structure.md)
