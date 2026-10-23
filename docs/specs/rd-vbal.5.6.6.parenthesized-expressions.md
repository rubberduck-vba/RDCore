# 5.6.6 Parenthesized Expressions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.6** Parenthesized Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/8c1b7427-670d-45e2-bc4b-39ab84ba5b20).

A parenthesized expression is one syntax for two operators, the way `=` is an assignment or a comparison and `!` a
type hint or a dictionary access. Which one a pair of parentheses is depends on where it is written:

|Where|Operator|What it does|
|---|---|---|
|Around an operand of an operator: `(a + b) * c`, `-(a + b)`, `Not (x Or y)`, `(o) Is Nothing`; around what is already in parentheses; around what `TypeOf` tests|`"__p()_op"`, the _precedence operator_|Groups the operand, as MS-VBAL §5.6.6 has it: its value and its declared type are the enclosed expression's, and the operator it is an operand of reads that value, as it reads any operand's. It coerces nothing of its own.|
|Anywhere else: an argument (`Foo (x)`, `Foo a, (b)`, `Call Foo((x))`), the value of an assignment (`x = (y)`), a condition (`If (x) Then`)|🧩 `"__c()_op"`, the _explicit let-coercion operator_|Forces a _let-coercion_ frame: what it yields is a value, bound to nothing - an argument written in parentheses is passed by value whatever its parameter is, and an object is its default member's value.|

Neither operator deviates from MS-VBAL: the precedence operator is what it specifies, and the explicit let-coercion
operator makes structural what it leaves implicit - what parentheses do to an argument, a classic trap. See
[**RD-VBAL §3.3.1** Unary Operators](rd-vbal.3.3.1.unary-operators.md) and
[**RD-VBAL §5.5.1** Let-coercion](rd-vbal.5.5.1.let-coercion.md).

---
> ⏮️ [**RD-VBAL §5.6.5** Literal Expressions](rd-vbal.5.6.5.literal-expressions.md) | ⏭️ [**RD-VBAL §5.6.7** TypeOf...Is Expressions](rd-vbal.5.6.7.typeof-is-expressions.md)
