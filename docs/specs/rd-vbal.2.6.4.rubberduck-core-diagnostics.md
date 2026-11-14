# 2.6.4 Rubberduck Core Diagnostics
|[`RDC01011`](../diagnostics/rdc01011.md)|Variable not used|Information|the host's reference counts of a local or `Private` variable: no read, no argument; not a `WithEvents` variable, not a loop's control variable|
|[`RDC01012`](../diagnostics/rdc01012.md)|Variable not assigned|Warning|the host's reference counts of a local or `Private` variable: read, never written, and passed to no call that may assign it (a procedure of the module is followed); not an array, a `ReDim`'d name, a `WithEvents` variable, an object or a user-defined type|
|[`RDC01013`](../diagnostics/rdc01013.md)|Parameter not used|Information|the host's reference counts of a parameter: none; not of a `Declare`, an `Event`, an implementation, a handler, or a `Public` member of a class module|
|[`RDC01014`](../diagnostics/rdc01014.md)|Assigned ByVal parameter|Information|the host's reference counts of a `ByVal` parameter (or the value parameter of a `Property Let`/`Set`): written|
|[`RDC01015`](../diagnostics/rdc01015.md)|Constant not used|Information|the host's reference counts of a constant of a procedure: no read, no argument; a constant of the module is not counted|
|[`RDC01016`](../diagnostics/rdc01016.md)|Line label not used|Information|the syntax tree: a `LineLabelNode` that no `GoTo`, `GoSub`, `Resume` or `On Error GoTo` of its procedure names; line numbers are not reported|
|[`RDC01017`](../diagnostics/rdc01017.md)|Write-only property|Information|the syntax tree: a `Public`, `Global` or implicitly public `Property Let`/`Set` with no `Property Get` of its name; once per name|

**Rubberduck Core diagnostics** are the analyzer findings issued by the `RDCore.Diagnostics` analyzers. They cover implicit declarations, obsolete syntax, misleading constructs, every inspection that the legacy Rubberduck add-in shipped, and further inspections beyond those.

|||
|---|---|
|Code family|`RDC`, defined by `RDCoreDiagnosticId` (in `RDCore.Diagnostics`); the enum value is the code|
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

Not every fact is ready at the same time, and a finding that is ready is not held back for one that is not. An analyzer belongs to a **phase**
(`IModuleAnalyzer.Phase`, `AnalysisPhase`): the *static* phase has the syntax tree and the facts of the host's static pass, which are there as soon as a module is
loaded; the *runtime* phase has the facts that come of evaluating the code of the module, which take longer. The language server asks for the phases in turn and, for a
client that is pushed diagnostics, publishes each as soon as it arrives together with the ones before it (a publication replaces the one before it). A client that
pulls diagnostics is answered when every phase is. An analyzer that does not say is of the static phase.

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
|[`RDC00112`](../diagnostics/rdc00112.md)|Implicit narrowing conversion|Warning|`ProcedureSemanticModel.Runtime`: the `ConversionFact`s the language core states about the code of the procedure|
|[`RDC00114`](../diagnostics/rdc00114.md)|Implicit array declaration|Information|`ModuleSemanticModel.Declarations`: a local whose `DeclarationFact.DeclaredBy` is `ReDim`|
|[`RDC00115`](../diagnostics/rdc00115.md)|Implicit result local|Information|the syntax tree, and `ProcedureSemanticModel.Expressions`: a name of the procedure, read in its body, bound to it and not a call of it|
|[`RDC00117`](../diagnostics/rdc00117.md)|Implicit constant type|Information|the syntax tree: a `ConstantDeclarationNode` with no `As` clause and no type hint|
|[`RDC00119`](../diagnostics/rdc00119.md)|Implicit default member access|Information|`ProcedureSemanticModel.Runtime`: a `ConversionFact` with `ConversionSemanticFlags.ObjectOperand`, at its operand of a class type; and the syntax tree's `Let` assignments whose target has a `MemberAccessOperationSemanticFlags.DefaultMember` access|
|[`RDC00120`](../diagnostics/rdc00120.md)|Implicit unbound default member access|Warning|the same facts as `RDC00119`, at an operand or a `Let` target declared `Object`|
|[`RDC00121`](../diagnostics/rdc00121.md)|Indexed default member access|Information|`ProcedureSemanticModel.Runtime`: the member accesses with `MemberAccessOperationSemanticFlags.Implicit` and `DefaultMember`, and neither `DictionaryAccess` nor `LateBound`|
|[`RDC00122`](../diagnostics/rdc00122.md)|Indexed unbound default member access|Warning|the same, with `LateBound` and not `DictionaryAccess`|
|[`RDC00123`](../diagnostics/rdc00123.md)|Bang notation|Information|the same, with `DictionaryAccess` and not `LateBound`|
|[`RDC00124`](../diagnostics/rdc00124.md)|Unbound bang notation|Warning|the same, with `DictionaryAccess` and `LateBound`|
|[`RDC00125`](../diagnostics/rdc00125.md)|Suspicious Let assignment|Warning|the syntax tree's `Let` assignments whose target is an object (a `DefaultMember` access, or declared `Object`) and whose value is the operand of a `ConversionSemanticFlags.ObjectOperand` conversion|
|[`RDC00201`](../diagnostics/rdc00201.md)|Integer data type declaration|Information|the syntax tree: `AsTypeExpressionNode`|
|[`RDC00202`](../diagnostics/rdc00202.md)|Module-scope Dim declaration|Information|the syntax tree: a module-level variable with no access modifier|
|[`RDC00203`](../diagnostics/rdc00203.md)|Multiline parameter declaration|Information|the syntax tree: the range of a `ParameterDeclarationNode`|
|[`RDC00204`](../diagnostics/rdc00204.md)|Multiple declarations|Information|the syntax tree: the declarations one statement makes|
|[`RDC00205`](../diagnostics/rdc00205.md)|Misleading ByRef parameter|Information|the syntax tree: `ParameterDeclarationNode.IsByRefIgnored`|
|[`RDC00206`](../diagnostics/rdc00206.md)|Not all code paths return a value|Warning|`ProcedureSemanticModel.ReturnValue`, from the `ControlFlowGraph` of the procedure|
|[`RDC00207`](../diagnostics/rdc00207.md)|IsMissing that is always False|Warning|`ValueExpressionSemanticFlags.IsMissingAlwaysFalse`|
|[`RDC00208`](../diagnostics/rdc00208.md)|Shadowed declaration|Information|`DeclarationFact.Hides`, from the resolver: what the name of a local or a parameter means as seen from the module|
|[`RDC00211`](../diagnostics/rdc00211.md)|On Error GoTo -1|Hint|the syntax tree: an `OnErrorGoToStatementNode` whose label is the literal `-1`|
|[`RDC00212`](../diagnostics/rdc00212.md)|Unrestored On Error Resume Next|Warning|the syntax tree: an `OnErrorResumeStatementNode` that no `OnErrorGoToStatementNode` to a label or `0` follows in its procedure, in the order they are written|
|[`RDC00213`](../diagnostics/rdc00213.md)|Line continuation between keywords|Information|the trivia of the parse: `LineContinuationTriviaNode.Keyword`|
|[`RDC00214`](../diagnostics/rdc00214.md)|Procedure can be written as a function|Information|the syntax tree: a `Sub` with one `ByRef` parameter that is no `ParamArray`; the host's reference counts of that parameter: written; not a `Declare`, an implementation, a handler, or a `Public` member of a class module|
|[`RDC00215`](../diagnostics/rdc00215.md)|Self-assigned declaration|Hint|the syntax tree: `AsTypeExpressionNode.AsAutoObject` of a variable declaration|
|[`RDC00302`](../diagnostics/rdc00302.md)|Obsolete Call statement|Hint|`ValueExpressionSemanticFlags.ExplicitCallKeyword`|
|[`RDC00303`](../diagnostics/rdc00303.md)|Obsolete comment syntax|Information|`ModuleSemanticModel.Language` (`SupportedLanguage.UsesClassicBasicSyntax`), and the comments of the syntax tree|
|[`RDC00304`](../diagnostics/rdc00304.md)|Obsolete error syntax|Information|`ModuleSemanticModel.Language`, and the syntax tree: an `Error` statement|
|[`RDC00305`](../diagnostics/rdc00305.md)|Obsolete Global modifier|Information|the syntax tree: `AccessModifier.Global`|
|[`RDC00306`](../diagnostics/rdc00306.md)|Obsolete Let statement|Information|the syntax tree: `AssignmentKind.ExplicitLet`|
|[`RDC00307`](../diagnostics/rdc00307.md)|Obsolete type hint|Information|the syntax tree: the type hints of declarations; `ExpressionFact.Binding` of the references|
|[`RDC00308`](../diagnostics/rdc00308.md)|Obsolete While…Wend|Information|the syntax tree: a `WhileWendStatementNode`|
|[`RDC00309`](../diagnostics/rdc00309.md)|Obsolete On Local Error statement|Information|the syntax tree: `OnErrorGoToStatementNode.IsLocal`|
|[`RDC00310`](../diagnostics/rdc00310.md)|Redundant ByVal argument|Hint|`ValueExpressionSemanticFlags.RedundantByVal`|
|[`RDC00311`](../diagnostics/rdc00311.md)|Early binding opportunity|Information|`ExpressionFact.Creates` (`ObjectClassSource.ReferencedLibrary`)|
|[`RDC00405`](../diagnostics/rdc00405.md)|Implementations should be private|Information|`DeclarationFact.Role` and `DeclarationFact.Access`|
|[`RDC00407`](../diagnostics/rdc00407.md)|Unmatched event handler|Hint|`DeclarationFact.Role` (`DeclarationRole.UnmatchedEventHandler`) and `DeclarationFact.References`|
|[`RDC00408`](../diagnostics/rdc00408.md)|Underscore in a public class member|Warning|`ModuleSemanticModel.Declarations` of a class module: a public procedure, property or variable of the module named with `_`, whose `DeclarationFact.Role` is `None`|
|[`RDC00409`](../diagnostics/rdc00409.md)|Public field|Information|`ModuleSemanticsDto.Class` (a class module), and the syntax tree: a module-level variable declared `Public`|
|[`RDC00410`](../diagnostics/rdc00410.md)|Default instance in its own class|Warning|`ModuleSemanticsDto.Class` (`ClassModuleSemanticFlags.DefaultInstance`), and `ExpressionFactDto.Binding`: a name bound to the default instance variable the global scope declares for the class|
|[`RDC01001`](../diagnostics/rdc01001.md)|Use meaningful identifier names|Information|the names the syntax tree declares, and the allow-list in `DiagnosticsOptions.MeaningfulNames`|
|[`RDC01002`](../diagnostics/rdc01002.md)|Hungarian notation|Information|the names the syntax tree declares, and the allow-list in `DiagnosticsOptions.HungarianNotation`|
|[`RDC01005`](../diagnostics/rdc01005.md)|Hosted document code|Error|the host's `DocumentModuleFact` of a document module whose code presumes its host, with the bindings of its expressions and its procedures named as the host's event handlers; and `ValueExpressionSemanticFlags.HostedDocumentName` of a name in another module, which it reports in place of _Variable not defined_|
|[`RDC01006`](../diagnostics/rdc01006.md)|Unsupported document module|Error|the host's `DocumentModuleFact`, which names no host class; or a `.doccls` document that has none|
|[`RDC01007`](../diagnostics/rdc01007.md)|Host library not referenced|Error|the host's `DocumentModuleFact`, whose host class the project's references do not have|
|[`RDC11005`](../diagnostics/rdc11005.md)|Bitwise operator|Hint|`ProcedureSemanticModel.Runtime`: `LogicalOperatorSemanticFlags.IsBitwiseSemantics` of the `LogicalOperatorFact`s|
|[`RDC11006`](../diagnostics/rdc11006.md)|Ambiguous concatenation|Information|`ProcedureSemanticModel.Runtime`: `ArithmeticOperatorSemanticFlags.VBStringEffectiveType` of the `ArithmeticOperatorFact`s of `+`|
|[`RDC11011`](../diagnostics/rdc11011.md)|Variant number compared with a Variant String|Warning|`ProcedureSemanticModel.Runtime`: `ComparisonOperatorSemanticFlags.IsVariantStringNumericException` of the `ComparisonOperatorFact`s|
|[`RDC11012`](../diagnostics/rdc11012.md)|Set of a call that always fails|Error|`ValueExpressionSemanticFlags.SetHasNoDefaultMemberToSet`, on a line where `ProcedureSemanticModel.Runtime` does not already report the error 438|
|[`RDC11013`](../diagnostics/rdc11013.md)|Variant compared with a String as text|Warning|`ProcedureSemanticModel.Runtime`: `ComparisonOperatorSemanticFlags.IsVariantComparedWithStringAsText` of the `ComparisonOperatorFact`s|
|[`RDC11014`](../diagnostics/rdc11014.md)|Case Is compared in the selector's type|Warning|`ProcedureSemanticModel.Runtime`: `CaseClauseSemanticFlags.ComparisonForm` and `ComparedInSelectorType` of the `CaseClauseStatementFact`s|
|[`RDC11015`](../diagnostics/rdc11015.md)|Object with no default member used as a value|Warning|`ValueExpressionSemanticFlags.ValueOfObjectWithNoDefaultMember`|
|[`RDC11016`](../diagnostics/rdc11016.md)|Empty or Null assigned to an array|Warning|`ValueExpressionSemanticFlags.EmptyLetToArray`|
|[`RDC11017`](../diagnostics/rdc11017.md)|Like pattern MS-VBA matches otherwise than specified|Warning|`ProcedureSemanticModel.Runtime`: `ComparisonOperatorSemanticFlags.LikePatternHasEmptyCharList` and `LikePatternIsInvalid` of the `ComparisonOperatorFact`s|
|[`RDC11018`](../diagnostics/rdc11018.md)|Integer arithmetic of Abs, Int or Fix that can overflow|Warning|`ValueExpressionSemanticFlags.OverflowsWhereVariantWidens`|
|[`RDC11019`](../diagnostics/rdc11019.md)|Type character ignored on a function of the library|Warning|`ValueExpressionSemanticFlags.IgnoredTypeCharacter`|
|[`RDC11020`](../diagnostics/rdc11020.md)|Variant String that is no number, compared with a number|Warning|`ProcedureSemanticModel.Runtime`: `ComparisonOperatorSemanticFlags.IsVariantStringThatIsNoValueRanked` of the `ComparisonOperatorFact`s|
|[`RDC11021`](../diagnostics/rdc11021.md)|Constant of a procedure valued with Len|Warning|the syntax tree: a `Const` of a procedure whose value calls `Len`|
|[`RDC11022`](../diagnostics/rdc11022.md)|ReDim of a variable declared implicitly|Warning|the syntax tree's `ReDim` statements, and `ModuleSemanticModel.Declarations`: a local whose `DeclarationFact.DeclaredBy` is `Implicit`, used before the `ReDim`|
|[`RDC11023`](../diagnostics/rdc11023.md)|Procedure MS-VBA refuses as too large|Warning|the syntax tree: the statements of a procedure|
|[`RDC11024`](../diagnostics/rdc11024.md)|Implements in a document module|Warning|the host's `DocumentModuleFact`, and the module's `Implements` directives|
|[`RDC11025`](../diagnostics/rdc11025.md)|Array given a second empty argument list|Warning|`ValueExpressionSemanticFlags.ArrayGivenASecondEmptyArgumentList`|
|[`RDC11026`](../diagnostics/rdc11026.md)|Public variable of an object passed ByRef as a copy|Warning|`ProcedureSemanticModel.Expressions`: `ValueExpressionSemanticFlags.ObjectVariablePassedAsACopy` of the `ExpressionFact`s|
|[`RDC11027`](../diagnostics/rdc11027.md)|String compared with a number as a Double|Warning|`ProcedureSemanticModel.Runtime`: `ComparisonOperatorSemanticFlags.IsStringComparedAsADouble` of the `ComparisonOperatorFact`s|
|[`RDC11028`](../diagnostics/rdc11028.md)|New of a class that is never created|Warning|`ProcedureSemanticModel.Expressions`: `ValueExpressionSemanticFlags.NewAlwaysFails` of the `ExpressionFact`s; `ModuleSemanticModel.Declarations`: `DeclarationFact.AutoInstantiationAlwaysFails`|
|[`RDC11029`](../diagnostics/rdc11029.md)|Public variable of an object passed late as a copy|Hint|`ProcedureSemanticModel.Expressions`: `ValueExpressionSemanticFlags.ObjectVariablePassedLateAsACopy` of the `ExpressionFact`s|
|[`RDC11031`](../diagnostics/rdc11031.md)|True value that is not -1|Warning|`ProcedureSemanticModel.Expressions`: `ValueExpressionSemanticFlags.TrueValueIsNotMinusOne` of the `ExpressionFact`s in a condition (`TruthValueOperations`); the syntax tree's precompiler trivia for `#If`, `#ElseIf` and `#Const`, with the module's own `#Const` values and the predefined constants, `1` or `0` (`PredefinedConditionalConstants`)|
|[`RDC11032`](../diagnostics/rdc11032.md)|For Each variable read after the loop ran out|Warning|`ProcedureSemanticModel.EmptiedControlReads`: the `EmptiedControlReadFact`s `ForEachControlAnalysis` finds on the `ControlFlowGraph`|
|[`RDC11033`](../diagnostics/rdc11033.md)|Reserved name that MS-VBA accepts|Warning|the names the syntax tree declares: an Enum member named `Date`, `DoEvents` or `CDec`, not in brackets|
|[`RDC11034`](../diagnostics/rdc11034.md)|Name that MS-VBA refuses|Warning|the names the syntax tree declares outside brackets: `Local`, or a reserved type identifier other than `Date` and `String`, except as a member of a user-defined type; `Me` as a member of one; `Name` as an Enum member|
|[`RDC11035`](../diagnostics/rdc11035.md)|Collection set to Nothing inside its own For Each loop|Warning|`ProcedureSemanticModel.Expressions`: `ValueExpressionSemanticFlags.CollectionSetToNothingInItsOwnLoop` of the `ExpressionFact`s|
|[`RDC11036`](../diagnostics/rdc11036.md)|Negative line number|Warning|the syntax tree: a `LineNumberNode` below 0, or a jump whose label is a negated literal (`On Error GoTo -1` aside)|
|[`RDC11037`](../diagnostics/rdc11037.md)|Non-breaking space in a name|Warning|the syntax tree: the name of a declaration or a line label with a U+00A0|
|[`RDC11039`](../diagnostics/rdc11039.md)|Function called whatever the IIf condition|Warning|the syntax tree (the `TruePart` and `FalsePart` arguments of a call bound to `Interaction.IIf`) and `ExpressionFactDto`: a `ProcedureCall` of a `Function` or `Property Get`, bound to anything but a VBA library function that changes nothing and raises no error|
|[`RDC20001`](../diagnostics/rdc20001.md)|Edit not applied to the waiting program|Information|the language server, while a program waits: the host's `rdcore/host/debug/editStatus`|

What the host's static pass found wrong with the code is not an opinion: it is reported as the `VBC` compile error it is, by the same
extension, from the same model. Likewise, a run-time error that evaluating the code finds an instruction raises *whenever it runs* (`RuntimeFacts.Errors`) is reported
in the runtime phase as the `VBR` error it is, where the instruction is, with the severity of an error: `n = 5678` into a `Byte` is
[`VBR00006`](../diagnostics/vbr00006.md) before the program runs. An error the code raises for itself (`Error`, `Err.Raise`) is a fact an analyzer may read, and is not
reported; see [**RD-VBAL §2.6.3** Runtime Errors](rd-vbal.2.6.3.runtime-errors.md).

### Runtime facts

What an analyzer of the runtime phase reads is `ProcedureSemanticModel.Runtime` ([`RuntimeFacts`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Semantics.Facts.RuntimeFacts.html)), which travels
as [`RuntimeFactsDto`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Platform.Protocol.RuntimeFactsDto.html). Every fact is stated by the runtime semantics themselves, as they evaluate each
instruction of the procedure once (the *sweep*), and only when it is true of the code however the procedure is called:

|Facts|What they say|Flags|
|---|---|---|
|`Conversions` ([`ConversionFact`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Semantics.Facts.ConversionFact.html))|every let-coercion the code asks for, its site and operand|`ConversionSemanticFlags`|
|`Operations` ([`OperatorFact`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Semantics.Facts.OperatorFact.html))|every operation of an operator, with its effective type; a comparison says how `String` values compare (`Option Compare`)|`ArithmeticOperatorSemanticFlags`, `ConcatOperationSemanticFlags`, `ComparisonOperatorSemanticFlags`, `LogicalOperatorSemanticFlags`|
|`Operations` ([`MemberAccessFact`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Semantics.Facts.MemberAccessFact.html))|every member the code accesses: named (`a.b`, `.b`), a dictionary access (`a!b`), the default member of an indexed object, the enumeration member a `For Each` uses, and the member an assignment writes|`MemberAccessOperationSemanticFlags`|
|`Statements` ([`StatementFact`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Semantics.Facts.StatementFact.html))|what an `LSet`, `RSet` or `With` statement is|`FixedAssignmentSemanticFlags`, `WithStatementSemanticFlags`|
|`Errors`|every run-time error an instruction raises whenever it runs|-|

An error is stated only when nothing the instruction did was *assumed*: the value of a variable or a parameter, what a procedure that is not called returns, the
state of the standard library or of the outside world (files, `Rnd`, `CreateObject`), or what another instruction left behind (a loop's counter, a `With` block's
target). `Dim c As Collection: c.Add 1` raises error 91 when it runs, but the sweep does not follow paths and cannot tell that nothing set `c`, so it says nothing.
The bounds of a fixed-size array are not assumed: they are its declaration's, whatever it holds. `Dim a(5) As Long: a(6) = 1` is error 9 whenever it runs, and
is stated; what an element holds is assumed, so `x = 1 \ a(1)` says nothing.
`MemberAccessOperationSemanticFlags.CallByNameAccess` is not stated yet: a `CallByName` call is a call of the library, which the sweep does not look into.

## Language Core Conditions and Analyzer Opinions

The `VBC`, `VBR` and `VBA` diagnostic families describe conditions the language core defines. `RDC` diagnostics are opinions of the analyzer.

Shadowing of `VBA` library definitions should be detected in the semantic layer and reported through semantic flags, so that RDCore.Diagnostics can issue shadowed declaration diagnostics; see [**RD-VBAL §2.3.1.3** Name Resolution](rd-vbal.2.3.1.3.name-resolution.md) and [**RD-VBAL §1.1.3** Core Semantic Flags](rd-vbal.1.1.3.core-semantic-flags.md).

## Extension Diagnostics

**Extension point:** diagnostics contributed by other extensions must use their own prefix, distinct from `RDC`, so that codes stay unique and traceable to their source. The `RDX00000` format and third-party prefixes are specified in [**RD-VBAL §1.1.4** Core Diagnostics](rd-vbal.1.1.4.core-diagnostics.md).
