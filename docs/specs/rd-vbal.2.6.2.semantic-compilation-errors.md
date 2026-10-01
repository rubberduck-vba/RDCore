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
|[`VBC09309`](../diagnostics/vbc09309.md)|Label not defined|a jump names a line label or line number the procedure does not define|
|[`VBC09319`](../diagnostics/vbc09319.md)|Duplicate label definition|a procedure defines the same line label or line number more than once|
|[`VBC09320`](../diagnostics/vbc09320.md)|Inconsistent property accessors|a property's Get/Let/Set sharing a name do not together describe one valid property|
|[`VBC09321`](../diagnostics/vbc09321.md)|Argument required for Property Let or Property Set|a Property Let or Property Set declares no parameters at all|
|[`VBC09322`](../diagnostics/vbc09322.md)|Event not defined|a RaiseEvent names an event the class module it is written in does not declare|
|[`VBC09323`](../diagnostics/vbc09323.md)|Wrong number of arguments or invalid argument for event|a RaiseEvent's arguments are not compatible with the parameter list of its event|
|[`VBC09324`](../diagnostics/vbc09324.md)|Invalid type for WithEvents variable|a WithEvents variable is not declared as a specific class that has an event, or is declared as the class of its own module|
|[`VBC09325`](../diagnostics/vbc09325.md)|Invalid event name|the name of an Event declaration contains an underscore|
|[`VBC09326`](../diagnostics/vbc09326.md)|Invalid event handler|a procedure named for a WithEvents variable and an event of its class is not a subroutine, or has a parameter list incompatible with the event's|

Instruction-list lowering reports `VBC09309` for a `Jump`/`JumpTable` label operand that does not resolve, and `VBC09319` for a repeated label definition; see [**RD-VBAL §3.5.3** Lowering Block Statements](rd-vbal.3.5.3.lowering-block-statements.md). `VBC09320` and `VBC09321` are raised by [ScopeTreeSymbolResolver](../api/RDCore.SDK.Model.Symbols.ScopeTreeSymbolResolver.html); see [**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md). `VBC09322` and `VBC09323` are raised by [StatementStaticSemanticsEvaluator](../api/RDCore.SDK.Semantics.Static.StatementStaticSemanticsEvaluator.html); see [**RD-VBAL §5.4.2.20** RaiseEvent Statement](rd-vbal.5.4.2.20.raiseevent-statement.md). `VBC09324`, `VBC09325` and `VBC09326`, and a repeated event name as `VBC09303`, are raised by [ClassModuleEventSemantics](../api/RDCore.SDK.Semantics.Static.ClassModuleEventSemantics.html), which checks what a class module declares about events; see [**RD-VBAL §5.2.4.3**](rd-vbal.5.2.4.class-module-declarations.md), [**§5.2.3.1.2**](rd-vbal.5.2.3.module-declarations.md) and [**§5.3.1.8**](rd-vbal.5.3.1.8.event-handler-declarations.md).

## Other Compilation Errors

This specification refers to the following semantic compilation errors on other pages.

|Code|`VBCompileErrorId`|Condition|See|
|---|---|---|---|
|`VBC09301`|`AmbiguousName`|A name resolves in more than one enclosing scope (members promoted from different modules or references). The reference must qualify the name.|[**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)|
|`VBC09302`|`VariableNotDefined`|An unresolved simple name. The static-semantics layer consumes the module's `ModuleDirectives` to decide whether such a name is a deferred `VBUnknownType` or this error.|[**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)|
|`VBC09303`|`DuplicateDeclaration`|A name is declared more than once within one module or procedure.|[**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md)|
|`VBC09304`|`InvalidUseOfObject`|`Set Widget = New Widget` or `Set Widget = Nothing`, for a predeclared class `Widget`.|[**RD-VBAL §3.1.1** Attributes](rd-vbal.3.1.1.attributes.md)|
|`VBC09311`|`UserDefinedTypeNotDefined`|An unknown type should raise this error; verbose diagnostic messages should help clarify its meaning.|[**RD-VBAL §2.4.2** Non-intrinsic Types](rd-vbal.2.4.2.non-intrinsic-types.md)|

An appropriate compile-time error should be issued for a duplicate declaration and for an ambiguous name. The symbol resolver reports the error kind; the caller, which knows where the reference is, builds the located diagnostic.

---
> ⏮️ [**RD-VBAL §2.6.1** Syntax Errors](rd-vbal.2.6.1.syntax-errors.md) | ⏭️ [**RD-VBAL §2.6.3** Runtime Errors](rd-vbal.2.6.3.runtime-errors.md)
