# 3.4.2 Simple Statements

Each simple statement is cross-referenced below to its section of [**MS-VBAL §5.4.2** Control Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/70d423da-18b4-42d2-9897-9f0b8100786b), [**MS-VBAL §5.4.3** Data Manipulation Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ee62ca0d-bf15-4679-8d11-6e411b37901b) or [**MS-VBAL §5.4.4** Error Handling Statements](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/47b18690-3175-44d9-9de1-31629f0aacc7), and to the RD-VBAL page that describes its implementation.

|Statement|Node type|MS-VBAL|RD-VBAL|
|---|---|---|---|
|`Call` / bare call|[CallStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.CallStatementNode.html)|[**MS-VBAL §5.4.2.1** Call Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f7c864a8-8fce-49dc-8347-30dc749d6576)|[**RD-VBAL §5.4.2.1** Call Statement](rd-vbal.5.4.2.1.call-statement.md)|
|`Let` assignment (`[Let] lExpression = expression`)|[AssignmentStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.AssignmentStatementNode.html) (`Kind`: `ImplicitLet` or `ExplicitLet`)|[**MS-VBAL §5.4.3.8** Let Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/ce2a98d4-2625-4cb7-982c-5c58e568cd18)|[**RD-VBAL §5.4.3.8** Let Statement](rd-vbal.5.4.3.8.let-statement.md)|
|`Set` assignment|`AssignmentStatementNode` (`Kind`: `Set`)|[**MS-VBAL §5.4.3.9** Set Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f343de03-5100-41f0-8197-93546c4fc21f)|[**RD-VBAL §5.4.3.9** Set Statement](rd-vbal.5.4.3.9.set-statement.md)|
|`LSet`|`AssignmentStatementNode` (`Kind`: `LSet`)|[**MS-VBAL §5.4.3.6** LSet Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6964d2a4-b3e3-497b-bb80-8bc98f0edab9)|[**RD-VBAL §5.4.3.6** LSet Statement](rd-vbal.5.4.3.6.lset-statement.md)|
|`RSet`|`AssignmentStatementNode` (`Kind`: `RSet`)|[**MS-VBAL §5.4.3.7** RSet Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/beccdd43-9dad-4bcf-b063-e542869917a1)|[**RD-VBAL §5.4.3.7** RSet Statement](rd-vbal.5.4.3.7.rset-statement.md)|
|`Mid`, `Mid$`, `MidB`, `MidB$`|[MidStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.MidStatementNode.html); see [Mid Statement](#mid-statement)|[**MS-VBAL §5.4.3.5** Mid/MidB/Mid$/MidB$ Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2a8f3567-c8e0-4176-a802-cf2edeba425f)|[**RD-VBAL §5.4.3.5** Mid/MidB/Mid$/MidB$ Statement](rd-vbal.5.4.3.5.mid-statement.md)|
|`ReDim`, `ReDim Preserve`|[RedimDeclarationNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Declarations.RedimDeclarationNode.html); see [ReDim Statement](#redim-statement)|[**MS-VBAL §5.4.3.3** ReDim Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/22b5d372-0a54-4617-9462-4934b5edc88c)|[**RD-VBAL §5.4.3.3** ReDim Statement](rd-vbal.5.4.3.3.redim-statement.md)|
|`Erase`|[KeywordStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.KeywordStatementNode.html) (`Token`: `Erase`)|[**MS-VBAL §5.4.3.4** Erase Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f7958382-95a7-47fa-91bd-42262ab9ad32)|[**RD-VBAL §5.4.3.4** Erase Statement](rd-vbal.5.4.3.4.erase-statement.md)|
|`Name...As`|`KeywordStatementNode` (`Token`: `Name`)|None|None|
|`RaiseEvent`|`KeywordStatementNode` (`Token`: `RaiseEvent`)|[**MS-VBAL §5.4.2.20** RaiseEvent Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3795fff1-ce8a-40f7-8d2c-b1e2c1a251c4)|[**RD-VBAL §5.4.2.20** RaiseEvent Statement](rd-vbal.5.4.2.20.raiseevent-statement.md)|
|`Stop`|`KeywordStatementNode` (`Token`: `Stop`)|[**MS-VBAL §5.4.2.11** Stop Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/3e8463a8-ee71-4e33-8008-0bd4910e68ea)|[**RD-VBAL §5.4.2.11** Stop Statement](rd-vbal.5.4.2.11.stop-statement.md)|
|`End`|`KeywordStatementNode` (`Token`: `End`)|[**MS-VBAL §5.4.2.22** End Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/76570466-0d4d-4159-9e84-1baf9d6e6d2f)|[**RD-VBAL §5.4.2.22** End Statement](rd-vbal.5.4.2.22.end-statement.md)|
|`Exit Do`|`KeywordStatementNode` (`Token`: `"Exit Do"`)|[**MS-VBAL §5.4.2.7** Exit Do Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/f672b312-fe2a-4f4d-9ad4-42729b110fe7)|[**RD-VBAL §5.4.2.7** Exit Do Statement](rd-vbal.5.4.2.7.exit-do-statement.md)|
|`Exit For`|`KeywordStatementNode` (`Token`: `"Exit For"`)|[**MS-VBAL §5.4.2.5** Exit For Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/aa978e90-6240-454c-a7af-0a3e80779dc7)|[**RD-VBAL §5.4.2.5** Exit For Statement](rd-vbal.5.4.2.5.exit-for-statement.md)|
|`Exit Sub`|`KeywordStatementNode` (`Token`: `"Exit Sub"`)|[**MS-VBAL §5.4.2.17** Exit Sub Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/fe55464d-a2c4-4ca6-ace1-e757dbe95e73)|[**RD-VBAL §5.4.2.17** Exit Sub Statement](rd-vbal.5.4.2.17.exit-sub-statement.md)|
|`Exit Function`|`KeywordStatementNode` (`Token`: `"Exit Function"`)|[**MS-VBAL §5.4.2.18** Exit Function Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/d70e6f6f-b830-4be2-acce-aa491c8acb5a)|[**RD-VBAL §5.4.2.18** Exit Function Statement](rd-vbal.5.4.2.18.exit-function-statement.md)|
|`Exit Property`|`KeywordStatementNode` (`Token`: `"Exit Property"`)|[**MS-VBAL §5.4.2.19** Exit Property Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/2c254f33-48b5-40b7-9dc7-4943313a1742)|[**RD-VBAL §5.4.2.19** Exit Property Statement](rd-vbal.5.4.2.19.exit-property-statement.md)|
|`GoTo`|[GoToStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.GoToStatementNode.html)|[**MS-VBAL §5.4.2.12** GoTo Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/9ce18dd2-6864-426e-aec3-fd30518024a8)|[**RD-VBAL §5.4.2.12** GoTo Statement](rd-vbal.5.4.2.12.goto-statement.md)|
|`GoSub`|[GoSubStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.GoSubStatementNode.html)|[**MS-VBAL §5.4.2.14** GoSub Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/492e1f84-c47f-40ef-819f-f1d23e475c91)|[**RD-VBAL §5.4.2.14** GoSub Statement](rd-vbal.5.4.2.14.gosub-statement.md)|
|`On expression GoTo label, ...`|[OnGoToStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.OnGoToStatementNode.html)|[**MS-VBAL §5.4.2.13** On...GoTo Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/371fa3be-b105-4334-8794-a7488107a6f8)|[**RD-VBAL §5.4.2.13** On...GoTo Statement](rd-vbal.5.4.2.13.on-goto-statement.md)|
|`On expression GoSub label, ...`|[OnGoSubStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.OnGoSubStatementNode.html)|[**MS-VBAL §5.4.2.16** On...GoSub Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/6f44253f-d6ae-4de5-af01-0b1d9a67d24d)|[**RD-VBAL §5.4.2.16** On...GoSub Statement](rd-vbal.5.4.2.16.on-gosub-statement.md)|
|`Return`|[ReturnStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.ReturnStatementNode.html)|[**MS-VBAL §5.4.2.15** Return Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/4cc2aabb-5940-4abb-ad6b-953bd5631073)|[**RD-VBAL §5.4.2.15** Return Statement](rd-vbal.5.4.2.15.return-statement.md)|
|`On Error GoTo <label>`|[OnErrorGoToStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.OnErrorGoToStatementNode.html)|[**MS-VBAL §5.4.4.1** On Error Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e2561165-c99a-444b-8bc0-be60a196867a)|[**RD-VBAL §5.4.4.1** On Error Statement](rd-vbal.5.4.4.1.on-error-statement.md)|
|`On Error Resume Next`|[OnErrorResumeStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.OnErrorResumeStatementNode.html)|**MS-VBAL §5.4.4.1**|[**RD-VBAL §5.4.4.1** On Error Statement](rd-vbal.5.4.4.1.on-error-statement.md)|
|Bare `Resume`, `Resume <label>`|[ResumeStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.ResumeStatementNode.html) (`LabelExpression` nullable)|[**MS-VBAL §5.4.4.2** Resume Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/00439540-cf97-451d-9f20-7856d4d98c9b)|[**RD-VBAL §5.4.4.2** Resume Statement](rd-vbal.5.4.4.2.resume-statement.md)|
|`Resume Next`|[ResumeNextStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.ResumeNextStatementNode.html)|**MS-VBAL §5.4.4.2**|[**RD-VBAL §5.4.4.2** Resume Statement](rd-vbal.5.4.4.2.resume-statement.md)|
|`Error <number>`|[ErrorStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.ErrorStatementNode.html)|[**MS-VBAL §5.4.4.3** Error Statement](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/70ca285b-7f18-4f0a-b0b9-7edcddf30ec4)|[**RD-VBAL §5.4.4.3** Error Statement](rd-vbal.5.4.4.3.error-statement.md)|

`Name...As` is not an MS-VBAL-numbered statement: MS-VBAL has no section for it.

## Assignment Statements

`Let`, `Set`, `LSet` and `RSet` share one node type, `AssignmentStatementNode`, distinguished by its [AssignmentKind](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.AssignmentKind.html) `Kind`. `LSet` and `RSet` have the same node shape as `Let` and `Set`.

The `Target` of an `AssignmentStatementNode` is always an `lExpression`. It draws from the shared expression family described in [**RD-VBAL §3.0.2** Node Types](rd-vbal.3.0.2.node-types.md): a member access, an index, a dictionary access, or a bare name.

The runtime semantics of `LSet` and `RSet` are described in [**RD-VBAL §5.4.3.6** LSet Statement](rd-vbal.5.4.3.6.lset-statement.md) and [**RD-VBAL §5.4.3.7** RSet Statement](rd-vbal.5.4.3.7.rset-statement.md).

## Mid Statement

`MidStatementNode` records the statement's spelling in two independent flags. `IsByteMode` distinguishes `MidB`/`MidB$` from `Mid`/`Mid$`; `IsStringInput` records the `$` suffix.

|Spelling|`IsByteMode`|`IsStringInput`|
|---|---|---|
|`Mid`|`false`|`false`|
|`Mid$`|`false`|`true`|
|`MidB`|`true`|`false`|
|`MidB$`|`true`|`true`|

The replacement-span mechanics of **MS-VBAL §5.4.3.5** split only on byte mode (`MidB`/`MidB$` versus `Mid`/`Mid$`), never on the `$` suffix. `IsStringInput` is preserved because it mirrors the `VBVariant`/`VBString` split of the `Mid`/`Mid$` function overloads, which matters for static semantics.

## ReDim Statement

`ReDim` and `ReDim Preserve` are represented by `RedimDeclarationNode`. `ReDim` is modelled as a declaration node, not a statement node, because it declares or resizes storage.

## Branching Statements

Statement labels and line numbers are captured as separate nodes, [LineLabelNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Abstract.LineLabelNode.html) and [LineNumberNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Abstract.LineNumberNode.html); see [**RD-VBAL §5.4.1.1** Statement Labels](rd-vbal.5.4.1.1.statement-labels.md).

A `GoTo` or `GoSub` statement's target is an expression naming or numbering a label or line. There is no static link between the `GoToStatementNode` or `GoSubStatementNode` and the `LineLabelNode` or `LineNumberNode` it targets.

`OnGoToStatementNode` and `OnGoSubStatementNode` hold their targets in a `Labels` list. They follow the same convention as `GoTo` and `GoSub`: each target is an expression naming or numbering a label or line, with no static link to the label node.

`OnGoToStatementNode` and `OnGoSubStatementNode` are separate node types rather than one shared node with a `Kind`. This mirrors the separate `GoToStatementNode` and `GoSubStatementNode`, because `GoTo` and `GoSub` are distinct branch mechanisms. By contrast, `Let`, `Set`, `LSet` and `RSet` share one node type, `AssignmentStatementNode`, distinguished by `Kind`.

## Error-Handling Statements

`On Error Resume Next` and `On Error GoTo` are parsed by the same grammar rule, disambiguated by the keyword that follows `On Error`.

The SDK documentation of `OnErrorGoToStatementNode` also covers the `On Error GoTo -1` form, which MS-VBAL does not document; see [**RD-VBAL §5.4.4.1** On Error Statement](rd-vbal.5.4.4.1.on-error-statement.md).

`ResumeStatementNode.LabelExpression` is nullable: a bare `Resume` has no label.

`Resume Next` has its own node type, `ResumeNextStatementNode`. It is not a `ResumeStatementNode` with a "Next" label.
