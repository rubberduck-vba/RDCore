# VB6 projects
<sup>_Ce document est disponible en [français](vb6-projects.fr.html)_</sup>

RDCore works on a _workspace_: a folder with a project file, `.rdproj`, that names the project's modules, other files and references. A VBA project
gets one with `rdc new` (see [Bringing a VBA project over](getting-started.md#bringing-a-vba-project-over)). A Visual Basic 6 project gets one from its
`.vbp`.

---
## Importing a `.vbp`

```
rdc import <path>
```

`<path>` is the `.vbp`, or a folder that holds one. The `.rdproj` is written next to the `.vbp`, which is left as it is. The `.rdproj` records the
`.vbp` it was made of (`SourceProject`), which makes the workspace a VB6 one for an editor that does not name the language itself. `rdc run`, `rdc bench` and `rdc build` take the language from it too, whatever the setting says, and a program `rdc build` makes runs as VB6.

|From the `.vbp`|In the `.rdproj`|
|---|---|
|The project's name|The project's name.|
|Modules, classes and forms|Modules.|
|User controls, property pages, user documents, designers, related documents, the resource file|Files of the project, which are not compiled.|
|References|References to the libraries the platform has a description of, by GUID and version.|
|Conditional compilation arguments (`CondComp`)|The project's precompiler constants.|

What cannot be carried over is skipped, and the import says what and why:

- a reference to a library the platform has no description of (`rdc export-library` can describe one; see [The shell and the rdc commands](rdc.md#commands));
- a reference to another project;
- an ActiveX control: the platform hosts none;
- a file outside the project's folder;
- the startup object and the project's type;
- a `CondComp` line that VB6 itself refuses.

The complete rules are in [**RD-VBAL §2.2.3.5** Importing a VB6 Project](specs/rd-vbal.2.2.3.projectfile.md#2235-importing-a-vb6-project).

### Importing again

After a change to the project in the VB6 IDE, import the `.vbp` again: `rdc import --force <path>`. The `.vbp` replaces what the `.rdproj` says of the
name, the references, the files and the conditional compilation arguments. The `.rdproj` keeps the rest, such as its settings files. Without `--force`,
`rdc import` keeps an existing `.rdproj`.

---
## The language

The platform serves a workspace as VBA unless its client names another language. A client names VB6 as `vb6`
([**RD-VBAL §2.0.2.1.1** The language](specs/rd-vbal.2.0.2.client-server-capabilities.md#20211-the-language)).

Served as VB6, a project has two libraries it does not list as references, as in the VB6 IDE:

- `VBRUN`: the constants of every VB6 project (`vbTwips`, `vbHourglass`, `vbChecked`, `vbLeftButton`) and the `PropertyBag` class;
- `VB`: `Form`, its controls, and the `App`, `Screen`, `Printer`, `Clipboard` and `Forms` objects.

A form's controls are names of its module. A control of a control array is named with its index: `Command1(0)`.

A control array, of menu items or of other controls, has `Count`, `LBound` and `UBound`, and `For Each` goes through its elements in the order of their
indexes. Its elements share one handler of each event, which takes the index of the element first: `Private Sub mnuRecent_Click(Index As Integer)`. An
element's `Index` is its index in the array; a control of no array raises 343 for it.

`Load picCell(3)` adds the element of index 3, and `Unload picCell(3)` takes out an element the program loaded. A loaded control is a hidden copy of the
element of the lowest index; a loaded menu item goes among the others in the order of their indexes. They raise what VB6 raises: 360 for an element that is
loaded already, 341 for a negative index, 6 for one above 32767, 340 for an index no element has, 362 for an element of the form's file, and 361 for a menu
item that is in no array.

A ListBox's and a ComboBox's items are reached as in VB6, on a list of the form's file and on one that `Controls.Add` adds: `AddItem`, `RemoveItem`,
`Clear`, `List(Index)`, `ItemData(Index)`, `ListIndex`, `ListCount`, `NewIndex` and `Text`, and a ListBox's `Selected(Index)` and `SelCount`. They raise
what VB6 raises: 5 for an `AddItem` index outside 0 to `ListCount` and a `RemoveItem` one outside the items, 381 for an `ItemData` or `Selected` one
outside the items, 380 for a `ListIndex` outside -1 to `ListCount` - 1, and 383 for an assignment to `ListCount`, `NewIndex` or `SelCount`. `Column` is
an MSForms ListBox's, and raises 438.

A control's `Font` is a `StdFont`, the same object on every read. A change through it changes every control that holds it, and
`Set Text2.Font = Text1.Font` shares the font. `Text1.Font = "Arial"` sets its `Name`, its default member, and a `Set` to `Nothing` raises 380.
`FontBold`, `FontItalic`, `FontName`, `FontSize`, `FontStrikethru` and `FontUnderline` are the `Font`'s `Bold`, `Italic`, `Name`, `Size`,
`Strikethrough` and `Underline`.

A form and a PictureBox draw with `Cls`, `Line`, `Circle`, `PSet`, `Point`, `PaintPicture`, `Scale`, `CurrentX` and `CurrentY`, in their own scale.
`PaintPicture` draws a picture of the program's, such as one `LoadPicture` gives, or another control's `Picture`. It raises what VB6 raises: 91 for
`Nothing`, and, given late bound, 13 for a value that is not an object and 481 for an object that is not a picture.

A control's `Container` is the form, Frame or PictureBox that holds it. `Set Command1.Container = Frame1` moves the control, with what it holds, into
another container of the same form. It keeps its place in pixels, and its `Left` and `Top` read in the new container's scale. `Nothing`, a control that
holds no others (a TextBox), the control itself, a control it holds, and another form or its controls raise 425. A Timer has no `Container` (438),
and an assignment without `Set` raises 438.

> [!NOTE]
> **Not implemented.**
> - A form or a PictureBox that is not shown draws nothing, and its `Point` reads -1, whatever its `AutoRedraw`. VB6 draws on one whose `AutoRedraw`
>   is True.
> - `TypeName` of a control's `Font` is `StdFont`, where VB6 gives `Font`.
> - A `New StdFont` cannot be given to a control's `Font`.

---
## Running a VB6 project

The import does not carry the startup object over. `rdc run` and `rdc build` start the program at the `Sub Main` of its one standard module that declares
one, or at the procedure the `.rdproj` names in its `build` section:

```json
"build": { "entryPoint": "Module1.Main" }
```

See [Running a program](rdc.md#running-a-program).

---
[ACCUEIL](index.fr.md) • [HOME](index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.html) | 🧩 [BÂTISSONS](getting-started.fr.md) • [BUILD](getting-started.html) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
