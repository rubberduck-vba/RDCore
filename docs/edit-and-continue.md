# Edit and Continue
<sup>_Ce document est disponible en [français](edit-and-continue.fr.html)_</sup>

---
## ✏️ What it is

Your program is stopped: at a breakpoint, at a `Stop` statement, or after a step. You read its variables, see what went wrong, and change the code right
there. Then you go on: `CONT` in the REPL, **Continue** or a step in the debugger. The program picks up where it was, running your edited code, without
starting over.

The VBA editor in Office does some of this too. But many edits make it reset the project ("This action will reset your project, proceed anyway?"), and
everything the program had built up so far is lost. RD-VBA applies every edit it can apply correctly, which is more than the VBA editor does. When an edit
can't be applied, RD-VBA doesn't end your program: it keeps waiting where it is, tells you which edit is in the way and why, and lets you choose. You can take
the edit back and go on, or run the program again.

## ✅ What applies when you go on

**Code you're not running right now:**
- The body of a procedure the program isn't in. Its next call runs the new code. This holds for a method of a class too, even while objects of the class
  are alive.

<!-- S2: the four bullets below depend on the active-frame body remap. -->
**The procedure you're stopped in, and the procedures that called it:**
- Edit any statement of the procedure you're stopped in. A line you edit above the stop isn't run again. A line you edit, insert or delete below it is run
  as it is now, when the program gets there.
- Edit the next line to run, the one marked as the stop (a breakpoint's line, or the line after a `Stop`), and it runs as edited.
- In a procedure that called the one you're in, edit the lines after its call. They run as edited when the call returns. Its call line has to stay as it
  was (see below).
- Lines can move: an edit that adds or removes lines above the stop is applied, and breakpoints follow the lines they were on.

**Declarations you add:**
- A procedure, in a module or a class.
- A module variable (`Public` or `Private`), a `Const`, a `Declare`.
- A `Type` or an `Enum`, or a member added to an `Enum`.

<!-- S2: the changes below are applied in S1 only where they touch no procedure the program is in; where they reach one (a Const or an Option line its body
uses), applying them needs the active-frame body remap. -->
**Other changes the program reads afresh:**
- A procedure, variable, `Const` or `Type` removed, when nothing the program is running still uses it.
- The value of a `Const` or of an `Enum` member, except a `Const` that sizes an array that already exists (see below).
- The parameters of a procedure the program isn't in, and its callers, which are checked again.
- A `Declare` statement changed.
- An `Option` line: `Option Compare`, `Option Base`, `Option Explicit`, `Option Private Module`.

Variables keep their values across every edit that applies. That includes module-level variables, `Static` variables and the fields of your objects.

## ⏸️ What waits until you run again

Some edits can't be applied to a program that's already running, because the program holds something the edit would change underneath it. RD-VBA doesn't
guess, and never applies half of an edit. When **Continue** or a step meets one of these, the program is refused and keeps waiting, and the edit is named
with its reason. Take the edit back and go on, or run the program again to have it.

| You changed… | Why it waits |
|---|---|
| A local variable added to, or removed from, a procedure the program is in | The procedure's variables are laid out already. Applying it is planned for a later version. |
| A field added to a class whose objects the program may hold | Objects made before the edit would have no room for it. Applying it is planned for a later version. |
| A local's type, or a variable's type, while it holds a value | The value would have to be converted under the program. |
| A `Const` that sizes an array, given another value | The array exists already, at its old size. |
| The parameters of a procedure the program is in | Its arguments were passed already, by the old parameter list. |
| The line a calling procedure is in the middle of (its call line) | Half of it has run already. |
| The opening or closing line of a block you're inside: a `For`, a `For Each`, a `With`, a `Select Case`, a `Do` | The loop's counter, the `With` object or the `Select` value was made by the old line. |
| A `GoSub` the program will return from, or the point a `Resume` goes back to | They point into the code as it was. |
| What a class `Implements`, or its `Event`s, while objects of it are alive | Those objects are reached through what the class implements. |

**Code that doesn't compile:** a typo, a renamed procedure that something still calls, a variable that isn't declared under `Option Explicit`. There's no
program to go on with until it compiles again. Fix it, and **Continue** goes on.

## 👀 Where you see it

### In the editor

As soon as you make an edit that won't apply, the editor shows an Information finding on the declaration or procedure you edited:
[**RDC20001** Edit that applies when the program is run again](diagnostics/rdc20001.md). It says why, and goes away when you take the edit back, or when the
program ends.

### In the REPL

`CONT` and `STEP` say which edits are in the way, each with its reason, and the program keeps waiting:

```text
> 10 Debug.Print "before"
> 20 Stop
> 30 Debug.Print "after"
> RUN
  before
  BREAK IN 20
  ✅ READY.
> 15 Dim Added As Long
> CONT
  ⛔ CAN'T CONTINUE
    Edits made while the program waited are not applied to it:
    Program.Main: a local added to or removed from a procedure the program is in, which is not applied to a program that waits yet
  RUN to start again.
  ✅ READY.
```

`EDITS` lists what's in the way at any time, without trying to go on:

```text
> EDITS
  ⚠️ Edits made while the program waited are not applied to it:
    Program.Main: a local added to or removed from a procedure the program is in, which is not applied to a program that waits yet
  RUN to start again.
  ✅ READY.
```

With nothing in the way, or with no program waiting, it says so:

```text
> EDITS
  No edit is waiting: CONT goes on with every edit made.
  ✅ READY.
```

```text
> EDITS
  No program is waiting.
  ✅ READY.
```

### In VS Code's debugger

- **Save the file** to have an edit applied. The debugger reads what's saved when you continue, step or set the next statement, and an edit you haven't
  saved isn't seen.
- When an edit is in the way, **Continue** and the steps are refused: the program stays paused, and the Debug Console says which edits, and why.
- The Problems panel doesn't show RDC20001 for a program VS Code is debugging. The Debug Console carries it there.

## ⚖️ Compared with the VBA editor

Measured in Excel (64-bit) at a `Stop` in a procedure, across 44 kinds of edit:

| Kind of edit | VBA editor | RD-VBA |
|---|---|---|
| A procedure you're not in | goes on | goes on |
| Statements of the procedure you're stopped in, above, at or below the stop | goes on | goes on <!-- S2 --> |
| Statements of a caller, away from its call line | goes on | goes on <!-- S2 --> |
| A caller's call line | **resets** | waits, and names the edit |
| A local added (`Dim`) | goes on | waits, and names the edit (for now) |
| A local removed; a `Static` added | **resets** | waits, and names the edit (for now) |
| A local retyped | **resets** | waits, and names the edit |
| A `Private` module variable, a `Const` or a `Declare` added | goes on | goes on |
| A `Public` module variable added | **resets** | goes on |
| A procedure added, in a module or a class | **resets** | goes on |
| A `Type` or an `Enum` added, or a member added to an `Enum` | **resets** | goes on |
| A `Const`'s value changed | **resets** | goes on, unless it sizes an array (waits) |
| A procedure's parameters changed, for one you're not in | **resets** | goes on |
| The same, for the one you're in | **resets** | waits, and names the edit |
| A `Declare` changed; an `Option` line added or removed | **resets** | goes on |
| A method body of a class with live objects | goes on | goes on |
| A field added to a class with live objects | goes on | waits, and names the edit (for now) |

Where the VBA editor resets, it asks first, and **Cancel** takes the edit back. Where RD-VBA waits, nothing is lost. The program stays where it was, and you
choose: take the edit back and go on, or run again.

## See also

- [**RDC20001** Edit that applies when the program is run again](diagnostics/rdc20001.md)
- [Getting Started](getting-started.md)

---
[ACCUEIL](index.fr.md) • [HOME](index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.html) | 🧩 [BÂTISSONS](getting-started.fr.md) • [BUILD](getting-started.html) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](/RDCore/api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
