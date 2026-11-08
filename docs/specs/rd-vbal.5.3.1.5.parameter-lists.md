# 5.3.1.5 Parameter Lists

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.3.1.5** Parameter Lists](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/78bcb344-4966-4401-bb55-72729790ebee).

## Syntax

A declared parameter is a [VBParameterSymbol](../api/RDCore.SDK.Model.Symbols.VBProject.VBParameterSymbol.html);
a trailing `ParamArray` parameter is a
[ParamArrayParameterSymbol](../api/RDCore.SDK.Model.Symbols.VBProject.ParamArrayParameterSymbol.html).

The parameter list of a `Property Let` or `Property Set` follows this **MS-VBAL §5.3.1.5** rule:

```
property-parameters = "(" [parameter-list ","] value-param ")"
```

## Static Semantics

In the `property-parameters` rule, `value-param` is never bracketed, so it is always mandatory, whether or not
there are index parameters. A `Property Let` or `Property Set` with no parameters has no value parameter to
receive what is being assigned, and is reported as [VBC09321](../diagnostics/vbc09321.md). See also
[**RD-VBAL §5.3.1.7** Property Declarations](rd-vbal.5.3.1.7.property-declarations.md).

> UDT values **must** be passed by reference (`ByRef`). See
> [**RD-VBAL §2.5.2.1.3** User-Defined Type (UDT) Values](rd-vbal.2.5.2.1.3.udt-values.md).

### Array parameters

An array parameter - a name written with `()`, `a() As Long` - is passed by reference: the procedure is given the array itself, and a parameter that is
declared `ByVal` is refused, as **MS-VBA** refuses it (recorded in Excel), wherever the parameter is declared - a procedure, a property, an event, a
`Declare`, an implementation of an interface's member:

|Declaration|Outcome|
|---|---|
|`a() As Long`, `ByRef a() As Long`|Valid.|
|`ByVal a() As Long`, `Optional ByVal a() As Long`|[VBC09371](../diagnostics/vbc09371.md) _Array argument must be ByRef_.|
|`ByVal v As Variant`|Valid: a `Variant` that holds an array holds a copy of it.|
|`ByVal ParamArray a() As Variant`, `ByRef ParamArray a() As Variant`|[VBC00001](../diagnostics/vbc00001.md) _Syntax error_: a `ParamArray` is written with neither.|

`DeclarationStaticSemanticsEvaluator.CheckSyntax` reports them, off the declarations of the syntax tree. An argument written `ByVal` for an array parameter
is `VBC09371` too ([**RD-VBAL §5.6.13.1** Argument Lists](rd-vbal.5.6.13.index-expressions.md#56131-argument-lists)).

### Parameters of a user-defined type

A record of a user-defined type is passed by reference, to a parameter of its type. A parameter of one declared `ByVal` - of a `Sub`, a `Function`, a property
(the value of a `Property Let` too) or a `Declare` - is [VBC09381](../diagnostics/vbc09381.md) _User-defined type may not be passed ByVal_, where the module's
declarations are checked, whether the procedure is called or not. An event's is not: `Event Moved(ByVal pt As TPoint)` compiles.

> [!IMPORTANT]
> **MS-VBAL §5.3.1.5** sets no such rule: a `ByVal` parameter "defines a local variable", and a record is a value a variable holds. **MS-VBA** refuses the
> declaration (recorded in Excel, `oracle\s56` I10, C06, C15, D05-D07, E01, E07) and compiles an event's (E03), and so does **RD-VBA**: a module RD-VBA
> compiles is one MS-VBA compiles.

What a call gives such a parameter is checked with the call ([**RD-VBAL §5.3.1.11**](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)): a record
given to a `Variant` is [VBC09382](../diagnostics/vbc09382.md), and one given in parentheses, `F (pt)`, is a value and no variable to pass,
[VBC09333](../diagnostics/vbc09333.md) _Variable required_.

A parameter is an array by the `()` after its name: a trailing `()` on its `As` clause (`a As Long()`, `ByVal a As Long()`) is a syntax error, as it is in
**MS-VBA** ([**RD-VBAL §5.2.3.1.3**](rd-vbal.5.2.3.module-declarations.md#52313-array-dimensions-and-bounds)).

### Optional parameters

`VBParameterSymbol.DefaultValue` holds an `Optional` parameter's default value.

|Declaration|`VBParameterSymbol.DefaultValue`|
|---|---|
|`Optional` with an `= ...` default-value clause|The clause's constant expression.|
|`Optional` with no `= ...` default-value clause|`null`|

When an `Optional` parameter has no argument mapped to it, the invocation reduces `VBParameterSymbol.DefaultValue` as a
`Const`'s expression is reduced, and Let-coerces it to the parameter's declared type, unless it is `Nothing`, which is
Set-assigned. When `DefaultValue` is `null`, it falls back to the parameter's declared type's default value, which for a
`Variant` is Missing. See
[**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md).

### ParamArray parameters

How the arguments passed to a `ParamArray` parameter are collected is described in
[**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md).
