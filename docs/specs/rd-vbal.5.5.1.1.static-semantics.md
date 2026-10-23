# 5.5.1.1 Static semantics

This section corresponds to [**MS-VBAL §5.5.1.1** Static semantics](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4ef84713-33da-445b-942e-345982b3267d).

The pairs of declared types MS-VBAL §5.5.1.1 makes invalid to Let-coerce are checked by
[LetCoercionStaticSemantics](../api/RDCore.SDK.Semantics.Static.LetCoercionStaticSemantics.html), and reported as
[`VBC09310`](../diagnostics/vbc09310.md) (Type mismatch).

## An object where a value is expected

Where a value is expected, an object stands for its _simple data value_: what its default member returns
(**MS-VBAL §5.5.1.2.13**). [ObjectValueStaticSemantics](../api/RDCore.SDK.Semantics.Static.ObjectValueStaticSemantics.html)
checks at compile time that it can have one, as MS-VBA does (recorded in Excel, each statement compiled alone) - and, where
the specification makes it a compile-time validation, where MS-VBA does not:

|The object|Where a value of a declared type is expected|`Abs`, `Int`, `Fix`|`Len`, `LenB`|Where a `Variant` is expected|
|---|---|---|---|---|
|The `Nothing` literal|[`VBC09304`](../diagnostics/vbc09304.md) Invalid use of object|`VBC09304`|`VBC09304`|compiles|
|Of a class whose default member takes an argument (a `Collection`)|[`VBC09339`](../diagnostics/vbc09339.md) Argument not optional|`VBC09339`|`VBC09339`|compiles|
|Of a class whose default member returns an object|[`VBC09310`](../diagnostics/vbc09310.md) Type mismatch|compiles|[`VBC09333`](../diagnostics/vbc09333.md) Variable required|compiles|
|Of a class whose default member returns a `String` or a `Variant`|compiles|compiles|compiles|compiles|
|Of a class whose default member returns another value|compiles|compiles|`VBC09333` Variable required|compiles|
|Of a class that has no default member|`VBC09310` Type mismatch 👉|compiles|compiles|compiles; `VBC09310` 👉 assigned to a `Variant` with `Let`|

A value of a declared type is expected:

- of the value of a `Let` assignment to a variable of a value type (a number, a `String`, a `Boolean`, a `Date`, an
  enumeration);
- of an argument of a parameter declared with a value type, `ByVal`, or `ByRef` and given an expression;
- of an operand of any operator but `Is`: arithmetic, `&`, the comparisons, `Like`, the logical operators;
- of an argument of a function of the standard library whose parameter MS-VBA types, although MS-VBAL declares it
  `Variant`: the conversions `CBool` to `CVErr` (not `CVar`, `CVDate` or `CDec`), `Sgn`, and the `$` functions of the
  `Strings` module (`Left$`, `Mid$`, `Trim$`, …).

`Abs`, `Int` and `Fix` expect a value of any type. `Len` and `LenB` expect "any valid string expression, or any valid
variable name" ([**RD-VBAL §6.1.2.11.1.22**](rd-vbal.6.1.2.11.strings.md#61211122-len--lenb)): an object's value is no
variable, so it must be a `String` or a `Variant`. Every other parameter declared `Variant` takes an object
as it is: `CVar`, `Round`, `Hex`, `Str`, `Left`, `Mid`, `Trim`, the functions of `DateTime` and `Information`, `IIf`,
`Choose`. A standard-library parameter says which it is with
[StdLibValueAttribute](../api/RDCore.SDK.Runtime.Abstract.StdLib.StdLibValueAttribute.html).

👉 MS-VBAL §5.5.1.1 makes an object of a class that has no accessible default member statically invalid to Let-coerce,
and RD-VBA reports it at compile time, as the specification says it is validated. MS-VBA compiles it, and raises run-time
error 438 (Object doesn't support this property or method) when it runs, which the error's message says. A parameter
declared `Variant`, and `Abs`, `Int`, `Fix`, `Len` and `LenB`, take the object as it is: the coercion there is the run
time's. And the specification asks only for an "accessible default Property Get or function", where MS-VBA also asks
that it take no argument, which RD-VBA does too.

## A `LongLong`

MS-VBAL §5.5.1.1 makes it invalid to implicitly Let-coerce a `LongLong` to any declared type other than `LongLong` or
`Variant`. MS-VBA refuses only what is narrower than it, and RD-VBA does what MS-VBA does (recorded in Excel, 64-bit):

|To|Implicitly|
|---|---|
|`Byte`, `Integer`, `Long`, an enumeration, `Currency`|[`VBC09310`](../diagnostics/vbc09310.md) Type mismatch|
|`Single`, `Double`, `String`, a fixed-length `String`, `Boolean`, `Date`, `LongPtr`, `Variant`|compiles|

A `LongPtr` is a `LongLong` in a 64-bit environment and a `Long` in a 32-bit one ([**RD-VBAL §2.4.1**](rd-vbal.2.4.1.intrinsic-types.md)),
and so is the address an `AddressOf` expression gives: `ByVal lpfn As Long` takes one in a 32-bit environment, and refuses
it in a 64-bit one. The rule holds wherever a value is Let-coerced: an assignment, and an argument given to a parameter of
a value type.


---
> ⏮️ [**RD-VBAL §5.5.1** Let-coercion](rd-vbal.5.5.1.let-coercion.md) | ⏭️ [**RD-VBAL §5.5.1.2** Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md)
