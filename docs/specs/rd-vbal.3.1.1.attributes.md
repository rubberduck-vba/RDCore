# 3.1.1 Attributes

> [!NOTE]
> [**MS-VBAL §5.2.3** Module Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/b40b8d00-3348-43c1-9cfb-c0eadef565ee):
> _"Composition and compilation of Attribute statements is not permitted in the Microsoft Visual Basic for
> Applications editor, however, they are consumed and produced by Microsoft Visual Basic for Applications without
> error upon import and export and are therefore considered valid VBA language constructs."_

The **RDCore** platform interprets this passage of the MS-VBAL specification as follows:

- It relates specifically to the _MS-VBA_ implementation and the _Microsoft VBIDE_. Both are _out of scope_ for
  **RD-VBA**.
- It affirms `Attribute` statements as _valid VBA language constructs_.

Therefore:

- `Attribute` statements are valid **RD-VBA** language constructs.
- Whether an RD-VBA client or editor displays `Attribute` statements is _implementation-dependent_.
- Whether an RD-VBA client or editor allows the composition of `Attribute` statements within the editor is
  _implementation-dependent_.
- _Compilation_ in RD-VBA is the responsibility of the _environment host_, i.e. the `rdc.exe` console client
  ([**RD-VBAL §2.0** RD-VBA Computational Environment](rd-vbal.2.0.computational-environment.md)). Compilation is
  normally not a concern for any other RD-VBA client or IDE.

Attributes in the _header_ section of a module determine the _static semantics_ of that module.

MS-VBA attribute semantics are much reduced compared with their original **VB6** intent. 🎯 RD-VBA honors
attribute semantics according to their original VB6 intent, because nothing in RD-VBA calls for the MS-VBA reduction.

|Section|Attribute|Determines|
|---|---|---|
|3.1.1.1|`VB_Name`|The `Name` of the module's symbol.|
|3.1.1.2|`VB_Creatable`|Whether a class module can be directly instantiated from a referencing project.|
|3.1.1.3|`VB_Exposed`|Whether a class module is visible at all to a referencing project.|
|3.1.1.4|`VB_GlobalNameSpace`|Whether a class module is exposed to the global namespace.|
|3.1.1.5|`VB_Customizable`|Whether a class, method or property is customizable in designer hosts.|
|3.1.1.6|`VB_PredeclaredId`|Whether the environment host declares a global auto-object instance of the class.|
|3.1.1.7|`VB_Description`|A short documentation string for IDE tooltips.|
|3.1.1.8|`VB_Extensible`|Whether a class module is an extensible ("document") module, whose members a host extends.|

Members of a class can also carry a `VB_UserMemId` attribute, which can specify a number of flags that modify the
behavior of the member. A class can have exactly one member with a `VB_UserMemId` attribute value of `0`: that member
is the class type's _default member_
([**RD-VBAL §2.4.2** Non-intrinsic Types](rd-vbal.2.4.2.non-intrinsic-types.md#24222-default-member)).

## 3.1.1.1 VB_Name

If present, the value of a `VB_Name` attribute determines the `Name` of the _symbol_ for that module. The `Name`
of an `RDCoreModule` is always supplied by a `VB_Name` attribute: on a mismatch between `RDCoreModule.Name` and the
value of the attribute, the attribute value always takes precedence
([**RD-VBAL §2.2.3** ProjectFile](rd-vbal.2.2.3.projectfile.md#2233-rdcoremodule)).

If `VB_Name` is omitted, the _environment host_ may inject a `VB_Name` attribute whose value matches the _file name_
of the module, stripped of spaces and of any other characters that would be illegal in a valid _identifier name_.
If no module name can be inferred from the file, the module is named as follows:

|The module header|Module name|
|---|---|
|Contains no other attributes|`Module` followed by as many digits as necessary to make a unique module name: `Module1`, then `Module2`, and so on until a unique name is determined.|
|Contains any other attributes|`Class` followed by as many digits as necessary to make a unique module name: `Class1`, then `Class2`, and so on until a unique name is determined.|

The environment host **must** inject any missing attributes _before_ requesting the parsing of that module. It
injects them only if the module file is not currently owned by any IDE or _editor client_.

If a module is missing a `VB_Name` attribute and is currently opened in an IDE or editor client, the language
server may send a `WorkspaceEdit` notification, to have the editor-owned file modified by the editor.
[LSP 3.17 § WorkspaceEdit](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#workspaceEdit)
describes commanding client-side _workspace edits_ from the _language server_.

An implementation that commands client-side workspace edits must ensure that the LSP client supports the
capabilities required for the requested edits
([**RD-VBAL §2.0.2** Client/Server Capabilities](rd-vbal.2.0.2.client-server-capabilities.md)).

## 3.1.1.2 VB_Creatable

`VB_Creatable` determines whether a class module can be directly instantiated using a `New` (or `CreateObject`)
expression from a _referencing project_.

|Module|Value of `VB_Creatable`|
|---|---|
|A **VBA** module|**Must** be `False`.|
|A **VB6** module, for RD-VBA clients that support the VB6 language|May be `True`.|

**VBA** is deemed a subset of the **VB6** language
([**RD-VBAL §2.0.1** Supported Languages](rd-vbal.2.0.1.supported-languages.md)).

A _not-creatable_ class module can only be directly instantiated within the project it is defined in (the
_enclosing project_). An instance of a not-creatable class may be consumed by any referencing project if the class
module is _exposed_ ([§3.1.1.3](#3113-vb_exposed)).

## 3.1.1.3 VB_Exposed

`VB_Exposed` determines whether a class module is visible at all to a _referencing project_.

The value of `VB_Exposed` is `False` for _private modules_, and `True` for _public modules_. A public module may be
consumed by a referencing project. Whether a new instance of a public module can be created outside the enclosing
project that defines it depends on the value of its `VB_Creatable` attribute.

👉 Together, `VB_Creatable` and `VB_Exposed` determine the _instancing mode_ of a class module
([**MS-VBAL §5.2.4.1.1** Class Accessibility and Instancing](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/a09fd48e-abed-4da8-8c4c-a110bf4ef6b6);
[**RD-VBAL §5.2.4** Class Module Declarations](rd-vbal.5.2.4.class-module-declarations.md)):

|`VB_Exposed`|`VB_Creatable`|Instancing mode|
|---|---|---|
|`False`|`False`|`Private`|
|`True`|`False`|`PublicNotCreatable`|
|`True`|`True`|`PublicCreatable`|

> [!NOTE]
> The `PublicCreatable` instancing mode is not a legal **VBA** configuration. **RD-VBA** implementations may allow
> it, with _semantic flags_ issued, if the _host environment_ is configured to allow building _library projects_.

## 3.1.1.4 VB_GlobalNameSpace

`VB_GlobalNameSpace` determines whether a class module is exposed to the _global namespace_.

> [!NOTE]
> `VB_GlobalNameSpace` is only meaningful in a _library project_.

## 3.1.1.5 VB_Customizable

`VB_Customizable` marks a class, method, or property as _customizable_ in host environments that support
_VB6 ActiveX Designers_ or _VB6 Object Template_. It indicates that the class or member:

- supports _design-time customization_;
- may participate in _persistence mechanisms_ used by _designer hosts_.

A customizable class or member is allowed to appear in a .frx file or a _property bag_.

**VB6** sets `VB_Customizable` automatically, depending on whether:

- the class is `Public` or part of an _ActiveX Project_;
- the member is eligible for design-time customization;
- the member is persisted (_serialized_) in a property bag.

`VB_Customizable` controls:

- how a component is described in a _type library_;
- how a consuming COM host interprets those type library descriptions;
- whether a _designer tool_ can _override_ or _persist_ the member.

🎯 VB6 ActiveX designer features are **out of scope** for the **RDCore** _language core_
([**RD-VBAL §1.1.1** Platform Extensions](rd-vbal.1.1.1.platform-extensions.md)).

## 3.1.1.6 VB_PredeclaredId

`VB_PredeclaredId` determines whether the _environment host_ declares a global _auto-object_ instance of the class
with a _predeclared ID_. The _identifier name_ of the global auto-object is the same as the name of the class module
it is a _predeclared_ instance of.

The "Id" refers to an internal _unique semantic identifier_ given to every object in the host environment.

### Static Semantics

The static semantics of `VB_PredeclaredId`
([**MS-VBAL §5.2.4.1.2** Default Instance Variables Static Semantics](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/189fb41b-cc3a-4999-a6d2-ba89f72d2870))
are modeled.

A class module with `VB_PredeclaredId = True` has a
[VBPredeclaredInstanceSymbol](../api/RDCore.SDK.Model.Symbols.VBPredeclaredInstanceSymbol.html)
([**RD-VBAL §2.5.1** Runtime Entities](rd-vbal.2.5.1.runtime-entities.md)). A `VBPredeclaredInstanceSymbol` is a
global variable named after the class, whose declared type is that class.

The predeclared instance variable is created as if declared `As New`. It is therefore an _automatic instantiation
variable_ (`SymbolProperties.AutoInstantiated`, see
[SymbolProperties](../api/RDCore.SDK.Model.Symbols.Abstract.SymbolProperties.html);
[**MS-VBAL §2.5.1** Automatic Object Instantiation](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/fef06761-45b9-48c2-825c-b75c28aee9b5)).
For variables declared with an `As New` clause, see
[**RD-VBAL §5.2.3** Module Declarations](rd-vbal.5.2.3.module-declarations.md).

The class name binds as follows
([**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)):

|Name|Binding context|Binds to|
|---|---|---|
|The name of a predeclared class|Default (`ISymbolResolver.ResolveValue`)|The class module's predeclared instance variable.|
|The name of a class that is not predeclared, used in an expression|Default (`ResolveValue`)|An undefined variable.|
|A class name|Type (`ResolveType`: an `As` clause or `New`)|The class.|

For a predeclared class `Widget`, `Widget.Size` is therefore a member access on a variable of type `Widget`
([**RD-VBAL §5.6.12** Member Access Expressions](rd-vbal.5.6.12.member-access-expressions.md)).

Other than through its predeclared instance, a class module is never a name in the default binding context
(`ResolveValue`). A class that is not predeclared has no default instance. Deferred class types cannot be presumed to
have a default instance
([**RD-VBAL §2.4.4** Deferred Types](rd-vbal.2.4.4.deferred-types.md#2442-vbdeferredclasstype)).

It is invalid for the default instance variable to be the target of a `Set` assignment, whatever is assigned to it
(**MS-VBAL §5.2.4.1.2**; [**RD-VBAL §5.4.3.9** Set Statement](rd-vbal.5.4.3.9.set-statement.md)). For a predeclared
class `Widget`:

|Statement|Outcome|
|---|---|
|`Set Widget = New Widget`|Compile error `VBC09304`.|
|`Set Widget = Nothing`|Compile error `VBC09304`.|
|`Widget.Size = 3`|Valid: it assigns a member of the object the default instance variable holds.|
|`Set Widget = …`, where a local variable or field is itself named `Widget`|Valid: the local variable or field hides the default instance of class `Widget`, and is an ordinary `Set` target.|

> [!NOTE]
> **Not implemented.** The declaration-level validity of `As New`
> ([**MS-VBAL §5.2.3.1.4** Variable Type Declarations](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d27d3dae-ac46-4113-8927-c7f60d844dbf))
> is not modeled. See [**RD-VBAL §5.2.3** Module Declarations](rd-vbal.5.2.3.module-declarations.md).

### Runtime Semantics

> [!NOTE]
> **Not implemented.** 🎯 The run-time behavior of the default instance described in this section (it is never
> `Nothing`; it is re-created on reference) is not modeled.

- Setting an auto-object (a predeclared instance) to `Nothing` destroys its internal state.
- _Semantic flags_ should identify whether a predeclared class module is _stateful_ or not
  ([**RD-VBAL §1.1.3** Core Semantic Flags](rd-vbal.1.1.3.core-semantic-flags.md)).
- An auto-object reference is re-created as soon as it is referred to, including within an
  `Is Nothing` reference check.
- An `Is Nothing` check on an auto-object is therefore _statically constant_ (`False`)
  ([**RD-VBAL §5.6.9.7** Is Operator](rd-vbal.5.6.9.7.is-operator.md)).

## 3.1.1.7 VB_Description

`VB_Description` holds a short _documentation string_ that IDE tooling can use to supply tooltips.

Surfacing attributes does not necessarily make `@Description` annotations obsolete
([**RD-VBAL §3.0.1.1** Comment Annotations Syntax](rd-vbal.3.0.1.token-semantics.md#3011-comment-annotations-syntax)): hiding `Attribute`
directives may or may not be a capability that an LSP client supports.

## 3.1.1.8 VB_Extensible

`VB_Extensible` marks a class module as _extensible_: a module of the kind a host extends, such as a document module.
Its value is the symbol's `SymbolProperties.Extensible`, read off the module by `ModuleNodeExtensions.IsExtensible`.

An extensible module cannot have an `Implements` directive
([**RD-VBAL §5.2.4.2**](rd-vbal.5.2.4.class-module-declarations.md)): [VBC09328](../diagnostics/vbc09328.md).

---
> ⏮️ [**RD-VBAL §3.1** Attributes and Directives](rd-vbal.3.1.attributes-directives.md) | ⏭️ [**RD-VBAL §3.2.0** Literal Expressions](rd-vbal.3.2.0.literals.md)
