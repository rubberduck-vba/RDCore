# 5.6.9.2 Simple Data Operators

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.9.2** Simple Data Operators](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/dccbdeae-5b2e-4c2e-857e-1ad9b861e196).

## Runtime Semantics

A simple data operator - every operator but `Is` - "first evaluates its operands as simple data values": an operand that
is an object, or a `Variant` that holds one, is what its default member returns (**MS-VBAL §5.6.2.2**), before anything
else. `o + 1.5` of an object whose default member returns `42` is `43.5`, as in MS-VBA. An operand that has no value is
the operation's error: `Nothing` is `91`, an object with no default member `438`, one whose default member takes an
argument `450`, and an object a default member returned that has no value `13` (**MS-VBAL §5.6.2.3**). An operand an
analysis assumes is not read.

The _evaluation pipeline_ of all operators then follows a fixed sequence of three steps:

1. **Effective type.** The _effective type_ of the operation is determined, based on the _declared type_ of its
   _operands_.
2. **Validation.** All non-null operands (every operand that is not a
   [VBNullValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBNullValue.html)) are let-coerced to the determined
   effective type of the operation ([**RD-VBAL §5.5.1.2** Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md)).
3. **Evaluation.** A templated method evaluates a result from the validated operands.

The sequence may be aborted at any point to return an _error result_. An error result encapsulates
[VBRuntimeErrorInfo](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorInfo.html) error metadata.

### Operand validation

The arithmetic, relational and concatenation (`&`) operators validate their operands through
`OperatorRuntimeSemantics.LetCoerceNonNullOperand`.

`LetCoerceNonNullOperand` has a `TypeInfo`-equality short-circuit: an operand whose `TypeInfo` equals the destination
type is not coerced. The short-circuit does not apply to a
[VBVariantValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBVariantValue.html) operand: a `Variant` operand is always
let-coerced, even when its `TypeInfo` equals the destination type.

A `VBVariantValue`'s `TypeInfo` mirrors its wrapped value's; see
[**RD-VBAL §5.5.1.2.12** Let-coercion to Variant](rd-vbal.5.5.1.2.runtime-semantics.md#551212-let-coercion-to-variant).

## When an operand is read

**MS-VBAL §5.6.9.2** says an operator evaluates its operands. It doesn't say when an operand that is a variable takes
its value if a call in the same expression changes that variable. MS-VBA decides by the operand's type, and RD-VBA
does what MS-VBA does:

- An operand of type `Byte`, `Boolean`, `Integer`, `Long`, `LongLong` or `Currency` is read where it's written:
  before a call to its right, after a call to its left. This holds for a variable, a field of a record and an element
  of an array.
- An operand of type `Single`, `Double`, `Date`, `String` or `Variant` is read when the operation is computed, after
  the calls on both sides of it. This holds for a variable and an element of an array, whose index is evaluated
  then too: `v(i) + Poke(v, i)`, where `Poke` adds 1 to `v(k)` and then to `k`, is `v(2)` for a `Double` array, and
  `v(1)` as it was for a `Long` one.

In these statements, `a` is `1` and `Bump` adds 1 to the variable it's given by reference and returns the new value.
These are the values measured in MS-VBA:

|Statement|Type of `a`|Result|
|---|---|---|
|`x = a + Bump(a)`|`Byte`, `Integer`, `Long`, `LongLong`, `Currency`|`x` is `3`|
|`x = a + Bump(a)`|`Boolean`, starting `False`|`x` is `-1`|
|`x = a + Bump(a)`|`Single`, `Double`, `Date`, `Variant`|`x` is `4`|
|`x = Bump(a) + a`|`Long`|`x` is `4`|
|`x = Bump(a) + a + Bump(a)`|`Long`|`x` is `7`|
|`x = (a) + Bump(a)`|`Long`|`x` is `3`|
|`x = (a) + Bump(a)`|`Double`|`x` is `4`|
|`If a < Bump(a) Then`|`Long`|the condition is `True`|
|`If a < Bump(a) Then`|`Double`|the condition is `False`|

A `String` reads the same way: `s & Ap(s)`, where `s` is `"a"` and `Ap` appends `"b"` to it, is `"abab"`.

> [!NOTE]
> **Not implemented.** MS-VBA computes some operations of a `Double` or `String` operand after the call:
> `a + 0 + Bump(a)` of a `Double` is `4`, `-a + Bump(a)` is `0` and `Bump(a) + a + Bump(a)` is `8`. RD-VBA computes
> the operation left of the last call before that call. `s & "" & Ap(s)` is `"aab"` in both. RD-VBA reads a `Double`
> field of a record where it's written.

## Computation in the effective type

The evaluation step (step 3) computes an operator's result in the operation's effective type, in that type's own
representation, and never through a `Double` intermediate.

|Effective type|Computed in|
|---|---|
|`Long`|The `Long` representation, a 32-bit `int`.|
|`Currency`, `Decimal`|`decimal`|
|`Single`|`float`|

See [**RD-VBAL §5.6.9.3** Arithmetic Operators](rd-vbal.5.6.9.3.arithmetic-operators.md) for checked arithmetic and
the `^` operator, [**RD-VBAL §5.6.9.5** Relational Operators](rd-vbal.5.6.9.5.relational-operators.md) for
comparisons, and [**RD-VBAL §5.6.9.8** Logical Operators](rd-vbal.5.6.9.8.logical-operators.md) for bitwise
computation.

## Implementation

- The node parameter of `OperatorRuntimeSemantics<TContext,TFlags>` is typed
  [ExpressionNode](../api/RDCore.SDK.Model.AST.Abstract.ExpressionNode.html).
- Nothing in the operator runtime-semantics hierarchy reads `.Left`, `.Right` or `.Token` off the node parameter.
  It reads the node's identity (`.Identity`) and its source location, to attribute the operation and its errors to
  the node. Beyond those, it only tests the node's type: to flag which operand position (unary, binary left or binary
  right) an operand holds in the semantic analysis, and to name the node type in an internal-error message.
- Only the token-dispatch layer, `OperatorRuntimeSemanticsProvider`, reads `.Token` off the operator node.

The operator strategies do not require a binary or unary operator node
([VBBinaryOperatorExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.VBBinaryOperatorExpressionNode.html),
[VBUnaryOperatorExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.VBUnaryOperatorExpressionNode.html)) as their
node parameter. Requiring one would force the fabrication of synthetic nodes, which RD-VBA rules out, as it does for
let-coercion ([**RD-VBAL §5.5.1.2** Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md)).

See [**RD-VBAL §5.4.2.10** Select Case Statement](rd-vbal.5.4.2.10.select-case-statement.md) for a caller that
invokes the operator strategies directly, with its own expression node.
