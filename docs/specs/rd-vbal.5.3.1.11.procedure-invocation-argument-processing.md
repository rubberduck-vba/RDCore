# 5.3.1.11 Procedure Invocation Argument Processing

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.3.1.11** Procedure Invocation Argument Processing](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/1fb9af32-fc48-4c4f-998a-ed8047048ca5).

Procedures are invoked through
[IProcedureInvoker](../api/RDCore.SDK.Runtime.Abstract.Execution.IProcedureInvoker.html).
`RDCore.Runtime.Execution.RuntimeProcedureInvoker` is the `IProcedureInvoker` implementation.

`IProcedureInvoker` and [CallableBindingHandle](../api/RDCore.SDK.Model.Values.Bindings.CallableBindingHandle.html)
are the call contract: given a procedure symbol, a resolver, and arguments, run the procedure.
`RuntimeProcedureInvoker` implements the call: frame setup, `ByVal`/`ByRef` parameter binding,
function result values, and the call-depth guard. See
[**RD-VBAL §3.5.5** Placement and Licensing](rd-vbal.3.5.5.placement-and-licensing.md).

## Invocation Sites

Each of these invokes a procedure through `IProcedureInvoker`:

|Site|Described in|
|---|---|
|A `Call` statement, or a bare call statement|[**RD-VBAL §5.4.2.1** Call Statement](rd-vbal.5.4.2.1.call-statement.md)|
|A bare reference to a `Sub`, `Function` or `Property Get`|[**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md)|
|An [IndexExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.IndexExpressionNode.html) whose `Callee` is a bare name resolving to a `Sub`, `Function` or `Property Get`|[**RD-VBAL §5.6.13** Index Expressions](rd-vbal.5.6.13.index-expressions.md)|

An index expression whose `Callee` is a bare name is checked for a procedure before the general recursive
`Evaluate(Callee)`. An index expression with a procedure `Callee` is the only shape that recurses (e.g.
`Foo(n - 1)`, even from within `Foo`'s own body).

## Static Semantics

Omitting an argument with a comma (`Foo(1, , 3)`) is legal MS-VBA for a parameter of any declared type.
MS-VBA defers argument-type validation to run time: it never rejects an omitted argument at compile time.

> [!NOTE]
> **Not implemented.** No diagnostic flags `IsMissing` used on a non-`Variant` parameter, for which MS-VBA's
> `IsMissing` always returns `False`. RD-VBA's `IsMissing` has no runtime implementation; see
> [**RD-VBAL §6.1.2.7** Information](rd-vbal.6.1.2.7.information.md).

## Runtime Semantics

A procedure invocation runs these steps:

1. `RuntimeExpressionEvaluator.MapArguments` maps the arguments to the parameters (see
   [Argument Mapping](#argument-mapping)).
2. The argument-evaluation loop of `RuntimeExpressionEvaluator` produces one argument per parameter: a
   `ParamArray` collection, an `Optional` parameter's default, a `ByRef` reference, or a Let-coerced copy (see
   [Optional Parameters](#optional-parameters), [ParamArray](#paramarray) and
   [ByVal and ByRef Binding](#byval-and-byref-binding)).
3. `RuntimeProcedureInvoker` looks up the callee's own lowered body by symbol.
4. `RuntimeProcedureInvoker` pushes a fresh [ICallStackFrame](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html)
   for the callee. Exceeding the call-depth limit raises run-time error 28.
5. For a `Function` or `Property Get`, `ICallStackFrame.ReturnValue` is seeded to the default value of the
   declared return type.
6. Each parameter is bound on the callee's frame: by reference through `CallStackFrame.PushByRef`, or to a fresh
   value binding.
7. `RuntimeProcedureInvoker.HoistLocals` creates the procedure's `Dim` and `Static` locals.
8. The callee's body runs through the same `ProcedureExecutor`, with `RuntimeEvaluationContext.Scope` set to the
   procedure's own `Uri`.
9. `RuntimeProcedureInvoker` reports the callee's outcome back to the caller (see [Return Value](#return-value)).

### Argument Mapping

`RuntimeExpressionEvaluator.MapArguments` maps named arguments and `Optional` parameters by the two-pass
argument-mapping algorithm of **MS-VBAL §5.3.1.11**:

1. Each argument is mapped to a parameter:
   - Positional arguments map to parameters left to right.
   - A [NamedArgumentNode](../api/RDCore.SDK.Model.AST.Expressions.NamedArgumentNode.html) maps to its
     parameter by name.
   - A [MissingArgumentNode](../api/RDCore.SDK.Model.AST.Expressions.MissingArgumentNode.html) mapped to a
     non-`Optional` parameter raises error 448. Error 448 is checked during argument mapping, per
     **MS-VBAL §5.3.1.11**; it is not part of the general error-449 sweep that follows.
   - An extra positional argument beyond the parameter count raises error 450.
   - Positional arguments from a trailing `ParamArray` parameter's position onward are collected by that
     parameter (see [ParamArray](#paramarray)).
2. A general sweep follows argument mapping, and raises error 449 for a non-`Optional` parameter that has no
   argument mapped to it.

The errors are listed in [Run-time Errors](#run-time-errors).

### Optional Parameters

An unmapped `Optional` parameter uses
[VBParameterSymbol](../api/RDCore.SDK.Model.Symbols.VBProject.VBParameterSymbol.html)`.DefaultValue` directly.
`VBParameterSymbol.DefaultValue` holds an `Optional` parameter's default value, and is described in
[**RD-VBAL §5.3.1.5** Parameter Lists](rd-vbal.5.3.1.5.parameter-lists.md).

|`VBParameterSymbol.DefaultValue`|Value bound to the unmapped `Optional` parameter|
|---|---|
|The pre-computed default value (the declaration has an `= ...` default-value clause)|That default value.|
|`null` (the declaration has no `= ...` default-value clause)|The declared type's default value.|

An unmapped `Optional` parameter's default is bound with no Let-coercion and no reference binding: an unmapped
`Optional` parameter has no caller expression to coerce from or alias.

### Omitted Arguments and IsMissing

In MS-VBA, `IsMissing`
([**MS-VBAL §6.1.2.7.1.6** IsMissing](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9ec6f6f1-14a6-458e-9024-05dd0d9afb26))
depends on an omitted argument's parameter being `Variant` (see
[**RD-VBAL §6.1.2.7** Information](rd-vbal.6.1.2.7.information.md)):

- The value of an omitted argument is a `VT_ERROR` `Variant` carrying `DISP_E_PARAMNOTFOUND`.
- `IsMissing` always returns `False` for a non-`Variant` parameter, without raising an error. Only a `Variant`
  can hold the `DISP_E_PARAMNOTFOUND` sentinel.

### ParamArray

The arguments passed to a `ParamArray` parameter are collected by
`RuntimeExpressionEvaluator.CollectParamArrayArguments`.

- A trailing [ParamArrayParameterSymbol](../api/RDCore.SDK.Model.Symbols.VBProject.ParamArrayParameterSymbol.html)
  collects every positional argument from its own position onward (**MS-VBAL §5.3.1.11**), instead of mapping
  1:1.
- `RuntimeExpressionEvaluator.CollectParamArrayArguments` collects the arguments into a fresh, 0-based array of
  `Variant`.
- Each argument collected into a `ParamArray` is Let-coerced to `Variant`, the same way any other `ByVal`
  argument is Let-coerced to its parameter's declared type (see
  [**RD-VBAL §5.5.1.2** Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md)).
- A `ParamArray` parameter is never targetable by name.
- A `ParamArray` parameter is always considered satisfied, even with nothing collected: it receives an empty
  array, not error 449.

`Call Callee(100)` against a `ParamArray rest()` parameter with no arguments left over collects an empty array
whose `Size` is 0. An empty `ParamArray` array has a non-positive storage size, and takes the
`SessionStorage.TryAllocate` non-positive-size path, like `Nothing`, `Null`, `Empty` and an uninitialized array.

A `ParamArray` call with nothing left over is the most common `ParamArray` call shape, which is why zero-size
storage is supported. See [**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md).

### ByVal and ByRef Binding

`ByRef` parameter binding follows **MS-VBAL §5.3.1.11**. A parameter is bound `ByVal` unless both of these hold:

1. the parameter is declared `ByRef`; and
2. the argument-evaluation loop of `RuntimeExpressionEvaluator` resolves the argument to an addressable,
   writable variable whose declared type exactly matches the parameter's declared type, or the parameter's
   declared type is `Variant`.

These two shapes (an exact declared-type match, or a `Variant` parameter) are the two that **MS-VBAL §5.3.1.11**
allows a plain reference binding for without a class/`Object` copy-back.

|Parameter|Argument|Binding|
|---|---|---|
|`ByVal`|Any|A fresh, Let-coerced [ValueBindingHandle](../api/RDCore.SDK.Model.Values.Bindings.ValueBindingHandle.html), which never aliases the caller's storage.|
|`ByRef`|An addressable, writable variable whose declared type exactly matches the parameter's: a name (`x`), or a public variable of an object (`obj.Count`, `.Count`)|A reference binding.|
|`ByRef`, declared `Variant`|An addressable, writable variable, of either of those|A reference binding.|
|`ByRef`, declared as a class or `Object`|A variable of a different declared type|A `ByVal`-style copy: the class/`Object` copy-back is not modeled.|
|`ByRef`|Not recognized as aliasable: an expression, a literal, a variable of mismatched declared type, a read-only target|The same `ByVal`-style Let-coerced copy: **MS-VBAL §5.3.1.11**'s "otherwise" case. Never an error.|
|`Optional`|None (unmapped)|The default value; see [Optional Parameters](#optional-parameters).|
|`ParamArray`|The remaining positional arguments|A fresh, 0-based `Variant` array; see [ParamArray](#paramarray).|

A reference binding is made as follows:

1. When a `ByRef` argument is aliasable, the argument is passed as a
   [VBRuntimeReference](../api/RDCore.SDK.Model.Values.Runtime.VBRuntimeReference.html), which carries the
   variable's address itself.
2. `RuntimeProcedureInvoker` binds the aliased `ByRef` parameter through `CallStackFrame.PushByRef`.
3. `CallStackFrame.PushByRef` creates a name-aliasing binding onto the same address as the caller's
   variable, not a copy.
4. [ISymbolResolver](../api/RDCore.SDK.Runtime.Abstract.Execution.ISymbolResolver.html)`.TryGetAddress` and
   `ICallStackFrame.TryGetAddress` resolve the `ByRef`-aliased parameter to the aliased address. See
   [**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md).

A write to a `ByRef`-aliased parameter inside the callee is immediately visible to the caller.

`CallStackFrame.ReleaseAll` never deallocates the address of a `ByRef` alias. The address belongs to its
original allocator; the callee only borrows it.

The `ByVal` and `ByRef`-fallback copy is made by a direct Let-coercion call, since there is no addressable
symbol to Let-assign through. A Let-assignment to the function result variable makes the same lower-level call,
for the same reason; see [**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md).

> [!NOTE]
> **Not implemented.** The **MS-VBAL §5.3.1.11** class/`Object` copy-back for `ByRef` arguments is not modeled.
> A `ByRef` parameter declared as a class or `Object`, whose argument is a variable of a different declared
> type, falls through to a `ByVal`-style copy.

> 👉 UDT values **must** be passed by reference (`ByRef`). See
> [**RD-VBAL §2.5.2.1.3** User-Defined Type (UDT) Values](rd-vbal.2.5.2.1.3.udt-values.md).

### Standard Library Calls

- The evaluator coerces every argument of an external (standard library) call to the parameter's declared type
  on the way in.
- A standard library member's `Variant` parameter accepts its argument.

> 👉 An external call carries runtime values rather than typed ones, so the declared type a member was called
> with is recovered when the call is dispatched. See [**RD-VBAL §6.0** Standard Library](rd-vbal.6.0.standard-library.md)
> and [**RD-VBAL §6.1.2.11** Strings](rd-vbal.6.1.2.11.strings.md) (`Len` / `LenB`).

### Frame Setup

- `RuntimeProcedureInvoker` looks up the callee's own lowered body by symbol, and pushes a fresh
  `ICallStackFrame` for the callee.
- `RuntimeCallStack` enforces a call-depth limit, in `OnBeforeTryPush`. Exceeding the call-depth limit raises
  run-time error 28, "Out of stack space".
- The `Me` value is pushed to the stack frame of an instance member call as any parameter is. See
  [**RD-VBAL §5.6.11** Instance Expressions](rd-vbal.5.6.11.instance-expressions.md).
- Each invocation of a `Function` or `Property Get` gets a fresh function result variable
  ([**MS-VBAL §5.3.1** Procedure Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/227005ad-78fb-479f-8145-fa3b8b610386)),
  modeled as `ICallStackFrame.ReturnValue`, a single per-activation slot. `ReturnValue`
  is seeded to the default value of the procedure's declared return type before the procedure body runs. See
  [**RD-VBAL §5.3.1.6** Subroutine and Function Declarations](rd-vbal.5.3.1.6.subroutine-and-function-declarations.md).
- **MS-VBAL** procedure invocation step 4 reads: "create the function result variable and any procedure extent
  local variables declared within the procedure". RD-VBA implements it for `Dim` and `Static` locals as well as
  for the function result variable: `RuntimeProcedureInvoker.HoistLocals` walks the procedure's `Locals`
  immediately after parameter binding, before the body runs. See
  [**RD-VBAL §5.4.3.1** Local Variable Declarations](rd-vbal.5.4.3.1.local-variable-declarations.md).
- `RuntimeProcedureInvoker` always sets `RuntimeEvaluationContext.Scope` to the invoked procedure's own `Uri`
  for the whole activation.
- `RuntimeProcedureInvoker` runs the callee body through the same `ProcedureExecutor` as the caller. See
  [**RD-VBAL §3.5.4** Execution](rd-vbal.3.5.4.execution.md).

### Return Value

`RuntimeProcedureInvoker` reports the callee's outcome back to the caller:

|Callee outcome|Invocation result|
|---|---|
|`ExitProcedure`, from a `Function` or `Property Get`|A successful result: the value of its own function result variable.|
|`ExitProcedure`, from a `Sub`|A successful result: [VBVoidValue](../api/RDCore.SDK.Model.Values.VBVoidValue.html).|
|`Error`|A [RuntimeSemanticsEvaluationResult](../api/RDCore.SDK.Runtime.Shared.RuntimeSemanticsEvaluationResult.html) error.|

A `Function` or `Property Get` return value is held in `ICallStackFrame.ReturnValue`, which models the function
result variable. `RuntimeProcedureInvoker` reads `frame.ReturnValue` back once `ExitProcedure` is reached, and
reports it as the call's result.

The read-back happens however `ExitProcedure` was reached: an explicit
`Exit Function`/`Exit Property`, or falling off the end of the body. See
[**RD-VBAL §5.4.2.18** Exit Function Statement](rd-vbal.5.4.2.18.exit-function-statement.md) and
[**RD-VBAL §5.4.2.19** Exit Property Statement](rd-vbal.5.4.2.19.exit-property-statement.md).

The caller's own `ExecuteCall` turns a callee's `RuntimeSemanticsEvaluationResult` error back into an `Error`
outcome. A nested call's runtime error therefore propagates as any other runtime error does; see
[**RD-VBAL §5.4.4.1** On Error Statement](rd-vbal.5.4.4.1.on-error-statement.md).

### Run-time Errors

|Condition|Run-time error|
|---|---|
|A `MissingArgumentNode` (an argument omitted with a comma) is mapped to a non-`Optional` parameter. Checked during argument mapping.|448 — Named argument not found|
|After argument mapping, a non-`Optional` parameter other than a `ParamArray` has no argument mapped to it.|449 — Argument not optional|
|An extra positional argument goes beyond the parameter count, and there is no trailing `ParamArray` parameter.|450 — Wrong number of arguments or invalid property assignment|
|Pushing the callee's frame exceeds the call-depth limit (`RuntimeCallStack.OnBeforeTryPush`).|28 — Out of stack space|

## Implementation

|Type or member|Role|
|---|---|
|`IProcedureInvoker`, `CallableBindingHandle`|The call contract (**RDCore.SDK**).|
|`RDCore.Runtime.Execution.RuntimeProcedureInvoker`|The `IProcedureInvoker` implementation (**RDCore.Runtime**).|
|`RuntimeExpressionEvaluator.MapArguments`|Argument mapping; errors 448, 449 and 450.|
|`RuntimeExpressionEvaluator.CollectParamArrayArguments`|`ParamArray` collection.|
|`RuntimeExpressionEvaluator.ProcedureInvoker`, `RuntimeExpressionEvaluator.LetCoercionProvider`|Settable properties, not constructor parameters.|
|`CallStackFrame.PushByRef`|Binds an aliased `ByRef` parameter.|
|`CallStackFrame.ReleaseAll`|Frees the frame's storage, never a `ByRef` alias's address.|
|`RuntimeCallStack.OnBeforeTryPush`|Enforces the call-depth limit; error 28.|
|`ICallStackFrame.ReturnValue`|The function result variable.|
|`ICallStackFrame.TryGetAddress`, `ISymbolResolver.TryGetAddress`|Resolve a `ByRef`-aliased parameter to the aliased address.|
|`VBParameterSymbol.DefaultValue`|An `Optional` parameter's pre-computed default value.|
|`ParamArrayParameterSymbol`|A trailing `ParamArray` parameter.|
|`VBRuntimeReference`|An aliasable `ByRef` argument: the variable's address itself.|
|`ValueBindingHandle`|A `ByVal` (or `ByRef`-fallback) parameter's fresh binding.|

`RuntimeExpressionEvaluator.ProcedureInvoker` is settable because `RuntimeProcedureInvoker` needs a
`ProcedureExecutor` built from a `StatementRuntimeSemanticsProvider` built from the same
`RuntimeExpressionEvaluator`: the evaluator must exist before its invoker can be built. See
[**RD-VBAL §2.3.1** Composition Root](rd-vbal.2.3.1.composition-root.md).

---
> ⏮️ [**RD-VBAL §5.3.1.10** Lifecycle Handler Declarations](rd-vbal.5.3.1.10.lifecycle-handler-declarations.md) | ⏭️ [**RD-VBAL §5.4** Procedure Bodies and Statements](rd-vbal.5.4.procedure-bodies-and-statements.md)
