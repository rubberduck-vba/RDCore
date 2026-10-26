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
(`VbDayOfWeek.vbSunday`, `Colour.Red`) - is [`VBC09346`](../diagnostics/vbc09346.md) (`AssignmentToConstantNotPermitted`),
and the module does not load. A constant or a function of the library is [`VBC09333`](../diagnostics/vbc09333.md)
(`VariableRequired`) as the target of an `LSet` or an `RSet` statement or the control variable of a `For` or a
`For Each` loop, each of which must be a variable (**MS-VBAL §5.6.16.6**; see
[**RD-VBAL §5.6.16.6** Bound Variable Expressions](rd-vbal.5.6.16.constrained-expressions.md#56166-bound-variable-expressions)):
`LSet Err = "x"` is refused.

A function named as the target outside its own body - one of the library, of the workspace, or a `Declare`, by its name
or qualified by its module - is a call of it, and so is a property of a module that has no `Property Let`, the
library's (`Now`, `Timer`) as the workspace's: what it returns is assigned through its default member. As in MS-VBA
(recorded in Excel), what cannot be called so is a compile error, and the module does not load:

|Target|Error|Example|
|---|---|---|
|A function whose declared type is neither a `Variant` nor an object|[`VBC09355`](../diagnostics/vbc09355.md) _Function call on left-hand side of assignment must return Variant or Object_ - before what its parameters are|`Left$ = "x"`, `F = 6` of a `Function F() As Long`, a `Declare`d function|
|A property of a module of such a type|[`VBC09341`](../diagnostics/vbc09341.md) _Can't assign to read-only property_|`Timer = 5`|
|One that takes an argument|[`VBC09339`](../diagnostics/vbc09339.md) _Argument not optional_|`Left = "x"`, `Strings.Left = "x"`, `Environ = "x"`|

An array is assigned an array, or a `Variant` that may hold one (and a `Byte()` a `String`, **MS-VBAL §5.5.1.2.6**). A fixed-size array is assigned nothing,
and a resizable array - declared (`Dim a() As Long`), or implicitly by a `ReDim` - no number: both are [`VBC09360`](../diagnostics/vbc09360.md) _Can't assign to
array_, as in MS-VBA (recorded in Excel), before what the value would be Let-coerced to: `a = 5` of a `Dim a() As Variant` is refused.

What passes is called when the statement runs, given the arguments written after it, if any (`G() = 6`,
`Left("ab", 1) = "x"`): what it returns is assigned through its default member (`Err = 5` assigns `Err.Number`), and
what returns no object is error `424` (`Command = "x"`, `Now = 5`, a `Variant` function of the workspace). A function of
the workspace assigned inside its own body is its result; a variable named like a member of the library is the
variable. A name written with a type-declaration character is what its identifier names (**MS-VBAL §5.6.10**): after
`Dim Left$`, or inside a `Function Left$`, `Left$ = "x"` is not refused, and assigns the variable or the result when it
runs.

A member of an object is held to the same rules, early-bound, as in MS-VBA (recorded in Excel):

|Target|Error|Example|
|---|---|---|
|A function of an object whose declared type is neither a `Variant` nor an object, with no argument|[`VBC09355`](../diagnostics/vbc09355.md) _Function call on left-hand side of assignment must return Variant or Object_|`c.Count = 3` of a `Collection`|
|A `Sub` of an object, or of a module named through it, with arguments or without|[`VBC09340`](../diagnostics/vbc09340.md) _Expected Function or variable_|`Err.Clear = 1`, `c.Remove(1) = 5`, `Module1.DoIt = 1`|

A `Sub` named by its bare name compiles in MS-VBA, and is not refused.

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
its declared type; with no such member, error 438 is raised. A `With` block assigns the same way. A member of an object of a standard-library class
reached late-bound that has no `Property Let` is assigned as MS-VBA's `IDispatch` assigns it: 450 for a read-only property, 438 for a method, and a
member written with arguments invoked, to assign the default member of what it returns
([**RD-VBAL §6.0** Late-Bound Calls](rd-vbal.6.0.standard-library.md#late-bound-calls)).

### Elements of an array

`a(i, j) = value`, and `obj.Items(i) = value` when the array is what a public variable of an object holds
(`LetAssignmentEvaluator.AssignArrayElement`), assigns an element in place: the value is Let-coerced to the element type of the array and
written to the element's own cell, and the array is never copied. A subscript outside the bounds of the array is error 9. `Set a(i) = obj` does
the same with Set-coercion, and lets go of the object the element held and takes a reference to the one it is given.

What is indexed can be an object - or a `Variant` holding one - rather than an array: `c(1) = value` assigns the object's default member with the
arguments, as `c.Item(1) = value` does, and `Set c(1) = obj` its `Property Set` ([**RD-VBAL §5.6.13** Object
Callee](rd-vbal.5.6.13.index-expressions.md#object-callee)).

A member of an object that is a function - or a `Property Get` that takes no argument, given arguments that index what it returns - has no
`Property Let` to assign through: it is called, with the arguments written after it, and what it returns is assigned through its default member, as
a function of a module is. `c(1) = 5` and `c.Item(1) = 5`, with `Dim c As Collection` holding a number, are error 424; an item that is an object is
assigned its own default member, and one that takes an argument is error 438.

A `ReDim` of an array gives it the element type of the variable's declaration (`Dim a() As Long`), including a variable of an object.

### UDT fields

A Let statement assigns a UDT field by Let-coercing the value to the field's own declared type and writing the
field's cell. It writes the cell because a UDT field is not an addressable symbol the way a variable is.

A UDT field is reachable from source code in both directions: a member access reads one, and a Let statement
assigns one. See [**RD-VBAL §5.6.12** Member Access Expressions](rd-vbal.5.6.12.member-access-expressions.md).

VBA copies a UDT on assignment. A copy of a UDT value gets a field store of its own, deep through a nested UDT;
see [**RD-VBAL §2.5.2.1.3** User-Defined Type (UDT) Values](rd-vbal.2.5.2.1.3.udt-values.md).

### A floating-point value that overflowed

A `Single` or `Double` operation that raises 6 or 11 still has the value IEEE-754 arithmetic gave it: an infinity,
or a NaN ([**RD-VBAL §5.6.9.3**](rd-vbal.5.6.9.3.arithmetic-operators.md)). A `Let` statement whose expression is such
an operation assigns that value to its target, then raises the error, when the target is declared `Single` or
`Double`. So does a `Double` past the range of a `Single`, assigned to one: the `Single` is infinite. A target of any
other type, a `Variant` included, is left as it was. `^` raises before anything is assigned.

```vb
Dim s As Single, z As Single, v As Variant
s = 3E+38
On Error Resume Next
s = s * 10        ' error 6; s is inf
v = 5: v = s * 10 ' error 6; v is still 5
s = 1 / z         ' error 11; s is inf
s = z / z         ' error 6; s is -nan(ind)
```

MS-VBAL §5.6.9.3.5 states this for a division of two operands declared `Double` only. MS-VBA does it for every
floating-point operation, and RD-VBA follows MS-VBA. As text, an infinity is `inf` or `-inf`, and a NaN is
`-nan(ind)`: MS-VBAL §5.5.1.2.4 has `1.#INF` and `-1.#IND`, which MS-VBA doesn't show.

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
