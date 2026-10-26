# 5.2.3 Module Declarations

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.3** Module Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/b40b8d00-3348-43c1-9cfb-c0eadef565ee).

A standard module's non-`Private` members (an explicit `Public` / `Global` / `Friend`, or an implicit
procedure-like member) are also declared in the project scope. Because of this, a sibling module resolves them
without qualification ([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)).


## 5.2.3.1 Module Variable Declaration Lists

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.3.1** Module Variable Declaration Lists](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/0f9113df-fd9c-485a-9583-fdb0e9d68e1b).

👉 When an array declaration specifies no _item type_, the declared item type of the array is `Variant`
([**RD-VBAL §2.4.1** Intrinsic Types](rd-vbal.2.4.1.intrinsic-types.md);
[**RD-VBAL §2.5.2.1.2** Array Values](rd-vbal.2.5.2.1.2.array-values.md)).

### 5.2.3.1.1 Variable Declarations

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.3.1.1** Variable Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/cf31e152-6b15-4ddd-a415-3257a30b1ac8).

Any variable declared with an `As New` clause is an _automatic instantiation variable_
([**MS-VBAL §2.5.1** Automatic Object Instantiation](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/fef06761-45b9-48c2-825c-b75c28aee9b5)).
Its symbol carries the `AutoInstantiated` property of
[SymbolProperties](../api/RDCore.SDK.Model.Symbols.Abstract.SymbolProperties.html)
([**RD-VBAL §2.5.1** Runtime Entities](rd-vbal.2.5.1.runtime-entities.md);
[**RD-VBAL §3.1.1** Attributes](rd-vbal.3.1.1.attributes.md)).

The default instance variable of a predeclared class is created as if declared `As New`
([**RD-VBAL §5.2.4** Class Module Declarations](rd-vbal.5.2.4.class-module-declarations.md), §5.2.4.1.2).

### 5.2.3.1.2 WithEvents Variable Declarations

This section corresponds to [**MS-VBAL §5.2.3.1.2** WithEvents Variable Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f41f8ec5-7a2d-4797-8ba9-0e3b52113b9e).

A `WithEvents` variable is a variable of a class module whose declared type is a class with events. The symbol of such a variable has the `WithEvents` property (`SymbolProperties.WithEvents`), which the language server sets from the `WithEvents` keyword and sends to the environment host in its descriptor (`SymbolDescriptor.IsWithEvents`). The procedures of the module named `VariableName_EventName` handle that event of the object the variable holds ([**RD-VBAL §5.3.1.8**](rd-vbal.5.3.1.8.event-handler-declarations.md)).

Assigning the variable with `Set` attaches its handlers to the object it is given, and detaches them from the object it held ([**RD-VBAL §5.4.3.9**](rd-vbal.5.4.3.9.set-statement.md)).

The declared type must be a specific class with at least one event, and must not be the class of the module containing the
declaration; [ClassModuleEventSemantics](../api/RDCore.SDK.Semantics.Static.ClassModuleEventSemantics.html) reports one
that is not as [VBC09324](../diagnostics/vbc09324.md). A type that is not resolved is not reported.

### 5.2.3.1.3 Array Dimensions and Bounds

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.3.1.3** Array Dimensions and Bounds](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/7c97ad30-4b76-45d9-9827-7455f98a5503).

The _declaration pass_ binds the _declared type_ of an array symbol from the _array-dim clause_ alone, before any
bound is evaluated ([**RD-VBAL §2.4.1** Intrinsic Types](rd-vbal.2.4.1.intrinsic-types.md)).

An _array declaration_ creates an _array value_ of the appropriate _array type_ in the _scope_ of the declaration
([**RD-VBAL §2.5.2.1.2** Array Values](rd-vbal.2.5.2.1.2.array-values.md)):

|Array-dim clause|Example|Declared type|Array value created|
|---|---|---|---|
|One or more _bounds_ (dimension specifications)|`Dim g(1 To 3, 0 To 4) As Long`|[VBFixedSizeArrayType](../api/RDCore.SDK.Model.Types.VBFixedSizeArrayType.html)|[VBFixedSizeArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBFixedSizeArrayValue.html)|
|An empty `()` clause|`Dim b() As Long`|[VBResizableArrayType](../api/RDCore.SDK.Model.Types.VBResizableArrayType.html)|[VBResizableArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBResizableArrayValue.html)|
|An empty `()` clause, with a `Byte` _item type_|`Dim b() As Byte`|[VBResizableByteArrayType](../api/RDCore.SDK.Model.Types.VBResizableByteArrayType.html)|[VBResizableByteArrayValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBResizableByteArrayValue.html)|

An array has up to 60 dimensions.

An array is declared by the `()` after its name, and only so: a trailing `()` on the `As` clause (`As Long()`, the `array-designator` of
**MS-VBAL §5.3.1.4**) is the type of what a `Function`, a `Property Get` or a `Declare`'s `Function` returns, and is a syntax error anywhere else, as it
is in **MS-VBA** (recorded in Excel): a variable of a module, a local (`Dim`, `Static`), a field of a `Type`, a constant, a parameter (of a procedure, an
event or a `Declare`), or the `As` clause of a `ReDim`. `DeclarationStaticSemanticsEvaluator.CheckSyntax` reports it.

|Declaration|Outcome|
|---|---|
|`Function F() As Long()`, `Property Get P() As Long()`, `Declare Function F Lib "…" () As Long()`|Valid: what is returned is an array of `Long`.|
|`Dim b As Long()`, `Public b As Long()`, `Static s As Long()`, `F As Long()` in a `Type`|[VBC00001](../diagnostics/vbc00001.md) _Syntax error_. Write `Dim b() As Long`.|
|`Sub X(a As Long())`, `Event E(a As Long())`|`VBC00001` _Syntax error_. Write `Sub X(a() As Long)`.|
|`ReDim a(2) As Long()`, `Const K As Long() = 1`|`VBC00001` _Syntax error_.|

Resolving an omitted _lower bound_ against `Option Base` is specified in
[**RD-VBAL §5.2.1** Option Directives](rd-vbal.5.2.1.option-directives.md) (§5.2.1.2).

A bound is a constant expression Let-coerced to `Long`, reduced when the module is compiled, of a variable, a local and a
field of a `Type` alike, as **MS-VBA** reduces it (recorded in Excel):

|Bound|Example|Outcome|
|---|---|---|
|No constant expression|`Dim a(n) As Long`, `X(Count) As Long` in a `Type`|[VBC09350](../diagnostics/vbc09350.md) _Constant expression required_|
|Past the range of a `Long`, written or given by a constant|`Dim a(1E10) As Long`, `Const K = 1E10: Dim a(K) As Byte`|[VBC09352](../diagnostics/vbc09352.md) _Overflow_|
|An upper bound below the lower one|`Dim a(5 To 1) As Long`, `Dim a(-1) As Long`|[VBC09365](../diagnostics/vbc09365.md) _Range has no values_|
|A fraction, or a `String` that reads as a number|`Dim a(2.6) As Long`, `Dim a("3") As Long`|Valid: the bound is rounded half to even, or read, as Let-coercion to `Long` does: `3`.|
|`Len` or `LenB` of a constant expression|`Const K = "abcd": Dim a(Len(K)) As Long`|Valid: `4`, and `8` for `LenB`, of a local and of a module's variable alike. **MS-VBAL §5.6.16.1** lists both among constant expressions; MS-VBA admits them in a bound (recorded in Excel), though not in the value of a `Const`: see below.|

**MS-VBAL §5.6.16.1** lists `Len` and `LenB` among constant expressions, and so a valid value of a `Const`. MS-VBA refuses them there with
[VBC09350](../diagnostics/vbc09350.md) _Constant expression required_ (recorded in Excel), and so does RD-VBA. That is no check MS-VBA skips but one it adds, so
no warning goes with it. One case is left as it is: a `Const` of a procedure valued `Len` of a constant (`Const K3 = Len(K)`) compiles in MS-VBA, and crashes
the host when it runs, and RD-VBA refuses it as every other `Const` of `Len` is refused. `String * Len(K)` is a syntax error in both.

### 5.2.3.1.4 Variable Type Declarations

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.3.1.4** Variable Type Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d27d3dae-ac46-4113-8927-c7f60d844dbf).

A declared type name binds in the _type binding context_. With the
[CompositeSymbolResolver](../api/RDCore.SDK.Model.Symbols.CompositeSymbolResolver.html), a module's `As SomeType`
binds to a sibling module's `Type` or `Enum`, or to a class, not only to a reserved data-type name
([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)). A name that binds an `Enum` type, of the
workspace or of a library, declares a `Long` ([**RD-VBAL §5.2.3.4** Enum Declarations](#5234-enum-declarations)). A
name that binds a procedural module or a project is not a type, and the declaration is the error
[**RD-VBAL §5.6.16.7** Type Expressions](rd-vbal.5.6.16.constrained-expressions.md#56167-type-expressions) lists for it. A type of the project ranks
ahead of a library's procedural module of the same name ([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md#lookup-order)).

The length of a fixed-length `String * n` is "the data value of its `<INTEGER>` element or the data value referenced by
its `<constant-name>` Let-coerced to declared type Long", reduced when the module is compiled, wherever it is written:
a variable, a local, an element of an array, a field of a `Type`, the `As` clause of a `ReDim`. **MS-VBA** refuses
what is no length (recorded in Excel):

|Length|Example|Outcome|
|---|---|---|
|1 to 65526, written or given by a constant or an `Enum` member|`String * 10`, `String * &H10`, `String * Red`|Valid.|
|A constant whose value is a fraction or a numeric `String`|`Const K = 2.5`, `Const K = "3"`|Valid: `2` and `3`, as Let-coercion to `Long` has them.|
|Less than 1, or more than 65526|`String * 0`, `String * 65527`, `Const K = 70000: … String * K`|[VBC09375](../diagnostics/vbc09375.md) _Invalid length for fixed-length string_|
|A number written past 65535, or no integer|`String * 65536`, `String * 1.5`|[VBC00001](../diagnostics/vbc00001.md) _Syntax error_|
|A name that is no constant|`String * n`, a variable|[VBC09350](../diagnostics/vbc09350.md) _Constant expression required_|
|A name nothing declares, under `Option Explicit`|`String * NoSuch`|[VBC09302](../diagnostics/vbc09302.md) _Variable not defined_|

> [!NOTE]
> **Not implemented.** A constant whose value is an expression (`Const K = 1 + 2`) is reduced to report a length that
> is invalid, but the variable `String * K` declares is a variable-length `String`: its type is decided before the
> constants are folded.

**MS-VBAL §5.2.3.1.4**: the type specified in an `As New` clause must be a named class, and must be creatable unless
it is declared in the same project. `DeclarationStaticSemanticsEvaluator` checks every declaration that has an
`As New` clause - a variable, a field, an array (its element type), a field of a `Type` - as **MS-VBA** does
(recorded in Excel):

|`As New` of|Outcome|
|---|---|
|A class of the project, creatable or not (`VB_Creatable = False`)|Valid.|
|A creatable class of a library (`As New Collection`)|Valid.|
|A class of a library that cannot be created (`As New ErrObject`, `As New Workbook`)|Compile error [`VBC09368`](../diagnostics/vbc09368.md) _Invalid use of New keyword_.|
|A user-defined type, or an `Enum` (of the project or of a library)|`VBC09368`.|
|A `WithEvents` variable (`Private WithEvents w As New Widget`, **MS-VBAL §5.2.3.1.2**)|`VBC09368`.|
|An intrinsic type (`As New Long`, `As New String`, `As New Variant`, `As New Object`)|Compile error [`VBC00001`](../diagnostics/vbc00001.md) _Syntax error_: `New` is followed by a type name no class can have.|

> [!IMPORTANT]
> **MS-VBA** compiles `As New` of a library's class that cannot be created, and raises run-time error -2147221164
> _Class not registered_ when the variable is first referred to. **RD-VBA** reports it when the module is compiled, as
> **MS-VBAL §5.2.3.1.4** has it; the verbose message says what MS-VBA does.

An automatic instantiation variable that is _dependent_ on another (**MS-VBAL §2.5.1**) is created on its first
reference too:

|Variable|Example|First reference|
|---|---|---|
|An element of an array declared `As New`|`Dim a(1 To 3) As New Collection`, then `a(2).Add 1`|Creates the object of that element, and only that one.|
|A field of a `Type` declared `As New`|`Dim r As TRecord`, then `r.Items.Add 1`|Creates the object of that field. A copy of the record (`r2 = r`) holds the same object.|
|A field of a class module declared `As New`|`Public Items As New Collection`, then `obj.Items.Count`|Creates the object of that field, of that object.|

Each is never `Nothing` when it is referred to: after `Set a(2) = Nothing`, the next reference to `a(2)` creates another
object.

### 5.2.3.1.5 Implicit Type Determination

This section corresponds to [**MS-VBAL §5.2.3.1.5** Implicit Type Determination](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6df70c22-dc30-45d0-b8a7-9a0fb4f068b3).

> [!NOTE]
> Reserved. This section has no content yet.


## 5.2.3.2 Const Declarations

This section corresponds to [**MS-VBAL §5.2.3.2** Const Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ffa99fa9-28da-4512-85eb-db376cab0711).

> [!NOTE]
> Reserved. This section has no content yet.


## 5.2.3.3 User Defined Type Declarations

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.3.3** User Defined Type Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a5fe374b-eddf-4808-9e73-477914940dba).

**MS-VBAL §5.2.3.3** uses the same accessibility wording as
[**MS-VBAL §5.2.3.4** Enum Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/da1d4885-946f-4937-9487-488143b97f08):
a user-defined type is accessible within the enclosing project, or within the enclosing module.

A public user-defined type declared in a class module reaches the project scope. A class module's other members
need an instance to be reached through, but a declared type needs none ([**RD-VBAL §5.2.3.4** Enum Declarations](#5234-enum-declarations)).

The _data type_ of a UDT value is defined by the UDT declaration of its _declared type_
([**RD-VBAL §2.5.2.1.3** User-Defined Type (UDT) Values](rd-vbal.2.5.2.1.3.udt-values.md)).


## 5.2.3.4 Enum Declarations

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.2.3.4** Enum Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/da1d4885-946f-4937-9487-488143b97f08).

**MS-VBAL §5.2.3.4** states that "the Enum type *and its Enum members* are accessible within the enclosing project"
/ "within the enclosing module".

An `Enum`'s members are lexically scoped like the Enum type itself: accessible within the enclosing project, or
within the enclosing module.

|Aspect|Enum member|
|---|---|
|Access modifier|An Enum member has no access modifier of its own.|
|Visibility|An Enum member takes its visibility from its Enum's own access modifier.|
|Symbol parent|An enum constant (Enum member) symbol parents to its Enum rather than to a scope.|
|Scope placement|[ScopeTreeBuilder](../api/RDCore.SDK.Model.Symbols.ScopeTreeBuilder.html) resolves an Enum member's scope placement through its Enum ([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)).|

👉 Because Enum members are placed through their Enum, an Enum member such as `vbSunday` is a name on its own,
resolvable without qualification.

A public `Enum` declared in a class module reaches the project scope. A class module's other members need an
instance to be reached through, but a declared type (an Enum or a user-defined type) needs none.

**MS-VBAL §5.2.3.4**: "When an enum name (possibly qualified by a project) appears in an `<as-type>` clause of any
declaration, the meaning is the same as if the enum name was replaced with the declared type Long." A variable, a
field, a parameter or a function's result declared as an `Enum` - of the enclosing module, of another module
(`As Mod2.Colour`), of a class module, or of a library - holds a `Long`: 0 until it is assigned, and a value assigned to
it is let-coerced to `Long` (`c = 1.7` stores 2). The type is `VBEnumType`, whose value is a `Long`. An `Enum` symbol
carries its type, with its members, whether a library or the workspace declares it: the environment host reads an
`Enum` of the workspace with its type (`SymbolDescriptorReader`). A member's value is read from its own symbol, not from
the type. A declaration that names an `Enum` of the workspace has the type only once the host has defined the module
that declares the `Enum` (below).

Since an `Enum`'s name means the declared type `Long`, an `Enum` and `Long`, or two `Enum`s, of the workspace or of a
library, are the same declared type where these match two declared types (`VBEnumType.AsDeclaredType`: `Long` for
an `Enum`, any other type itself):

|Where|What is matched|
|---|---|
|An event handler ([**RD-VBAL §5.3.1.8**](rd-vbal.5.3.1.8.event-handler-declarations.md))|Each parameter and the event's.|
|An implemented name declaration ([**RD-VBAL §5.3.1.9**](rd-vbal.5.3.1.9.implemented-name-declarations.md))|Each parameter, the result, or what a property carries, and the interface member's.|
|A `ByRef` argument of a call ([**RD-VBAL §5.3.1.11**](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)) or of a `RaiseEvent` ([**RD-VBAL §5.4.2.20**](rd-vbal.5.4.2.20.raiseevent-statement.md))|The parameter and the variable it is given, which it is bound to rather than given a copy of.|

An `Integer` and a `Long` are not the same declared type, and only the type itself is mapped, not an array's element
type (below). A record stores a field of an `Enum` as the `Long` it holds, four bytes: in memory, which `LSet`
between records copies ([**RD-VBAL §5.4.3.6**](rd-vbal.5.4.3.6.lset-statement.md)), and in a file, which `Put`
writes and `Get` reads ([**RD-VBAL §5.4.5.11**](rd-vbal.5.4.5.11.put-statement.md)).

> [!NOTE]
> **Not implemented.** A member of an `Enum` the workspace declares has no value yet, so reading one stops the run
> ([**RD-VBAL §6.1.1** Predefined Enums](rd-vbal.6.1.1.predefined-enums.md)); a variable of the `Enum` holds what is
> assigned to it.
>
> A member of a variable of an `Enum` (`c.cRed`) is not refused when the module loads, though the variable is a `Long`,
> which has no members: it stops the run, as a member of a `Long` variable does.
>
> The host defines the modules of the workspace one at a time, so a declaration that names an `Enum` of the workspace,
> bare or qualified, in a module the host defines before the module that declares the `Enum` keeps no type: assigning
> to it raises error 13, Type mismatch, and reading it raises error 51, Internal error, where a variable of a
> user-defined type of such a module stops the run.
>
> An array's element type is not mapped: an array of an `Enum` given to a `ByRef` array parameter of `Long`, or an
> array of `Long` given to one of an `Enum`, is not bound to the parameter, and copying it to the parameter raises
> error 13, Type mismatch.


## 5.2.3.5 External Procedure Declaration

This section corresponds to [**MS-VBAL §5.2.3.5** External Procedure Declaration](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/75679e90-7e14-420d-af11-f83ffaf60418).

> [!NOTE]
> Reserved. This section has no content yet.


## 5.2.3.6 Circular Module Dependencies

This section corresponds to [**MS-VBAL §5.2.3.6** Circular Module Dependencies](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4e4256bb-0f8c-4a7e-8355-a0c8e25e37bd).

> [!NOTE]
> Reserved. This section has no content yet.

---
> ⏮️ [**RD-VBAL §5.2.2** Implicit Definition Directives](rd-vbal.5.2.2.implicit-definition-directives.md) | ⏭️ [**RD-VBAL §5.2.4** Class Module Declarations](rd-vbal.5.2.4.class-module-declarations.md)
