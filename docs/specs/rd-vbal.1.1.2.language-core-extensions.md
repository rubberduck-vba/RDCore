# 1.1.2 Language Core Extensions

Where the specification defines implicit behaviours, RD-VBA may choose to make those behaviours explicit, provided that all three of the following conditions hold:

1. The underlying semantics remain unchanged.
2. Existing code continues to behave identically.
3. The added explicitness improves clarity, diagnostics, or tooling.

The following are *language core extensions*:

|Language core extension|See|
|---|---|
|The comment annotations syntax from the *legacy Rubberduck VBIDE add-in*.|[**RD-VBAL §3.0.1** Token Semantics](rd-vbal.3.0.1.token-semantics.md)|
|An explicit let-coercion operator (`"__c()_op"`, `UnaryLetCoerceOperatorStaticSemantics`) for parentheses that are not around an operand of an operator. It tells the coercion to a value that MS-VBAL §5.6.6 gives a parenthesized expression apart from the grouping of an operand (`"__p()_op"`).|[**RD-VBAL §3.3.1** Unary Operators](rd-vbal.3.3.1.unary-operators.md)|
|A unary `+`, which MS-VBAL's grammar does not have, read and erased as the VBE erases it.|[**RD-VBAL §5.6.9.3** Arithmetic Operators](rd-vbal.5.6.9.3.arithmetic-operators.md)|

These extensions do not *alter* the language; they *reveal* it.

> **A valid language core extension makes explicit what was implicit.**
>
> **An invalid language core extension introduces new semantics.**

## Valid Examples

**`Option Strict`.** A new `Option Strict` *directive* that *restricts* existing static and/or runtime semantics, by issuing error diagnostics or new, explicitly specified compile-time errors, is a valid language core extension. It is valid only if no new *tokens* are introduced without also introducing the native capability to output them as *comment annotations*, keeping the output strictly compliant with MS-VBAL. Compile-time error codes for language core extensions are described in [**RD-VBAL §1.1.4** Core Diagnostics](rd-vbal.1.1.4.core-diagnostics.md).

**Deferred types.** VBA ubiquitously permits late binding and *duck typing*. Leveraging them to introduce and surface inherent *deferred types* to *design-time* symbols is a valid language core extension. Deferred types enable LSP-level enhanced capabilities, notably around auto-completion lists; see [**RD-VBAL §2.4.4** Deferred Types](rd-vbal.2.4.4.deferred-types.md).

## Invalid Examples

**Reflection.** Exposing symbols for *descriptors* and other internal meta-types is an invalid language core extension. Such symbols could introduce *reflection semantics* to the language.

**Deferred execution.** MS-VBAL explicitly specifies every `Variant` input in the [**MS-VBAL §6** VBA Standard Library](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/c645c903-9bd4-4849-8735-3136e867536a) as an *expression to be evaluated*. Building on this to introduce *deferred execution semantics* to the language (leveraging meta-types to introduce new semantics) is an invalid language core extension.

Deferred execution semantics would effectively make *functions* a first-class RD-VBA *runtime entity* that can be passed around as *values*. Functions should not be made first-class values without thorough consideration of the implications for the rest of the semantic model.

## Platform-level Extensions

The invalid examples above are not invalid extensions in the *RDCore ecosystem*. Each of them could be a [*platform-level* extension](rd-vbal.1.1.1.platform-extensions.md).
