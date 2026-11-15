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
`.vbp` it was made of (`SourceProject`), which makes the workspace a VB6 one for an editor that does not name the language itself.

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
