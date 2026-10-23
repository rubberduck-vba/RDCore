# 5.6.6 Parenthesized Expressions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.6** Parenthesized Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/8c1b7427-670d-45e2-bc4b-39ab84ba5b20).

A parenthesized expression is one syntax for two operators, the way `=` is an assignment or a comparison and `!` a
type hint or a dictionary access. Which one a pair of parentheses is depends on where it is written:

|Where|Operator|What it does|
|---|---|---|
|Around an operand of an operator: `(a + b) * c`, `-(a + b)`, `Not (x Or y)`, `(o) Is Nothing`; around what is already in parentheses|`"__p()_op"`, the _precedence operator_|Groups the operand, as MS-VBAL §5.6.6 has it.|
|Anywhere else: an argument (`Foo (x)`, `Foo a, (b)`, `Call Foo((x))`), the value of an assignment (`x = (y)`, `Set x = (o)`), a condition (`If (x) Then`)|🧩 `"__c()_op"`, the _explicit let-coercion operator_|Forces a _let-coercion_ frame: an argument written in parentheses is passed by value whatever its parameter is.|

Either way, the parenthesized expression's declared type is the enclosed expression's, and it "evaluates to the simple
data value of its enclosed expression" (MS-VBAL §5.6.6) - a value bound to nothing. For an object, that is what its
default member returns, read **once** for each pair of parentheses, whatever the parentheses are around (recorded in
Excel):

|Expression, `o`'s default member returning `p`|Is|
|---|---|
|`(o) Is Nothing`, `(o) Is o`|`p Is Nothing`, `p Is o` - a trap: the parentheses do not group `o`, they read it|
|`TypeName((o))`, `Set x = (o)`|`TypeName(p)`, `Set x = p`|
|`((o))`|`p`'s default member in turn - `Argument not optional` when `p` is a `Collection`, whose `Item` takes the key|

An object with no default member has no simple data value, which raises error 438 when the parentheses are evaluated
(MS-VBAL §5.6.2.2, as MS-VBA has it). `TypeOf` tests an object as it is written: `TypeOf (o) Is T` is a syntax error.

Neither operator deviates from MS-VBAL: the precedence operator is what it specifies, and the explicit let-coercion
operator makes structural what it leaves implicit - what parentheses do to an argument, a classic trap. See
[**RD-VBAL §3.3.1** Unary Operators](rd-vbal.3.3.1.unary-operators.md) and
[**RD-VBAL §5.5.1** Let-coercion](rd-vbal.5.5.1.let-coercion.md).

---
> ⏮️ [**RD-VBAL §5.6.5** Literal Expressions](rd-vbal.5.6.5.literal-expressions.md) | ⏭️ [**RD-VBAL §5.6.7** TypeOf...Is Expressions](rd-vbal.5.6.7.typeof-is-expressions.md)
