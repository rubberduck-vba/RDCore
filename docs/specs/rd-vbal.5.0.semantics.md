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

Every rule is evaluated against a [StaticEvaluationContext](../api/RDCore.SDK.Semantics.Static.Abstract.StaticEvaluationContext.html) — the [ISymbolResolver](../api/RDCore.SDK.Runtime.Abstract.Execution.ISymbolResolver.html) and the [LexicalScope](../api/RDCore.SDK.Model.Symbols.LexicalScope.html) an expression is lexically found in (see §2.3.1.2 for how a scope is resolved). Module-level facts a rule needs — today, whether the enclosing module declares `Option Explicit` — are not parameters of this context; they live on [ModuleDirectives](../api/RDCore.SDK.Model.Symbols.ModuleDirectives.html), reachable from any scope via `LexicalScope.EnclosingModuleDirectives()`. This keeps the context's shape stable as the directive surface MS-VBAL and RD-VBA both define (`Option Compare`, `Attribute` declarations, …) grows over time.

> [!NOTE]
> Each subsection below documents one node kind's own rule in isolation — none of them recurse into
> their own children to produce the `operandDeclaredTypes` they're given. [ExpressionStaticSemanticsEvaluator](../api/RDCore.SDK.Semantics.Static.ExpressionStaticSemanticsEvaluator.html)
> is the piece that does: given any (possibly deeply nested) expression, it dispatches by the node's
> own type — and, for an operator node, by its token — evaluating children first and short-circuiting
> on the first error, so `Foo.Bar.Baz` or `x + 1` resolves end to end instead of only being exercised
> with hand-fed operand types. A node kind with no rule yet, or an operator token with no mapped rule
> (`Mod`), defers to `VBUnknownType` rather than erroring.

### 5.0.1.1 Simple Name Expressions
> [!NOTE]
> This section describes the implementation of **MS-VBAL §5.6.10 Simple Name Expressions**.

The declared type of a _simple name expression_ is the declared type of the entity its identifier
resolves to, per the ordered lookup of §2.3.1.2: a `Symbol` that determines its own declared type
([ITypedSymbol](../api/RDCore.SDK.Model.Symbols.Abstract.ITypedSymbol.html), unifying bound and unbound
typed symbols) yields that type directly — a bare procedure reference yields its return type (or
`VBVoidType` for a `Sub`, already the type its own symbol carries).

Three outcomes fork on the resolver's result:
- **Ambiguous** (`Duplicate`/`Ambiguous`, see §2.3.1.2) → an `Error` carrying
  [AmbiguousName](../api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) or
  [DuplicateDeclaration](../api/RDCore.SDK.Model.Errors.VBCompileErrorId.html).
- **Unresolved, under `Option Explicit`** → an `Error` carrying
  [VariableNotDefined](../api/RDCore.SDK.Model.Errors.VBCompileErrorId.html).
- **Unresolved, otherwise** → `Success(VBUnknownType)`. MS-VBA permits an implicit `Variant`
  declaration here; RD-VBA defers the actual guess to a later type-inference pass
  ([IVBInferableType](../api/RDCore.SDK.Model.Types.Complex.VBDeferredType.html)) rather than deciding
  it in this rule.


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
3. Evaluation: a templated method evaluates a result from the validated _operands_.

The sequence may be aborted at any point to return an _error result_ that encapsulates [VBRuntimeErrorInfo](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorInfo.html) error metadata.

**Computation in the effective type.** The result of step 3 is computed in the _effective type_'s own
representation — `Long` arithmetic in 64-bit integers, `Currency`/`Decimal` in `decimal`, `Single` in
`float`, and so on — never through a `Double` intermediate. Arithmetic runs in a _checked_ context, so
an integral or fixed-point result that does not fit the effective type raises
[Overflow](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorId.html) rather than wrapping or silently
narrowing; an integral division or `Mod` by zero raises
[DivisionByZero](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorId.html). The `^` operator is the sole
exception — its effective type is always `Double`, and it is evaluated as IEEE-754 exponentiation.
Relational operators compare in the effective type (integral comparisons in 64-bit integers,
fixed-point in `decimal`) and yield a [VBBooleanValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBBooleanValue.html);
a `NaN` operand raises [Overflow](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorId.html). Logical
operators compute bitwise in the effective integral type (`Boolean` over its `-1`/`0` representation).

**The `Variant` String/Numeric comparison exception (MS-VBAL §5.6.9.5).** When both relational
operands are `Variant`, one originally holding a `String` value and the other a numeric value, the
numeric operand is always considered less than the `String` operand — regardless of their actual
values, and without ever attempting to coerce the `String` to a number (which would fail, or succeed
incorrectly, depending on its content). `BinaryRelationalOperatorRuntimeSemantics` detects this before
normal effective-type determination and coercion ever run, reducing it to a synthetic `Integer` rank
(`0` for the numeric side, `1` for the `String` side) that the operator's own ordinary `Integer`
evaluation branch then compares for real — no bespoke evaluation path needed.


### 5.0.2.2 Let-Coercion
> [!NOTE]
> This section describes the implementation of **MS-VBAL §5.5.1.2 Let-coercion (run-time semantics)**.

_Let-coercion_ is the implicit conversion applied to an operand (or an assignment RHS) so that its
value fits a required _destination declared type_. It is driven by a let-coercion _provider_ that
dispatches to a per-_destination-type_ strategy resolved by walking the destination
[VBType](../api/RDCore.SDK.Model.Types.Abstract.VBType.html)'s base-type chain — one strategy keyed
on `VBNumericType` serves every concrete numeric type. The
provider maintains a coercion frame stack so that a _recursive let-coercion_ (a strategy that must
coerce through an intermediate type, e.g. `Date → Double → Integer`) is detected and reported as
[OutOfStackSpace](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorId.html) rather than overflowing the
call stack. Each step yields a [LetCoercionResult](../api/RDCore.SDK.Runtime.Shared.LetCoercionResult.html)
that is `Success` (a coerced [VBTypedValue](../api/RDCore.SDK.Model.Values.Abstract.VBTypedValue.html)),
`Error` ([TypeMismatch](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorId.html) or `Overflow`), or
`NotApplicable`.

**Numeric let-coercion (MS-VBAL §5.5.1.2.1).** Coercion between numeric types validates that the
source value is within the destination's representable range (`Overflow` otherwise), then:

- widening, and narrowing to a wider-or-equal integral type: the value is copied, converted to the
  destination's representation;
- narrowing a floating-point or fixed-point value to an integral type: the value is rounded to the
  nearest integer using **round-half-to-even ("banker's rounding", MS-VBAL §5.5.1.2.1.1)** before
  conversion.

> [!NOTE]
> **RD-VBAL diverges from MS-VBAL** in the integral → floating-point block of §5.5.1.2.1: the MS
> document specifies it as a verbatim copy of the preceding (narrowing) block, including the
> finite-value and banker's-rounding checks — conditions no integer value can meet, for a conversion
> that is unambiguously widening. RD-VBAL treats it as a plain widening copy. Divergences of this
> kind (obvious copy/paste and transcription errors in the MS specification, and anything that
> implicitly depends on the Windows Registry, ActiveX, or MSForms — all out of scope for the
> run-time) are resolved in favour of the evident intent.

**`Variant` let-coercion and storage (MS-VBAL §5.5.1.2.12).** Any value except a class or `Nothing`
Let-coerces to `Variant` as a copy, wrapped in a
[VBVariantValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBVariantValue.html). A `VBVariantValue`'s
own `TypeInfo` deliberately mirrors its wrapped value's — so ordinary destination-type dispatch (both
here and in operator/effective-type determination) picks the same strategy it would for the
unwrapped value — but its runtime *instance* stays a `VBVariantValue`, wrapping the whole value, not
just a scalar. Storage round-trips it as a
[VBRuntimeVariantValue](../api/RDCore.SDK.Model.Values.Runtime.VBRuntimeVariantValue.html) box (the
same pattern a
[VBArrayValue](../api/RDCore.SDK.Model.Values.Abstract.VBArrayValue.html) uses via
`VBRuntimeArrayValue`), so a `Variant` read back from a variable, array element, or field carries the
exact value that was stored — never a fresh, unrelated `Empty`.

Because `TypeInfo` mirrors the wrapped value, any code that short-circuits on a `TypeInfo` match, or
pattern-matches a `VBTypedValue` operand against a concrete value type directly, must unwrap a
`VBVariantValue` first (recursively — a `Variant` may wrap another `Variant`) or it will see the box
instead of the value. `LetCoercionRuntimeSemanticsProvider.EvaluateLetCoercionSemantics` does this
once, centrally, for every let-coercion; `OperatorRuntimeSemantics.LetCoerceNonNullOperand` never
skips its own "already the right type" short-circuit for a `Variant` operand;
`SetCoercionRuntimeSemantics` unwraps before its own object pattern-match; and
`RuntimeExpressionEvaluator.EvaluateIndex`, `ProcedureExecutor.ExecuteForEachOpener`, and
`BinaryConcatOperatorRuntimeSemantics.IsByteArray` each unwrap before matching a wrapped array.

**`VarType` and COM interop shape (MS-VBAL §6.1.1.16).** A `Variant`'s own COM `VARENUM`-compatible
tag — `VBVarType`, the same numeric values `VarType()` reports and OLE Automation marshals a
`VARIANT` against — is computed from the wrapped value's declared type by `VBVarTypeExtensions.VarType`
and carried on its `VBRuntimeVariantValue` box, so it round-trips through storage alongside the value
itself. An array's own tag is `VBArray` combined with its element type's own tag, recursively. A
`VBClassType` with a known class module defers to `VBClassModuleSymbol.AutomationKind` —
`Dispatch` (`VT_DISPATCH`, true of every RD-VBA class module today) or `Unknown` (`VT_DISPATCH`'s
`vbDataObject` sibling, `IUnknown`-only — groundwork for a future external/COM reference kind, not
constructed anywhere yet); a generic `VBObjectType` reference — a live object's concrete class is only
knowable by looking up the actual instance, which this mapping has no access to — defaults to
`Dispatch`, the only sound default absent that lookup.

### 5.0.2.3 Statement Evaluation
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

**RDCore** implements the MS-VBAL type-coercion rules through _pattern-matching_ against its type
system, verbatim except for the resolved specification errors noted in §5.0.2.2.

---
> ⏮️ [**RD-VBAL §4.0** Program Structure](rd-vbal.4.0.program-structure.html) | ⏭️ [**RD-VBAL §6.0** Standard Library](rd-vbal.6.0.standard-library.html)
