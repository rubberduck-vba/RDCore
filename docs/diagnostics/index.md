# RDCore Diagnostics

Every problem the RDCore platform reports carries a stable **code** and, through the LSP
`codeDescription` field, a link to its page here. See
[**RD-VBAL §2.6** Diagnostics](../specs/rd-vbal.2.6.diagnostics.md) for the definition of each code
family, and [**RD-VBAL §2.6.5** Diagnostics Pipeline](../specs/rd-vbal.2.6.5.diagnostics-pipeline.md)
for the pull pipeline.

|Family|Prefix|Raised by|
|---|---|---|
|[Syntax errors](../specs/rd-vbal.2.6.1.syntax-errors.md)|`VBC` (`VBC00001`–`VBC00999`)|the parser, walking the concrete syntax tree|
|[Semantic compilation errors](../specs/rd-vbal.2.6.2.semantic-compilation-errors.md)|`VBC` (`VBC09300`+)|the static semantics layer, walking the abstract syntax tree|
|[Runtime errors](../specs/rd-vbal.2.6.3.runtime-errors.md)|`VBR` / `VBA`|the runtime semantics layer (`VBR`) / a workspace `Err.Raise` (`VBA`)|
|[Rubberduck Core diagnostics](../specs/rd-vbal.2.6.4.rubberduck-core-diagnostics.md)|`RDC`|the `RDCore.Diagnostics` analyzers|

## Stability

A code has a page here **as soon as the platform can emit it**.

Once published, a code is **not renumbered and not retired**: a workspace built against an older
release must still resolve its diagnostic links. The *content* of a page may evolve as the ideal set
of codes is narrowed down; the code and its abstract meaning do not.

Each page describes the condition in the abstract. The specifics of a particular occurrence (which
token, which literal, which type) are carried in the diagnostic's verbose detail, not in the code.

## Published codes

### Syntax errors

|Code|Condition|
|---|---|
|[VBC00001](vbc00001.md)|Syntax error — a token the grammar cannot place|
|[VBC00042](vbc00042.md)|Numeric literal overflow — a literal outside the range of its type|
|[VBC00043](vbc00043.md)|Next without For — a `Next` statement ends no `For` loop|
|[VBC00044](vbc00044.md)|For without Next — a `For` loop is not ended before its procedure is|
|[VBC00045](vbc00045.md)|Loop without Do — a `Loop` statement ends no `Do` loop|
|[VBC00046](vbc00046.md)|Do without Loop — a `Do` loop is not ended before its procedure is|
|[VBC00047](vbc00047.md)|End If without block If — an `End If` statement ends no block `If`|
|[VBC00048](vbc00048.md)|Block If without End If — a block `If` is not ended before its procedure is|
|[VBC00049](vbc00049.md)|Else without If — an `Else` statement belongs to no block `If`|
|[VBC00050](vbc00050.md)|Case without Select Case — a `Case` clause belongs to no `Select Case` statement|
|[VBC00051](vbc00051.md)|Select Case without End Select — a `Select Case` statement is not ended before its procedure is|
|[VBC00052](vbc00052.md)|End With without With — an `End With` statement ends no `With` block|
|[VBC00053](vbc00053.md)|Expected End With — a `With` block is not ended before its procedure is|
|[VBC00054](vbc00054.md)|Invalid outside procedure — a statement that runs is written in the declarations section of a module|
|[VBC00055](vbc00055.md)|Only comments may appear after End Sub, End Function, or End Property — a declaration or a statement is written after the first procedure of a module|

### Semantic compilation errors

|Code|Condition|
|---|---|
|[VBC09301](vbc09301.md)|Ambiguous name detected — an identifier names more than one declaration at one level of scope|
|[VBC09302](vbc09302.md)|Variable not defined — a name is used that no declaration defines, under Option Explicit|
|[VBC09303](vbc09303.md)|Duplicate declaration in current scope — a name is declared more than once at one level of scope|
|[VBC09304](vbc09304.md)|Invalid use of object — an object is used where a value of another kind is required|
|[VBC09305](vbc09305.md)|Invalid ParamArray use — a ParamArray passed to a ByRef parameter of your own code|
|[VBC09306](vbc09306.md)|Invalid ReDim — a ReDim re-dimensions a member of an object declared As Object|
|[VBC09307](vbc09307.md)|Expected array — LBound or UBound is asked the bounds of a variable that can never hold an array|
|[VBC09309](vbc09309.md)|Label not defined — a jump names a line label or line number the procedure does not define|
|[VBC09310](vbc09310.md)|Type mismatch — a value is of a type that cannot be let-coerced to the type it is used as|
|[VBC09311](vbc09311.md)|User-defined type not defined — a declared type is a name that does not resolve to a type|
|[VBC09312](vbc09312.md)|Exit Do not within Do...Loop — an Exit Do that is not inside a Do loop|
|[VBC09313](vbc09313.md)|Exit For not within For...Next — an Exit For that is not inside a For or For Each loop|
|[VBC09314](vbc09314.md)|Exit Function not allowed in Sub or Property — an Exit Function in a Sub, or a Property Let or Set|
|[VBC09315](vbc09315.md)|Exit Property not allowed in Sub or Function — an Exit Property in a Sub or a Function|
|[VBC09316](vbc09316.md)|Method or data member not found — a member is accessed on a type known not to have it|
|[VBC09317](vbc09317.md)|Invalid use of Me — Me is written where there is no object it could be|
|[VBC09318](vbc09318.md)|With expression outside With block — a leading-dot member has no enclosing With block|
|[VBC09319](vbc09319.md)|Duplicate label definition — a procedure defines the same line label or line number more than once|
|[VBC09320](vbc09320.md)|Inconsistent property accessors — a property's Get/Let/Set do not together describe one valid property|
|[VBC09321](vbc09321.md)|Argument required for Property Let or Property Set — a Let/Set declares no parameters at all|
|[VBC09322](vbc09322.md)|Event not defined — a RaiseEvent names an event the class module does not declare|
|[VBC09323](vbc09323.md)|Wrong number of arguments or invalid argument for event — a RaiseEvent's arguments do not match the event's parameters|
|[VBC09324](vbc09324.md)|Invalid type for WithEvents variable — not a specific class with events, or the class of its own module|
|[VBC09325](vbc09325.md)|Invalid event name — an Event declaration whose name has an underscore|
|[VBC09326](vbc09326.md)|Invalid event handler — a procedure named for an event is not a subroutine with a compatible parameter list|
|[VBC09327](vbc09327.md)|ByVal argument not allowed here — no longer raised: a ByVal argument is the error MS-VBA reports (RD-VBAL §5.6.13.1)|
|[VBC09328](vbc09328.md)|Invalid Implements directive — the class itself, a repeat, an underscore in a public member, or overlapping interface prefixes|
|[VBC09329](vbc09329.md)|Object module needs to implement all members of its interface — a public member of the interface has no implemented name declaration|
|[VBC09330](vbc09330.md)|Invalid implemented member — an implemented name declaration of another kind, parameters or type than its member|
|[VBC09331](vbc09331.md)|Sub or Function not defined — a statement or a call the language the code is written in does not have|
|[VBC09332](vbc09332.md)|Exit Sub not allowed in Function or Property — an Exit Sub in a Function or a property|
|[VBC09333](vbc09333.md)|Variable required — an expression that is not a variable is where a statement requires one|
|[VBC09334](vbc09334.md)|Access not valid for the file mode — the Access clause of an Open is not one its For mode allows|
|[VBC09335](vbc09335.md)|Type-declaration character does not match declared data type — a name is written with the character of a type other than the one it was declared as|
|[VBC09336](vbc09336.md)|Private Enum and user defined types cannot be used in public members — a public member is declared with a private Enum, or in a class with a private type|
|[VBC09337](vbc09337.md)|Not allowed as a Public member of an object module — a class exposes a constant, fixed-length string, array, user-defined type or Declare|
|[VBC09338](vbc09338.md)|Cannot define a Public user-defined type within an object module — a class module declares a public Type|
|[VBC09339](vbc09339.md)|Argument not optional — an object whose default member takes an argument is where a value is expected|
|[VBC09340](vbc09340.md)|Expected Function or variable — a subroutine is used where a value is expected|
|[VBC09341](vbc09341.md)|Can't assign to read-only property — a property that can only be read is assigned with `Let`|
|[VBC09342](vbc09342.md)|Wrong number of arguments or invalid property assignment — a procedure is called with arguments it does not declare|
|[VBC09343](vbc09343.md)|LSet allowed only on strings and user-defined types — the target of an `LSet` statement is neither a `String` nor a user-defined type|
|[VBC09344](vbc09344.md)|RSet allowed only on strings — the target of an `RSet` statement is not a `String`|
|[VBC09345](vbc09345.md)|Invalid use of property — a property that can only be read is assigned with `Set`|
|[VBC09346](vbc09346.md)|Assignment to constant not permitted — a constant is assigned|
|[VBC09347](vbc09347.md)|Object required — a `Set` statement assigns to a variable that cannot hold an object|
|[VBC09348](vbc09348.md)|Named argument already specified — a call gives the same parameter two arguments|
|[VBC09349](vbc09349.md)|Named argument not found — a call names a parameter the procedure does not declare|
|[VBC09350](vbc09350.md)|Constant expression required — the value of a constant reads something that has no value until the code runs|
|[VBC09351](vbc09351.md)|Invalid data type for constant — the value of a constant is an object|
|[VBC09352](vbc09352.md)|Overflow — the value of a constant does not fit its type|
|[VBC09353](vbc09353.md)|Division by zero — the value of a constant divides by zero|
|[VBC09354](vbc09354.md)|Invalid qualifier — a member is accessed on a value that has no members|
|[VBC09355](vbc09355.md)|Function call on left-hand side of assignment must return Variant or Object — a call of a function is assigned|
|[VBC09356](vbc09356.md)|Wrong number of dimensions — an element of an array is selected by the wrong number of subscripts|
|[VBC09357](vbc09357.md)|Array already dimensioned — an array declared with fixed dimensions is given others|
|[VBC09358](vbc09358.md)|For control variable already in use — a loop is controlled by the variable of a loop it is nested in|
|[VBC09359](vbc09359.md)|Invalid Next control variable reference — a `Next` statement names a variable that is not the control variable of its loop|
|[VBC09360](vbc09360.md)|Can't assign to array — an array declared with fixed dimensions is assigned an array|
|[VBC09361](vbc09361.md)|ByRef argument type mismatch — a variable is passed to a `ByRef` parameter of another type|
|[VBC09362](vbc09362.md)|Type mismatch: array or user-defined type expected — an array is passed to a parameter declared as an array of another type|
|[VBC09363](vbc09363.md)|Expected variable or procedure, not module — the name of a standard module is used by itself, as a value or a procedure|
|[VBC09364](vbc09364.md)|Expected Sub, Function, or Property — a call statement names a variable or a constant of the procedure, not a procedure|
|[VBC09365](vbc09365.md)|Range has no values — an array is declared with an upper bound less than its lower bound|
|[VBC09366](vbc09366.md)|A module is not a valid type — a declaration is typed with the name of a standard module|
|[VBC09367](vbc09367.md)|Invalid use of AddressOf operator — an `AddressOf` expression names a method of a class module, or of an object|
|[VBC09368](vbc09368.md)|Invalid use of New keyword — a variable is declared `As New` of a type that is no class, of a library's class that cannot be created, or `WithEvents`|
|[VBC09369](vbc09369.md)|Can't change data types of array elements — a `ReDim` gives an array elements of another type than it is declared with|
|[VBC09370](vbc09370.md)|Expected procedure, not variable — a call statement names a variable or a constant of a module, not a procedure|
|[VBC09371](vbc09371.md)|Array argument must be ByRef — an argument of an array parameter is written `ByVal`|
|[VBC09372](vbc09372.md)|Member already exists in an object module from which this object module derives — the code of a form declares something with the name of one of its controls|
|[VBC09373](vbc09373.md)|Automation type not supported in Visual Basic — a type is written with the name of a library's module, `Strings`|
|[VBC09374](vbc09374.md)|Expected user-defined type, not project — a type is written with the name of a project or a library, `VBA`|
|[VBC09375](vbc09375.md)|Invalid length for fixed-length string — a `String * n` is declared with a length under 1 or past 65526|
|[VBC09376](vbc09376.md)|Expected variable or procedure, not project — a project, `VBA`, is written where a value is|
|[VBC09377](vbc09377.md)|Expected variable or procedure, not enum type — an enumeration, `Color`, is written where a value is|
|[VBC09378](vbc09378.md)|With object must be user-defined type, Object, or Variant — a `With` block is about a value that has no members|
|[VBC09379](vbc09379.md)|For Each may only iterate over a collection object or an array — a `For Each` loop iterates a value that has no elements|

### Runtime errors

|Code|Condition|
|---|---|
|[VBR-0001](vbr-0001.md)|Application-defined or object-defined error — an error is raised with a number that has no message of its own, or by a member that cannot answer|
|[VBR00003](vbr00003.md)|Return without GoSub — a `Return` runs with no `GoSub` to return to|
|[VBR00005](vbr00005.md)|Invalid procedure call or argument — an argument is outside what a procedure accepts|
|[VBR00006](vbr00006.md)|Overflow — a result does not fit the type it is computed in or assigned to|
|[VBR00007](vbr00007.md)|Out of memory — the session's memory cannot hold an allocation|
|[VBR00009](vbr00009.md)|Subscript out of range — an index is outside the bounds of an array, or a collection has no such item|
|[VBR00010](vbr00010.md)|This array is fixed or temporarily locked — an array that is fixed-size or locked is resized or erased|
|[VBR00011](vbr00011.md)|Division by zero — a divisor is zero|
|[VBR00013](vbr00013.md)|Type mismatch — a value cannot be coerced to the type that is required|
|[VBR00014](vbr00014.md)|Out of string space — a string would be longer than the platform allows|
|[VBR00016](vbr00016.md)|Expression too complex — an expression is nested too deeply to evaluate|
|[VBR00017](vbr00017.md)|Can't perform requested operation — an operation is not possible in the state the session is in|
|[VBR00018](vbr00018.md)|User interrupt occurred — the program is interrupted|
|[VBR00020](vbr00020.md)|Resume without error — a `Resume` runs with no error to resume from|
|[VBR00028](vbr00028.md)|Out of stack space — the call stack is full|
|[VBR00035](vbr00035.md)|Sub or Function not defined — a call names a procedure that is not defined|
|[VBR00047](vbr00047.md)|Too many DLL application clients — a `Declare`d library has more clients than it supports|
|[VBR00048](vbr00048.md)|Error in loading DLL — a library named by a `Declare` cannot be loaded|
|[VBR00049](vbr00049.md)|Bad DLL calling convention — a `Declare`d procedure is called with a convention it does not have|
|[VBR00051](vbr00051.md)|Internal error — the interpreter has no implementation for what the program does. This is a gap in the platform, not an error of the program|
|[VBR00052](vbr00052.md)|Bad file name or number — a file number is not valid, or is not open|
|[VBR00053](vbr00053.md)|File not found — a file that is to be read or deleted does not exist|
|[VBR00054](vbr00054.md)|Bad file mode — an operation is not valid for the mode a file was opened in|
|[VBR00055](vbr00055.md)|File already open — a file is already open, or the file number is in use|
|[VBR00057](vbr00057.md)|Device I/O error — the device reported an input or output failure|
|[VBR00058](vbr00058.md)|File already exists — a file that is to be created already exists|
|[VBR00059](vbr00059.md)|Bad record length — a record is not the length the file was opened with|
|[VBR00061](vbr00061.md)|Disk full — there is no room left on the disk|
|[VBR00062](vbr00062.md)|Input past end of file — a read goes past the end of the file|
|[VBR00063](vbr00063.md)|Bad record number — a record number is not valid|
|[VBR00067](vbr00067.md)|Too many files — there are no file numbers left|
|[VBR00068](vbr00068.md)|Device unavailable — the device is not available|
|[VBR00070](vbr00070.md)|Permission denied — access to a file or record is refused|
|[VBR00071](vbr00071.md)|Disk not ready — the drive is not ready|
|[VBR00074](vbr00074.md)|Can't rename with different drive — a file is renamed onto a different drive|
|[VBR00075](vbr00075.md)|Path/File access error — a path or file cannot be accessed as asked|
|[VBR00076](vbr00076.md)|Path not found — a path does not exist|
|[VBR00091](vbr00091.md)|Object variable or With block variable not set — an object is used that is not set to anything|
|[VBR00092](vbr00092.md)|For loop not initialized — a loop is run from the middle, or a `For Each` array was never dimensioned|
|[VBR00093](vbr00093.md)|Invalid pattern string — a `Like` pattern is not valid|
|[VBR00094](vbr00094.md)|Invalid use of Null — `Null` is used where it is not allowed.|
|[VBR00096](vbr00096.md)|Unable to sink events of object because the object is already firing events to the maximum number of event receivers that it supports — an object fires events to more receivers than it supports|
|[VBR00097](vbr00097.md)|Can not call friend function on object which is not an instance of defining class — a `Friend` member is called on an object that is not an instance of the class that defines it|
|[VBR00098](vbr00098.md)|A property or method call cannot include a reference to a private object, either as an argument or as a return value — a public call includes a reference to a private object|
|[VBR00321](vbr00321.md)|Invalid file format — a file's format is not valid|
|[VBR00322](vbr00322.md)|Can't create necessary temporary file — a temporary file the operation needs cannot be created|
|[VBR00325](vbr00325.md)|Invalid format in resource file — a resource file's format is not valid|
|[VBR00361](vbr00361.md)|Can't load or unload this object — `Load` or `Unload` is given an object that is no form|
|[VBR00380](vbr00380.md)|Invalid property value — a property is given a value it does not accept|
|[VBR00381](vbr00381.md)|Invalid property array index — a property array index is not valid|
|[VBR00382](vbr00382.md)|Set not supported at runtime — a property cannot be set at run time|
|[VBR00383](vbr00383.md)|Set not supported (read-only property) — a property is read-only|
|[VBR00385](vbr00385.md)|Need property array index — a property array needs an index|
|[VBR00387](vbr00387.md)|Set not permitted — setting this property is not permitted|
|[VBR00393](vbr00393.md)|Get not supported at runtime — a property cannot be read at run time|
|[VBR00394](vbr00394.md)|Get not supported (write-only property) — a property is write-only|
|[VBR00422](vbr00422.md)|Property not found — a property is not found|
|[VBR00423](vbr00423.md)|Property or method not found — a property or method is not found|
|[VBR00424](vbr00424.md)|Object required — an object is required where a value is given|
|[VBR00429](vbr00429.md)|ActiveX component can't create object — an ActiveX component cannot create the object|
|[VBR00430](vbr00430.md)|Class does not support Automation or does not support expected interface — a class does not support Automation or the interface expected|
|[VBR00432](vbr00432.md)|File name or class name not found during Automation operation — a file or class name is not found during an Automation operation|
|[VBR00438](vbr00438.md)|Object doesn't support this property or method — an object does not have the property or method|
|[VBR00440](vbr00440.md)|Automation error — an Automation operation failed|
|[VBR00442](vbr00442.md)|Connection to type library or object library for remote process has been lost. Press OK for dialog to remove reference. — the connection to a type library or object library is lost|
|[VBR00443](vbr00443.md)|Automation object does not have a default value — an Automation object has no default value|
|[VBR00444](vbr00444.md)|Method not applicable in this context — a form's `Controls.Remove` is given a control its designer declared|
|[VBR00445](vbr00445.md)|Object doesn't support this action — an object does not support the action|
|[VBR00446](vbr00446.md)|Object doesn't support named arguments — an object does not support named arguments|
|[VBR00447](vbr00447.md)|Object doesn't support current locale setting — an object does not support the current locale setting|
|[VBR00448](vbr00448.md)|Named argument not found — a named argument is not found|
|[VBR00449](vbr00449.md)|Argument not optional — a required argument is not given|
|[VBR00450](vbr00450.md)|Wrong number of arguments or invalid property assignment — the number of arguments is wrong, or a property assignment is invalid|
|[VBR00451](vbr00451.md)|Property let procedure not defined and property get procedure did not return an object — a `Property Let` is not defined and the `Property Get` did not return an object|
|[VBR00452](vbr00452.md)|Invalid ordinal — an ordinal is not valid|
|[VBR00453](vbr00453.md)|Specified DLL function not found — a function named by a `Declare` is not found in the library|
|[VBR00454](vbr00454.md)|Code resource not found — a code resource is not found|
|[VBR00455](vbr00455.md)|Code resource lock error — a code resource cannot be locked|
|[VBR00457](vbr00457.md)|This key is already associated with an element of this collection — a key is already used by an element of the collection|
|[VBR00458](vbr00458.md)|Variable uses an Automation type not supported in Visual Basic — a variable uses an Automation type that Visual Basic does not support|
|[VBR00459](vbr00459.md)|Object or class does not support the set of events. — an object or class does not support the set of events|
|[VBR00460](vbr00460.md)|Invalid clipboard format — a clipboard format is not valid|
|[VBR00461](vbr00461.md)|Method or data member not found — a method or data member is not found|
|[VBR00462](vbr00462.md)|The remote machine does not exist or is unavailable — the remote server machine does not exist or is unavailable|
|[VBR00463](vbr00463.md)|Class not registered on local machine — a class is not registered on the local machine|
|[VBR00481](vbr00481.md)|Invalid picture — a picture is not valid|
|[VBR00482](vbr00482.md)|Printer error — a printer error occurred|
|[VBR00735](vbr00735.md)|Can't save file to TEMP — a file cannot be saved to the temporary directory|
|[VBR00744](vbr00744.md)|Search text not found — the text searched for is not found|
|[VBR00746](vbr00746.md)|Replacements too long — the replacement text is too long|

### Rubberduck Core diagnostics

|Code|Condition|
|---|---|
|[RDC00101](rdc00101.md)|Implicit declarations enabled — a module that does not state Option Explicit|
|[RDC00102](rdc00102.md)|Implicit non-default array base — a module that states Option Base 1|
|[RDC00103](rdc00103.md)|Implicit type declarations enabled — a Def&lt;Type&gt; directive|
|[RDC00104](rdc00104.md)|Implicit ByRef modifier — a parameter that states neither ByRef nor ByVal|
|[RDC00105](rdc00105.md)|Implicit Public member — a member that states no access modifier|
|[RDC00106](rdc00106.md)|Implicit Variant declaration — a variable or parameter declared without a type|
|[RDC00107](rdc00107.md)|Implicit Variant return type — a function declared without a return type|
|[RDC00112](rdc00112.md)|Implicit narrowing conversion — a value converted implicitly into a type that holds less of it|
|[RDC00114](rdc00114.md)|Implicit array declaration — a `ReDim` that declares the array it re-dimensions|
|[RDC00201](rdc00201.md)|Integer data type declaration — an As Integer clause|
|[RDC00202](rdc00202.md)|Module-scope Dim declaration — a module-level variable declared with Dim|
|[RDC00203](rdc00203.md)|Multiline parameter declaration — one parameter declared over several lines|
|[RDC00204](rdc00204.md)|Multiple declarations — a statement that declares several names|
|[RDC00205](rdc00205.md)|Misleading ByRef parameter — ByRef on the value parameter of a property Let or Set|
|[RDC00206](rdc00206.md)|Not all code paths return a value — a function or property getter with a code path that does not assign its return value|
|[RDC00302](rdc00302.md)|Obsolete Call statement — a call statement written with the Call keyword|
|[RDC00303](rdc00303.md)|Obsolete comment syntax — a comment written with the Rem keyword|
|[RDC00304](rdc00304.md)|Obsolete error syntax — an Error statement|
|[RDC00305](rdc00305.md)|Obsolete Global modifier — a declaration with the Global modifier|
|[RDC00306](rdc00306.md)|Obsolete Let statement — an assignment written with the Let keyword|
|[RDC00307](rdc00307.md)|Obsolete type hint — a name written with a type-declaration character|
|[RDC00308](rdc00308.md)|Obsolete While…Wend — a loop written While…Wend|
|[RDC00309](rdc00309.md)|Obsolete On Local Error statement — an error handler set with On Local Error|
|[RDC00310](rdc00310.md)|Redundant ByVal argument — an argument written `ByVal` for a parameter that is `ByVal` already|
|[RDC00311](rdc00311.md)|Early binding opportunity — `CreateObject` of a class that a referenced library declares|
|[RDC00405](rdc00405.md)|Implementations should be private — a Public implementation of an interface member or handler of an event|
|[RDC00407](rdc00407.md)|Unmatched event handler — a handler of a form control's event that does not match the event, and that nothing calls|
|[RDC01001](rdc01001.md)|Use meaningful identifier names — a name too short, ending with a digit, or without a vowel|
|[RDC01002](rdc01002.md)|Hungarian notation — a name that begins with a prefix that states its type|
|[RDC11005](rdc11005.md)|Bitwise operator — a logical operator given numbers, which works on their bits|
|[RDC11006](rdc11006.md)|Ambiguous concatenation — two String values joined with +|
|[RDC11011](rdc11011.md)|Variant number compared with a Variant String — the number is less, whatever the values|
|[RDC11012](rdc11012.md)|Set of a call that always fails — a function whose default member has no Property Set, as the target of a Set|
|[RDC11013](rdc11013.md)|Variant compared with a String as text — MS-VBA compares their text, where MS-VBAL specifies a type mismatch|
|[RDC11020](rdc11020.md)|Variant String that is no number, compared with a number — MS-VBA ranks the String above it, where MS-VBAL specifies a type mismatch|

---
> ⏭️ [**VBC00001** Syntax error](vbc00001.md)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](../specs/rd-vbal.md) | [SDK](/RDCore/api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)
