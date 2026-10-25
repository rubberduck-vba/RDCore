# 5.4.2.21 With Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.2.21** With Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/52caae3d-3ded-436f-a36a-8d5a30c21600).

## Syntax

|AST node|Instruction kind|Notes|
|---|---|---|
|[WithStatementNode](../api/RDCore.SDK.Model.AST.Statements.WithStatementNode.html)|`With`, `EndWith`|The opener's [Instruction](../api/RDCore.SDK.Semantics.Instructions.Instruction.html)`.End` is right past the block, past its `EndWith`.|

`With...End With` is a block statement
([**RD-VBAL §3.4.1** Block Statements](rd-vbal.3.4.1.block-statements.md)). Its opener lowers to the
[InstructionKind](../api/RDCore.SDK.Semantics.Instructions.InstructionKind.html) `With`, and its `End With` to an
`EndWith` whose `Matching` is the opener ([**RD-VBAL §3.5.2** Instruction](rd-vbal.3.5.2.instruction.md)). The
`EndWith` is outside the block: it is reached by running through the block, and a jump out of the block, to the
opener's `End` or beyond, goes past it.

## Static Semantics

What a `With` block is about is an object, a record of a user-defined type, or a `Variant`: what its `.Member`
references can be members of. Any other value is
[VBC09378](../diagnostics/vbc09378.md) _With object must be user-defined type, Object, or Variant_, as **MS-VBA** has
it (recorded in Excel): a variable of an intrinsic type (`With n`), a literal or an operator's result (`With 5`,
`With n + 1`), a project, a module or an Enum type (`With VBA`, `With Strings`, `With Color`). `With Nothing` compiles.
The error is reported once: the body is still in a `With` block, of a target whose type is not known.

Lowering records the enclosing `With` block on every instruction inside it
([**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md)):

- Every instruction lexically inside a `With` block, however deeply nested (through an `If` or a loop), carries
  `Instruction.EnclosingWith` set to that `With`'s opener offset.
- When lowering leaves a `With` block, `EnclosingWith` is restored to its value before the block.
- `EnclosingWith` is computed once, at lowering time. It is not tracked as a runtime stack the interpreter
  pushes and pops.

Because `EnclosingWith` is static, a `GoTo` into or out of a `With` block leaves no stale state to unwind
([**RD-VBAL §5.4.2.12** GoTo Statement](rd-vbal.5.4.2.12.goto-statement.md)).

## Runtime Semantics

The executor runs the `With` instruction as follows:

1. The header expression (the target) is evaluated once.
2. The target is Set-coerced or Let-coerced:
   - a class-valued target is Set-assigned through
     [ISetCoercionRuntimeSemantics](../api/RDCore.SDK.Runtime.Abstract.ISetCoercionRuntimeSemantics.html)
     ([**RD-VBAL §5.5.2.2** Runtime semantics](rd-vbal.5.5.2.2.runtime-semantics.md));
   - a UDT-valued target is Let-assigned through `ILetCoercionRuntimeSemanticsProvider.EvaluateLetCoercionSemantics`
     directly.
3. The result is stored on the activation, keyed by the `With` instruction's own offset, and an object is held:
   counted as a reference until the `EndWith`.
4. Execution falls through into the body.

The `EndWith` forgets the stored target and lets go of the object, which then terminates if nothing else holds it.

Every `Simple` and `ConditionalBranch` instruction's `RuntimeEvaluationContext` is recomputed before each
dispatch, from `Instruction.EnclosingWith` ([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)). A
`.Member` or `!member` with-expression then resolves against the innermost enclosing `With`'s stored target,
however control reached the instruction, including via `GoTo`
([**RD-VBAL §5.6.15** With Expressions](rd-vbal.5.6.15.with-expressions.md)).

### The block variable

**MS-VBAL §5.4.2.21** says that a target "classified as a variable" *is* the `With` block variable, and that any
other target is held by an anonymous one. MS-VBA, which RD-VBA follows, does this for a record only: a record
variable is the block variable, and the block writes its fields where they are. A target that is an object is
held, whether a variable names it or not. The block reads the object the variable held when the `With` statement
ran, whatever the block assigns to the variable afterwards, and the object lives until `End With`:

```vb
Set Kept = New Widget
With Kept
    Set Kept = Nothing   ' the block still holds the Widget
    Debug.Print .Hello   ' works: Err.Number is 0
End With                 ' the Widget terminates here
```

The block holds its object from its `With` statement to its `End With`, wherever the program goes in between:

|How the program reaches an instruction of the block|`.Member`|
|---|---|
|By running through its `With` statement.|The object.|
|A `GoTo` out of the block and back in, or an error handler and `Resume Next` back in.|The object, still held.|
|A `GoTo` into the block, past its `With` statement.|Runtime error 91, `Object variable or With block variable not set`.|
|`Resume Next` past a `With` statement whose target raised an error: the body runs.|Runtime error 91.|
|A `GoTo` back into the block after its `End With` ran.|Runtime error 91.|

## Implementation

- `RDCore.Runtime.Execution.ProcedureExecutor` dispatches `With` instructions.
- `RDCore.Runtime.Semantics.Statements.WithStatementRuntimeSemantics` performs the Set-coercion or Let-coercion
  of step 2.
- The target is stored through `CallStackFrame.SetBlockState` and read back through
  [ICallStackFrame](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html)`.TryGetBlockState`,
  keyed by the opener's offset. `Select Case` stores its selector the same way
  ([**RD-VBAL §5.4.2.10** Select Case Statement](rd-vbal.5.4.2.10.select-case-statement.md)); a single
  block-state value is enough for either.
- `RuntimeExpressionEvaluator` resolves the with-expression target through its `EnclosingWithTarget`
  resolution.

---
> ⏮️ [**RD-VBAL §5.4.2.20** RaiseEvent Statement](rd-vbal.5.4.2.20.raiseevent-statement.md) | ⏭️ [**RD-VBAL §5.4.2.22** End Statement](rd-vbal.5.4.2.22.end-statement.md)
