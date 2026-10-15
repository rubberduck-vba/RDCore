# 2.3.1.3 Name Resolution

Name resolution binds an _identifier name_, as seen from a scope, to the [Symbol](../api/RDCore.SDK.Model.Symbols.Abstract.Symbol.html) it refers to. The static
semantics ([**RD-VBAL §5.0** Semantics](rd-vbal.5.0.semantics.md)) and the simple name expression
([**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md)) resolve names as this
section specifies.

The name-resolution algorithm is a separate concern from the workspace's ordered reference list,
`IRuntimeSession.References` ([**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md)).

## ISymbolResolver

The static and runtime semantic layers read symbols through
[ISymbolResolver](../api/RDCore.SDK.Runtime.Abstract.Execution.ISymbolResolver.html):

|Member|Description|
|---|---|
|`ResolveValue`|Resolves a specified _identifier name_ in the _default binding context_, as seen from the scope that the symbol at a specified _handle_ `Uri` belongs to, to a [SymbolResolutionResult](../api/RDCore.SDK.Runtime.Shared.SymbolResolutionResult.html).|
|`ResolveType`|Resolves a specified _identifier name_ in the _type binding context_, as seen from the scope that the symbol at a specified _handle_ `Uri` belongs to, to a `SymbolResolutionResult`.|
|`ResolveQualifier`|Resolves a specified _identifier name_ as the _qualifier_ of a qualified type name (the `A` in `A.B`). It binds the project, or a procedural or class module, and never a user-defined type or an Enum type.|
|`GetValue`|Gets the [IBindingHandle](../api/RDCore.SDK.Model.Values.Bindings.IBindingHandle.html) currently bound to a specified `Symbol`.|
|`TryRead`|Gets the `IBindingHandle` held at a specified [MemoryAddress](../api/RDCore.SDK.Runtime.Shared.MemoryAddress.html), if any.|
|`TryGetAddress`|Resolves the address that a `ByRef` parameter binding aliases ([**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)).|
|`TryAllocate`|Allocates a `Static` local's own module-extent storage, in the session's module-level heap ([**RD-VBAL §5.4.3.1** Local Variable Declarations](rd-vbal.5.4.3.1.local-variable-declarations.md)).|

The _handle_ `Uri` of a symbol is a semantic ID that uniquely identifies the symbol across an entire workspace
([**RD-VBAL §2.5.1** Runtime Entities](rd-vbal.2.5.1.runtime-entities.md)).

`TryGetAddress` and `TryAllocate` apply the read-only SDK-interface / Runtime-implementation split to name
resolution ([**RD-VBAL §3.5.5** Placement and Licensing](rd-vbal.3.5.5.placement-and-licensing.md)).
`TryAllocate` mutates state: it allocates a `Static` local's storage, unlike every other SDK-interface member
listed in §3.5.5.

|Resolver|Kind|`TryGetAddress` / `TryAllocate`|
|---|---|---|
|`CallStackAwareSymbolResolver` (RDCore.Runtime)|Runtime|Resolves the address; allocates the storage.|
|`RuntimeSymbolResolver` (RDCore.Runtime)|Runtime|Resolves the address; allocates the storage.|
|[CompositeSymbolResolver](../api/RDCore.SDK.Model.Symbols.CompositeSymbolResolver.html)|Compile-time only|Returns `false` for both.|
|[ScopeTreeSymbolResolver](../api/RDCore.SDK.Model.Symbols.ScopeTreeSymbolResolver.html)|Compile-time only|Returns `false` for both.|
|`IntrinsicSymbolResolver`|Compile-time only|Returns `false` for both.|

`CallStackAwareSymbolResolver` and `RuntimeSymbolResolver` are the only two resolvers that resolve an address for
`TryGetAddress` and allocate storage for `TryAllocate`. A compile-time-only resolver returns `false` for both, as it
does for `TryRead`.

## Binding contexts

Which `ISymbolResolver` lookup a name is resolved through is decided by the node being evaluated, never by a
parameter ([**RD-VBAL §3.0.3** Binding Contexts](rd-vbal.3.0.3.binding-contexts.md);
[**MS-VBAL §5.6.4** Expression Binding Contexts](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/b892f8d6-cd0e-419e-8a02-bc932b8eff5c)):

|Node|Binding context|Resolver call|
|---|---|---|
|A simple name expression ([SimpleNameExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.SimpleNameExpressionNode.html))|Default|`ResolveValue`|
|A type name: an `As` clause|Type|`ResolveType`|
|A type name: the operand of `New` ([NewExpressionNode](../api/RDCore.SDK.Model.AST.Expressions.NewExpressionNode.html))|Type|`ResolveType`|
|The part of a qualified type name that precedes a dot|Namespace (qualifier)|`ResolveQualifier`, instead of `ResolveType`|

`ISessionSymbols` mirrors the `ResolveValue` / `ResolveType` pair as `TryResolveValue` and `TryResolveType`
([**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md)).

### Candidates

`ResolveValue` and `ResolveType` bind different candidates
([**MS-VBAL §5.6.10** Simple Name Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e3af3398-f090-40db-ade6-de3a93589c76)).
`ResolveQualifier` is `ResolveType` without the user-defined types and Enum types: only the enclosing project, or a
procedural or class module, is a candidate.

|Candidate|`ResolveValue`|`ResolveType`|`ResolveQualifier`|
|---|---|---|---|
|Variable (including a local or a parameter)|Yes|No|No|
|Constant|Yes|No|No|
|Enum type|Yes|Yes|No|
|Enum member|Yes|No|No|
|Property, function, subroutine|Yes|No|No|
|User-defined type|No|Yes|No|
|Class module|No; only through its predeclared instance|Yes|Yes|
|Procedural module|Yes|Yes|Yes|
|Project|Yes|Yes|Yes|

`ResolveType` starts its lookup from the enclosing module, in the order given under [Lookup order](#lookup-order).
Because of this, a local, parameter or constant can neither be bound by `ResolveType` nor hide the type it shadows.
A procedural module and a project are candidates of `ResolveType`, because **MS-VBAL §5.6.10** lists them in the
type binding context, but neither is a type: a declared type that binds a procedural module is
[VBC09311](../diagnostics/vbc09311.md) _User-defined type not defined_
([**RD-VBAL §5.6.16.7** Type Expressions](rd-vbal.5.6.16.constrained-expressions.md#56167-type-expressions)).

### Qualified type names

In a qualified type name `A.B`, the lookup is positional
([**RD-VBAL §3.0.3** Binding Contexts](rd-vbal.3.0.3.binding-contexts.md)):

|Part|Bound as|Resolver call|
|---|---|---|
|`B`, the last part|Like a bare name, in the type binding context.|`ResolveType`|
|`A`, the qualifier|A namespace: the project, or a procedural or class module.|`ResolveQualifier`|

Neither a user-defined type nor an Enum type is a candidate for the qualifier, because neither can contain a type.

When the qualifier is a procedural or class module, `B` is that module's own user-defined type or Enum type, `Public`
unless the name is written in the module itself
([**MS-VBAL §5.6.12** Member Access Expressions](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/af3a4059-3059-4e79-8aa5-685324fb266a)),
whatever else the project or a library calls `B`. With a procedural module `Helpers` and `Public Enum Helpers` in
`Mod2`, `Mod2.Helpers` is the Enum, where a bare `Helpers` is the module; with `Public Type TRec` in both `Mod2` and
`Mod3`, `Mod2.TRec` is `Mod2`'s, where a bare `TRec` does not resolve; and with `Public Type Collection` in `Mod2`,
`Mod2.Collection` is the type, where a bare `Collection` is the `VBA` library's class (see the note under
[Lookup order](#lookup-order)). The language server sends a user-defined type or an Enum type that another module
declares to the environment host with that module's name (`Mod2.TRec`), so that the host, which resolves the name
again, binds the type the language server bound. A type of the module being defined is sent by its bare name, which
order 1 of the type binding context binds first.

### Class names and predeclared instances

In the default binding context (`ResolveValue`), a class is a value only through its _predeclared instance_
([**RD-VBAL §3.1.1.6** VB_PredeclaredId](rd-vbal.3.1.1.attributes.md);
[**MS-VBAL §5.2.4.1.2** Default Instance Variables Static Semantics](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/189fb41b-cc3a-4999-a6d2-ba89f72d2870)):

|Name|Binding context|Binds to|
|---|---|---|
|The name of a predeclared class|Default (`ResolveValue`)|The class module's predeclared instance variable.|
|The name of a class that is not predeclared, used in an expression|Default (`ResolveValue`)|Nothing: it is an undefined variable.|
|A class name|Type (`ResolveType`: an `As` clause or `New`)|The class.|

Other than through its predeclared instance, a class module is never a name in the default binding context. A local
variable or field that is itself named `Widget` hides the default instance of class `Widget`, and is an ordinary
`Set` target.

## Resolution results

`ResolveValue` and `ResolveType` each return a `SymbolResolutionResult`:

|Result|Meaning|
|---|---|
|The bound `Symbol`|The name binds to that symbol.|
|Unbound|The name is declared nowhere visible.|
|**VBC09303** _Duplicate declaration_|A compile-time error: the name is declared more than once within one module or procedure. The colliding declarations are attached.|
|**VBC09301** _Ambiguous name_|A compile-time error: within one tier of the project or global scope, the name resolves to more than one symbol of the highest rank (members of two modules of the workspace - of a class module, only a public Enum or user-defined type and that Enum's members - or of two modules of one library; see [Reference priority](#reference-priority)), and the reference must qualify the name. The colliding declarations are attached.|

The symbol resolver reports the error _kind_. The caller, which knows where the reference is, builds the located
diagnostic ([**RD-VBAL §2.6.2** Semantic Compilation Errors](rd-vbal.2.6.2.semantic-compilation-errors.md)).

## Lookup order

The correctly-scoped allocation of all symbols upon their definition should suffice to make symbol resolution
follow the **MS-VBAL** order in which an _identifier name_ is resolved (**MS-VBAL §5.6.10**), provided that lookups
are done in the specified order.

Identifier name lookups are done in this order (the heaps are described in
[**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md)):

|Order|A name that refers to a symbol defined in|Resolves to a symbol that is|
|---|---|---|
|1|The local _stack frame_|Locally scoped.|
|2|The _static locals heap_|Locally scoped, but preserves its value between calls ([**RD-VBAL §5.4.3.1** Local Variable Declarations](rd-vbal.5.4.3.1.local-variable-declarations.md)).|
|3|The _workspace heap_|Workspace-scoped.|
|4|The _global heap_|Globally-scoped.|

In the default binding context, a name that a member of one of the project's procedural modules and a procedural
module of a referenced library both have - a `Public Strings` variable of another module, and the library's `Strings`
module - is the project's member. **MS-VBAL §5.6.10** ranks the members of the enclosing project's procedural modules
before a referenced project's procedural modules, and those before the members of a referenced project's modules (the
`VBA` library's `Constants` module before Excel's Enum `Constants`). The library's module is what the name means when
no procedural module of the project has an accessible member of that name (`Strings.LenB("ab")`). A class module's
predeclared instance of that name does not count: it ranks after the library's module, so the name is still the
module.

In the type binding context, `ResolveType` looks a name up in this order (**MS-VBAL §5.6.10**):

|Order|A name that refers to|
|---|---|
|1|A user-defined type or Enum type declared at the module level of the enclosing module.|
|2|The enclosing project, a referenced project, a procedural or class module of the enclosing project, or a class module of a referenced library.|
|3|An accessible user-defined type or Enum type of another module of the enclosing project.|
|4|A procedural module of a referenced library.|
|5|An accessible user-defined type or Enum type of a module of a referenced library; of two libraries, the one referenced later.|

A type of the project named like a library's procedural module - another module's `Public Type Strings` - is
therefore the type, and the library's module is what the name means only when the project has no type of that name.
A library's procedural module ranks before a library's types: with Excel referenced, `As Constants` names the `VBA`
library's `Constants` module, which is not a type, and Excel's Enum is `Excel.Constants`
([**RD-VBAL §6.1.2.2** Constants Module](rd-vbal.6.1.2.2.constants-module.md)).

> [!NOTE]
> **Not implemented.** A class module of a referenced library ranks with the enclosing project's modules (order 2),
> ahead of a type of another module of the project, where **MS-VBAL §5.6.10** ranks it with a referenced library's
> procedural modules (order 4). With another module's `Public Type Collection`, `As Collection` is still the `VBA`
> library's `Collection` class.

## Scope tree

The mechanism behind the ordered lookup is a [ScopeTree](../api/RDCore.SDK.Model.Symbols.ScopeTree.html). A
[ScopeTreeBuilder](../api/RDCore.SDK.Model.Symbols.ScopeTreeBuilder.html) folds the composed symbols into a tree of
[LexicalScope](../api/RDCore.SDK.Model.Symbols.LexicalScope.html)s, of the kinds in
[LexicalScopeKind](../api/RDCore.SDK.Model.Symbols.LexicalScopeKind.html):

|`LexicalScopeKind`|Scopes|Position in the tree|
|---|---|---|
|`Global`|The global scope.|The root.|
|`Project`|The project scope.|Beneath the global scope.|
|`Module`|One scope per module.|Beneath the project scope.|
|`Procedure`|One scope per procedure body.|Beneath its module's scope.|

The project scope is an ancestor of a module's own scope and of nothing else.

### Placement

Each symbol is placed structurally in the `ScopeTree`, from its `ParentUri`, its concrete type, and its access
modifier.

A standard module's non-`Private` members (an explicit `Public` / `Global` / `Friend`, or an implicit
procedure-like member,
[**MS-VBAL §5.2.3** Module Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/b40b8d00-3348-43c1-9cfb-c0eadef565ee))
are also declared in the project scope. Because of this, a sibling module resolves them without qualification. A
bare `Err`, for example, yields the error object in either shape
([**RD-VBAL §6.1.3.2** Err Class](rd-vbal.6.1.3.2.err-class.md)).

An enum constant (Enum member) symbol parents to its Enum rather than to a scope, and `ScopeTreeBuilder` resolves
an Enum member's scope placement through its Enum
([**RD-VBAL §5.2.3** Module Declarations](rd-vbal.5.2.3.module-declarations.md)):

- An `Enum`'s members are lexically scoped like the Enum type itself: accessible within the enclosing project, or
  within the enclosing module.
- An Enum member takes its visibility from its Enum's own access modifier.
- An Enum member such as `vbSunday` is therefore a name on its own, resolvable without qualification.

A public `Enum`, and a public user-defined type, declared in a class module reach the project scope.

### Procedure locals

A procedure's parameters and its own `Dim` / `Static` / `Const` locals are carried on the member symbol, not
registered as separate entries. This is a design principle of the scope tree:

- `VBProcedureMemberSymbol.Locals` and `VBReturningMemberSymbol.Locals` list every `Dim`, `Static` and `Const`
  declared in the procedure body ([**RD-VBAL §5.4.3.1** Local Variable Declarations](rd-vbal.5.4.3.1.local-variable-declarations.md)).
- `ScopeTreeBuilder` (**RDCore.SDK**) extracts a procedure symbol's `Locals` for name resolution, the same way it
  extracts its `Parameters`.
- A local therefore never needs a second, flat registration of its own to resolve by name.

### Walking the tree

Resolving a name from a scope walks `SelfAndAncestors()` outward. The first scope that declares the name binds it.

A name declared more than once in a single scope is one of the error cases of
[Duplicate and ambiguous names](#duplicate-and-ambiguous-names), below. The exception is a property's `Get` / `Let` /
`Set` accessors: they share one name by design and resolve as a group, raising VBC09320 or VBC09321 only when they
do not form a valid property (see [ScopeTreeSymbolResolver](#scopetreesymbolresolver)).

## Module directives

A module's `LexicalScope` carries its [ModuleDirectives](../api/RDCore.SDK.Model.Symbols.ModuleDirectives.html).
`ModuleDirectives` holds the module-level facts a static semantics rule needs:

|Member|Description|
|---|---|
|`Explicit`|Whether the module declares `Option Explicit` ([**RD-VBAL §5.2.1** Option Directives](rd-vbal.5.2.1.option-directives.md)).|
|`Compare`|The module's `Option Compare` mode.|
|`Strict`|Reserved for RD-VBA's `'@OptionStrict` annotation. No symbol provider sets it, so it is always `false`.|

`ModuleDirectives` is reachable from any scope nested under the module, via
`LexicalScope.EnclosingModuleDirectives()`.

The static-semantics layer consumes `ModuleDirectives` to decide what an _unresolved_ simple name is:

|Module declares `Option Explicit`|An unresolved simple name is|
|---|---|
|No|A deferred [VBUnknownType](../api/RDCore.SDK.Model.Types.VBUnknownType.html) ([**RD-VBAL §2.4.4** Deferred Types](rd-vbal.2.4.4.deferred-types.md)).|
|Yes|A **VBC09302** _Variable not defined_ compile-time error.|

Without a qualifier, a deferred symbol is deemed to be an undeclared local variable, as per MS-VBAL scoping rules.
If a global-scope deferred symbol with the same identifier name exists, such an unqualified deferred symbol should
resolve to the global-scope deferred symbol ([**RD-VBAL §2.4.4** Deferred Types](rd-vbal.2.4.4.deferred-types.md)).

How a `SimpleNameExpression`'s declared type is determined from a `ResolveValue` outcome is specified in
[**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md).

## Duplicate and ambiguous names

|Condition|Compile-time error|
|---|---|
|Multiple symbols match a specified name within one _module_ or _procedure_ scope.|**VBC09303** _Duplicate declaration_|
|Multiple symbols of one tier of the _project_ or _global_ scope match a specified name and share the highest rank ([Reference priority](#reference-priority)): members of two modules of the workspace (of a class module, only a public Enum or user-defined type and that Enum's members), or of two modules of one library, the `VBA` library included. Every loaded library has a rank of its own, so the symbols of two libraries never share it.|**VBC09301** _Ambiguous name_: the reference must qualify the name.|

> [!NOTE]
> **Not implemented.** A declared type whose name ties this way is not reported as **VBC09301**: the declaration is
> [VBC09311](../diagnostics/vbc09311.md) _User-defined type not defined_ ("The declared type 'TRec' could not be
> resolved"), as for a name nothing declares. So it is with `Public Type TRec` in both `Mod2` and `Mod3`, or in a class
> module and a procedural module, and with `Public Enum Colours` in a class module and a procedural module, for
> `Dim t As TRec` or `Dim c As Colours`. Qualified by its module, the type resolves
> ([Qualified type names](#qualified-type-names)).

An appropriate compile-time error ([VBCompileErrorId](../api/RDCore.SDK.Model.Errors.VBCompileErrorId.html)) should
be issued for a duplicate declaration and for an ambiguous name
([**RD-VBAL §2.6.2** Semantic Compilation Errors](rd-vbal.2.6.2.semantic-compilation-errors.md)).

[SimpleNameExpressionStaticSemantics](../api/RDCore.SDK.Semantics.Static.Expressions.SimpleNameExpressionStaticSemantics.html)
([**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md)) consumes `ResolveValue`'s
error outcomes, and reports an ambiguous or duplicate name as a coded compile-time error: the simple name expression
yields an `Error` carrying `AmbiguousName` or `DuplicateDeclaration`.

## Reference priority

When multiple symbols match a specified name within the _global_ scope, the name is disambiguated using the
_reference priority_ order of the _referenced library_ each matching symbol is defined in. Name resolution across
referenced projects and libraries shall consult the `IRuntimeSession.References` ordering to disambiguate a
global-scope name ([**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md)). A referenced library's own
members are contributed by an `ISymbolProvider` and resolved through `ISymbolResolver`, not from that list.

Reference priority is determined by the order in which project references appear in the `.rdproj` file of a
_workspace folder_ ([**RD-VBAL §2.2.3** ProjectFile](rd-vbal.2.2.3.projectfile.md)).

The **VBA** standard library always has the _lowest_ reference priority: it always appears first in the reference
order. Any other project reference that defines an identically-named class type or public/global member always
_shadows_ the `VBA` library definition.

Shadowing of `VBA` library definitions should be detected in the _semantic layer_ and reported through _semantic
flags_ ([**RD-VBAL §1.1.3** Core Semantic Flags](rd-vbal.1.1.3.core-semantic-flags.md)), so that
**RDCore.Diagnostics** can issue _shadowed declaration_ diagnostics
([**RD-VBAL §2.6** Diagnostics](rd-vbal.2.6.diagnostics.md)).

`ScopeTreeSymbolResolver` consults the priority within one tier of the lookup order above: the tier comes first, and
the priority decides only among the candidates of one tier. The symbols of a referenced library carry its rank
(`SymbolProperties.LibraryPriority`, from `ReferencedLibraries.Priorities`), and when a name matches symbols of more
than one library in one tier of the project or global scope, the name means the symbol of the library that ranks
highest: a library the project references later ranks above one it references before, a library loaded only because
another depends on it ranks below every library the project references, and the `VBA` library, whose symbols carry no
rank, ranks lowest of all. Within a tier, a symbol of the workspace ranks above every library's; across tiers it need
not, since a class module of a referenced library ranks ahead of a type of another module of the project (orders 2
and 3 of the type binding context). Only symbols of one tier that share the highest rank, such as the members of two
procedural modules of the workspace that both declare the name, are reported as **VBC09301** _Ambiguous name_ (see
[Duplicate and ambiguous names](#duplicate-and-ambiguous-names)).

## Resolver implementations

### ScopeTreeSymbolResolver

The compile-time implementation of `ISymbolResolver` is `ScopeTreeSymbolResolver`. It walks the `ScopeTree` and
binds names only.

`ScopeTreeSymbolResolver.GetValue` throws, and `ScopeTreeSymbolResolver.TryRead` returns `false`: a
`ScopeTreeSymbolResolver` holds no run-time bindings.

The session exposes a `ScopeTreeSymbolResolver` over its own symbols as `ISessionSymbols.Resolver`.
`ISessionSymbols.Resolver` is rebuilt as symbols are defined.

`ScopeTreeSymbolResolver` raises two property-accessor diagnostics:

- [VBC09320](../diagnostics/vbc09320.md) _Inconsistent property accessors_, where the property accessors are
  resolved as a group;
- [VBC09321](../diagnostics/vbc09321.md) _Argument required for Property Let or Property Set_.

### Design-time resolver composition

A design-time host composes its own symbol resolver the same way, in two passes:

1. The first pass extracts every parsed module's declarations with an intrinsic-only resolver. This is enough to
   know which types, classes and enums the workspace declares.
2. The second pass extracts every parsed module's declarations again, through a resolver over the first pass's
   declarations. After the second pass, every declared type name (a field's, a local's, a parameter's, a
   function's return type) binds, in the type binding context, to the workspace type it names.

A `CompositeSymbolResolver` then layers a `ScopeTreeSymbolResolver` over the second pass's symbols in front of the
intrinsic resolver. With the `CompositeSymbolResolver`, a module's `As SomeType` binds to a sibling module's `Type` or `Enum`, or to a
class, not only to a reserved data-type name.

A type is a reference to its declaration. A member access reads the members of a class or user-defined type from
the type's declaration, by the type's own identity
([**RD-VBAL §5.6.12** Member Access Expressions](rd-vbal.5.6.12.member-access-expressions.md)). A class whose member
is typed as the class itself therefore resolves through any number of member-access hops.

### CallStackAwareSymbolResolver

`CallStackAwareSymbolResolver` (**RDCore.Runtime**) falls through to session-level storage for any `Local`-scoped
symbol that the current frame does not itself declare
([**RD-VBAL §5.4.3.1** Local Variable Declarations](rd-vbal.5.4.3.1.local-variable-declarations.md)).

---
> ⏮️ [**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md) | ⏭️ [**RD-VBAL §2.3.2** Mode / State](rd-vbal.2.3.2.mode-state.md)
