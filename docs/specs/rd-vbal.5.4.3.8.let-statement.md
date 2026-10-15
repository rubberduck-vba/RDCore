# 5.4.3.8 Let Statement

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.4.3.8** Let Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ce2a98d4-2625-4cb7-982c-5c58e568cd18).

## Syntax

|AST node|Instruction kind(s)|Notes|
|---|---|---|
|[AssignmentStatementNode](../api/RDCore.SDK.Model.AST.Statements.AssignmentStatementNode.html) (`Kind`: `ImplicitLet` or `ExplicitLet`)|`Simple`|`[Let] lExpression = expression`. The `Target` of an `AssignmentStatementNode` is always an `lExpression`; see [**RD-VBAL §3.0.2** Node Types](rd-vbal.3.0.2.node-types.md).|

The target of an implicit Let is never the `Print` of an object with an output list, since an l-expression has none: `Debug.Print x = 0` is the
print of the comparison `x = 0` ([**RD-VBAL §5.4.5.8** Print Statement](rd-vbal.5.4.5.8.print-statement.md)). `Let Debug.Print x = 0`, with
the explicit `Let`, is still a Let statement whose target is an `ObjectPrintExpressionNode`, and it does not run.

See [**RD-VBAL §3.4.2** Simple Statements](rd-vbal.3.4.2.simple-statements.md).

## Static Semantics

For a predeclared class `Widget`, `Widget.Size = 3` is valid: it assigns a member of the object the default
instance variable holds. See [**RD-VBAL §3.1.1** Attributes](rd-vbal.3.1.1.attributes.md) (`VB_PredeclaredId`).

The target is a variable, a property, or the result of the function or property the statement is in. **MS-VBAL
§5.4.3.8** makes the statement invalid when the target "is classified as a value" and its declared type is not a class
or `Object`, which is so of a constant, or is classified as a function that is not the enclosing procedure and whose
declared type is not a class, `Object` or `Variant`. Assigning to a constant - one the standard library declares
(`vbCrLf`, `vbSunday`), a `Const` of a module or of a procedure, or a member of an `Enum`, by its name, qualified by its
project or its module, or qualified by the `Enum` that declares it, a library's or the workspace's
(`VbDayOfWeek.vbSunday`, `Colour.Red`) - or to a function of the library whose declared type is not a class, `Object`
or `Variant` (`Left$ = "x"`, a `String`) is
[`VBC09333`](../diagnostics/vbc09333.md) (`VariableRequired`), and the module does not load. So is a constant or a
function of the library, whatever its declared type, as the target of a `Set` statement, which must be a variable, a
property or an unbound member (**MS-VBAL §5.4.3.9**), and as the target of an `LSet` or an `RSet` statement or the
control variable of a `For` or a `For Each` loop, each of which must be a variable (**MS-VBAL §5.6.16.6**; see
[**RD-VBAL §5.6.16.6** Bound Variable Expressions](rd-vbal.5.6.16.constrained-expressions.md#56166-bound-variable-expressions)):
`Set Left = Nothing` and `LSet Err = "x"` are refused, though `Left` returns a `Variant` and `Err` an object.

A function of the library whose declared type is a class, `Object` or `Variant` is not refused as the target of a `Let`
statement, since **MS-VBAL §5.4.3.8** admits it: it assigns the value to an object's default member (`Err = 5`), and
has the statement raise error `450` when it runs for a `Variant` (`Left = "x"`, `Array = 5`). A function of the workspace
assigned inside its own body is its result, and is not refused; nor is a property, nor a variable named like a member of
the library. A name written with a type-declaration character is what its identifier names (**MS-VBAL §5.6.10**):
after `Dim Left$`, or inside a `Function Left$`, `Left$ = "x"` is not refused.

> [!NOTE]
> **Not implemented.** A function of the library whose declared type is `Variant`, let-assigned to (`Left = "x"`), does
> not raise error `450` when it runs: the statement fails in the host rather than raising a run-time error, and
> qualified by its module (`Strings.Left = "x"`) it stops as not yet implemented.

> [!NOTE]
> **Not implemented.** A function of the library that returns an object, let-assigned to (`Err = 5`), does not
> assign the object's default member yet: the statement raises error `438`.

> [!NOTE]
> **Not implemented.** A function of the workspace or a `Declare`d function assigned from outside its own body
> (`F = 6` in another procedure) is not refused, though **MS-VBAL §5.4.3.8** makes the statement invalid when its
> declared type is not a class, `Object` or `Variant`. It does not run: it fails in the host rather than raising a
> run-time error.

> [!NOTE]
> **Not implemented.** When it runs, a name written with a type-declaration character still finds the library's
> member named with that character before what its identifier names (see [`VBC09335`](../diagnostics/vbc09335.md)):
> after `Dim Left$`, `Left$ = "x"` assigns to the library's `Left$`, and fails in the host rather than raising a
> run-time error.

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

### Elements of an array

`a(i, j) = value`, and `obj.Items(i) = value` when the array is what a public variable of an object holds
(`LetAssignmentEvaluator.AssignArrayElement`), assigns an element in place: the value is Let-coerced to the element type of the array and
written to the element's own cell, and the array is never copied. A subscript outside the bounds of the array is error 9. `Set a(i) = obj` does
the same with Set-coercion, and lets go of the object the element held and takes a reference to the one it is given.

A `ReDim` of an array gives it the element type of the variable's declaration (`Dim a() As Long`), including a variable of an object.

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
