# 3.3.0 Operator Expressions

An _operator_ consists of a _bound expression node_ that yields a deterministic _result_ given one or more
_operand_ inputs. The semantics of operator expressions are described in
[**RD-VBAL §5.6.9** Operator Expressions](rd-vbal.5.6.9.operator-expressions.md).

|Inputs|Operator kind|Notation|
|---|---|---|
|One|**Unary operator**|All unary operators are _prefix_: the operator token appears _before_ its operand.|
|Two|**Binary operator**|All binary operators are _infix_: a _left_ and a _right_ operand, with the operator token between them.|
|Three|**Ternary operator**|Undefined in **RD-VBA**.|

> [!NOTE]
> **RD-VBA** does not define any _ternary operators_. Ternary operators should never be introduced in the
> _language core_.

## Operator Node Hierarchy

All operators ultimately inherit `SyntaxNode`, which represents any type of AST node
([**RD-VBAL §3.0.2** Node Types](rd-vbal.3.0.2.node-types.md)):

- [SyntaxNode](../api/RDCore.SDK.Model.AST.Abstract.SyntaxNode.html)
  - [ExpressionNode](../api/RDCore.SDK.Model.AST.Abstract.ExpressionNode.html)
    - [VBOperatorExpression](../api/RDCore.SDK.Model.AST.Expressions.VBOperatorExpression.html)
      - [VBUnaryOperatorExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.VBUnaryOperatorExpressionNode.html)
      - [VBBinaryOperatorExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.VBBinaryOperatorExpressionNode.html)

Each layer of this inheritance hierarchy refines its members with more specialized signatures in
_templated methods_. The layers usually seal their overrides, leaving only one or two methods to implement at the
leaves. For example, each layer exposes the node's inputs as follows:

|Node type|Inputs exposed as|
|---|---|
|`ExpressionNode`|A general-purpose _inputs_ array of values.|
|`VBOperatorExpression`|Indexed _operands_ (`Children[n]`).|
|`VBUnaryOperatorExpressionNode`|`Operand` only.|
|`VBBinaryOperatorExpressionNode`|`Left` and `Right`.|

The same layered refinement through templated methods applies to all _semantics_, both _static_ and _runtime_
([**RD-VBAL §5.0** Semantics](rd-vbal.5.0.semantics.md)).

---
## In this section

|§|Title|
|---|---|
|3.3.1|[Unary Operators](rd-vbal.3.3.1.unary-operators.md)|
|3.3.2|[Arithmetic Operators](rd-vbal.3.3.2.arithmetic-operators.md) — *reserved*|
|3.3.3|[Logical (Bitwise) Operators](rd-vbal.3.3.3.logical-operators.md) — *reserved*|
|3.3.4|[Relational (Comparison) Operators](rd-vbal.3.3.4.relational-operators.md) — *reserved*|

---
> ⏮️ [**RD-VBAL §3.2.0** Literal Expressions](rd-vbal.3.2.0.literals.md) | ⏭️ [**RD-VBAL §3.3.1** Unary Operators](rd-vbal.3.3.1.unary-operators.md)
