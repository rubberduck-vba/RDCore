# 6.0 Standard Library

---
## 6.1 VBA Project
The `VBA` project is a _host project_ that is present in every _VBA environment_. The `VBA` project consists of a set of classes, functions, `Enum` and constants that form VBA's _standard library_.

🎯 The **RDCore** platform must therefore implement this library, and the _environment host_ shall inject its symbols into all `VBA` projects; the symbols shall carry the appropriate _return type_ metadata.

The SDK defines all the interfaces for the _internal representation_ of each module - the _environment host_ exposes the symbols provided by the library to the _workspace_:

- **MS-VBAL §6.1.1 Predefined Enums**
  - [FormShowConstants](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBFormShowConstants.html)
  - [VbAppWinStyle](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBAppWinStyle.html)
  - [VbCalendar](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBCalendar.html)
  - [VbCallType](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBCallType.html)
  - [VbCompareMethod](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBCompareMethod.html)
  - [VbDateTimeFormat](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBDateTimeFormat.html)
  - [VbDayOfWeek](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBDayOfWeek.html)
  - [VbFileAttribute](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBFileAttribute.html)
  - [VbFirstWeekOfYear](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBFirstWeekOfYear.html)
  - [VbIMEStatus](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBIMEStatus.html)
  - [VbMsgBoxResult](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBMsgBoxResult.html)
  - [VbMsgBoxStyle](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBMsgBoxStyle.html)
  - [VbQueryClose](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBQueryClose.html)
  - [VbStrConv](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBStrConv.html)
  - [VbTriState](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBTriState.html)
  - [VbVarType](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBVarType.html)

- **MS-VBAL §6.1.2 Predefined Procedural Modules**
  - [ColorConstantsModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdColorConstantsModule.html)
  - [ConstantsModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdConstantsModule.html)
  - [ConversionModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdConversionModule.html)
  - [DateTimeModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdDateTimeModule.html)
  - [FileSystemModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdFileSystemModule.html)
  - [FinancialModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdFinancialModule.html)
  - [InformationModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdInformationModule.html)
  - [InteractionModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdInteractionModule.html)
  - [KeyCodeConstants](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBKeyCodeConstants.html)
  - [MathModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdMathModule.html)
  - [StringsModule](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdStringsModule.html)
  - [SystemColorsConstants](../api/RDCore.SDK.Runtime.Abstract.StdLib.VBSystemColorConstants.html)

- **MS-VBAL §6.1.3 Predefined Class Modules**
  - [CollectionClass](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdCollectionClass.html)
  - [ErrClass](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdErrClass.html)
  - [GlobalClass](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdGlobalClass.html)

> [!NOTE]
> The **VBScript RegExp 5.5** _regular expressions_ library was recently folded (as-is) into the **MS-VBA** _VBA Standard Library_; this reference MS-VBAL section does not actually exist, the folded VBScript library does not appear to be officially documented by its publisher at this time.

- **MS-VBAL §6.2.1 VBScript RegExp 5.5 Class Modules**
  - [RegExpClass](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdRegExpClass.html)
  - [MatchClass](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdMatchClass.html)
  - [MatchCollectionClass](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdMatchCollectionClass.html)
  - [SubMatchesClass](../api/RDCore.SDK.Runtime.Abstract.StdLib.IStdSubMatchesClass.html)


---
> ⏮️ [**RD-VBAL §5.0** Semantics](rd-vbal.5.0.semantics.html)
