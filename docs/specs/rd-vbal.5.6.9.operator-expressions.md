# 5.6.9 Operator Expressions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.9** Operator Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4d159cdd-6796-43c7-8a61-021cc81d0594).

At runtime, operators evaluate a [VBTypedValue](../api/RDCore.SDK.Model.Values.Abstract.VBTypedValue.html) from their
_operands_ ([**RD-VBAL §5.0** Semantics](rd-vbal.5.0.semantics.md)).

See [**RD-VBAL §3.3.0** Operator Expressions](rd-vbal.3.3.0.operators.md) for the operator node hierarchy, and
[**RD-VBAL §5.6.9.2** Simple Data Operators](rd-vbal.5.6.9.2.simple-data-operators.md) for the evaluation pipeline
all operators share.

---
## In this section

|§|Title|MS-VBAL|
|---|---|---|
|5.6.9.1|[Operator Precedence and Associativity](rd-vbal.5.6.9.1.operator-precedence-and-associativity.md) — *reserved*|[§5.6.9.1](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/24cb214a-ef26-4e36-973d-43f715c8b127)|
|5.6.9.2|[Simple Data Operators](rd-vbal.5.6.9.2.simple-data-operators.md)|[§5.6.9.2](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/dccbdeae-5b2e-4c2e-857e-1ad9b861e196)|
|5.6.9.3|[Arithmetic Operators](rd-vbal.5.6.9.3.arithmetic-operators.md)|[§5.6.9.3](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e070115f-8d40-40cf-ac6d-ab18b9c6c906)|
|5.6.9.4|[& Operator](rd-vbal.5.6.9.4.ampersand-operator.md)|[§5.6.9.4](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9f072ffc-e943-4fcc-a4d0-f3c7db96abd9)|
|5.6.9.5|[Relational Operators](rd-vbal.5.6.9.5.relational-operators.md)|[§5.6.9.5](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f8acd631-55c1-4199-bc1e-022aaab6d9c8)|
|5.6.9.6|[Like Operator](rd-vbal.5.6.9.6.like-operator.md)|[§5.6.9.6](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/792d4f80-72c1-49da-98a7-37d5783a8615)|
|5.6.9.7|[Is Operator](rd-vbal.5.6.9.7.is-operator.md) — *reserved*|[§5.6.9.7](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4730e541-8747-4259-ae76-128c7024b3b8)|
|5.6.9.8|[Logical Operators](rd-vbal.5.6.9.8.logical-operators.md)|[§5.6.9.8](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2d70780d-6e99-47d6-9759-2b3a46b8e862)|

---
> ⏮️ [**RD-VBAL §5.6.8** New Expressions](rd-vbal.5.6.8.new-expressions.md) | ⏭️ [**RD-VBAL §5.6.9.1** Operator Precedence and Associativity](rd-vbal.5.6.9.1.operator-precedence-and-associativity.md)
