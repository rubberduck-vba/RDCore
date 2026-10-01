# 5.5 Implicit coercion

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.5** Implicit coercion](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/72801139-6d53-4492-ad30-4d4363d6c6f9).

**RDCore** implements the MS-VBAL type-coercion rules through _pattern-matching_ against its type system.

The rules are implemented verbatim, except for the resolved specification errors noted in
[**RD-VBAL §5.5.1.2.1** Let-coercion between numeric types](rd-vbal.5.5.1.2.runtime-semantics.md#55121-let-coercion-between-numeric-types).

---
## In this section

|§|Title|MS-VBAL|
|---|---|---|
|5.5.1|[Let-coercion](rd-vbal.5.5.1.let-coercion.md)|[§5.5.1](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/74614d3e-7068-4c33-b149-029534522472)|
|5.5.2|[Set-coercion](rd-vbal.5.5.2.set-coercion.md) — *reserved*|[§5.5.2](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4a496c57-5e6f-4f38-9cf9-ef804ea04350)|

---
> ⏮️ [**RD-VBAL §5.4.5.13** Name Statement](rd-vbal.5.4.5.13.name-statement.md) | ⏭️ [**RD-VBAL §5.5.1** Let-coercion](rd-vbal.5.5.1.let-coercion.md)
