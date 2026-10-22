# 2.6.2 Semantic Compilation Errors

A **semantic compilation error** is raised by the static semantics layer while walking the *abstract syntax tree* (AST) with symbol information. Examples are a duplicate declaration, an undefined name, and a type mismatch in a constant expression.

|||
|---|---|
|Code family|`VBC`, range `VBC09300`–`VBC09999`|
|Title|_Compile error_|
|Raised by|the static semantics layer (abstract syntax tree)|
|Source metadata|[VBCompileErrorInfo](../api/RDCore.SDK.Model.Errors.VBCompileErrorInfo.html)|
|Severity|`Error`|
|Detail|the offending symbol / expression, on `Diagnostic.data`|

## Numeric Range

[VBCompileErrorId](../api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) reserves the range `[9300..]` for semantic compilation errors: errors issued from the static semantics analysis pass report a value greater than or equal to 9300. MS-VBAL does not distinguish a compile-time error raised in CST semantics from one raised in AST semantics; RDCore splits [syntax errors](rd-vbal.2.6.1.syntax-errors.md) and semantic compilation errors by numeric range only.

Compilation errors issued from a *language core extension* report a `VBCompileErrorId` value between 8000 and 9299; see [**RD-VBAL §1.1.4** Core Diagnostics](rd-vbal.1.1.4.core-diagnostics.md).

## Emission

Semantic compilation errors are emitted by the resolver and the static semantic pass.

[ICoreDiagnosticsFactory](../api/RDCore.SDK.Model.Diagnostics.ICoreDiagnosticsFactory.html)`.FromVBCompileError` is the projection for semantic compilation errors, the counterpart of `FromVBSyntaxError`, and [SemanticContextBuilder](../api/RDCore.SDK.Semantics.Builders.SemanticContextBuilder-2.html)`.AddDiagnosticOnError` uses it. No production code path calls `AddDiagnosticOnError`, so semantic compilation errors are not currently projected to LSP diagnostics.

> [!NOTE]
> **Not implemented.** The RDCore.Diagnostics handler for `rdcore/diagnostics/document` projects the syntax errors of the parse result only. Semantic compilation errors do not reach the `textDocument/diagnostic` pull through it; see [**RD-VBAL §2.6.5** Diagnostics Pipeline](rd-vbal.2.6.5.diagnostics-pipeline.md).

## Published Codes

|Code|Title|Condition|
|---|---|---|
|[`VBC09306`](../diagnostics/vbc09306.md)|Invalid ReDim|a `ReDim` re-dimensions a member of an object declared `As Object` (of a declared class, it is `VBC09316`)|
|[`VBC09307`](../diagnostics/vbc09307.md)|Expected array|`LBound` or `UBound` of a variable declared as a number, a `String`, a `Boolean` or a `Date`|
|[`VBC09309`](../diagnostics/vbc09309.md)|Label not defined|a jump names a line label or line number the procedure does not define|
|[`VBC09310`](../diagnostics/vbc09310.md)|Type mismatch|a value is of a type that cannot be let-coerced to the type it is used as|
|[`VBC09312`](../diagnostics/vbc09312.md)|Exit Do not within Do...Loop|an `Exit Do` statement is not lexically inside a `Do` loop|
|[`VBC09313`](../diagnostics/vbc09313.md)|Exit For not within For...Next|an `Exit For` statement is not lexically inside a `For` or `For Each` loop|
|[`VBC09314`](../diagnostics/vbc09314.md)|Exit Function not allowed in Sub or Property|an `Exit Function` statement is in a `Sub`, or in a `Property Let` or `Property Set` (it is accepted in a `Property Get`, as MS-VBA does)|
|[`VBC09315`](../diagnostics/vbc09315.md)|Exit Property not allowed in Sub or Function|an `Exit Property` statement is in a `Sub` or a `Function`|
|[`VBC09316`](../diagnostics/vbc09316.md)|Method or data member not found|a member is accessed on a type known not to have it|
|[`VBC09317`](../diagnostics/vbc09317.md)|Invalid use of Me|Me is written where there is no object it could be|
|[`VBC09318`](../diagnostics/vbc09318.md)|With expression outside With block|a leading-dot member has no enclosing With block|
|[`VBC09319`](../diagnostics/vbc09319.md)|Duplicate label definition|a procedure defines the same line label or line number more than once|
|[`VBC09320`](../diagnostics/vbc09320.md)|Inconsistent property accessors|a property's Get/Let/Set sharing a name do not together describe one valid property|
|[`VBC09321`](../diagnostics/vbc09321.md)|Argument required for Property Let or Property Set|a Property Let or Property Set declares no parameters at all|
|[`VBC09322`](../diagnostics/vbc09322.md)|Event not defined|a RaiseEvent names an event the class module it is written in does not declare|
|[`VBC09323`](../diagnostics/vbc09323.md)|Wrong number of arguments or invalid argument for event|a RaiseEvent's arguments are not compatible with the parameter list of its event|
|[`VBC09324`](../diagnostics/vbc09324.md)|Invalid type for WithEvents variable|a WithEvents variable is not declared as a specific class that has an event, or is declared as the class of its own module|
|[`VBC09325`](../diagnostics/vbc09325.md)|Invalid event name|the name of an Event declaration contains an underscore|
|[`VBC09326`](../diagnostics/vbc09326.md)|Invalid event handler|a procedure named for a WithEvents variable and an event of its class is not a subroutine, or has a parameter list incompatible with the event's|
|[`VBC09327`](../diagnostics/vbc09327.md)|ByVal argument not allowed here|an argument is written with ByVal in an argument list that is not that of an invocation of an external procedure|
|[`VBC09328`](../diagnostics/vbc09328.md)|Invalid Implements directive|an Implements directive names the class itself, a class another directive names, a class with an underscore in a public member, or an interface whose prefix begins another's|
|[`VBC09329`](../diagnostics/vbc09329.md)|Object module needs to implement all members of its interface|a public variable or method of an interface class has no implemented name declaration|
|[`VBC09330`](../diagnostics/vbc09330.md)|Invalid implemented member|an implemented name declaration is not the kind of declaration its member is, or its parameters or type are not equivalent|
|[`VBC09331`](../diagnostics/vbc09331.md)|Sub or Function not defined|a statement the language the code is written in does not have: a bare `Print` outside the platform's BASIC|
|[`VBC09332`](../diagnostics/vbc09332.md)|Exit Sub not allowed in Function or Property|an `Exit Sub` statement is in a `Function` or a property|
|[`VBC09333`](../diagnostics/vbc09333.md)|Variable required|an expression that is certainly not a variable (a literal, an operator's result, a constant, a function of the library) is the target of an assignment (`Let`, `Set`, `LSet`, `RSet`; but a `Let` may assign to a function of the library whose declared type is a class, `Object` or `Variant`), of a `Mid` statement or of a `ReDim` statement, the control variable of a `For` or `For Each` loop, or the variable a `Line Input #`, `Input #` or `Get` reads into; or a constant is an element of an `Erase` statement|
|[`VBC09334`](../diagnostics/vbc09334.md)|Access not valid for the file mode|the `Access` clause of an `Open` statement is not one its `For` mode allows|
|[`VBC09335`](../diagnostics/vbc09335.md)|Type-declaration character does not match declared data type|a name is written with the type-declaration character of a type other than the one it was declared as|
|[`VBC09336`](../diagnostics/vbc09336.md)|Private Enum and user defined types cannot be used as parameters or return types for public procedures, public data members, or fields of public user defined types|a public member is declared with a private `Enum` - a variable, a result, a parameter - or, in a class module, with a private user-defined type; as MS-VBA has it, not a field of a `Public Type`, and not a private type in a standard module|
|[`VBC09337`](../diagnostics/vbc09337.md)|Constants, fixed-length strings, arrays, user-defined types and Declare statements not allowed as Public members of object modules|a class module declares one of them as a public member, or a `Global` variable|
|[`VBC09338`](../diagnostics/vbc09338.md)|Cannot define a Public user-defined type within an object module|a class module declares a `Public Type`, or a `Type` with no modifier|
|[`VBC09339`](../diagnostics/vbc09339.md)|Argument not optional|an object whose default member takes an argument (a `Collection`) is where a value is expected: assigned to a variable of a value type, an operand, an argument of a parameter of a value type or of a library function that reads a value; see [**RD-VBAL §5.5.1.1**](rd-vbal.5.5.1.1.static-semantics.md)|
|[`VBC09340`](../diagnostics/vbc09340.md)|Expected Function or variable|a `Sub` where a value is expected|
|[`VBC09341`](../diagnostics/vbc09341.md)|Can't assign to read-only property|a `Let` of a property that has a `Property Get` and no `Property Let`|
|[`VBC09342`](../diagnostics/vbc09342.md)|Wrong number of arguments or invalid property assignment|an argument no parameter takes|
|[`VBC09343`](../diagnostics/vbc09343.md)|LSet allowed only on strings and user-defined types|the target of an `LSet` statement|
|[`VBC09344`](../diagnostics/vbc09344.md)|RSet allowed only on strings|the target of an `RSet` statement|
|[`VBC09345`](../diagnostics/vbc09345.md)|Invalid use of property|a `Set` of a property that has a `Property Get` and no `Property Set`|
|[`VBC09346`](../diagnostics/vbc09346.md)|Assignment to constant not permitted|a `Let` or a `Set` of a constant|
|[`VBC09347`](../diagnostics/vbc09347.md)|Object required|a `Set` of a variable that holds no object|
|[`VBC09348`](../diagnostics/vbc09348.md)|Named argument already specified|a parameter given two arguments|
|[`VBC09349`](../diagnostics/vbc09349.md)|Named argument not found|a named argument the procedure does not declare|

Instruction-list lowering reports `VBC09309` for a `Jump`/`JumpTable` label operand that does not resolve, and `VBC09319` for a repeated label definition; see [**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md). `VBC09320` and `VBC09321` are raised by [ScopeTreeSymbolResolver](../api/RDCore.SDK.Model.Symbols.ScopeTreeSymbolResolver.html); see [**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md). `VBC09322` and `VBC09323` are raised by [StatementStaticSemanticsEvaluator](../api/RDCore.SDK.Semantics.Static.StatementStaticSemanticsEvaluator.html); see [**RD-VBAL §5.4.2.20** RaiseEvent Statement](rd-vbal.5.4.2.20.raiseevent-statement.md). `VBC09324`, `VBC09325` and `VBC09326`, and a repeated event name as `VBC09303`, are raised by [ClassModuleEventSemantics](../api/RDCore.SDK.Semantics.Static.ClassModuleEventSemantics.html), which checks what a class module declares about events; see [**RD-VBAL §5.2.4.3**](rd-vbal.5.2.4.class-module-declarations.md), [**§5.2.3.1.2**](rd-vbal.5.2.3.module-declarations.md) and [**§5.3.1.8**](rd-vbal.5.3.1.8.event-handler-declarations.md). `VBC09327` is raised by [ExpressionStaticSemanticsEvaluator](../api/RDCore.SDK.Semantics.Static.ExpressionStaticSemanticsEvaluator.html); see [**RD-VBAL §5.6.13.1** Argument Lists](rd-vbal.5.6.13.index-expressions.md). `VBC09328`, `VBC09329` and `VBC09330`, and `VBC09311` for an interface class that does not exist, are raised by [ImplementsSemantics](../api/RDCore.SDK.Semantics.Static.ImplementsSemantics.html); see [**RD-VBAL §5.2.4.2**](rd-vbal.5.2.4.class-module-declarations.md) and [**§5.3.1.9**](rd-vbal.5.3.1.9.implemented-name-declarations.md).

## Other Compilation Errors

This specification refers to the following semantic compilation errors on other pages.

|Code|`VBCompileErrorId`|Condition|See|
|---|---|---|---|
|`VBC09301`|`AmbiguousName`|Within one tier of the project or global scope, a name resolves to more than one symbol of the highest rank: members of two modules of the workspace (of a class module, only a public Enum or user-defined type and that Enum's members), or of two modules of one library, the `VBA` library included. The symbols of two libraries never share a rank. The reference must qualify the name. A declared type whose name ties this way is `VBC09311` instead.|[**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)|
|`VBC09302`|`VariableNotDefined`|An unresolved simple name. The static-semantics layer consumes the module's `ModuleDirectives` to decide whether such a name is a deferred `VBUnknownType` or this error.|[**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)|
|`VBC09303`|`DuplicateDeclaration`|A name is declared more than once within one module or procedure.|[**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)|
|`VBC09304`|`InvalidUseOfObject`|`Set Widget = New Widget` or `Set Widget = Nothing`, for a predeclared class `Widget`.|[**RD-VBAL §3.1.1** Attributes](rd-vbal.3.1.1.attributes.md)|
|`VBC09305`|`InvalidParamArrayUse`|A `ParamArray` parameter, bare or parenthesized, is the argument of a `ByRef` parameter of a procedure of the workspace's own.|[**RD-VBAL §5.3.1.11** Procedure Invocation Argument Processing](rd-vbal.5.3.1.11.procedure-invocation-argument-processing.md)|
|`VBC09311`|`UserDefinedTypeNotDefined`|An unknown type should raise this error, and so does a declared type that names a procedural module; verbose diagnostic messages should help clarify its meaning.|[**RD-VBAL §2.4.2** Non-intrinsic Types](rd-vbal.2.4.2.non-intrinsic-types.md), [**RD-VBAL §5.6.16.7** Type Expressions](rd-vbal.5.6.16.constrained-expressions.md#56167-type-expressions)|

An appropriate compile-time error should be issued for a duplicate declaration and for an ambiguous name. The symbol resolver reports the error kind; the caller, which knows where the reference is, builds the located diagnostic.

---
> ⏮️ [**RD-VBAL §2.6.1** Syntax Errors](rd-vbal.2.6.1.syntax-errors.md) | ⏭️ [**RD-VBAL §2.6.3** Runtime Errors](rd-vbal.2.6.3.runtime-errors.md)
