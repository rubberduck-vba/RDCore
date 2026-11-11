# Getting started with RDCore
<sup>_Ce document est disponible en [français](getting-started.fr.html)_</sup>

> [!NOTE]
> **The platform is in alpha.** Expect rough edges, and expect things to change between versions.

---
## Getting the platform

Download the archive for your system from RDCore's [releases](https://github.com/rubberduck-vba/RDCore/releases): `rdcore-<version>-win-x64.zip` for Windows, or a `.tar.gz` for Linux and macOS. The platform needs the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) for your system's architecture.

1. Extract the archive into a folder of its own. On Linux and macOS, extract it with `tar -xzf`, which keeps the programs' execute bit. On macOS, then clear the folder's quarantine: `xattr -dr com.apple.quarantine <folder>`.
1. Open a terminal in the extracted folder's `RDCore.CLI` folder and run `.\rdc install` (Windows) or `./rdc install` (Linux and macOS). It installs the platform for your user account and puts `rdc` on your PATH (see [Installing the platform](rdc.md#installing-the-platform)).
1. Open a new terminal: `rdc` runs from any folder. You can then delete the extracted folder.

The programs are not code-signed, so on Windows SmartScreen may warn about them.

To build the platform from its source instead, with the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), see [Building the platform from source](building-on-rdcore.md#building-the-platform-from-source). `rdc install` installs a platform you built the same way, from its `RDCore.CLI` folder.

---
## The RD-VBA shell

Run **`rdc`** in a terminal. If all goes well, you see the platform's splash screen:

![RDCore CLI splash screen](images/rdvba-cli-splash.png)

From here you are somewhere between the program mode of _Commodore 64 BASIC_, the _immediate_ pane of the VBIDE, and a modern editor:

- Type `HELP` for the list of commands.
- Type any RD-VBA statement to run it right away: `Debug.Print 6 * 7`.
- Start a line with a _line number_ to write it into the _current program_: `10 Debug.Print "Hello"`, then `LIST` it and `RUN` it.

Every shell command, and the commands `rdc` runs without the shell, are in [The shell and the rdc commands](rdc.md).

---
## Bringing a VBA project over

RDCore works with your project's source files, exported from the VBA editor. Legacy Rubberduck exports a whole project in one click; the VBIDE exports one module at a time, unless you script it against the _VBIDE Extensibility_ library.

Put the exported files in a folder of their own, then make it an RDCore workspace. From the `RDCore.CLI` folder:

```
rdc new <path>
```

This writes a `.rdproj` file into that folder, naming each source file it finds. Files it doesn't recognize are listed under "other files": they're still part of the workspace, just not source code.

Then open the shell on it with `rdc --workspace <path>`.

A Visual Basic 6 project gets its `.rdproj` from its `.vbp` instead: see [VB6 projects](vb6-projects.md).

---
## Using RDCore from an IDE

The shell is fun, but the real work happens in the language server behind it, and a language server needs an editor. We recommend **[HexIDE](https://github.com/hexide-io/HexIDE)**, which was made for VB developers: see [RDCore in HexIDE](hexide.md).

---
## Good to know

- **RDCore is a 64-bit process**, so what it loads must be 64-bit too:
  - UserForms need 64-bit Office (the 64-bit Microsoft Forms 2.0);
  - a `Declare` statement's library must be a 64-bit DLL;
  - Office automation (`CreateObject("Excel.Application")`) needs the Office application installed.
- **Your settings** are kept in `%APPDATA%\RDCore`, and the **logs** in `%LOCALAPPDATA%\RDCore\Logs`. Nothing is written into the platform's folder, so updating means replacing that folder.
- **RDCore speaks English and French**, and follows your machine's display language. `CONFIG Environment.Regional.Language fr` (or `en`) picks one the next time RDCore starts. It changes no number or date formatting.
- **Your projects are yours**: the GPLv3 covers RDCore itself, not the code you write or what RDCore builds from it (see [NOTICE.md](https://github.com/rubberduck-vba/RDCore/blob/main/NOTICE.md)).
- **Want to contribute, or build on the platform?** See [Building on RDCore](building-on-rdcore.md).

---
[ACCUEIL](index.fr.md) • [HOME](index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.html) | 🧩 [BÂTISSONS](getting-started.fr.md) • BUILD | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
