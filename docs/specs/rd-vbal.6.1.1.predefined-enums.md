# 6.1.1 Predefined Enums

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.1** Predefined Enums](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/cc0c4b7c-bd09-448b-9eeb-19a9d4c19504).

RD-VBA implements all sixteen **MS-VBAL §6.1.1** predefined enums. The SDK defines all sixteen, and the standard
library's symbols are read off those SDK declarations
([**RD-VBAL §6.0.1** Symbol Injection](rd-vbal.6.0.standard-library.md#601-symbol-injection)).

|§|Enum|SDK declaration|Notes|
|---|---|---|---|
|6.1.1.1|[FormShowConstants](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/bea504ff-53dd-48c8-b165-e0a4e8ce3bba)|[VBFormShowConstants](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBFormShowConstants.html)|No naming convention recovers the name `FormShowConstants`, so `StdLibEnumAttribute` states it.|
|6.1.1.2|[VbAppWinStyle](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2bf1e79f-d181-4e3d-bbb9-bda24b2b8ea4)|[VBAppWinStyle](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBAppWinStyle.html)||
|6.1.1.3|[VbCalendar](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9ec53148-9a34-4add-8728-99d8e1ef1feb)|[VBCalendar](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBCalendar.html)||
|6.1.1.4|[VbCallType](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3e5c6a2c-757b-4107-81f3-ef7112afdf2d)|[VBCallType](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBCallType.html)||
|6.1.1.5|[VbCompareMethod](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/92f41ee1-59f5-4792-9a20-208e6b62d699)|[VBCompareMethod](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBCompareMethod.html)||
|6.1.1.6|[VbDateTimeFormat](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/b6fd0457-1221-4b32-b709-57abd0235230)|[VBDateTimeFormat](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBDateTimeFormat.html)||
|6.1.1.7|[VbDayOfWeek](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3cedd8d4-1e8a-4955-a34f-8c54e82456f1)|[VBDayOfWeek](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBDayOfWeek.html)||
|6.1.1.8|[VbFileAttribute](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/50ea77ee-b398-4a8d-943a-edf517d35401)|[VBFileAttribute](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBFileAttribute.html)||
|6.1.1.9|[VbFirstWeekOfYear](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/fe5b3612-55ed-4757-807c-c3608c90b495)|[VBFirstWeekOfYear](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBFirstWeekOfYear.html)||
|6.1.1.10|[VbIMEStatus](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/910e9de1-70aa-4bb2-a793-b380faad2052)|[VBIMEStatus](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBIMEStatus.html)||
|6.1.1.11|[VbMsgBoxResult](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f566f64b-9667-40f5-9b6d-8c3264e3f562)|[VBMsgBoxResult](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBMsgBoxResult.html)||
|6.1.1.12|[VbMsgBoxStyle](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f18d1d63-22a9-499f-b970-ecd0d4c5a30b)|[VBMsgBoxStyle](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBMsgBoxStyle.html)||
|6.1.1.13|[VbQueryClose](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/89023539-ebf1-4e5c-ac50-b7d335ec42d3)|[VBQueryClose](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBQueryClose.html)||
|6.1.1.14|[VbStrConv](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f03af91f-5ee0-42dc-8b2c-f93e0a372524)|[VBStrConv](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBStrConv.html)||
|6.1.1.15|[VbTriState](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/98bc1324-a43f-4bdd-a1a6-a5cba137a809)|[VBTriState](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBTriState.html)||
|6.1.1.16|[VbVarType](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/c15d7483-4ac0-48e0-a23b-8bfb91a57cad)|[VBVarType](../api/RDCore.SDK.Model.Values.Runtime.VBVarType.html)|Declared in the core value model. See [§6.1.1.16](#61116-vbvartype).|

The other enum names follow the naming convention: the SDK enum `VBDayOfWeek` is the enum `VbDayOfWeek`, and its
member `VBSunday` is the constant `vbSunday`.

Because an enum member is placed through its enum, an enum member such as `vbSunday` is a name on its own, resolvable
without qualification ([**RD-VBAL §5.2.3.4** Enum Declarations](rd-vbal.5.2.3.module-declarations.md#5234-enum-declarations)).

An enum member is a constant: it has no storage, and its value, a `Long`, is on its symbol, where a use of it reads
it.

> [!NOTE]
> **Not implemented.** A constant that has no value to read, and an enum's own name evaluated as a value
> (`Debug.Print VbCompareMethod`), are not evaluated: reading one reports `InternalError`, and the run stops as not
> yet implemented. A constant has no value when it is a member of an `Enum` the workspace declares, whose symbol is
> given no value yet, or a `Const` declared with no expression.


## 6.1.1.16 VbVarType

> [!NOTE]
> This section describes the implementation of [**MS-VBAL §6.1.1.16** VbVarType](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/c15d7483-4ac0-48e0-a23b-8bfb91a57cad).

### Tag Space

The `VbVarType` tag space is COM `VARENUM`-compatible. It is declared in the core value model as
[VBVarType](../api/RDCore.SDK.Model.Values.Runtime.VBVarType.html) (`RDCore.SDK.Model.Values.Runtime.VBVarType`).

A `Variant`'s own tag is a `VBVarType`. `VBVarType` has the same numeric values that
`VarType()` reports ([**RD-VBAL §6.1.2.7** Information](rd-vbal.6.1.2.7.information.md)), and the same numeric values
that OLE Automation marshals a `VARIANT` against.

### Mapping a Declared Type to a Tag

[VBVarTypeExtensions](../api/RDCore.SDK.Model.Types.VBVarTypeExtensions.html).`VarType` maps a
[VBType](../api/RDCore.SDK.Model.Types.Abstract.VBType.html) to its `VBVarType` tag:

|Declared type|`VBVarType` tag|
|---|---|
|An array type ([VBArrayType](../api/RDCore.SDK.Model.Types.VBArrayType.html))|The array's own tag, `VBArray`, combined with its element type's own tag, computed recursively.|
|A [VBClassType](../api/RDCore.SDK.Model.Types.Complex.VBClassType.html) whose class module is known|Defers to the class module's `AutomationKind` (see [Automation Kind](#automation-kind)).|
|A generic [VBObjectType](../api/RDCore.SDK.Model.Types.VBObjectType.html) reference|`VT_DISPATCH` (the `Dispatch` tag), by default.|

A generic `VBObjectType` reference defaults to `VT_DISPATCH` because a live object's concrete class is only knowable
by looking up the actual instance, which the `VarType` mapping has no access to. Without that lookup, `VT_DISPATCH`
is the only sound default.

### Variant Values

[VBVariantValue](../api/RDCore.SDK.Model.Values.Intrinsic.VBVariantValue.html) computes its `VarType`, its
`VBVarType` tag, on construction: `VBVarTypeExtensions.VarType` maps the wrapped value's declared type to the tag.

A `Variant`'s `VBVarType` tag is carried on its
[VBRuntimeVariantValue](../api/RDCore.SDK.Model.Values.Runtime.VBRuntimeVariantValue.html) box. The tag round-trips
through storage alongside the value itself
([**RD-VBAL §2.5.2.1.5** Variant Values](rd-vbal.2.5.2.1.5.variant-values.md)).

### Automation Kind

[VBClassModuleSymbol](../api/RDCore.SDK.Model.Symbols.VBClassModuleSymbol.html).`AutomationKind`
([VBAutomationKind](../api/RDCore.SDK.Model.Symbols.VBAutomationKind.html)) distinguishes an Automation-capable
(`VT_DISPATCH`) class module from an `IUnknown`-only class module. `VarType` consults it for any `VBClassType` whose
class module is known.

|`AutomationKind`|Class module|`VBVarType` tag|
|---|---|---|
|`Dispatch` (default)|Automation-capable.|`VT_DISPATCH`|
|`Unknown`|`IUnknown`-only.|`vbDataObject`, the sibling of `VT_DISPATCH`. Its tag value is 13.|

`AutomationKind` defaults to `Dispatch`. Every RD-VBA class module has `AutomationKind` `Dispatch` (`VT_DISPATCH`)
([**RD-VBAL §5.2.4** Class Module Declarations](rd-vbal.5.2.4.class-module-declarations.md)).

> [!NOTE]
> **Not implemented.** An `IUnknown`-only class module. Nothing in RD-VBA constructs a class module whose
> `AutomationKind` is `Unknown`, so every class module is `Dispatch`.

---
> ⏮️ [**RD-VBAL §6.1** VBA Project](rd-vbal.6.1.vba-project.md) | ⏭️ [**RD-VBAL §6.1.2** Predefined Procedural Modules](rd-vbal.6.1.2.predefined-procedural-modules.md)
