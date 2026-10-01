# 5.4.3.8 Let Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.3.8** Let Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ce2a98d4-2625-4cb7-982c-5c58e568cd18).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[AssignmentStatementNode](../api/RDCore.SDK.Model.AST.Statements.AssignmentStatementNode.html) (`Kind`: `ImplicitLet` or `ExplicitLet`)|`Simple`|`[Let] lExpression = expression`. The `Target` of an `AssignmentStatementNode` is always an `lExpression`; see [**RD-VBAL §3.0.2** Node Types](rd-vbal.3.0.2.node-types.md).|

See [**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md).

## Static Semantics

For a predeclared class `Widget`, `Widget.Size = 3` is valid: it assigns a member of the object the default
instance variable holds. See [**RD-VBAL §3.1.1** Attributes](rd-vbal.3.1.1.attributes.md) (`VB_PredeclaredId`).

## Runtime Semantics

The executor dispatches a Let-assignment statement to its statement runtime semantics: Let-assignment is one of
the statement kinds the `Simple` statement provider handles
([**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md)).

1. `StatementRuntimeSemanticsProvider.ExecuteLetAssignment` evaluates the value expression.
2. `ExecuteLetAssignment` passes the value to `LetAssignmentEvaluator`, which assigns it to the target:

|Target|Assignment|
|---|---|
|A bare reference to the procedure's own name, from within its own body|Assigns the `ReturnValue` slot (the function result variable) instead of the general symbol table. The value is Let-coerced directly. See [Function result variable](#function-result-variable).|
|A UDT field|Let-coerces the value to the field's own declared type, and writes the field's cell. See [UDT fields](#udt-fields).|
|A variable|Assigns through the `"__let_op"` Let-assignment operator: Let-assignment reuses the operator pipeline.|

The Set statement does not use the operator pipeline; see
[**RD-VBAL §5.4.3.9** Set Statement](rd-vbal.5.4.3.9.set-statement.md). Let-coercion is described in
[**RD-VBAL §5.5.1.2** Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md).

### Function result variable

A Let-assignment to a bare reference to a procedure's own name, from within its own body, assigns the
`ReturnValue` slot instead of the general symbol table. See
[**RD-VBAL §5.3.1.6** Subroutine and Function Declarations](rd-vbal.5.3.1.6.subroutine-and-function-declarations.md).

- `LetAssignmentEvaluator.Assign` detects a self-reference the same way as
  `RuntimeExpressionEvaluator.EvaluateSimpleName`: by comparing the resolved symbol's `Uri` against
  `RuntimeEvaluationContext.Scope`. `StatementRuntimeSemanticsProvider.ExecuteLetAssignment` reaches it through
  `LetAssignmentEvaluator.Assign`.
- A bare reference to the function result variable never goes through `"__let_op"`. The function result variable
  is not an addressable `Symbol` with an
  [IBindingHandle](../api/RDCore.SDK.Model.Values.Bindings.IBindingHandle.html).
- The self-reference branch of `LetAssignmentEvaluator.Assign` Let-coerces the assigned value directly.
- That direct Let-coercion is the same lower-level call that `ByVal` and `ByRef`-fallback parameter passing make,
  for the same reason: there is no addressable symbol. See
  [**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md).

### Members of an object

`obj.Member = value` assigns a member of the object `obj` holds (`LetAssignmentEvaluator.AssignObjectMember`). A `Nothing` reference
raises error 91. The member is the `Property Let` declared for it (an indexed one takes the index arguments before the value) or else a
public variable of the class, found through the declared interface when `obj` is declared as one its object's class implements
([**RD-VBAL §5.3.1.9**](rd-vbal.5.3.1.9.implemented-name-declarations.md)). A public variable is assigned by Let-coercing the value to
its declared type; with no such member, error 438 is raised. A `With` block assigns the same way.

> **Not implemented.** An element of an array held by a member: `obj.Items(1) = value`.

### UDT fields

A Let statement assigns a UDT field by Let-coercing the value to the field's own declared type and writing the
field's cell. It writes the cell because a UDT field is not an addressable symbol the way a variable is.

A UDT field is reachable from source code in both directions: a member access reads one, and a Let statement
assigns one. See [**RD-VBAL §5.6.12** Member Access Expressions](rd-vbal.5.6.12.member-access-expressions.md).

VBA copies a UDT on assignment. A copy of a UDT value gets a field store of its own, deep through a nested UDT;
see [**RD-VBAL §2.5.2.1.3** User-Defined Type (UDT) Values](rd-vbal.2.5.2.1.3.udt-values.md).

### Other statements that Let-assign

|Statement|Shares|
|---|---|
|`For` (`ForOpener`)|Let-assigns the loop counter to `start-value` through the same Let-assignment machinery a `Let` statement uses. See [**RD-VBAL §5.4.2.3** For Statement](rd-vbal.5.4.2.3.for-statement.md).|
|`Input #`, `Line Input #`, `Get`|Resolve their targets with `LetAssignmentEvaluator.TryResolveTarget`, the same target resolution the `Let` statement uses. See [**RD-VBAL §5.4.5** File Statements](rd-vbal.5.4.5.file-statements.md).|

## Implementation

|Type or member|Role|
|---|---|
|`RDCore.Runtime.Semantics.Statements.StatementRuntimeSemanticsProvider.ExecuteLetAssignment`|Evaluates the value expression and passes it to `LetAssignmentEvaluator`.|
|`RDCore.Runtime.Semantics.Statements.LetAssignmentEvaluator.Assign`|Assigns an evaluated value to the target: the function result variable, a UDT field, or a variable through `"__let_op"`.|
|`LetAssignmentEvaluator.TryResolveTarget`|Resolves the symbol a target names; shared with `Input #`, `Line Input #` and `Get`.|
|[OperatorSymbolNames](../api/RDCore.SDK.Model.Symbols.Operators.OperatorSymbolNames.html)`.BinaryAssignmentValueOp`|The name of the `"__let_op"` Let-assignment operator (**RDCore.SDK**).|
|[ICallStackFrame](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html)`.ReturnValue`|The function result variable.|

---
> ⏮️ [**RD-VBAL §5.4.3.7** RSet Statement](rd-vbal.5.4.3.7.rset-statement.md) | ⏭️ [**RD-VBAL §5.4.3.9** Set Statement](rd-vbal.5.4.3.9.set-statement.md)
