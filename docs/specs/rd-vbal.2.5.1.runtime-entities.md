# 2.5.1 Runtime Entities

A _runtime entity_ is a simple abstraction that associates a [VBType](../api/RDCore.SDK.Model.Types.Abstract.VBType.html) with a [Symbol](../api/RDCore.SDK.Model.Symbols.Abstract.Symbol.html).

Because a runtime entity associates a `Symbol`, every RD-VBA runtime entity is addressable with a `Uri` that is unique across the _workspace_.

## 2.5.1.1 Symbols

A _symbol_ necessarily has a `Uri` and a `Name`. A symbol also has a [ScopeKind](../api/RDCore.SDK.Model.Symbols.Abstract.ScopeKind.html) and an (extended) LSP symbol kind, [SymbolKindExt](../api/RDCore.SDK.Model.Source.SymbolKindExt.html).

|Member|Meaning|
|---|---|
|`Uri`|A semantic ID that uniquely identifies the symbol across the workspace.|
|`Name`|The symbol's name.|
|`ScopeKind`|How, and whether, the symbol is allocated in memory.|
|`SymbolKindExt`|The (extended) LSP symbol kind of the symbol.|

### Uri

The `Uri` of a symbol is a _semantic ID_ that uniquely identifies the symbol across an entire workspace. Symbol `Uri` namespaces are defined in the SDK; see [RDCoreUriNamespaces](../api/RDCore.SDK.Extensibility.RDCoreUriNamespaces.html).

A symbol `Uri` is assembled from the _workspace root_ `Uri` and a relative `Uri`:

- The relative `Uri` may include a _fragment_. The content of the fragment is implementation-defined.
- The relative `Uri` path should be that of the symbol's parent module or procedure scope.

> [!WARNING]
> Symbol `Uri`s are hierarchical, but they should never be used to rebuild a client-side tree-like structure.

Name resolution is described in [**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md).

### Name

- The `Name` of a symbol defined in workspace source code **must** be a valid _identifier name_.
- The `Name` of a _static symbol_ corresponds to its identifier name, if the static symbol has one.
- A static symbol that has no identifier name (an operator, for example) should have a `Name` that clearly is not a legal VBA name. This avoids any possible confusion with a VBA name.

### Scope Kind

`ScopeKind` defines the allocation scopes. The scope kind of a `Symbol` determines how, and whether, the symbol is allocated in memory.

|Value|Level|Allocation|
|---|---|---|
|`Unallocated`|A pseudo-scope for pseudo-symbols.|Not allocated in memory. Example: [VBVoidValue](../api/RDCore.SDK.Model.Values.VBVoidValue.html).|
|`Global`|Mostly [StaticSymbol](../api/RDCore.SDK.Model.Symbols.Abstract.StaticSymbol.html) instances and symbols obtained from referenced libraries.|Lives in the _globals_ heap.|
|`Local`|Procedure level.|Scoped to the local [ICallStackFrame](../api/RDCore.SDK.Runtime.Abstract.Execution.ICallStackFrame.html).|
|`Module`|Module level.|Lives in the _workspace statics_ heap.|
|`Instance`|Instance level.|Lives in the _object_ heap.|
|`External`|Allocated externally.|Lives out of process, at a known address.|

The heaps and the services that manage them are described in [**RD-VBAL §2.3.1.2** Session Services](rd-vbal.2.3.1.2.session-services.md).

### Symbol Kind

The symbol kind of a `Symbol` is as specified in **LSP 3.17**. The symbol kinds RD-VBA uses are the members of `SymbolKindExt`, each with an LSP `SymbolKind` equivalent:

|RD-VBA symbol kind|LSP equivalent|
|---|---|
|`Module`|`SymbolKind.Module`|
|`Project`|`SymbolKind.Namespace`|
|`Class`|`SymbolKind.Class`|
|`Procedure`|`SymbolKind.Method`|
|`Field`|`SymbolKind.Field`|
|`Enum`|`SymbolKind.Enum`|
|`Interface`|`SymbolKind.Interface`|
|`Function`|`SymbolKind.Function`|
|`Variable`|`SymbolKind.Variable`|
|`Constant`|`SymbolKind.Constant`|
|`StringLiteral`|`SymbolKind.String`|
|`NumberLiteral`|`SymbolKind.Number`|
|`BooleanLiteral`|`SymbolKind.Boolean`|
|`Array`|`SymbolKind.Array`|
|`Object`|`SymbolKind.Object`|
|`Key`|`SymbolKind.Key`|
|`Null`|`SymbolKind.Null`|
|`EnumMember`|`SymbolKind.EnumMember`|
|`UserDefinedType`|`SymbolKind.Struct`|
|`Event`|`SymbolKind.Event`|
|`Operator`|`SymbolKind.Operator`|

> [!TIP]
> The LSP standard symbol kinds `File`, `Constructor` and `TypeParameter` are not used in RD-VBA.
>
> RD-VBA repurposes the LSP `Namespace` symbol kind to a different meaning: the RD-VBA `Project` symbol kind maps to `SymbolKind.Namespace`. There is no concept of a namespace in VBA.
>
> The token that follows the `!` operator in a dictionary access expression represents a dictionary key, which the LSP `Key` symbol kind would fit; see [**RD-VBAL §5.6.14** Dictionary Access Expressions](rd-vbal.5.6.14.dictionary-access-expressions.md).

### Extension Symbol Kinds

RD-VBA additionally defines the following _extension symbol kinds_ in `SymbolKindExt`:

- `Ignored`
- `Attribute`
- `Directive`
- `LineLabel`
- `DateLiteral`
- `VariantLiteral`
- `TypeDescriptor`

The extension symbol kinds may or may not be supported by an LSP client (editor). When a client supports them, they help it supply more precise _hover tips_.

### Typed and Member Symbols

[ITypedSymbol](../api/RDCore.SDK.Model.Symbols.Abstract.ITypedSymbol.html) unifies bound and unbound typed symbols; see [**RD-VBAL §5.6.10** Simple Name Expressions](rd-vbal.5.6.10.simple-name-expressions.md).

[VBReturningMemberSymbol](../api/RDCore.SDK.Model.Symbols.Abstract.VBReturningMemberSymbol.html) is the base type of a `Function`'s and a `Property Get`'s member symbol. `Const`, `EnumConst`, module-level and instance fields, and UDT fields share this base type with `Function` and `Property Get` symbols.

[VBProcedureMemberSymbol](../api/RDCore.SDK.Model.Symbols.VBProject.VBProcedureMemberSymbol.html)`.Locals` and `VBReturningMemberSymbol.Locals` list every `Dim`, `Static` and `Const` declared in the procedure body. The `Locals` property of a procedure member symbol mirrors its `Parameters` property; see [**RD-VBAL §5.4.3.1** Local Variable Declarations](rd-vbal.5.4.3.1.local-variable-declarations.md).

A class module with `VB_PredeclaredId = True` has a [VBPredeclaredInstanceSymbol](../api/RDCore.SDK.Model.Symbols.VBPredeclaredInstanceSymbol.html): a global variable named after the class, whose declared type is that class. It is an automatic instantiation variable ([SymbolProperties](../api/RDCore.SDK.Model.Symbols.Abstract.SymbolProperties.html)`.AutoInstantiated`, [**MS-VBAL §2.5.1** Automatic Object Instantiation](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/fef06761-45b9-48c2-825c-b75c28aee9b5)), as is any variable declared with an `As New` clause. See [**RD-VBAL §3.1.1** Attributes](rd-vbal.3.1.1.attributes.md) and [**RD-VBAL §5.2.3** Module Declarations](rd-vbal.5.2.3.module-declarations.md).

---
> ⏮️ [**RD-VBAL §2.5** Runtime Values](rd-vbal.2.5.runtime-values.md) | ⏭️ [**RD-VBAL §2.5.2** VBTypedValue](rd-vbal.2.5.2.vbtypedvalue.md)
