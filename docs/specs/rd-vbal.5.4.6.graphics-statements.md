# 5.4.6 Graphics Statements

> [!NOTE]
> This section is an RD-VBA addition. MS-VBAL reserves `Circle` and `Scale` as special forms and `PSet` and `Scale` as reserved names
> ([**MS-VBAL §3.3.5.2** Reserved Identifiers and IDENTIFIER](https://learn.microsoft.com/en-us/openspecs/microsoft_general_purpose_programming_languages/ms-vbal/e1ac8be4-9b8d-4a4c-a1a4-6cc4ecba6b95)),
> and gives none of them a syntax.

`Circle`, `Line`, `PSet` and `Scale` are methods of the objects that can be drawn on: a Visual Basic 6.0 form and a PictureBox. They are written as
statements, with coordinate pairs, `Step` and `-` in place of an argument list:

```vb
[object.]Circle [Step] (x, y), radius [, [color] [, [start] [, [end] [, aspect]]]]
[object.]Line [[Step] (x1, y1)] - [Step] (x2, y2) [, [color] [, B | BF]]
[object.]PSet [Step] (x, y) [, color]
[object.]Scale [(x1, y1) - (x2, y2)]
```

## Syntax

|AST node|Instruction kind|Notes|
|---|---|---|
|[GraphicsMethodStatementNode](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Model.AST.Statements.GraphicsMethodStatementNode.html)|`Simple`|`Token` says which of the four it is. `Target` is the object before the dot; `IsWithRelative` is set for a statement written with a leading dot in a `With` block (`.Line`). An argument left out is `null` in `Arguments`.|

> [!NOTE]
> **Not implemented.** `Scale` written with no points (`Form1.Scale`, which sets the scale back to twips) is read as a call of the `Scale` method, and
> fails with _Argument not optional_.

## Static Semantics

|Written|In|Meaning|
|---|---|---|
|With an object (`Form1.Line …`, `Me.PSet …`, `.Circle …` in a `With` block)|Any module|A call of the object's method of that name.|
|With no object (`Line (0, 0)-(1, 1)`)|A Visual Basic 6.0 form's code|A call of the form's own method, `Me.Line`.|
|With no object|Any other module, a UserForm included|Compile error [`VBC09331`](../diagnostics/vbc09331.md) _Sub or Function not defined_. The statement is not lowered.|

The expressions in the points and the arguments are evaluated by the static pass like any other, whether or not the statement is valid.

## Runtime Semantics

The statement calls its method as the Visual Basic 6.0 type library declares it, with a flags argument first that says how the statement was written:

|Method|Arguments after the flags|
|---|---|
|`Line`|`X1`, `Y1`, `X2`, `Y2`, `Color`|
|`Circle`|`X`, `Y`, `Radius`, `Color`, `Start`, `End`, `Aspect`|
|`PSet`|`X`, `Y`, `Color`|
|`Scale`|`X1`, `Y1`, `X2`, `Y2`|

An argument the statement leaves out is passed as `0`, and a flag says it was left out. The other arguments are let-coerced to the declared parameter types,
as for any call.

|Situation|Effect|
|---|---|
|A Visual Basic 6.0 form or PictureBox that is shown|It draws on its surface. `CurrentX` and `CurrentY` move to where the figure ends: the point of a `PSet`, the second point of a `Line`, the center of a `Circle`.|
|A color left out|The object's `ForeColor`.|
|`Step`|The point is measured from `CurrentX` and `CurrentY`, or, for the second point of a `Line`, from its first point.|
|A `Line` with no first point|It starts at `CurrentX` and `CurrentY`.|
|A coordinate or a color that is not a number|Run-time error 13, _Type mismatch_ ([`VBR00013`](../diagnostics/vbr00013.md)).|
|A color that is out of range|Run-time error 5, _Invalid procedure call or argument_ ([`VBR00005`](../diagnostics/vbr00005.md)).|
|A form or PictureBox that is loaded and not shown|Nothing is raised. What is drawn is lost: `Point` returns `-1` until the form is shown, and the background color once it is.|
|An object with no such method (a TextBox, a class)|Run-time error 438, _Object doesn't support this property or method_ ([`VBR00438`](../diagnostics/vbr00438.md)).|

> [!NOTE]
> **Not implemented.** Where `AutoRedraw` is `True`, Visual Basic 6.0 keeps what is drawn on a form or PictureBox that is not shown: `Point` reads it, and
> it appears when the form is shown. RD-VBA keeps nothing, whatever `AutoRedraw` says.

The other drawing members of a form and a PictureBox are ordinary members: `Cls` clears the surface and sets `CurrentX` and `CurrentY` to `0`; `Point(x, y)`
returns the color at a point, `BackColor` where nothing was drawn, and `-1` outside the surface.

## Implementation

|Type or member|Role|
|---|---|
|`GraphicsStatementStaticSemantics`|The compile error for a statement with no object outside a Visual Basic 6.0 form's code.|
|`StaticSemanticsOptions.DrawsOnMe`, `InstructionLoweringOptions.DrawsOnMe`|Set by `ModuleLoader` for a module whose designer is a Visual Basic 6.0 form.|
|`StatementRuntimeSemanticsProvider`|Finds the object (the target, the `With` object, or `Me`) and calls its method with the synthesized arguments.|
|`GraphicsFlags`|The flags argument.|
|`RDFormsGraphics`|Serves the drawing members of a form and a PictureBox hosted by RDForms.|
