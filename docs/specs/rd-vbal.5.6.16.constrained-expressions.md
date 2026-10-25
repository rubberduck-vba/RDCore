# 5.6.16 Constrained Expressions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.16** Constrained Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4ac0d7e3-a3e9-4c24-83ba-17295c91823b).


## 5.6.16.1 Constant Expressions

This section corresponds to [**MS-VBAL §5.6.16.1** Constant Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d3ffced9-66c1-4352-9f55-ef7942e4b1bc).

> [!NOTE]
> Reserved. This section has no content yet.


## 5.6.16.2 Conditional Compilation Expressions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.16.2** Conditional Compilation Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/efd3d84d-0d78-4456-882e-8422cf2938a5).

Precompiler constants are interpreted as `Integer` values. A precompiler constant value is a
[PrecompilerConstantValue](../api/RDCore.SDK.Model.Values.PrecompilerConstantValue.html), an additional numeric data
value. See [**RD-VBAL §2.5.2.1.1** Numeric Values](rd-vbal.2.5.2.1.1.numeric-values.md).


## 5.6.16.3 Boolean Expressions

This section corresponds to [**MS-VBAL §5.6.16.3** Boolean Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/77c33f33-dfd4-402e-83d6-8fe78918bde6).

> [!NOTE]
> Reserved. This section has no content yet.


## 5.6.16.4 Integer Expressions

This section corresponds to [**MS-VBAL §5.6.16.4** Integer Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/38dc09d1-50ce-45f1-97b0-535c64dc7216).

> [!NOTE]
> Reserved. This section has no content yet.


## 5.6.16.5 Variable Expressions

This section corresponds to [**MS-VBAL §5.6.16.5** Variable Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e59342b3-dfe4-4c1e-9c43-acd48fc18a3b).

> [!NOTE]
> Reserved. This section has no content yet.


## 5.6.16.6 Bound Variable Expressions

This section corresponds to [**MS-VBAL §5.6.16.6** Bound Variable Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/018d50ba-a3f8-48ff-affc-39bc5ac41888).

A bound variable expression - the target of an `LSet` or an `RSet` statement, the control variable of a `For` or a
`For Each` loop, the string argument of a `Mid` statement, an input variable of an `Input #` statement - "is invalid if
it is classified as something other than a variable expression". A constant or a function of the library there is
[`VBC09333`](../diagnostics/vbc09333.md) (`VariableRequired`), whatever its declared type; see
[**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md), which states the rule for the target of every
assignment, [**RD-VBAL §5.4.3.5** Mid Statement](rd-vbal.5.4.3.5.mid-statement.md) and
[**RD-VBAL §5.4.5** File Statements](rd-vbal.5.4.5.file-statements.md).


## 5.6.16.7 Type Expressions

This section corresponds to [**MS-VBAL §5.6.16.7** Type Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/0b940919-e81d-4601-8d30-bcd6dd4d3ddb).

A type expression that is not a reserved data-type name binds under the type binding context
([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md#lookup-order)), and is invalid if what it
binds is not a type: a user-defined type, an Enum type or a class is a type, and a procedural module or a project is
not. A type qualified by a module binds that module's own type, whatever else has its bare name: `Mod2.Helpers`, beside
a procedural module `Helpers`, is `Mod2`'s type
([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md#qualified-type-names)).

What a type expression binds that is no type is an error of its own, as MS-VBA has it (recorded in Excel), wherever a
type is written: the `As` clause of a declaration (a variable, a constant, a parameter, a result, a field of a
user-defined type, a local of any procedure), the `As` clause of a `ReDim`, and the type of a `TypeOf ... Is`
([**RD-VBAL §5.6.7**](rd-vbal.5.6.7.typeof-is-expressions.md)).

|It binds|Example|Error|
|---|---|---|
|A procedural module of the project|`Dim g As Globals`|[VBC09366](../diagnostics/vbc09366.md) _A module is not a valid type_|
|A procedural module of a library|`Dim s As Strings`, `Dim s As VBA.Strings`|[VBC09372](../diagnostics/vbc09372.md) _Automation type not supported in Visual Basic_|
|A project or a library|`Dim p As VBA`|[VBC09373](../diagnostics/vbc09373.md) _Expected user-defined type, not project_|
|Nothing that is a type, or nothing at all|`Dim w As Widgit`|[VBC09311](../diagnostics/vbc09311.md) _User-defined type not defined_|

`New` naming a procedural module is [VBC09310](../diagnostics/vbc09310.md) _Type mismatch_, as is `New` naming
anything else that binds but is not a class; `New` with a name nothing declares is VBC09311
([**RD-VBAL §5.6.8** New Expressions](rd-vbal.5.6.8.new-expressions.md)).

An intrinsic type written where a value is (`x = Long`, `Debug.Print Variant`) is
[VBC00001](../diagnostics/vbc00001.md) _Syntax error_, as MS-VBA has it: a type is no value. `Object` is no
reserved type name there, and is read as a name, as MS-VBA reads it (`Debug.Print Object` is _Variable not defined_
under `Option Explicit`).

> [!NOTE]
> **Not implemented.**
>
> - The declared type of a field of a user-defined type, or of a local constant, is checked only for a module or a
>   project: a name that does not resolve there is not refused (`s As Missing` in a `Type`, or
>   `Const c As Missing = 1` in a procedure, loads) ([**RD-VBAL §5.0** Semantics](rd-vbal.5.0.semantics.md)).
> - A declared type qualified by the name of the enclosing project is VBC09311 though it names a type, since the
>   qualifier does not resolve: `Dim w As Project1.Widget`.


## 5.6.16.8 AddressOf Expressions

This section corresponds to [**MS-VBAL §5.6.16.8** AddressOf Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/0e8eb1ba-f6fb-4865-a334-17ec012c1756).

`AddressOf <procedure>` is an argument, and only an argument, as the grammar of **MS-VBAL** has it: `x = AddressOf P` is a syntax error, as it is in
**MS-VBA**. As in **MS-VBA** (recorded in Excel):

- **Static semantics.** What it names is a `Sub`, a `Function` or a `Property Get` of a standard module, named bare or qualified by its module, `Private` ones
  of the module it is written in included. A variable is [`VBC09364`](../diagnostics/vbc09364.md) _Expected Sub, Function, or Property_, and a member of a
  class module or of an object [`VBC09367`](../diagnostics/vbc09367.md) _Invalid use of AddressOf operator_. Its declared type is implementation-defined: it
  is the environment's `LongPtr`, which a `LongPtr` or a `Variant` parameter takes - and a `Long` one does not, in a 64-bit environment
  ([**RD-VBAL §5.5.1.1**](rd-vbal.5.5.1.1.static-semantics.md)).
- **Runtime semantics.** It is the address of the procedure in the session's memory: never `0`, the same every time, bare or qualified, and another for
  another procedure ([**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md)). The address is made the first time it is asked for and
  lasts as long as the session; what is at it reads as zeros, and a `POKE` there is refused.

> [!NOTE]
> 🚧 Not implemented yet (*Ptr plan phase B): native code given the address - the callback of `EnumWindows`, say - has nothing it can call that runs the
> procedure. In **MS-VBA** the procedure runs inside the native call, and an error raised in it is the error of the `Declare` call.

---
> ⏮️ [**RD-VBAL §5.6.15** With Expressions](rd-vbal.5.6.15.with-expressions.md) | ⏭️ [**RD-VBAL §6.0** Standard Library](rd-vbal.6.0.standard-library.md)
