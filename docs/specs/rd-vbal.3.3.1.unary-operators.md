# 3.3.1 Unary Operators

Parentheses written in source are one of two unary operators ([**RD-VBAL §5.6.6** Parenthesized Expressions](rd-vbal.5.6.6.parenthesized-expressions.md)):

- the `"__p()_op"` _precedence operator_, around an operand of an operator, which groups it as MS-VBAL §5.6.6
  specifies and coerces nothing;
- 🧩 the `"__c()_op"` _explicit let-coercion operator_, anywhere else, which forces a _let-coercion_ frame
  ([**RD-VBAL §5.5.1** Let-coercion](rd-vbal.5.5.1.let-coercion.md)). The explicit coercion operator is a language
  core extension ([**RD-VBAL §1.1.2** Language Core Extensions](rd-vbal.1.1.2.language-core-extensions.md)).

See [**RD-VBAL §5.6.9.3** Arithmetic Operators](rd-vbal.5.6.9.3.arithmetic-operators.md) for the unary `-`
operator, and [**RD-VBAL §5.6.9.8** Logical Operators](rd-vbal.5.6.9.8.logical-operators.md) for the `Not`
operator.

---
> ⏮️ [**RD-VBAL §3.3.0** Operator Expressions](rd-vbal.3.3.0.operators.md) | ⏭️ [**RD-VBAL §3.3.2** Arithmetic Operators](rd-vbal.3.3.2.arithmetic-operators.md)
