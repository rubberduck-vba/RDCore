# 3.0.3 Binding Contexts

[**MS-VBAL §5.6.4** Expression Binding Contexts](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/b892f8d6-cd0e-419e-8a02-bc932b8eff5c)
breaks down _expression binding contexts_, used for resolving _name lookups_, as follows:

|Binding context|Used by|
|---|---|
|_Default binding context_|Most expressions.|
|_Type binding context_|Expressions that expect to reference a _type_ or _class name_.|
|_Procedure pointer binding context_|Expressions that expect to return a _pointer to a procedure_.|
|_Conditional compilation binding context_|Expressions within _conditional compilation_ statements.|

See also [**RD-VBAL §5.6.4** Expression Binding Contexts](rd-vbal.5.6.4.expression-binding-contexts.md).

## RDCore Model

In **RDCore**, _name lookups_ are an _explicit evaluation step_ involving specific AST nodes, such as
[SimpleNameExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.SimpleNameExpressionNode.html)
([**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md)).

The binding context is chosen by the node being evaluated, never by a parameter:

|Node|Binding context|Lookup|
|---|---|---|
|A `SimpleNameExpressionNode`|Default binding context|`ISymbolResolver.ResolveValue`|
|An `As` clause|Type binding context|`ISymbolResolver.ResolveType`|
|The operand of a [NewExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.NewExpressionNode.html)|Type binding context|`ISymbolResolver.ResolveType`|
|The _qualifier_ of a qualified type name|Bound as a namespace, not under the type binding context|`ISymbolResolver.ResolveQualifier`|

The [ISymbolResolver](../api/RDCore.SDK.Runtime.Abstract.Execution.ISymbolResolver.html) members `ResolveValue`,
`ResolveType` and `ResolveQualifier` are described in
[**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md).

Evaluation returns an _evaluation result record_,
[RuntimeSemanticsEvaluationResult](../api/RDCore.SDK.Runtime.Shared.RuntimeSemanticsEvaluationResult.html). The
record describes and encapsulates either the evaluation result or runtime error metadata
([**RD-VBAL §5.0** Semantics](rd-vbal.5.0.semantics.md)).

The **RD-VBA** type system includes and uses _meta-types_ such as
[VBTypeDescValue](../api/RDCore.SDK.Model.Values.Meta.VBTypeDescValue.html)
([**RD-VBAL §2.4.3** Meta and Advanced Types](rd-vbal.2.4.3.meta-and-advanced-types.md)). Because of this, the
binding context can be inferred from the managed type of a provided value.

> [!WARNING]
> A `VBTypeDescValue` is a _data value_ that represents a _data type_. The implementation of both static and
> runtime semantics must be mindful of the possibility of accidentally pattern-matching such a _type descriptor_.

## Qualified Type Names

In a qualified type name `A.B`, the lookup is _positional_:

|Part|Bound as|Lookup|
|---|---|---|
|`B`, the last part|Like a bare name, in the _type binding context_.|`ISymbolResolver.ResolveType`|
|`A`, the _qualifier_|A _namespace_: the project, or a procedural or class module.|`ISymbolResolver.ResolveQualifier`|

Neither a _user-defined type_ nor an _Enum type_ is a candidate for the qualifier, because neither can contain a
type. `ResolveType` binds only a user-defined type, an Enum type, a class or procedural module, or the project, in
that order of precedence; `ResolveQualifier` is `ResolveType` without the user-defined types and Enum types
([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)).

The positional rule holds wherever a type name appears:

- an `As` clause;
- an `As New` clause;
- the operand of `New` ([**RD-VBAL §5.6.8** New Expressions](rd-vbal.5.6.8.new-expressions.md)).

### Types Named Like a Namespace

The positional rule matters when a module declares a `Type` named like the project, or like another module. Given a
module that declares `Type MyProject` in a project named `MyProject`:

|Type name|Names|
|---|---|
|`MyProject`|The `Type` `MyProject`: the first tier of the _type binding context_ ([**MS-VBAL §5.6.10** Simple Name Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e3af3398-f090-40db-ade6-de3a93589c76)).|
|`MyProject.ClassName`|The class `ClassName` in the project.|

Read literally,
[**MS-VBAL §5.6.12** Member Access Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/af3a4059-3059-4e79-8aa5-685324fb266a)
does not say which binding context the left-hand side of a member access under the _type binding context_ is bound
in. Applied literally, the first-match rule of **MS-VBAL §5.6.10** would select the `Type` `MyProject`, which no
member access could then qualify. The positional rule matches the observed behaviour of the VBA and VB6 compilers:

|Source|VBA compiler (VBE)|VB6 compiler|
|---|---|---|
|`New MyProject.Class`, next to a `Type MyProject`|Compiles; names the class.|Names the class.|
|`Dim c As MyProject.Class`|Not verified.|Names the class.|
|`Dim u As MyProject`|Not verified.|Finds the `Type` `MyProject`.|

Only the `New MyProject.Class` form is verified against the VBA compiler. The `As New` form follows from the positional
rule, and is not verified against a compiler.

Whether a class named by `New` is _creatable_ is not a name-lookup concern. See
[**RD-VBAL §5.6.8** New Expressions](rd-vbal.5.6.8.new-expressions.md).

---
> ⏮️ [**RD-VBAL §3.0.2** Node Types](rd-vbal.3.0.2.node-types.md) | ⏭️ [**RD-VBAL §3.1** Attributes and Directives](rd-vbal.3.1.attributes-directives.md)
