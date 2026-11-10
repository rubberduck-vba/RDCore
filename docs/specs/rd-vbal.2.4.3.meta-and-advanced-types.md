# 2.4.3 Meta and Advanced Types

_Meta types_ are _introspective_ data types that can _reflectively_ describe the type system itself.

> [!WARNING]
> Meta types must not be exposed directly to RD-VBA source code. Doing so would break the language on a fundamental level.

> Meta types provide _language-level extension_ possibilities.

The type system includes and uses meta types, such as `VBTypeDescValue`; see [**RD-VBAL §3.0.3** Binding Contexts](rd-vbal.3.0.3.binding-contexts.md).

The meta types are:

|Meta type|Section|
|---|---|
|[VBTypeDesc](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.Meta.VBTypeDesc.html)|[§2.4.3.1](#2431-vbtypedesc)|
|[VBMemberDesc](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.Meta.VBMemberDesc.html)|[§2.4.3.2](#2432-vbmemberdesc)|

## 2.4.3.1 VBTypeDesc

`VBTypeDesc` represents (describes) a [VBType](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.Abstract.VBType.html) within the type system.

A value of the `VBTypeDesc` meta type, a [VBTypeDescValue](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Values.Meta.VBTypeDescValue.html), is used in the implementation of:

- the `Is` relational operator (see [**RD-VBAL §5.6.9.7** Is Operator](rd-vbal.5.6.9.7.is-operator.md));
- _let-coercion_ (see [**RD-VBAL §5.5.1.2** Runtime semantics](rd-vbal.5.5.1.2.runtime-semantics.md)).

Both use a `VBTypeDescValue` because their semantics demand knowledge of a _data type_ where a _value_ is normally required.

> [!WARNING]
> Because a `VBTypeDescValue` is a data value that represents a data type, the implementation of both static and runtime semantics must account for the possibility of pattern-matching such a _type descriptor_ by accident.

> [!NOTE]
> **Not implemented.** None of the descriptor types other than `VBTypeDesc` are in use.

## 2.4.3.2 VBMemberDesc

`VBMemberDesc` is an _abstract_ descriptor that represents (describes) any [VBTypeMemberSymbol](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.Abstract.VBTypeMemberSymbol.html). It holds the member's parameters as they are declared, each a [VBParameterSymbol](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.VBProject.VBParameterSymbol.html).

RD-VBAL groups the following member descriptors under `VBMemberDesc`:

|Section|Descriptor|
|---|---|
|[§2.4.3.2.1](#24321-vbprocedurememberdesc)|`VBProcedureMemberDesc`|
|[§2.4.3.2.2](#24322-vbpropertyletproceduredesc)|`VBPropertyLetProcedureDesc`|
|[§2.4.3.2.3](#24323-vbpropertysetproceduredesc)|`VBPropertySetProcedureDesc`|
|[§2.4.3.2.4](#24324-vbreturningmemberdesc)|`VBReturningMemberDesc`|

### 2.4.3.2.1 VBProcedureMemberDesc

[VBProcedureMemberDesc](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.Meta.VBProcedureMemberDesc.html) is a descriptor that represents (describes) any [VBProcedureMemberSymbol](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.VBProject.VBProcedureMemberSymbol.html).

### 2.4.3.2.2 VBPropertyLetProcedureDesc

[VBPropertyLetProcedureDesc](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.Meta.VBPropertyLetProcedureDesc.html) is a descriptor that represents (describes) a [VBPropertyLetMemberSymbol](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.VBProject.VBPropertyLetMemberSymbol.html).

### 2.4.3.2.3 VBPropertySetProcedureDesc

[VBPropertySetProcedureDesc](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.Meta.VBPropertySetProcedureDesc.html) is a descriptor that represents (describes) a [VBPropertySetMemberSymbol](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.VBProject.VBPropertySetMemberSymbol.html).

### 2.4.3.2.4 VBReturningMemberDesc

[VBReturningMemberDesc](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.Meta.VBReturningMemberDesc.html) is an _abstract_ descriptor that represents (describes) any [VBReturningMemberSymbol](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.Abstract.VBReturningMemberSymbol.html).

RD-VBAL groups the following descriptors under `VBReturningMemberDesc`:

|Section|Descriptor|
|---|---|
|[§2.4.3.2.4.1](#243241-vbfunctionproceduredesc)|`VBFunctionProcedureDesc`|

#### 2.4.3.2.4.1 VBFunctionProcedureDesc

[VBFunctionProcedureDesc](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Types.Meta.VBFunctionProcedureDesc.html) is a descriptor that represents (describes) a [VBFunctionMemberSymbol](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.Symbols.VBProject.VBFunctionMemberSymbol.html).
