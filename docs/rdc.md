# The shell and the rdc commands
<sup>_Ce document est disponible en [français](rdc.fr.html)_</sup>

`rdc.exe`, in the `RDCore.CLI` folder of the platform, is RDCore's command-line program. Started alone, it opens the RD-VBA shell. Given a command, it
runs that command and exits.

|Command line|What it does|
|---|---|
|`rdc`|Opens the shell on a scratch workspace of its own, deleted when the shell exits.|
|`rdc --workspace <path>`|Opens the shell on a workspace (see [Bringing a VBA project over](getting-started.md#bringing-a-vba-project-over)).|
|`rdc <command> [<options>]`|Runs a command (below), without the shell.|
|`rdc --help`|Lists the options `rdc` itself takes.|
|`rdc <command> --help`|Lists the options of that command.|

A command `rdc` does not know is answered with the list of the commands it knows.

---
## The shell

The shell runs what you type. A line with no line number runs right away: `Debug.Print 6 * 7`. A line that starts with a line number is written into
the _current program_: `10 Debug.Print "Hello"`. The current program is the body of an implicit procedure named `Main`, in an implicit module named
`Program`. It stays in memory until `NEW`, `LOAD` or `EXIT`.

On a workspace (`rdc --workspace <path>`), the shell runs each line in the workspace's project: `Main`, `Program.Main` and the other public procedures
of the project can be called from the prompt. There is no current program. A numbered line, and the commands that work on the program (`LIST`,
`FOLD`, `NEW`, `SAVE`, `LOAD`, `RUN`, `ANALYZE`, `DUCKY`, and `CONT` to a line), answer `NO PROGRAM ERROR`.

On a workspace, `BREAK` takes a module and a line of its file: `BREAK Other 9`, `BREAK Other 9 I > 5`, `BREAK TOGGLE Other 9`. `BREAK` alone lists them
as `Other 9`, and `BREAK CLEAR` removes them all. A module the workspace does not have answers `UNDEF'D MODULE ERROR`, and a line no statement begins on
answers `NO STATEMENT AT Other 9`.

The shell's language is RDCore BASIC: a variable one line assigns is the one the next line reads, without a `Dim`, and `Print` alone is a statement.

|Command|What it does|
|---|---|
|`HELP`|Lists the shell commands.|
|`LIST`|Shows the program, formatted, or a range of its lines: `LIST 100-200`.|
|`FOLD <line#>`|Folds the block that starts at a line (an `If`, a `Select Case`, a loop, a `With`) in a listing, or unfolds it.|
|`INLAYS ON`, `INLAYS OFF`|Shows the language server's hints in a listing (the parameter each argument is given to, the type of a declaration that states none), or hides them.|
|`NEW`|Clears the program.|
|`SAVE [<name>]`, `LOAD <name>`|Writes the program to a `.rdc` text file, or reads it from one. `SAVE` alone writes to the file the program came from.|
|`RUN`|Runs the program.|
|`ANALYZE`|Analyzes the program and reports its [diagnostics](diagnostics/index.md).|
|`BREAK <line#> [<condition>]`|Sets or clears a breakpoint at a line; with a condition, it waits on it: `BREAK 40 I > 5`. `BREAK TOGGLE <line#>` turns one off or on again, `BREAK` alone lists them, and `BREAK CLEAR` removes them all.|
|`CONT [<line#>]`|Goes on from a stop, or from a line: `CONT 200`.|
|`STEP`, `STEP INTO`, `STEP OUT`|Runs one statement of the stopped program: over calls, into them, or to the end of the current procedure.|
|`STACK`|Shows the procedures the stopped program is in, innermost first.|
|`VARS [<n>]`, `FRAME <n>`|Shows the variables of the stopped program. `VARS 1`, or `FRAME 1` then `VARS`, is about the procedure that called.|
|`EDITS`|Shows the edits made at a stop that are not applied to the program, and why (see [Edit and Continue](edit-and-continue.md)).|
|`DUCKY [<line#>]`|Proposes what can be done about the program, or one of its lines, and does what you choose.|
|`CONFIG`|Shows your settings and sets them, with completion. Formatting changes apply right away.|
|`THEME LIST`, `THEME <name>`|Lists the shell's color themes, or picks one.|
|`CLEAR`|Clears the console.|
|`MEMORY`, `PEEK <address>`, `POKE <address>, <byte>`|Shows a summary of the session's memory, reads a byte of it, or writes one.|
|`EXIT`|Ends the session and exits. `BYE` and `QUIT` do the same.|

`Ctrl+C` stops a running program. At a stop (a `Stop` statement, a failed `Debug.Assert`, a breakpoint or `Ctrl+C`) the shell lists the two lines above
the stopped line and the two below it, with the stopped line highlighted, then says `BREAK IN` and the line number. A run-time error that nothing
handles stops the program at the statement that raised it, as in the VBIDE: the shell lists the lines around that statement the same way, then shows
the error. `CONT` runs the statement again, and `END` ends the program. An error that an `On Error` statement handles does not stop it. A step lists
nothing: it says only `BREAK IN` and the line number, or shows the error it stopped at. `CONFIG CLI.BreakListingLines`
sets how many lines are listed on each side, from the next start of the shell; `0` lists none. At a stop you can type statements as in the VBIDE's
immediate pane, and edit the program before you go on.

A line typed at the prompt runs under the debugger as `RUN` does: a `Stop`, a breakpoint or an error in the code it calls stops it there, and `STACK`
shows the line's own procedure under the one that stopped. A stop in a module of a workspace lists the module's lines around it as `LIST` lists the
program, then says `BREAK IN`, the module and its line. A stop at an error in a module of a workspace lists the module's lines the same way, then shows the error with the module and its line.

> [!NOTE]
> The shell is a way to try the platform. Its current program is a single procedure: a project with more procedures runs in the shell on a workspace.
> The shell does not handle BASIC-style line numbers in your own modules.

---
## Commands

Each command takes a workspace as a folder, with or without a `.rdproj`, or as a single module file. Without one, it works on the current folder.

|Command|Alias|What it does|
|---|---|---|
|`rdc new <path>`|`n`|Makes a workspace of a folder: writes a `.rdproj` that names each module file the folder holds. `--name` sets the project's name (the folder's by default), `--empty` writes a project with no files, and `--force` replaces an existing `.rdproj`.|
|`rdc import <path>`|`i`|Makes the `.rdproj` of a VB6 project, next to its `.vbp` (see [VB6 projects](vb6-projects.md)). `--force` replaces an existing `.rdproj`.|
|`rdc run [<path>] [-- <arguments>]`|`r`|Runs the workspace's program (below). What follows `--` is the program's command line, which `Command$` returns.|
|`rdc build [<path>]`|`bd`|Builds the workspace into a program: its executable, and the runtime it runs on (below).|
|`rdc config`|`configure`|Shows the platform's settings and sets them, as the shell's `CONFIG` does, without starting the platform.|
|`rdc export-library <library>`|`xl`|Describes a type library of this machine (a file such as `scrrun.dll`, its identifier, or its name) as a file a project can reference by name. `--dependencies` describes the libraries it depends on too.|
|`rdc compilation [<path>]`|`cc`|Says, for each procedure, whether it runs compiled, and what keeps it in the interpreter.|
|`rdc bench [<path>]`|`b`|Times the interpreter on a workspace: its parse, its load, its analysis, then each `Public Sub Bench*()` of its standard modules.|
|`rdc describe-ext <extension>`|`x`|Writes the manifest of a platform extension. It is for extension authors (see [Building on RDCore](building-on-rdcore.md)).|
|`rdc install`||Installs the platform `rdc` runs from for your user account, and puts `rdc` on your PATH (below). It takes no workspace.|

### Running a program

`rdc run` runs the program's entry point:

- the procedure the `.rdproj` names as its entry point, `Module.Procedure` or `Procedure`;
- otherwise, the `Sub Main` of the one standard module that declares one.

A project with no entry point, or with a `Sub Main` in more than one standard module, does not run. Neither does one with a module that does not load. `Debug.Print`
writes to the standard output. A `MsgBox` or an `InputBox` is a dialog box on Windows. On Linux and macOS it is drawn in the console and answered there, as in the shell.

|Option|What it does|
|---|---|
|`-c`, `--configuration`|`Debug` (the default) or `Release`.|
|`--unattended`|Nobody answers a dialog: a `MsgBox` or an `InputBox` fails the run.|

The exit code says how the run went:

|Exit code|Meaning|
|---|---|
|`0`|The program ended.|
|`1`|A run-time error ended it. The error's number and description are written to the standard error.|
|`2`|It could not start: no entry point, or a module that does not load.|

### Building a program

`rdc build` compiles the workspace into a program that runs without RDCore's parser. It needs the same entry point as `rdc run`.

|Option|What it does|
|---|---|
|`-o`, `--output`|The folder the program is written to; `bin/<configuration>/<runtime>/` in the workspace by default.|
|`-n`, `--name`|The program's name; the project's by default.|
|`-c`, `--configuration`|`Debug` (the default) or `Release`.|
|`-r`, `--runtime`|The platform the program is built for, such as `win-x64` or `linux-x64`; the one `rdc` runs on by default.|

The program shows its dialogs as `rdc run` does. Run with `--unattended` among its arguments, it fails at the first `MsgBox`, `InputBox` or form it shows. The switch stays in `Command$` and in the arguments of `Main`.

### Installing the platform

`rdc install` installs the platform it runs from, such as a release archive you unpacked, for your user account. The IDE add-in installs the
platform in the same place, so each one uses what the other installed.

- It copies the platform into `RDCore/Platform/versions/<version>/` in your local application data folder: `%LOCALAPPDATA%` on Windows,
  `~/.local/share` on Linux, `~/Library/Application Support` on macOS. A version that is installed already is not copied again, and no version
  is removed.
- It makes that version the current one, unless the current version is newer.
- It puts the current version's `rdc` on your PATH, in place of an earlier version's. On Windows, it adds the version's `RDCore.CLI` folder to
  your user `Path`. On Linux and macOS, it links `~/.local/bin/rdc` to it; when `~/.local/bin` is not on your PATH, it adds a block of lines that
  puts it there to `~/.profile` (Linux) or `~/.zprofile` (macOS). Open a new terminal to use it.

|Option|What it does|
|---|---|
|`--path-only`|Only puts the current version's `rdc` on your PATH.|
|`--no-modify-path`|Installs the platform without changing your PATH or your profile.|

The exit code says how the installation went:

|Exit code|Meaning|
|---|---|
|`0`|The platform is installed.|
|`1`|It is not: the folder `rdc` runs from is not a whole platform, no version is installed (`--path-only`), or `~/.local/bin/rdc` is a file of your own. The message says which.|
|`2`|The options are not valid.|

---
[ACCUEIL](index.fr.md) • [HOME](index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.html) | 🧩 [BÂTISSONS](getting-started.fr.md) • [BUILD](getting-started.html) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
