# 6.0 Standard Library

---
## 6.1 VBA Project
The `VBA` project is a _host project_ that is present in every _VBA environment_. The `VBA` project consists of a set of classes, functions, `Enum` and constants that form VBA's _standard library_.

🎯 The **RDCore** platform must therefore implement this library, and the _environment host_ shall inject its symbols into all `VBA` projects; the symbols shall carry the appropriate _return type_ metadata.

### 6.1.1 Symbol injection

The SDK declarations _are_ the library's definition, and its symbols are read off them: a module's members, their names, their parameters and their return types all come from the signature an implementation has to satisfy, so a symbol the _workspace_ resolves cannot describe a member the runtime does not have.

- A declaration states its **return type** in its own signature: a `RuntimeSemanticsEvaluationResult<TValue>` names the value the member produces, and the non-generic `RuntimeSemanticsEvaluationResult` names none — which is what makes the member a `Sub`.
- What a signature cannot express is stated by an attribute, and only where it applies: `StdLibModuleAttribute` / `StdLibClassAttribute` / `StdLibEnumAttribute` mark a declaration and may name it; `StdLibMemberAttribute` carries a name no convention recovers (`Hex` beside `Hex$`), an accessor kind, or a return type that is a _class_ or an _enum_ rather than an intrinsic type.
- Everything regular is left to convention: `IStdInformationModule` is `Information`, `VBDayOfWeek` is `VbDayOfWeek`, `VBSunday` is `vbSunday`.
- Nothing _references_ the library and nothing opts into it: the set of modules a project gets is whatever carries a marker, so the symbols are present whether or not a `.rdproj` mentions the library at all.

The `Err` _function_ (**MS-VBAL §6.1.3.2**) illustrates one deliberate shape. MS-VBAL describes the error object as the default instance of a global class module named `Err`; **MS-VBA** exposes it as a zero-argument `Function` of `Information` returning an instance of a class named `ErrObject`. The two are indistinguishable from source — a standard module's members are promoted to the _project scope_, so a bare `Err` yields the error object either way — and the latter is what also leaves `ErrObject` nameable in an `As` clause instead of shadowed by its own default instance. **RD-VBA implements the MS-VBA shape.**

### 6.1.2 `ErrObject.StackTrace`

🎯 **RD-VBA adds one member to MS-VBAL's `Err` class**: a read-only `StackTrace` property reporting the call stack the current error was raised on, innermost activation first. VBA can say _what_ an error was but never _where_ it came from, which is what makes an `Err.Description` from deep in a call chain so uninformative.

- The trace is **captured when the error is raised** — at the interpreter's own error-interception point, the one place every run-time error passes through — rather than derived when it is read: by the time a handler reads it, the activations it names have been unwound.
- Only the activation the error was raised in carries a _location_; a caller's activation record does not say where in itself it is suspended.
- It is empty when no error is current, and when source made one current by assigning `Err.Number` rather than by raising one.

### 6.1.3 Modules

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
