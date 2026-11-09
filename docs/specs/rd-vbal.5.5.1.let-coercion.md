# 5.5.1 Let-coercion

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.5.1** Let-coercion](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/74614d3e-7068-4c33-b149-029534522472).

_Let-coercion_ is the implicit conversion applied to an operand, or to an assignment RHS, so that its value fits a
required _destination declared type_. Its run-time semantics are specified in
[**RD-VBAL §5.5.1.2** Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md).

Parentheses that are not around an operand of an operator are an explicit let-coercion operator in the syntax tree
(`"__c()_op"`, `UnaryLetCoerceOperatorStaticSemantics`):
the coercion to a value that MS-VBAL §5.6.6 gives a parenthesized expression is written out, which makes it a language
core extension ([**RD-VBAL §1.1.2** Language Core Extensions](rd-vbal.1.1.2.language-core-extensions.md)). See
[**RD-VBAL §3.3.1** Unary Operators](rd-vbal.3.3.1.unary-operators.md).

---
## In this section

|§|Title|MS-VBAL|
|---|---|---|
|5.5.1.1|[Static semantics](rd-vbal.5.5.1.1.static-semantics.md)|[§5.5.1.1](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4ef84713-33da-445b-942e-345982b3267d)|
|5.5.1.2|[Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md)|[§5.5.1.2](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3e5fb49f-eb20-4562-a6bd-4a26dc5fa733)|
