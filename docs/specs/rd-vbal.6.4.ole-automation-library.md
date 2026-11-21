# 6.4 OLE Automation Library

> [!NOTE]
> This section is RD-VBAL's own. MS-VBAL has no section for the OLE Automation library (`stdole`).

A project that references `stdole` gets its fonts and pictures from the platform itself, on every operating system: no automation server holds them,
and an environment that allows no automation (`AllowAutomation` false) still has them.

## StdFont

`New StdFont` and `Dim f As New StdFont` make a font with the values of a new one: `MS Sans Serif`, 8.25 points, weight 400, character set 0.
`TypeName` gives `Font`. `TypeOf f Is StdFont` and `TypeOf f Is Font` are True, and `TypeOf f Is IFont` is False, as in Excel (Visual Basic 6.0 gives True).

|Member|What it is|
|---|---|
|`Name`|The face name. The default member, so `VarType(f)` is 8 (`vbString`).|
|`Size`|The size in points, a `Currency`, snapped as MS-VBA snaps it when it is read.|
|`Bold`|Setting it sets `Weight` to 700 or 400.|
|`Italic`, `Underline`, `Strikethrough`|`Boolean`.|
|`Weight`|An `Integer` from 0 to 1000. It reads 400 for a weight up to 550, and 700 above it.|
|`Charset`|An `Integer` from 0 to 255.|

A value the font refuses raises MS-VBA's error and leaves the font as it was:

|Value|Error|
|---|---|
|A `Size` of 0 or less, a `Weight` outside 0 to 1000, a `Charset` outside 0 to 255, a `Name` of 32 characters or more|380|
|A `Size` of 24576 or more|6|

A change raises `FontChanged(PropertyName)` to the `WithEvents` variables that hold the font, before the assignment returns, and only when a value
that reads back changes: `Bold` set twice raises it once, and `Weight` 700 on a bold font raises nothing. An error a handler leaves unhandled is the
assignment's, as with `RaiseEvent` ([**RD-VBAL §5.4.2.20** RaiseEvent Statement](rd-vbal.5.4.2.20.raiseevent-statement.md)).

**RD-VBA differs from MS-VBA:**

- `Name` and `Charset` read back as they were set. MS-VBA reads them as the Windows font mapper chose them.
- A `Size` of 24575.75 reads 24575.25.

A font given to a control of a Visual Basic 6.0 form is described in [Visual Basic 6.0 projects](../vb6-projects.md).

## StdPicture

`LoadPicture` and `SavePicture`, of `stdole` and of Visual Basic 6.0's `VB` library, are one implementation. A picture is its file, read through the
session's file channels.

|Member|What it is|
|---|---|
|`Handle`|The default member: a value that tells one picture from another, and 0 for none. No graphics device is behind it to hand to a `Declare`d procedure.|
|`Type`|What the file's header says it is.|
|`Width`, `Height`|The picture's size, in HIMETRIC units.|
|`hPal`|The palette of a picture that has one. For any other, it fails as MS-VBA's does.|

An icon or a cursor loads the image of the size asked for, or its first.

|Situation|Error|
|---|---|
|`LoadPicture` given `Null`, or a value that is not a `String`|13|
|A file that is not a picture `LoadPicture` reads, or an empty one|481 ([VBR00481](../diagnostics/vbr00481.md))|
|A name that is a folder|75|
|A name that ends in a path separator|76|
|A name that is a pattern|75|
|`SavePicture` to a folder that does not exist|76|
|`SavePicture` given no picture|380|
|`SavePicture` given no file name|53|
|`SavePicture` of a format it does not write|445|

**RD-VBA addition:** a PNG loads as a bitmap (`Type` 1) that keeps its alpha channel, and so does an icon's or a cursor's image stored as a PNG. MS-VBA
raises 481 for them.

## Implementation

|Type or member|Role|
|---|---|
|`FontCallProvider`|Makes the fonts of `New StdFont` and runs their members, each a [`StdFontObject`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Stdole.StdFontObject.html). After a change, tells the server that holds the font, if one does, what it holds now.|
|`PictureCallProvider`|Runs `LoadPicture` and `SavePicture`, and the members of the pictures they make.|
