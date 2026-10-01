# 5.3.1.9 Implemented Name Declarations

This section corresponds to [**MS-VBAL §5.3.1.9** Implemented Name Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9e68b3a3-7c21-47ba-8621-2d03aebd83cf).

## Static Semantics

A procedure of a class module is an implemented name declaration when its name is `InterfaceName_MemberName`: the name of an interface class
the module implements ([**RD-VBAL §5.2.4.2**](rd-vbal.5.2.4.class-module-declarations.md)), an underscore, and the name of a public variable or
method of that class, the _interface member_. It is the same naming that handles an event ([**RD-VBAL §5.3.1.8**](rd-vbal.5.3.1.8.event-handler-declarations.md))
and a lifecycle event ([**RD-VBAL §5.3.1.10**](rd-vbal.5.3.1.10.lifecycle-handler-declarations.md)).
[VBClassModuleSymbol](../api/RDCore.SDK.Model.Symbols.VBClassModuleSymbol.html)`.FindImplementation` finds it, of the kind the member calls for.

[ImplementsSemantics](../api/RDCore.SDK.Semantics.Static.ImplementsSemantics.html) reports an implemented name declaration that does not
correspond to its member as [VBC09330](../diagnostics/vbc09330.md):

|Interface member|The implemented name declaration|
|---|---|
|A method|The same kind of declaration: a subroutine, a function, a `Property Get`, a `Property Let` or a `Property Set`.|
|A variable|A property declaration, and what it carries is of the variable's declared type: what a `Get` returns, the value parameter of a `Let` or a `Set`.|
|A method|A parameter list equivalent to the member's: the same parameters, `Optional` parameters and `ParamArray`, each of the same declared type, constant default and parameter mechanism. The parameters may differ in name, and in whether the mechanism is written out.|
|A function, or a `Property Get`|The same declared type.|

A procedure that begins with an interface's prefix but is not named for one of its members is not an implemented name declaration, and is not
checked. Only a default of a parameter that is a literal is compared.

Which of them a module needs is [VBC09329](../diagnostics/vbc09329.md): see [**RD-VBAL §5.2.4.2**](rd-vbal.5.2.4.class-module-declarations.md).

The `Class` interface that every class module implements is the one whose members need no implemented name declaration: each has an
implementation of its own (`SymbolProperties.DefaultImplementation`), and `VBClassModuleSymbol.IsImplemented` says a member is implemented when
there is a declaration or a default.

## Runtime Semantics

When the target object of an invocation has a declared type that is an interface class of the actual class of the object, and the member
named is a member of that interface class, the method invoked is the implemented name declaration of the object's class, not a member of the same name
the class declares for itself. The same object is therefore dispatched by how the variable it is held in is declared:

```vb
Set d = New Disc    ' d As Disc
d.Draw              ' Disc's own Draw
Set s = d           ' s As IShape
s.Draw              ' Disc's IShape_Draw
```

A public variable of the interface is read through the `Property Get` that implements it.

The declared type of the expression the object is reached through is what the runtime asks, of the rules that type an expression at compile time
([ExpressionStaticSemanticsEvaluator](../api/RDCore.SDK.Semantics.Static.ExpressionStaticSemanticsEvaluator.html)): a value carries the object and
nothing of how it was declared. It is asked only of an object whose class implements an interface, and of an expression it can type. A member
reached any other way, through a `With` block for one, is found among the class's own members.

> [!NOTE]
Assigning to a public variable of the interface goes to the `Property Let` (or `Property Set`) that implements it, indexed or not.

> **Not implemented.** A member reached through a `With` block whose target is declared as an interface.

---
> ⏮️ [**RD-VBAL §5.3.1.8** Event Handler Declarations](rd-vbal.5.3.1.8.event-handler-declarations.md) | ⏭️ [**RD-VBAL §5.3.1.10** Lifecycle Handler Declarations](rd-vbal.5.3.1.10.lifecycle-handler-declarations.md)
