# 6.1.2.13 Hidden Module

> [!NOTE]
> The `_HiddenModule` is **not** one of the modules [**MS-VBAL §6.1.2**](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e74eaaeb-e2e5-4ec5-9fb0-f3c739c53403)
> specifies, and has no section of its own there. It is the module of the `VBA` library that its type library marks _hidden_, which an object browser lists under that name;
> `Array` is [documented](https://learn.microsoft.com/en-us/office/vba/language/reference/user-interface-help/array-function), the other members are not.
> The platform declares it as it does the modules the specification names, because the language needs it all the same.

The `_HiddenModule` module is represented in the SDK by the type
[IStdHiddenModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdHiddenModule.html), and implemented by `RDCore.Runtime.StdLib.StdHidden`.

A hidden member is left out of a completion list and resolves anyway, being a member of a standard module like any other. The module is declared hidden
(`StdLibModuleAttribute.IsHidden`), and so are its members; the flag is the `hidden` bit (`SymbolProperties.HiddenMemberFlag`, `0x40`) of the symbol's
`SymbolProperties.MemberFlags`. `ObjPtr`, `StrPtr` and `VarPtr` are hidden members of the module on their own account.

|Member|Implemented|
|---|---|
|`Array(ParamArray arglist())`|✅ See below.|
|`Input(Number, FileNumber)`, `Input$(Number, FileNumber)`|✅ The characters read from a file opened for `Input` or `Binary`, with no parsing: a quotation mark or a comma is a character. Error 62 when the file ends first, 52 when the file number is not open, 54 when it was opened in a mode that cannot be read this way ([**RD-VBAL §5.4.5.1**](rd-vbal.5.4.5.file-statements.md)).|
|`InputB(Number, FileNumber)`, `InputB$(Number, FileNumber)`|The bytes of the file as they are, not the characters they encode: a `String` of `Number` bytes (`LenB` is `Number`), which `StrConv(…, vbUnicode)` reads as characters. It reads at the same position `Input` does, on a file opened `For Input` or `For Binary`; on any other, error `54`; past the end of the file, `62`; a negative `Number`, `5`. As **MS-VBA** has it (recorded in Excel).|
|`ObjPtr(Ptr As Unknown)`|✅ The address of an interface of an object in the session's memory, a `LongPtr`. See below.|
|`StrPtr(Ptr As String)`|✅ The address of the characters of a `String` in the session's memory, a `LongPtr`. See below.|
|`VarPtr(Ptr As Any)`|✅ The address of a variable in the session's memory, a `LongPtr`. See below.|
|`Width(FileNumber, Width)`|✅ The member the [**Width statement**](rd-vbal.5.4.5.file-statements.md) is the syntax of: error 5 outside `0`–`255`, 52 and 54 as for any file statement.|

## Array

`Array(<element>, ...)` yields a `Variant` holding a resizable array of `Variant`, with an element for each argument, in order. With no arguments it is _zero-length_: its upper bound is one below its lower, and
`UBound(Array())` is `-1` (which is not the error 9 of an array that has no dimensions at all).

The language has **two** of it, and which one a call is depends on whether the name is qualified:

|Written|Is|Lower bound|
|---|---|---|
|`Array(1, 2, 3)`|The `Array` _keyword_: [ArrayExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.ArrayExpressionNode.html)|The `Option Base` of the module the expression is written in: `Option Base 1` makes `LBound(Array(1, 2, 3))` `1`.|
|`VBA.Array(1, 2, 3)`|A call of the library's member of the hidden module|Always `0`, whatever `Option Base` says.|

👉 This is the classic gotcha of the language, and it is modelled rather than papered over. Like `LBound` and `UBound` (**MS-VBAL §3.3.5.2**, the special forms; see [**RD-VBAL §2.5.2.1.2**](rd-vbal.2.5.2.1.2.array-values.md)),
the keyword is let through by the grammar as an identifier, and the parse listener recognizes it by its token when it is written without a qualifier and with positional arguments: it builds an `ArrayExpressionNode`
rather than an `IndexExpressionNode`. The qualified form reaches the listener as a member access, which stays what it is and resolves to the library's member like any other call of one. A name written `[Array]`, or
typed, or a member called that, is an ordinary name; arguments that are named or omitted leave the call an index expression for the rules about calls to say what is wrong with it.

- **Static semantics.** The expression is a `Variant`; each element is an expression like any other, and can be wrong in its own right.
- **Runtime semantics.** Each element is evaluated and held as a `Variant` (`RuntimeExpressionEvaluator.EvaluateArray`); an object in one is referenced by its cell, as it is by any variable. The lower bound is
  the executing frame's `Option Base`, like the `ReDim` statement's omitted lower bound ([**RD-VBAL §5.4.3.3**](rd-vbal.5.4.3.3.redim-statement.md)).

## VarPtr

`VarPtr` is declared as the `VBA` type library declares it, `VarPtr(Ptr As Any) As LongPtr`: its parameter is `ByRef … As Any`, as a `Declare`'s can be
([StdLibAsAnyAttribute](../api/RDCore.SDK.Runtime.Abstract.StdLib.StdLibAsAnyAttribute.html)). It takes a variable of any type and gives its address in
the session's memory, which is where a `PEEK` or a `POKE` reads and writes it ([**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md#memory-model),
which has what is at every address in one place).
The address space begins above the first 64 KiB, so that no address is `0` or a small number.

As in **MS-VBA** (recorded in Excel):

|Argument|`VarPtr` gives|
|---|---|
|A variable - a local, a parameter, a `Static` local, a variable of a module bare or qualified, a field of the class in its own code by its bare name|Its address: never `0`, aligned to its size, and the same for as long as the variable exists, whatever is assigned to it (a `String`, a `Variant`, an `Object` set and set to `Nothing`).|
|A `ByRef` parameter|The address of the variable it was given; a `ByVal` parameter has its own.|
|The name of a `Function` or a `Property Get`, in its own code|The address of its result variable (**MS-VBAL §5.3.1**): one of its own, which no local has, the same for the whole call whatever is assigned to it. What is at it is what the call returns.|
|A `Variant` `ByRef` parameter given a variable of another type|The address of a `VARIANT` of its own, the same for the whole call, which is not the variable's.|
|An element of an array|Its address in the block the array's elements are laid out in, side by side: the first element's plus its index times the width of an element (1 for a `Byte`, 2 for an `Integer` or a `Boolean`, 4 for a `Long`, 8 for a `Double`, a `Currency` or a `Date`, a pointer for a `String` or an object, a `VARIANT` for a `Variant`, a record's in-memory size for a record), the first subscript varying fastest. The block is the array's for as long as the array is: every `ReDim` makes another, `Preserve` or not, and so does an array assigned another. Error 9 for an array that has no elements.|
|An element of a fixed-size array field of a record (`r.N(2)`)|Inline, in the record: the record's address, plus the field's offset, plus the element's.|
|A field of a record (`r.X`, `a(1).X`, `r.Inner.X`)|The record's address plus the field's offset, in **MS-VBA**'s layout for the session's pointer width: each field aligned to its own size, a nested record to its strictest field's. A fixed-length `String`, a variable or a field, is passed as a `String` made of it: a temporary.|
|What is no variable: a literal, an expression, `(x)`, a constant, what a function returns, a public variable of an object named through it (`h.Value`, `Me.Value`)|The address of a temporary, which is freed when the statement is over: two temporaries of one statement have two addresses, so `VarPtr(1) = VarPtr(1)` is `False`.|
|`ByVal n`|The bytes `n` is stored in, read as a whole number: a whole number or a `Boolean` is itself, a `Double`, a `Date` or a `Currency` its eight bytes, a `Variant` its `VARIANT`'s type (`VarPtr(ByVal Null)` is `1`). A `String` or an object is passed in a temporary.|
|An array|Compile error [`VBC09310`](../diagnostics/vbc09310.md) _Type mismatch_, as it is for a `Declare`'s `As Any`; an element of it is valid.|

> [!NOTE]
The bytes at an element's or a record's address are what it holds, laid out as **MS-VBA** lays them out: a number, a `Boolean`, a `Date` and a
fixed-length `String` are their bytes, a variable-length `String` the address of its characters ([StrPtr](#strptr)), an object the address of its interface
([ObjPtr](#objptr)), a `Variant` its `VARIANT` ([below](#variant)), and a record its fields' at their offsets, padding zeroed. A `POKE` there writes the element
or the field the byte is part of ([**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md)).

> [!NOTE]
> 🚧 Not implemented yet: a `Single` passed `ByVal` has its four bytes and zeros above them, where **MS-VBA** leaves whatever was there.

### SAFEARRAY

A dynamic array - a field of a record, an element of a `Variant` array - and a `Variant` holding an array, is the address of a `SAFEARRAY`, `0` for an array
that has no dimensions. As in **MS-VBA** (recorded in Excel), the descriptor is the number of dimensions (2 bytes), its features (2 bytes), the width of an
element (4), its locks (4), the address of the elements (a pointer, eight bytes in on 64-bit), then each dimension's count of elements and lower bound,
4 bytes each, the **last** dimension first.

|Elements|Features|
|---|---|
|A number, a `Boolean`, a `Date`, a fixed-length `String`|`&H80` (`FADF_HAVEVARTYPE`)|
|A `String`|`&H180`, `BSTR`s|
|A `Variant`|`&H880`, `VARIANT`s|
|An object of a class, `Object`|`&H440`, interfaces|
|`IUnknown`|`&H240`|
|A record|`0`|

The address of the elements is the block they are laid out in ([VarPtr](#varptr)), and the locks are the `For Each` statements over the array and the calls
it is passed an element to. A dynamic array's descriptor is the same for as long as it holds an array - a `ReDim` moves the elements, not the descriptor - and
is freed when it holds none, after an `Erase`; a `Variant`'s is the array's, and another array assigned to it has another.

A `POKE` into a lower bound moves the elements to other subscripts, where they are; into a count, makes the array shorter. A count that makes it longer than
it is would be memory it is not, and is refused, as is a `POKE` into anything else of the descriptor. A `POKE` into the pointer a dynamic array is makes it
the array of the descriptor it points at, which the two then hold - an element written through either is the other's - and `0` makes it an array of no
dimensions; the descriptor it pointed at is not freed.

### Variant

A `Variant` is a `VARIANT`, 24 bytes in a 64-bit session and 16 in a 32-bit one, as in **MS-VBA** (recorded in Excel): its first two bytes are what `VarType`
says it holds - an array's being `vbArray` and its elements' - and its data is eight bytes in. A number, a `Boolean`, a `Date` and a `Currency` are their
bytes, a `String` the address of its characters, an object the address of its class's interface, an array the address of its [SAFEARRAY](#safearray), and a
`Decimal` is a `DECIMAL` over the whole of it but its type. A `POKE` into the type or the data makes it what they say; into the data of a `String`, an object
or an array, it is the pointer it is, as a variable's is. A type changed to or from one of those is refused: what it would point at is nothing the session has.

## StrPtr

`StrPtr` is declared as the `VBA` type library declares it, `StrPtr(Ptr As String) As LongPtr`, and is given where its argument is
([StdLibByAddressAttribute](../api/RDCore.SDK.Runtime.Abstract.StdLib.StdLibByAddressAttribute.html)): it gives the address of the characters of the very
`String` it is passed. Those are in a `BSTR` of the session's memory, as **MS-VBA** lays one out: a `Long` of the length in bytes just before them
(`StrPtr(s) - 4`), the characters two bytes each - and the odd byte of a `String` that has one - then a null character
([**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md)). The bytes of a `String` variable, at `VarPtr(s)`, are this address.

As in **MS-VBA** (recorded in Excel):

|Argument|`StrPtr` gives|
|---|---|
|`vbNullString`, a `String` variable nothing was assigned to|`0`. A zero-length `String`, `""`, is not `0`.|
|A `String` variable, an element of a `String` array, a `String` field of a record|The address of its characters, the same until a `String` is assigned to it - any `String`: the same text, and `s = s` itself, are another. `t = s` gives `t` characters of its own, and so does a `ByVal` parameter; a `ByRef` one is the variable's.|
|A `Variant` holding a `String`|The address of the `Variant`'s own characters.|
|What is no `String` variable: a literal, an expression, a fixed-length `String`, a number|The address of the characters of a `String` made of it, a temporary freed when the statement is over.|

A `POKE` into the characters changes the `String`, in place: `StrPtr(s)` is where it was. So does the `Mid` statement, which writes the characters of its
target where they are.

The bytes of the `BSTR` are its own, as in **MS-VBA** (recorded in Excel):

|A `POKE` into|Makes|
|---|---|
|The length, `StrPtr(s) - 4`|The `String` as long as the length says, at the same address: `LenB` and `Len` are what it says, and the characters past it stay where they are - the length written back is the `String` it was. A length of `0` is a zero-length `String`, not `vbNullString`. A length that goes on past the `BSTR` is refused, as reading memory it is not.|
|A `String`'s pointer, `VarPtr(t)` - a variable, an element, a field|`0`: `vbNullString`; the `BSTR` it pointed at is not freed, and pointing at it again is the `String` again. `StrPtr(s)`: the `String` of `s`, which the two then hold - a change to the characters through either, a `POKE` or the `Mid` statement, is the other's. A pointer is written a byte at a time, and one that points at no `BSTR` yet leaves the `String` as it was, its bytes as they were written.|

## ObjPtr

`ObjPtr` is declared as the `VBA` type library declares it, `ObjPtr(Ptr As Unknown) As LongPtr`, and is given where its argument is
([StdLibByAddressAttribute](../api/RDCore.SDK.Runtime.Abstract.StdLib.StdLibByAddressAttribute.html)): it gives the address of the interface of the object
that the variable it is passed is declared as. An object has one for each interface it is asked for through, made the first time its address is asked for and
the same for as long as the object lives; it is freed with the object. The bytes of an object variable, at `VarPtr(o)`, are this address.

As in **MS-VBA** (recorded in Excel):

|Argument|`ObjPtr` gives|
|---|---|
|`Nothing`|`0`.|
|A variable of the object's class, of `Object`, a `Variant` holding the object|The address of the class's own interface: one address for the three.|
|A variable of an interface the class implements, of `IUnknown`|The address of another interface, one for each.|
|An object variable declared `As New` that holds nothing yet|The new object's: passing it is using it.|
|What is no variable: `New Collection`, what a function returns|The address of an interface of the object, which lives as long as something holds it.|

The first pointer at the address is the address of the table of the class's interface, which every object of the class shares and an object of another class
does not; what is past it reads as zeros. A `POKE` there is refused: the interface is the session's.

A `POKE` into an object variable's pointer makes it the object whose interface the pointer is the address of - without a reference of its own, as **MS-VBA**
counts none: the weak reference programs make so - and `0` makes it `Nothing`. A pointer that points at no interface yet leaves the variable as it was, its
bytes as they were written.

> [!NOTE]
> 🚧 Not implemented yet: the table of an interface holds no address a program can call through.

---
> ⏮️ [**RD-VBAL §6.1.2.12** SystemColorConstants](rd-vbal.6.1.2.12.systemcolorconstants.md) | ⏭️ [**RD-VBAL §6.1.3** Predefined Class Modules](rd-vbal.6.1.3.predefined-class-modules.md)
