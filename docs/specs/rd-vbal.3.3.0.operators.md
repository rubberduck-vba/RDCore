# 3.3.0 Operator Expressions

An _operator_ consists of a _bound expression node_ that yields a deterministic _result_ given one or more _operand_ inputs.

- An operator that accepts a single input is a **unary operator**;
- An operator that accepts two inputs is a **binary operator**;
- An operator that accepts three inputs is a **ternary operator**.

> [!NOTE]
> 🧩 RD-VBA does not currently define any _ternary operators_.

- All _unary operators_ are _prefix_, with the operator token appearing _before_ its operand;
- All _binary operators_ are _infix_, with a _left_ and a _right_ operand and the operator token between them;
- _ternary operators_ are **undefined in RD-VBA** and should never be introduced in the _language core_.

All operators ultimately inherit `BoundNode`, which represents any type of AST node:

- [BoundNode](../api/RDCore.SDK.Model.AST.Abstract.BoundNode.html)
  - [BoundExpression](../api/RDCore.SDK.Model.AST.Abstract.BoundExpression.html)
    - [VBOperatorExpression](../api/RDCore.SDK.Model.AST.Expressions.VBOperatorExpression-2.html)

Each layer of this inheritance hierarchy refines its members with more specialized signatures in _templated methods_, usually sealing overrides to leave only one or two methods to implement at the leaves. For example a `BoundExpression` has a general-purpose _inputs_ array of values, but an _operator expression_ exposes them as _indexed operands_, and a _unary operator_ only sees one while a _binary operator_ gets `Left` and `Right` operands.  

> [!TIP]
> This is also the case for all _semantics_, both _static_ and _runtime_.

<!-- TODO
## In this section

- [**RD-VBAL §3.3.1** Unary Operators](./rd-vbal.3.3.1.unary-operators.html)
- [**RD-VBAL §3.3.2** Arithmetic Operators](./rd-vbal.3.3.2.arithmetic-operators.html)
- [**RD-VBAL §3.3.3** Logical (Bitwise) Operators](./rd-vbal.3.3.3.logical-operators.html)
- [**RD-VBAL §3.3.4** Relational (Comparison) Operators](./rd-vbal.3.3.4.relational-operators.html) 
-->

> ⏮️ [**RD-VBAL §3.2** Literals](./rd-vbal.3.2.literals.html) | ⏭️ [**RD-VBAL §4.0** Program Structure](./rd-vbal.4.0.program-structure.html)

