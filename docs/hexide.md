# RDCore in HexIDE
<sup>_Ce document est disponible en [français](hexide.fr.html)_</sup>

[HexIDE](https://github.com/hexide-io/HexIDE) is an IDE made for Visual Basic developers. The RDCore add-in for HexIDE,
[RDCore.HexIDE](https://github.com/rubberduck-vba/RDCore.HexIDE), brings RDCore into it.

> [!NOTE]
> **The add-in is pre-alpha.** It runs on HexIDE v0.5.0 and later versions, which have the add-in support it builds on.

---
## What the add-in does

- **Opens RDCore projects.** HexIDE opens a `.rdproj` as a project, and lists its modules, other files and references. The project file is RDCore's:
  the add-in reads it, and follows the changes RDCore's language server writes to it.
- **Runs RDCore's language server** while an RDCore project is open: its [diagnostics](diagnostics/index.md), completion and the other editor
  features come from RDCore.
- **Offers RDCore's debugger to F5.** A program runs and debugs in RDCore's runtime, with breakpoints, stepping and
  [Edit and Continue](edit-and-continue.md) on saved edits.
- **Installs the platform**, with your consent: it downloads the platform from RDCore's releases, checks it, and keeps it up to date.

A VB6 project is opened in HexIDE once it has a `.rdproj` (see [VB6 projects](vb6-projects.md)).

---
## Getting the add-in

Get the add-in from within HexIDE. It is published separately from the platform. Once it is installed, it uses the platform that `rdc install`
installed (see [Getting the platform](getting-started.md#getting-the-platform)), or installs the platform itself, with your consent.

To build the add-in from its source instead:

1. Install the platform, or build it (see [Getting the platform](getting-started.md#getting-the-platform)).
1. Build the add-in and lay it out as a HexIDE add-in package, as its [README](https://github.com/rubberduck-vba/RDCore.HexIDE#building) says. Copy
   the package into the `addins` folder beside HexIDE's executable.
1. If you built the platform, point the add-in at it: in `%LOCALAPPDATA%\RDCore\HexIDE\settings.json`, set `platformRoot` to the platform's folder. With
   it set, the add-in downloads nothing.

A package you build is unsigned, so HexIDE loads it only in developer mode, with unsigned add-ins allowed.

---
[ACCUEIL](index.fr.md) • [HOME](index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.html) | 🧩 [BÂTISSONS](getting-started.fr.md) • [BUILD](getting-started.html) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
