# 6.1.2.2 Constants Module

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.2.2** Constants Module](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/52c853c1-fda2-4853-8069-172e515310f9).

The `Constants` module is represented in the SDK by the interface
[IStdConstantsModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdConstantsModule.html).

RD-VBA implements the eleven constants of **MS-VBAL §6.1.2.2**. Each is declared on `IStdConstantsModule` as the value
it is: a `static readonly` field of a value type, in the order of the specification's table. Nothing implements the
interface, because a constant is substituted where it is used, never called.

|Constant|Type|Value|
|---|---|---|
|`vbBack`|`String`|`Chr$(8)`|
|`vbCr`|`String`|`Chr$(13)`|
|`vbCrLf`|`String`|`Chr$(13) + Chr$(10)`|
|`vbFormFeed`|`String`|`Chr$(12)`|
|`vbLf`|`String`|`Chr$(10)`|
|`vbNewLine`|`String`|🎯 `Chr$(13) + Chr$(10)`, whatever platform the environment host runs on.|
|`vbNullChar`|`String`|`Chr$(0)`|
|`vbTab`|`String`|`Chr$(9)`|
|`vbVerticalTab`|`String`|`Chr$(11)`|
|`vbNullString`|`String`|A `String` bound to a null pointer rather than to a zero-length string ([VBStringValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBStringValue.html)`.VBNullString`).|
|`vbObjectError`|`Long`|`-2147221504`|

**MS-VBAL §6.1.2.2** leaves `vbNewLine` and `vbNullString` implementation-defined. `vbNewLine` is the line break of a
VBA `String`, which is the same on every platform. `vbNullString` compares equal to `""` and has no data, so its `Len`
and `LenB` are `0`; a copy of it is still `vbNullString`
([**RD-VBAL §5.5.1.2.4** Let-coercion to and from String](rd-vbal.5.5.1.2.runtime-semantics.md#55124-let-coercion-to-and-from-string)).
A `Declare`d procedure's `ByVal` `String` or `ByVal` `As Any` parameter given `vbNullString` receives a null pointer,
where `""` is a pointer to a zero-length string.

> [!NOTE]
> **Not implemented.** A `ByRef` `String` parameter of a `Declare`d procedure given `vbNullString`, and a `String`
> field that holds it in a record passed to one, are given a zero-length string instead of a null one: the `BSTR` they
> point to is an empty one.

## Symbols

[StdLibSymbolReader](../api/RDCore.SDK.Runtime.StdLib.StdLibSymbolReader.html) reads each field as a `Public Const`
member of the `Constants` module, of the field's VBA type, whose value is a literal of the field's value
([**RD-VBAL §6.0.1** Symbol Injection](rd-vbal.6.0.standard-library.md#601-symbol-injection)). Like any constant, it has
no storage: a use of it folds the value.

`Public`, because a module `Const` with no access modifier is not visible outside its module, and a constant has to
reach the project scope to resolve unqualified, as the module's procedures do. A constant resolves unqualified
(`vbCrLf`), qualified by its module (`Constants.vbTab`), and qualified by its library (`VBA.vbCrLf`). A `Public`
constant that a standard module of the workspace declares under the same name shadows the library's, from any module.
A variable named `Constants` that another standard module declares is what the name means, not this module
([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md#lookup-order)). A referenced library's
member of that name is not: Excel's library declares an Enum `Constants`, and with Excel referenced the name is still
this module, because **MS-VBAL §5.6.10** ranks a referenced project's procedural module before the members of a
referenced project's modules. `Constants.xlAll` is then [VBC09316](../diagnostics/vbc09316.md) _Method or data member
not found_, and Excel's constant is `Excel.Constants.xlAll`, or `xlAll` alone.

In the type binding context too, `Constants` is this module ahead of Excel's Enum `Constants`, and a procedural module
is not a type (**MS-VBAL §5.6.16.7**): `Dim c As Constants` is [VBC09311](../diagnostics/vbc09311.md) _User-defined
type not defined_, with Excel referenced or not, and Excel's Enum is `Excel.Constants`. A user-defined type that another
module of the project declares under the name `Constants` is the type
([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md#lookup-order)).

> [!NOTE]
> **Not implemented.** A standard module of the workspace named `Constants` shares this module's scope, which is
> addressed by the module's name. A variable or a procedure it declares under one of these constants' names
> (`Public vbTab As String`) collides with the constant as a duplicate declaration where it is named through the
> module (`Constants.vbTab`) or used inside the module itself. Used unqualified from another module, it is the
> module's own, and a `Public Const` of such a name collides with nothing. A module named like another module of the
> library (`Strings`) collides with that module's members the same way.

A field that is not `static readonly`, or whose type is not a VBA value type, is refused when the library is read. A
C# `const` is a primitive, whose VBA type would be a guess: a C# `long` is a VBA `LongLong`.

---
> ⏮️ [**RD-VBAL §6.1.2.1** ColorConstants Module](rd-vbal.6.1.2.1.colorconstants-module.md) | ⏭️ [**RD-VBAL §6.1.2.3** Conversion Module](rd-vbal.6.1.2.3.conversion-module.md)
