# [RD-VBAL]: VBA Language Platform Specification

> [!NOTE]
> Cette section n'est disponible qu'en anglais.    
> _This section is only available in English_.

---
## Table of Contents

- 1. [Introduction](rd-vbal.1.0.introduction.html)
  - 1.1. [Philosophy](rd-vbal.1.1.philosophy.html)
- 2. [RD-VBA Computational Environment](rd-vbal.2.0.computational-environment.html)
  - 2.1. [Implicit Storage](rd-vbal.2.1.implicit-storage.html)
  - 2.2. [Project Structure](rd-vbal.2.2.rdproj-structure.html)
  - 2.3. [Application Host](rd-vbal.2.3.application-host.html)
  - 2.4. [Static Types](rd-vbal.2.4.static-types.html)
  - 2.5. [Runtime Values](rd-vbal.2.5.runtime-values.html)
  - 2.6. [Diagnostics](rd-vbal.2.6.diagnostics.html)
- 3. [Abstract Syntax Tree](rd-vbal.3.0.syntax-tree.html)
  - 3.1. [Attributes and Directives](rd-vbal.3.1.attributes-directives.html)
  - 3.2. [Literal Expressions](rd-vbal.3.2.0.literals.html)
  - 3.3. [Operators](rd-vbal.3.3.0.operators.html)
  - 3.4. [Statements](rd-vbal.3.4.0.statements.html)
  - 3.5. [Instructions](rd-vbal.3.5.0.instructions.html)
- 4. [Program Structure](rd-vbal.4.0.program-structure.html)
- 5. [Semantics](rd-vbal.5.0.semantics.html)
- 6. [Standard Library](rd-vbal.6.0.standard-library.html)

**RD-VBA** is an implementation of the **MS-VBAL specification** that is independent from its historical **MS-VBA** runtime host. **RD-VBAL** is the name of the specification/documentation of the _language server platform_, which _includes_ the **RD-VBA** _language core_ but is wider than the sole language specification.

🎯 **The formalization of _RD-VBAL_ is a work in progress**.  

This platform specification presents a similar _technical prose_ style as its inspirational _Open Spec_ source material.

---
## Intellectual Property Rights Notice for Open Specifications Documentation

> [!IMPORTANT]
> **This documentation IS NOT A REVISION of the MS-VBAL specification**.

The publisher of the **RDCore** platform project and of _this present documentation_, is claiming the rights described in the following paragraph of the _Intellectual Property Rights Notice for Open Specifications Documentation_ section (emphasis added):

> **Copyrights**. This documentation [MS-VBAL] is covered by Microsoft copyrights. Regardless of any other terms that are contained in the terms of use for the Microsoft website that hosts this documentation, you can **make copies of it in order to develop implementations of the technologies that are described** in this documentation [MS-VBAL] and can distribute portions of it in your implementations that use these technologies **or in your documentation as necessary to properly document the implementation**. **You can also distribute in your implementation**, with or without modification, any schemas, IDLs, or code samples that are included in the documentation. **This permission also applies to** any documents that are referenced in the Open Specifications documentation.
- [MS-VBAL: VBA Language Specification](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d5418146-0bd2-45eb-9c7a-fd9502722c74#intellectual-property-rights-notice-for-open-specifications-documentation)


✅ **Challenge: Accepted**.

---
## Revisions

|Date|Version|Description|
|---|---|---|
|2026-06-25|1.0|Initial public version|
|2026-09-06|1.1|§2.3.1.2 session services (`IRuntimeSession` root; `IVirtualHeap` removed); §2.5.2.1.2 array values are a flat column-major store; §3.2.0.1 numeric literal types (type-declaration characters); §5.0.2.1 results are computed in the effective type; §5.0.2.2 let-coercion provider/strategy dispatch and the MS-VBAL-divergence principle; §2.5.2.1.3 UDT values are addressable IDs|
|2026-09-09|1.2|§2.6 Diagnostics — the `VBC`/`VBR`/`VBA`/`RDC` code families, help-URL convention, and the LSP-pull provider pipeline (`textDocument/diagnostic`; the `DiagnoseDocument` provider capability; result identity and the version staleness gate)|
|2026-09-13|1.3|§3.4 Statements — block/simple/file statement node families, each cross-referenced to its MS-VBAL section|
|2026-09-23|1.4|§3.5 Instructions — the `InstructionList`/`Instruction` model, lowering, and execution (`ProcedureExecutor`'s fetch/decode loop, `ICallStackFrame.Pc`, Let/Set-assignment statement dispatch)|
|2026-09-24|1.5|§3.5.4/§3.5.5 — procedure invocation (`IProcedureInvoker`/`RuntimeProcedureInvoker`, `Call`/bare-call/bare-`Sub`/`Function`/`Property Get`), `ByRef` parameter binding (`CallStackFrame.PushByRef`, `ISymbolResolver.TryGetAddress`), `Function`/`Property Get` return values (`ICallStackFrame.ReturnValue`, the function result variable), hoisted `Dim`/`Static` locals (`VBProcedureMemberSymbol.Locals`/`VBReturningMemberSymbol.Locals` riding on the procedure symbol like `Parameters`, `RuntimeProcedureInvoker.HoistLocals`, `ISymbolResolver.TryAllocate` for a `Static` local's own module-extent storage), and named arguments/`Optional` parameters (`RuntimeExpressionEvaluator.MapArguments`, `VBParameterSymbol.DefaultValue`, errors 448/449/450)|
|2026-09-25|1.6|§3.5.4 — `ParamArray` (`RuntimeExpressionEvaluator.CollectParamArrayArguments`, a fresh 0-based `Variant` array), closing two root-caused prerequisite gaps: zero-size storage (`SessionStorage.TryAllocate` mints its own address for a non-positive size instead of ever reaching the allocator — `Nothing`/`Null`/`Empty`/an uninitialized array/an empty `ParamArray` alike) and `Variant` handle handling (`VBRuntimeVariantValue` now boxes the wrapped `VBTypedValue` itself, `VBVariantValue`'s own constructor self-binds to it, `SymbolAddressTable.FreshBinding` re-boxes it fresh on every store, and `LetCoercionRuntimeSemanticsProvider.EvaluateLetCoercionSemantics` unwraps a `Variant` source before dispatch so every strategy's own direct cast sees the real wrapped value). Follow-up Variant-hardening pass, same root cause (a `VBVariantValue`'s own `TypeInfo` mirrors its wrapped value's, so a direct cast/pattern-match downstream breaks the instant the operand is a real, non-default `Variant`): fixed in the arithmetic/relational/concat operators' own operand validation (`OperatorRuntimeSemantics.LetCoerceNonNullOperand`'s TypeInfo-equality short-circuit never skips a `VBVariantValue` operand anymore), in `SetCoercionRuntimeSemantics` (a `Variant` wrapping an object now unwraps before the `VBObjectValue` pattern-match), and in three more array-holding sites (`RuntimeExpressionEvaluator.EvaluateIndex`, `ProcedureExecutor.ExecuteForEachOpener`, `BinaryConcatOperatorRuntimeSemantics.IsByteArray`) so `v(0)`/`For Each x In v`/`v1 & v2` all work on a `Variant` holding an array the same as on a declared one. Also implemented MS-VBAL §5.6.9.5's own Variant String/Numeric comparison exception for real (previously a dead, never-firing analysis flag and no actual runtime behavior): a numeric-holding `Variant` compared against a String-holding `Variant` is always considered less than it, regardless of actual values, short-circuited in `BinaryRelationalOperatorRuntimeSemantics` before normal coercion would otherwise try (and fail) to coerce the String to a number. §6.1.1.16 — `VbVarType`'s own COM `VARENUM`-compatible tag space moved from a stdlib-only declaration to `RDCore.SDK.Model.Values.Runtime.VBVarType`, the core value model, with a real `VBType`→`VBVarType` mapping (`VBVarTypeExtensions.VarType`, including an array's own tag combined with its element type's, recursively) that `VBVariantValue` now actually computes on construction (previously always hardcoded to `Empty`, a real bug: `VBVariantValue.Value` was a separately-settable property the constructors never touched, now computed straight from `Handle`, the single source of truth). Groundwork for `IDispatch`/COM interop: `VBClassModuleSymbol.AutomationKind` (default `Dispatch`, every RD-VBA class module today) distinguishes an Automation-capable (`VT_DISPATCH`) class from a future `IUnknown`-only (`VT_DISPATCH`'s `vbDataObject` sibling, tag 13) one nothing constructs yet — `VarType` consults it for any `VBClassType` with a known class; a generic `VBObjectType` reference (a live object's concrete class is only known by looking up the instance) still defaults to `VT_DISPATCH`, the only sound default absent that lookup. Closed the suite's last two pre-existing `[Ignore]`d gaps: §5.4.2.10 — `ExecuteCaseHeader`'s own Null-selector short-circuit unwraps a `Variant` selector first (a `Null` can only ever reach `Select Case` through one — a directly Long-declared local can never hold it), the same fix pattern as this row's other array/object-holding sites; and §5.5.1.2.1 — a `Date` source coerced to a numeric or `Boolean` destination now actually reports `ConversionSemanticFlags.DateSerial` (`VBNumericLetCoercionTypeRuntimeSemantics.DateSerialFlagsOf`, reused by `VBBooleanLetCoercionRuntimeSemantics`): the flag existed only in `VBDateLetCoercionRuntimeSemantics`'s own dead code, unreachable since the provider dispatches by destination type and that strategy only ever runs for `Date` as the *destination*.|
| | | |

