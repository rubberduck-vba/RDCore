# 3.1 Attributes and Directives

_Directives_ are **non-executable statements** that influence the semantics of the module they're located in, or the member they're referring to.

These include `Option` statements:

|Directive|Description
|---|---|
|`Option Explicit`|Implicit declarations become compile-time errors|
|`Option Base`|Determines the base (0 or 1) of implicitly-sized arrays|
|`Option Compare`|Determines the comparison mode (`Text` or `Binary`, or a _host-defined token_ to dynamically configure this value) for string comparisons|
|`Option Private Module`|Determines the _accessibility_ of a module|

Directives also include `Def<Type>` _implicit definition_ statements:

|Directive|Description
|---|---|
|`DefBool`|Configures implicit definitions for [VBBooleanType](../api/RDCore.SDK.Model.Types.VBBooleanType.html)|
|`DefByte`|Configures implicit definitions for [VBByteType](../api/RDCore.SDK.Model.Types.VBByteType.html)|
|`DefInt`|Configures implicit definitions for [VBIntegerType](../api/RDCore.SDK.Model.Types.VBIntegerType.html)|
|`DefLng`|Configures implicit definitions for [VBLongType](../api/RDCore.SDK.Model.Types.VBLongType.html)|
|`DefLngLng`|Configures implicit definitions for [VBLongLongType](../api/RDCore.SDK.Model.Types.VBLongLongType.html) in 64-bit environments|
|`DefLngPtr`|Configures implicit definitions for [VBLongPtrType_x86](../api/RDCore.SDK.Model.Types.VBLongPtrType_x86.html) (32-bit) or [VBLongPtrType_x86](../api/RDCore.SDK.Model.Types.VBLongPtrType_x64.html) (64-bit)|
|`DefCur`|Configures implicit definitions for [VBCurrencyType](../api/RDCore.SDK.Model.Types.VBCurrencyType.html)|
|`DefSng`|Configures implicit definitions for [VBSingleType](../api/RDCore.SDK.Model.Types.VBSingleType.html)|
|`DefDbl`|Configures implicit definitions for [VBDoubleType](../api/RDCore.SDK.Model.Types.VBDoubleType.html)|
|`DefDate`|Configures implicit definitions for [VBDateType](../api/RDCore.SDK.Model.Types.VBDateType.html)|
|`DefStr`|Configures implicit definitions for [VBStringType](../api/RDCore.SDK.Model.Types.VBStringType.html)|
|`DefObj`|Configures implicit definitions for [VBObjectType](../api/RDCore.SDK.Model.Types.VBObjectType.html)|
|`DefVar`|Configures implicit definitions for [VBVariantType](../api/RDCore.SDK.Model.Types.VBVariantType.html)|

Other directives include `Implements` and `Attribute` statements:

|Directive|Description
|---|---|
|`Implements`|Specifies that the (class) module _implements_ an _interface class_.|
|`Attribute`|Specifies flags and modifiers that alter the semantics of a module or member.|


---
## 3.1.1 Attributes
> [!NOTE]
> **MS-VBAL 5.2.3 Module Declaration:** _Composition and compilation of Attribute statements is not permitted in the **Microsoft Visual Basic for Applications editor**, however, they are consumed and produced by **Microsoft Visual Basic for Applications** without error upon import and export and are therefore **considered valid VBA language constructs**._

The interpretation of the **RDCore** platform is that this section of the **MS-VBAL** specification:
- Relates specifically to the _MS-VBA_ implementation and the _Microsoft VBIDE_, which is _out of scope_ for **RD-VBA**;
- Affirms `Attribute` statements as _valid VBA language constructs_;

Therefore:
- `Attribute` statements are valid **RD-VBA** language constructs;
- Whether a RD-VBA client / editor displays `Attribute` statements and/or allows their composition within the editor, is _implementation-dependent_.
- **Compilation** in RD-VBA is the responsibility of the _environment host_, i.e. the `rdc.exe` console client - specifically, it is normally **NOT** a concern for any other client or IDE.

Attributes in the _header_ section of a module determine the _static semantics_ of that module.

> [!TIP]
> **MS-VBA** attribute semantics are severely truncated; **RD-VBA** has no reason not to honor their semantics accordingly with their original **VB6** intent.


### 3.1.1.1 VB_Name
If present, the value of a `VB_Name` attribute determines the `Name` of the _symbol_ for that module.

If omitted, the _environment host_ may inject one with a value that matches the _file name_ of the module, stripped of any empty spaces or other characters that would be illegal in a valid _identifier name_:
- If there are no other attributes in the header, and no module name can be inferred from the file, the module is named `Module` followed by as many digits as necessary to make a unique module name, starting with `Module1`, then `Module2`, and so on until a unique name is determined.
- If the header contains any other attributes, and no module name can be inferred from the file, the module is named `Class` followed by as many digits as necessary to make a unique module name, starting with `Class1`, then `Class2`, and so on until a unique name is determined.

The _environment host_ **must** inject any missing attributes _before_ requesting the parsing of that module, only if the file is NOT currently owned by any IDE or _editor client_.
- If a module is missing a `VB_Name` attribute and is currently opened in an IDE or _editor client_, the language server may send a `WorkspaceEdit` notification to have any editor-owned files modified by the editor.

> [!TIP]
> See [LSP 3.17 § WorkspaceEdit](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#workspaceEdit) for more information about commanding client-side _workspace edits_ from the _language server_. Note that the implementation must ensure that the LSP client _does_ support the required capabilities for the requested edits.


### 3.1.1.2 VB_Creatable
Determines whether a class module can be directly instantiated using a `New` (or `CreateObject`) expression from a _referencing project_.  

The value of this attribute **MUST** be `False` in a **VBA** module, but may be `True` in a **VB6** module, for RD-VBA clients that support the **VB6** language, of which **VBA** is deemed a subset.

> [!TIP]
> In practical terms, a _not-creatable_ class module can **only** be directly instantiated within the project it is defined in (the _enclosing project_), but an _instance_ of that class may be consumed by any _referencing project_ if the module is _exposed_.


### 3.1.1.2 VB_Exposed
Determines whether a class module is visible at all to a _referencing project_.

The value of this attribute is `False` for _private modules_, or `True` for _public modules_; a _public module_ may be consumed by a _referencing project_, but whether a new instance of the module can be created outside of the _enclosing project_ that defines it, depends on the value of its `VB_Creatable` attribute.

👉 Together, `VB_Creatable` and `VB_Exposed` determine the _instancing mode_ of a class module this value is:
- `Private` when both attribute values are `False`;
- `PublicNotCreatable` given `VB_Exposed=True` but `VB_Creatable=False`;
- `PublicCreatable` given `VB_Exposed=True` and `VB_Creatable=True`.

> [!NOTE]
> The `PublicCreatable` _instancing mode_ is not a legal **VBA** configuration, but **RD-VBA** implementations may allow it, given _semantic flags_ being issued if the _host environment_ is configured to allow building _library projects_.


### 3.1.1.3 VB_GlobalNameSpace
Determines whether a class module is exposed to the _global namespace_.

> [!NOTE]
> This attribute is only meaningful in a _library project_.


### 3.1.1.4 VB_Customizable
This attribute marks a class, method, or property as _customizable_ in host environments that support _VB6 ActiveX Designers_ or _VB6 Object Template_; it indicates that the class or member supports _design-time customization_ and may participate in _persistence mechanisms_ used by _designer hosts_.

> [!NOTE]
> A _customizable_ class or member is allowed to appear in a .frx or _property bag_.

**VB6** sets it automatically depending on whether:
- the class is `Public` or part of an _ActiveX Project_;
- the member is eligible for _design-time customization_;
- the member is persisted (_serialized_) in a _property bag_.

This attribute controls:
- How a component is described in a _type library_;
- How a consuming COM host interprets those descriptions;
- Whether a _designer tool_ can _override_ or _persist_ the member.

> 🎯 _VB6 ActiveX designer features_ are **out-of-scope** for the **RDCore** _language core_.  
> 🧩 _VB6 ActiveX Designer features_ would be an very cool eventual _platform extension_ though.


### 3.1.1.5 VB_PredeclaredId
Determines whether the _environment host_ declares a global _auto-object_ instance of the class with a _predeclared ID_, where the _identifier name_ of the global _auto-object_ has the same name as the class module it is a _predeclared_ instance of.

The "Id" refers to an internal _unique semantic identifier_ given to every object in the _host environment_.

> [!TIP]
> Setting an _auto-object_ to `Nothing` destroys its internal state (_semantic flags_ should identify whether a _predeclared_ class module is _stateful_ or not), but the object reference is immediately re-created as soon as it is being referred to, _including_ within a `Is Nothing` reference check - that check is therefore _statically constant_ (`false`).


### 3.1.1.6 VB_Description
This attribute holds a short _documentation string_ that IDE tooling can then use to supply helpful tooltips.

> [!TIP]
> Surfacing attributes does not necessarily make `@Description` annotations obsolete, because hiding `Attribute` directives may or may not be a capability that is supported by a LSP client.


---
> ⏮️ [**RD-VBAL §3.0** Syntax Tree](rd-vbal.3.0.syntax-tree.html) | ⏮️ [**RD-VBAL §3.2** Literals](rd-vbal.3.2.0.literals.html)
