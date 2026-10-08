# 2.6.3 Runtime Errors

A **runtime error** is raised by the runtime semantics layer and left unhandled by workspace code. Examples are a subscript out of range, a type-mismatch coercion, and division by zero.

|||
|---|---|
|Code family|`VBR`; the numeric portion matches the corresponding MS-VBA run-time error code|
|Title|_Run-time error_|
|Raised by|the runtime semantics layer|
|Source metadata|[VBRuntimeErrorInfo](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorInfo.html); its identifier is a [VBRuntimeErrorId](../api/RDCore.SDK.Model.Errors.VBRuntimeErrorId.html) whose numeric value corresponds to its MS-VBAL specified error code|
|Severity|`Error`|

Regardless of the error message content, RD-VBA must still raise the MS-VBA equivalent error code in the relevant contexts (e.g. `VBR00461` `MethodOrDataMemberNotFound`, whose message uses the term "data member"; see [**RD-VBAL §2.4.2** Non-intrinsic Types](rd-vbal.2.4.2.non-intrinsic-types.md)).

`InternalError` is never reported for a language-level condition that a well-formed program meets; such a condition raises a run-time error.

On a run-time error, the interactive shell renders an icon, the title with the program's own line number, the diagnostic code and description, `Err.Source`, and the stack trace; see [**RD-VBAL §2.0.2** Client/Server Capabilities](rd-vbal.2.0.2.client-server-capabilities.md).

## Run-time Errors Referenced by Statement Semantics

|Error|`VBRuntimeErrorId`|Raised when|See|
|---|---|---|---|
|13|`TypeMismatch`|`For Each` over anything other than an array or an object (a scalar)|[**RD-VBAL §5.4.2.4** For Each Statement](rd-vbal.5.4.2.4.for-each-statement.md)|
|91|`ObjectVariableOrWithBlockVariableNotSet`|`For Each` over `Nothing` (invoking `_NewEnum` on an unset reference)|[**RD-VBAL §5.4.2.4** For Each Statement](rd-vbal.5.4.2.4.for-each-statement.md)|
|92, "For loop not initialized"|`ForLoopNotInitialized`|a `ForNext` or `ForEachNext` with no stored loop state (a `GoTo` landing directly on the closer), or a `For Each` over an array that was never dimensioned|[**RD-VBAL §5.4.2.3** For Statement](rd-vbal.5.4.2.3.for-statement.md), [**RD-VBAL §5.4.2.4** For Each Statement](rd-vbal.5.4.2.4.for-each-statement.md)|
|438|`ObjectDoesntSupportThisPropertyOrMethod`|`For Each` over a live object with no `VB_UserMemId = -4` member|[**RD-VBAL §5.4.2.4** For Each Statement](rd-vbal.5.4.2.4.for-each-statement.md)|

The verbose message for `ForLoopNotInitialized` is the resx entry `VBForLoopNotInitialized_Verbose` when a `Next` closer runs without its opener, and `VBForEach_ArrayNotInitialized_Verbose` when a `For Each` enumerates a never-dimensioned array; both are provided in both languages.

## Published Run-time Errors

|Code|Error|
|---|---|
|[`VBR-0001`](../diagnostics/vbr-0001.md)|Application-defined or object-defined error|
|[`VBR00003`](../diagnostics/vbr00003.md)|Return without GoSub|
|[`VBR00005`](../diagnostics/vbr00005.md)|Invalid procedure call or argument|
|[`VBR00006`](../diagnostics/vbr00006.md)|Overflow|
|[`VBR00007`](../diagnostics/vbr00007.md)|Out of memory|
|[`VBR00009`](../diagnostics/vbr00009.md)|Subscript out of range|
|[`VBR00010`](../diagnostics/vbr00010.md)|This array is fixed or temporarily locked|
|[`VBR00011`](../diagnostics/vbr00011.md)|Division by zero|
|[`VBR00013`](../diagnostics/vbr00013.md)|Type mismatch|
|[`VBR00014`](../diagnostics/vbr00014.md)|Out of string space|
|[`VBR00016`](../diagnostics/vbr00016.md)|Expression too complex|
|[`VBR00017`](../diagnostics/vbr00017.md)|Can't perform requested operation|
|[`VBR00018`](../diagnostics/vbr00018.md)|User interrupt occurred|
|[`VBR00020`](../diagnostics/vbr00020.md)|Resume without error|
|[`VBR00028`](../diagnostics/vbr00028.md)|Out of stack space|
|[`VBR00035`](../diagnostics/vbr00035.md)|Sub or Function not defined|
|[`VBR00047`](../diagnostics/vbr00047.md)|Too many DLL application clients|
|[`VBR00048`](../diagnostics/vbr00048.md)|Error in loading DLL|
|[`VBR00049`](../diagnostics/vbr00049.md)|Bad DLL calling convention|
|[`VBR00051`](../diagnostics/vbr00051.md)|Internal error|
|[`VBR00052`](../diagnostics/vbr00052.md)|Bad file name or number|
|[`VBR00053`](../diagnostics/vbr00053.md)|File not found|
|[`VBR00054`](../diagnostics/vbr00054.md)|Bad file mode|
|[`VBR00055`](../diagnostics/vbr00055.md)|File already open|
|[`VBR00057`](../diagnostics/vbr00057.md)|Device I/O error|
|[`VBR00058`](../diagnostics/vbr00058.md)|File already exists|
|[`VBR00059`](../diagnostics/vbr00059.md)|Bad record length|
|[`VBR00061`](../diagnostics/vbr00061.md)|Disk full|
|[`VBR00062`](../diagnostics/vbr00062.md)|Input past end of file|
|[`VBR00063`](../diagnostics/vbr00063.md)|Bad record number|
|[`VBR00067`](../diagnostics/vbr00067.md)|Too many files|
|[`VBR00068`](../diagnostics/vbr00068.md)|Device unavailable|
|[`VBR00070`](../diagnostics/vbr00070.md)|Permission denied|
|[`VBR00071`](../diagnostics/vbr00071.md)|Disk not ready|
|[`VBR00074`](../diagnostics/vbr00074.md)|Can't rename with different drive|
|[`VBR00075`](../diagnostics/vbr00075.md)|Path/File access error|
|[`VBR00076`](../diagnostics/vbr00076.md)|Path not found|
|[`VBR00091`](../diagnostics/vbr00091.md)|Object variable or With block variable not set|
|[`VBR00092`](../diagnostics/vbr00092.md)|For loop not initialized|
|[`VBR00093`](../diagnostics/vbr00093.md)|Invalid pattern string|
|[`VBR00094`](../diagnostics/vbr00094.md)|Invalid use of Null|
|[`VBR00096`](../diagnostics/vbr00096.md)|Unable to sink events of object because the object is already firing events to the maximum number of event receivers that it supports|
|[`VBR00097`](../diagnostics/vbr00097.md)|Can not call friend function on object which is not an instance of defining class|
|[`VBR00098`](../diagnostics/vbr00098.md)|A property or method call cannot include a reference to a private object, either as an argument or as a return value|
|[`VBR00321`](../diagnostics/vbr00321.md)|Invalid file format|
|[`VBR00322`](../diagnostics/vbr00322.md)|Can't create necessary temporary file|
|[`VBR00325`](../diagnostics/vbr00325.md)|Invalid format in resource file|
|[`VBR00380`](../diagnostics/vbr00380.md)|Invalid property value|
|[`VBR00381`](../diagnostics/vbr00381.md)|Invalid property array index|
|[`VBR00382`](../diagnostics/vbr00382.md)|Set not supported at runtime|
|[`VBR00383`](../diagnostics/vbr00383.md)|Set not supported (read-only property)|
|[`VBR00385`](../diagnostics/vbr00385.md)|Need property array index|
|[`VBR00387`](../diagnostics/vbr00387.md)|Set not permitted|
|[`VBR00393`](../diagnostics/vbr00393.md)|Get not supported at runtime|
|[`VBR00394`](../diagnostics/vbr00394.md)|Get not supported (write-only property)|
|[`VBR00422`](../diagnostics/vbr00422.md)|Property not found|
|[`VBR00423`](../diagnostics/vbr00423.md)|Property or method not found|
|[`VBR00424`](../diagnostics/vbr00424.md)|Object required|
|[`VBR00429`](../diagnostics/vbr00429.md)|ActiveX component can't create object|
|[`VBR00430`](../diagnostics/vbr00430.md)|Class does not support Automation or does not support expected interface|
|[`VBR00432`](../diagnostics/vbr00432.md)|File name or class name not found during Automation operation|
|[`VBR00438`](../diagnostics/vbr00438.md)|Object doesn't support this property or method|
|[`VBR00440`](../diagnostics/vbr00440.md)|Automation error|
|[`VBR00442`](../diagnostics/vbr00442.md)|Connection to type library or object library for remote process has been lost. Press OK for dialog to remove reference.|
|[`VBR00443`](../diagnostics/vbr00443.md)|Automation object does not have a default value|
|[`VBR00445`](../diagnostics/vbr00445.md)|Object doesn't support this action|
|[`VBR00446`](../diagnostics/vbr00446.md)|Object doesn't support named arguments|
|[`VBR00447`](../diagnostics/vbr00447.md)|Object doesn't support current locale setting|
|[`VBR00448`](../diagnostics/vbr00448.md)|Named argument not found|
|[`VBR00449`](../diagnostics/vbr00449.md)|Argument not optional|
|[`VBR00450`](../diagnostics/vbr00450.md)|Wrong number of arguments or invalid property assignment|
|[`VBR00451`](../diagnostics/vbr00451.md)|Property let procedure not defined and property get procedure did not return an object|
|[`VBR00452`](../diagnostics/vbr00452.md)|Invalid ordinal|
|[`VBR00453`](../diagnostics/vbr00453.md)|Specified DLL function not found|
|[`VBR00454`](../diagnostics/vbr00454.md)|Code resource not found|
|[`VBR00455`](../diagnostics/vbr00455.md)|Code resource lock error|
|[`VBR00457`](../diagnostics/vbr00457.md)|This key is already associated with an element of this collection|
|[`VBR00458`](../diagnostics/vbr00458.md)|Variable uses an Automation type not supported in Visual Basic|
|[`VBR00459`](../diagnostics/vbr00459.md)|Object or class does not support the set of events.|
|[`VBR00460`](../diagnostics/vbr00460.md)|Invalid clipboard format|
|[`VBR00461`](../diagnostics/vbr00461.md)|Method or data member not found|
|[`VBR00462`](../diagnostics/vbr00462.md)|The remote machine does not exist or is unavailable|
|[`VBR00463`](../diagnostics/vbr00463.md)|Class not registered on local machine|
|[`VBR00481`](../diagnostics/vbr00481.md)|Invalid picture|
|[`VBR00482`](../diagnostics/vbr00482.md)|Printer error|
|[`VBR00735`](../diagnostics/vbr00735.md)|Can't save file to TEMP|
|[`VBR00744`](../diagnostics/vbr00744.md)|Search text not found|
|[`VBR00746`](../diagnostics/vbr00746.md)|Replacements too long|

## Application Errors

An **application error** is a custom run-time error explicitly raised from workspace source code with `Error` or `Err.Raise`; see [**RD-VBAL §5.4.4.3** Error Statement](rd-vbal.5.4.4.3.error-statement.md) and [**RD-VBAL §6.1.3.2** Err Class](rd-vbal.6.1.3.2.err-class.md). MS-VBAL does not distinguish an application error from a semantic run-time error.

|||
|---|---|
|Code family|`VBA`, a pseudo-code; the numeric portion matches the application-supplied error code|
|Title|_Application error_|
|Raised by|workspace source code (`Error` or `Err.Raise`)|
|Source metadata|[VBApplicationErrorInfo](../api/RDCore.SDK.Model.Errors.VBApplicationErrorInfo.html)|
|Severity|`Error`|

RDCore removes the need for the `vbObjectError` constant by internally representing run-time errors and application errors as different error metadata types; see [**RD-VBAL §1.1.4** Core Diagnostics](rd-vbal.1.1.4.core-diagnostics.md).

---
> ⏮️ [**RD-VBAL §2.6.2** Semantic Compilation Errors](rd-vbal.2.6.2.semantic-compilation-errors.md) | ⏭️ [**RD-VBAL §2.6.4** Rubberduck Core Diagnostics](rd-vbal.2.6.4.rubberduck-core-diagnostics.md)
