# 5.3.1.7 Property Declarations

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.3.1.7** Property Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d19bfeaa-cca6-43d8-9188-27355b8ee025).

## Static Semantics

A module's `Property Get`, `Property Let` and `Property Set` sharing a name must together describe one valid
property. Two diagnostics report a property declaration that does not:

|Condition|Example|Diagnostic|
|---|---|---|
|A `Property Let` or `Property Set` has no parameters, so it has no value parameter to receive what is being assigned.|`Property Let Foo()`|[VBC09321](../diagnostics/vbc09321.md)|
|Two accessors' index-parameter lists differ in count, name, declared type, or `ByRef`/`ByVal` mechanism.|`Property Get Foo(i As Integer)` next to `Property Let Foo(j As Long, v As Integer)`|[VBC09320](../diagnostics/vbc09320.md)|
|An index parameter is `Optional` or a `ParamArray`, and the property has more than one accessor.|`Property Get Foo(Optional i As Integer)` next to `Property Let Foo(i As Integer, v As Integer)`|VBC09320|
|`Property Let` and `Property Get` sharing a name declare a different value type.|`Property Get Foo() As Long` next to `Property Let Foo(v As String)`|VBC09320|
|`Property Set`'s value parameter is not `Object`, `Variant`, or a named class.|`Property Set Foo(v As Long)`|VBC09320|

- The index-parameter-equivalence and `Property Set` value-type conditions of VBC09320 are literal
  **MS-VBAL §5.3.1.7** static-semantics bullets.
- Implicit vs. explicit `ByRef` on a corresponding index parameter is not a difference: only the actual
  `ByRef`-vs-`ByVal` mechanism is compared. This matches **MS-VBAL §5.3.1.7**'s own exception for that case.
- `Optional`/`ParamArray` index parameters are legal only on a single-accessor property.
- A `Property Get` needs no value parameter: it has no value being assigned to it, only (optionally) index
  parameters to read by. The `value-param` rule is described in
  [**RD-VBAL §5.3.1.5** Parameter Lists](rd-vbal.5.3.1.5.parameter-lists.md).

## Runtime Semantics

- A `Property Get` has a function result variable, as a `Function` does: each invocation gets a fresh one,
  modeled as [ICallStackFrame](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html)`.ReturnValue`.
  `Property Get` return values follow
  [**MS-VBAL §5.3.1** Procedure Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/227005ad-78fb-479f-8145-fa3b8b610386).
  See [**RD-VBAL §5.3.1.6** Subroutine and Function Declarations](rd-vbal.5.3.1.6.subroutine-and-function-declarations.md).
- [VBVoidType](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.Complex.VBVoidType.html) is the data type returned by
  `Property Let` and `Property Set` procedures.
