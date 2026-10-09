# 2.6.4 Rubberduck Core Diagnostics

**Rubberduck Core diagnostics** are the analyzer findings issued by the `RDCore.Diagnostics` analyzers. They cover implicit declarations, obsolete syntax, misleading constructs, every inspection that the legacy Rubberduck add-in shipped, and further inspections beyond those.

|||
|---|---|
|Code family|`RDC`, defined by [RDCoreDiagnosticId](../api/RDCore.SDK.Model.Diagnostics.RDCoreDiagnosticId.html); the enum value is the code|
|Title|per finding|
|Raised by|the `RDCore.Diagnostics` analyzers|
|Severity|spans `Hint` through `Error`, per finding|

Any other *language core diagnostics* issued from RDCore.Diagnostics are to be documented under an `RDC00000` code; see [**RD-VBAL §1.1.4** Core Diagnostics](rd-vbal.1.1.4.core-diagnostics.md).

## Published codes

An analyzer is a pure function of facts (`IModuleAnalyzer`): the environment host runs the semantic analysis pass and says what
is true of the code, and an analyzer decides what is worth saying
([**RD-VBAL §5.0.3** Semantic Analysis](rd-vbal.5.0.semantics.md)). An analyzer that is not given the fact it reads has nothing to say.

What the code *means* is a fact the host vouches for. How it is *written* - a keyword that is there or is not, where a name is - is a fact of the syntax tree, which
the analyzer is given along with the model: those analyzers read the tree, and a module that did not parse has nothing for them to say.

The severity of a finding is a choice of the analyzer ([**RD-VBAL §2.6** Diagnostics](rd-vbal.2.6.diagnostics.md)): a **Hint** states a fact, an **Information**
(a *suggestion*) is a finding that a code action could fix, and a **Warning** is serious enough to break a build that treats warnings as errors.

|Code|Title|Severity|Reads|
|---|---|---|---|
|[`RDC00101`](../diagnostics/rdc00101.md)|Implicit declarations enabled|Warning|`ModuleSemanticModel.OptionExplicit`|
|[`RDC00102`](../diagnostics/rdc00102.md)|Implicit non-default array base|Hint|the syntax tree: an `Option Base 1` directive|
|[`RDC00103`](../diagnostics/rdc00103.md)|Implicit type declarations enabled|Hint|the syntax tree: a `Def<Type>` directive|
|[`RDC00104`](../diagnostics/rdc00104.md)|Implicit ByRef modifier|Information|the syntax tree: `ParameterDeclarationNode.ParameterKind`|
|[`RDC00105`](../diagnostics/rdc00105.md)|Implicit Public member|Information|the syntax tree: `MemberDeclarationNode.AccessModifier`|
|[`RDC00106`](../diagnostics/rdc00106.md)|Implicit Variant declaration|Information|the syntax tree: a declaration with no type, and the `Def<Type>` directives|
|[`RDC00107`](../diagnostics/rdc00107.md)|Implicit Variant return type|Information|the syntax tree: a function with no type, and the `Def<Type>` directives|
|[`RDC00201`](../diagnostics/rdc00201.md)|Integer data type declaration|Information|the syntax tree: `AsTypeExpressionNode`|
|[`RDC00202`](../diagnostics/rdc00202.md)|Module-scope Dim declaration|Information|the syntax tree: a module-level variable with no access modifier|
|[`RDC00203`](../diagnostics/rdc00203.md)|Multiline parameter declaration|Information|the syntax tree: the range of a `ParameterDeclarationNode`|
|[`RDC00204`](../diagnostics/rdc00204.md)|Multiple declarations|Information|the syntax tree: the declarations one statement makes|
|[`RDC00205`](../diagnostics/rdc00205.md)|Misleading ByRef parameter|Information|the syntax tree: `ParameterDeclarationNode.IsByRefIgnored`|
|[`RDC00206`](../diagnostics/rdc00206.md)|Not all code paths return a value|Warning|`ProcedureSemanticModel.ReturnValue`, from the [`ControlFlowGraph`](../api/RDCore.SDK.Semantics.Flow.ControlFlowGraph.html) of the procedure|
|[`RDC00302`](../diagnostics/rdc00302.md)|Obsolete Call statement|Hint|`ValueExpressionSemanticFlags.ExplicitCallKeyword`|
|[`RDC00303`](../diagnostics/rdc00303.md)|Obsolete comment syntax|Information|`ModuleSemanticModel.Language` (`SupportedLanguage.UsesClassicBasicSyntax`), and the comments of the syntax tree|
|[`RDC00304`](../diagnostics/rdc00304.md)|Obsolete error syntax|Information|`ModuleSemanticModel.Language`, and the syntax tree: an `Error` statement|
|[`RDC00305`](../diagnostics/rdc00305.md)|Obsolete Global modifier|Information|the syntax tree: `AccessModifier.Global`|
|[`RDC00306`](../diagnostics/rdc00306.md)|Obsolete Let statement|Information|the syntax tree: `AssignmentKind.ExplicitLet`|
|[`RDC00307`](../diagnostics/rdc00307.md)|Obsolete type hint|Information|the syntax tree: the type hints of declarations; `ExpressionFact.Binding` of the references|
|[`RDC00308`](../diagnostics/rdc00308.md)|Obsolete While…Wend|Information|the syntax tree: a `WhileWendStatementNode`|
|[`RDC00309`](../diagnostics/rdc00309.md)|Obsolete On Local Error statement|Information|the syntax tree: `OnErrorGoToStatementNode.IsLocal`|
|[`RDC00405`](../diagnostics/rdc00405.md)|Implementations should be private|Information|`DeclarationFact.Role` and `DeclarationFact.Access`|
|[`RDC01001`](../diagnostics/rdc01001.md)|Use meaningful identifier names|Information|the names the syntax tree declares, and the allow-list in `DiagnosticsOptions.MeaningfulNames`|
|[`RDC01002`](../diagnostics/rdc01002.md)|Hungarian notation|Information|the names the syntax tree declares, and the allow-list in `DiagnosticsOptions.HungarianNotation`|

What the host's static pass found wrong with the code is not an opinion: it is reported as the `VBC` compile error it is, by the same
extension, from the same model.

## Language Core Conditions and Analyzer Opinions

The `VBC`, `VBR` and `VBA` diagnostic families describe conditions the language core defines. `RDC` diagnostics are opinions of the analyzer.

Shadowing of `VBA` library definitions should be detected in the semantic layer and reported through semantic flags, so that RDCore.Diagnostics can issue shadowed declaration diagnostics; see [**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md) and [**RD-VBAL §1.1.3** Core Semantic Flags](rd-vbal.1.1.3.core-semantic-flags.md).

## Extension Diagnostics

🧩 Diagnostics contributed by other extensions must use their own prefix, distinct from `RDC`, so that codes stay unique and traceable to their source. The `RDX00000` format and third-party prefixes are specified in [**RD-VBAL §1.1.4** Core Diagnostics](rd-vbal.1.1.4.core-diagnostics.md).

---
> ⏮️ [**RD-VBAL §2.6.3** Runtime Errors](rd-vbal.2.6.3.runtime-errors.md) | ⏭️ [**RD-VBAL §2.6.5** Diagnostics Pipeline](rd-vbal.2.6.5.diagnostics-pipeline.md)
