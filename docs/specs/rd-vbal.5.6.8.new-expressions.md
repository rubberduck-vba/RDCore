# 5.6.8 New Expressions

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §5.6.8** New Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/b16c311f-3648-45fc-a382-1e6ddea34191).

## Syntax

|Expression|AST node|
|---|---|
|`New <class>`|[NewExpressionNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Expressions.NewExpressionNode.html)|

See [**RD-VBAL §3.0.2** Node Types](rd-vbal.3.0.2.node-types.md).

## Static Semantics

The operand of a `NewExpressionNode` binds under the _type binding context_, through
[ISymbolResolver](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Abstract.Execution.ISymbolResolver.html)`.ResolveType`. See
[**RD-VBAL §3.0.3** Binding Contexts](rd-vbal.3.0.3.binding-contexts.md) and
[**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md).

The positional qualified-type-name rule holds wherever a type name appears: an `As` clause, an `As New` clause, and
the operand of `New`. In `New A.B`, the qualifier `A` is bound as a namespace (`ISymbolResolver.ResolveQualifier`),
and the last part `B` in the type binding context. See
[**RD-VBAL §3.0.3** Binding Contexts](rd-vbal.3.0.3.binding-contexts.md#qualified-type-names).

Whether a class named by `New` is _creatable_ is not a name-lookup concern. It is checked once the name is bound.

A class whose `VB_Creatable` is `False` (the default instancing mode, _Private_, and _Public Not Creatable_) can only be
created by the modules of the project that defines it (**MS-VBAL §5.2.4.1.1**): `New` of one is a type mismatch from any
other project, and valid within its own.

A class of a library is creatable when the library's type library says so. **MS-VBA** compiles `New` of one, and of no
other (recorded in Excel):

|`New` of|Example|Outcome|
|---|---|---|
|An interface of a library|`New Range`, `New Office.CommandBar`|Compile error [`VBC09368`](../diagnostics/vbc09368.md) _Invalid use of New keyword_.|
|A class its library does not say can be created|`New Office.CommandBars`, `New VBA.Match`|`VBC09368`.|
|A class its library says can be created|`New Collection`, `New RegExp`|Valid: the object is created when the expression is evaluated.|
|A class its library says can be created, and whose objects are never created|`New ErrObject`|Valid, and Warning [`RDC11028`](../diagnostics/rdc11028.md). Evaluating it raises run-time error -2147221164 _Class not registered_.|

## Runtime Semantics

Evaluating a `New` expression creates an object of the class and initializes it (**MS-VBAL §5.3.1.10**). Creating an
object of `ErrObject` fails: the VBA library says the class can be created, and nothing creates one. Evaluating
`New ErrObject` raises run-time error -2147221164 _Class not registered_, as **MS-VBA** does, and a variable that was
`Nothing` before `Set e = New ErrObject` is `Nothing` after it. A variable declared `As New ErrObject` raises the same error each time a
reference to it would create its object; `Set e = Nothing` creates nothing, and raises nothing.

What creating an object of another library's class gives when the program runs is that library's to say: an object, or the
error its server reports.
