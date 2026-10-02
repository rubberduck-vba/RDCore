# 5.6.9.7 Is Operator

This section corresponds to [**MS-VBAL §5.6.9.7** Is Operator](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4730e541-8747-4259-ae76-128c7024b3b8).

## Runtime Semantics

The result is a `Boolean`: `True` when both operands refer to the same object, which is also what two `Nothing` references
do. An operand that is not an object reference (nor a `Variant` holding one) is error 424, *Object required*.

The operands are compared as they are. The `Boolean` of the result is the type of what the operator returns, not an
effective type the operands are let-coerced to, which would coerce an object to a `Boolean`, and fail.

See [**RD-VBAL §3.1.1.6** `VB_PredeclaredId`](rd-vbal.3.1.1.attributes.md#3116-vb_predeclaredid) for an `Is Nothing` check on an auto-object.

---
> ⏮️ [**RD-VBAL §5.6.9.6** Like Operator](rd-vbal.5.6.9.6.like-operator.md) | ⏭️ [**RD-VBAL §5.6.9.8** Logical Operators](rd-vbal.5.6.9.8.logical-operators.md)
