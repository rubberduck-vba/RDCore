# RDCore
<sup>_Ce document est disponible en [français](./README.fr.md)_</sup>

![VIVAT CUCUMIS](./assets/vivat-cucumis-stonecore.png)

## Before we begin.
> 👋 Hi! New here? _Rubberduck_ was always an open-source initiative. **RDCore honors it with an Open-Core formula**.  
> <small>See [rubberduckvba.ca](https://rubberduckvba.ca) for more information.</small>

This repository contains different projects **under active development** producing different libraries and executables, under one licence:

- **Everything in this repository** is licensed under **⚖️GPLv3 or later**. It builds on the [RDCore.SDK](https://github.com/rubberduck-vba/RDCore.SDK) packages, which are licensed under **⚖️MIT** and published on NuGet.
- **Your projects are yours:** the VBA and RD-VBA code you write, and whatever RDCore builds from it, belong to you, to license as you please (see [NOTICE.md](NOTICE.md)).

This arrangement protects both the legacy and current contributors while enabling the future: **The RDCore runtime implementation shall remain open-source**.

👉 We're building a solid _language core_ foundation here. The [documentation site](https://rubberduck-vba.github.io/RDCore/index.html) remains the main reference, but the platform is now producing real deliverables: `rdc.exe` carries a workspace from load through parse to symbol definition, end to end, resolved across modules by a real MS-VBAL-ordered symbol resolver — and `rdc.exe` with no arguments is an interactive RD-VBA shell that runs what you type on it.

<a id="projectstatus"/>

## 📊 Project Status
RDCore is in active **alpha** development. The **specification** and **documentation** are the stable deliverables; the platform runs end to end (workspace → parse → symbols → execution) but is not completed yet. It is published as [prereleases](https://github.com/rubberduck-vba/RDCore/releases), for evaluation: see [Getting started](https://rubberduck-vba.github.io/RDCore/getting-started.html).

**Contributions**  

- Individuals: ✅ Open ([CLA](CLA.md))  
- Corporate: ⏳ Planned  


[![Build and Test](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml/badge.svg)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml)
[![Coverage](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml)

| Project | Role | Coverage |
|---|---|---|
| **RDCore.Runtime** | RD-VBA runtime semantics + standard library | [![RDCore.Runtime](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.Runtime.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.Runtime.LanguageCore** | The language core: name binding, static semantics, the instruction set, and the standard library's declarations | [![RDCore.Runtime.LanguageCore](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.Runtime.LanguageCore.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.Runtime.Support** | The session a program runs in, and the runner `rdc run` and a built program share | [![RDCore.Runtime.Support](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.Runtime.Support.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.External** | Outside world abstractions: automation servers, native libraries | [![RDCore.External](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.External.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.External.Windows** | Outside world on Windows: COM automation, BSTR/VARIANT and ordinals for native calls | [![RDCore.External.Windows](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.External.Windows.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.ParseServer** | Stateless parsing server, around RDCore.Parsing | [![RDCore.ParseServer](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.ParseServer.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.Parsing** | The VBA parser: grammar, syntax trees | [![RDCore.Parsing](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.Parsing.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.Symbols** | Workspace symbols built from syntax trees | [![RDCore.Symbols](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.Symbols.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.CLI** | CLI client / environment host | [![RDCore.CLI](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-rdc.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.LanguageServer** | Platform coordinator | [![RDCore.LanguageServer](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.LanguageServer.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.Diagnostics** | Core diagnostics extension | [![RDCore.Diagnostics](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.Diagnostics.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |

✅ Parser, symbol resolution and the **language server** are functional. The server answers diagnostics (pull and push), completion, signature help,
hover, inlay hints, semantic tokens, document and workspace symbols, folding and selection ranges, go to definition, declaration, type definition and
implementation, references and highlights, call and type hierarchy, rename and code actions.

### Performance

The time RD-VBA takes to run the `rdc bench` corpus, as a multiple of the time MS-VBA takes for the same code in Excel (below 1× is faster). The RD-VBA
figures are measured on every merge, against a reference run of MS-VBA on Excel 16 (64-bit), Windows 11 x64, 12 cores. The Excel automation cases need
Excel, so they come from the reference run only. [Every case, and how they are measured](https://rubberduck-vba.github.io/RDCore/performance.html).

| Group | Fastest | Median | Slowest |
|---|---|---|---|
| Calls | ![Calls, fastest](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-calls-best.json) | ![Calls, median](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-calls-median.json) | ![Calls, slowest](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-calls-worst.json) |
| Statements | ![Statements, fastest](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-statements-best.json) | ![Statements, median](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-statements-median.json) | ![Statements, slowest](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-statements-worst.json) |
| Arrays | ![Arrays, fastest](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-arrays-best.json) | ![Arrays, median](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-arrays-median.json) | ![Arrays, slowest](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-arrays-worst.json) |
| Strings | ![Strings, fastest](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-strings-best.json) | ![Strings, median](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-strings-median.json) | ![Strings, slowest](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-strings-worst.json) |
| Numeric | ![Numeric, fastest](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-numeric-best.json) | ![Numeric, median](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-numeric-median.json) | ![Numeric, slowest](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-numeric-worst.json) |
| Variables and parameters | ![Variables and parameters, fastest](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-slots-best.json) | ![Variables and parameters, median](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-slots-median.json) | ![Variables and parameters, slowest](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-slots-worst.json) |
| Standard library | ![Standard library, fastest](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-stdlib-best.json) | ![Standard library, median](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-stdlib-median.json) | ![Standard library, slowest](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-stdlib-worst.json) |
| Excel automation (reference run) | ![Excel automation, fastest](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-excel-best.json) | ![Excel automation, median](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-excel-median.json) | ![Excel automation, slowest](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-excel-worst.json) |


---
### See also
- [Getting Started](https://rubberduck-vba.github.io/RDCore/getting-started.html)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)
- [RD-VBAL](https://rubberduck-vba.github.io/RDCore/specs/rd-vbal.1.0.introduction.html)
- [RDCore.SDK](https://github.com/rubberduck-vba/RDCore.SDK): the language model and platform plumbing that every RDCore component and extension builds on
- [RDCore.HexIDE](https://github.com/rubberduck-vba/RDCore.HexIDE): the add-in that brings RDCore into HexIDE
- [HexIDE](https://github.com/hexide-io/HexIDE): the IDE for VB developers that RDCore.HexIDE extends

<hr/>
<p align='left' style='margin-left: 32px;'>
<a href='https://rubberduck-vba.github.io/RDCore/index.fr.html'>ACCUEIL</a> • <a href='https://rubberduck-vba.github.io/RDCore/index.html'>HOME</a>  | ℹ️ <a href='https://rubberduck-vba.github.io/RDCore/introduction.fr.html'>BIENVENUE</a> • <a href='https://rubberduck-vba.github.io/RDCore/introduction.html'>WELCOME</a>  | 🧩 <a href='https://rubberduck-vba.github.io/RDCore/getting-started.fr.html'>BÂTISSONS</a> • <a href='https://rubberduck-vba.github.io/RDCore/getting-started.html'>BUILD</a>  | <a href='https://rubberduck-vba.github.io/RDCore/specs/rd-vbal.html'><strong>RD-VBAL</strong></a>  |  <a href='https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html'>SDK</a>  | 🌐 <a href='https://rubberduckvba.ca'>rubberduckvba.ca</a>
</p>
<hr/>
<p align='center'><img alt='Logo™ 9562-7303 Québec inc.' src='./assets/vector-ducky.svg' style='width:200px; align:center;' /></p>
<h6 align='center'>V I V A T ❤️ C U C U M I S ™</h6>
<p align='center' style='font-size:8pt;'>
<small>© Copyright <strong>9562-7303 Québec inc.</strong> (2026)<br/><em>Seul, &quot;Rubberduck&quot; est utilisé pour fins de référence au projet open-source legacy <strong>utilisé publiquement ainsi depuis 2015</strong> et sans lien ni affiliation avec tout tiers détenteur d'une marque semblable dans quelque juridiction que ce soit.<br/>&quot;Rubberduck VBA&quot;, &quot;RDCore&quot; et &quot;VIVAT CUCUMIS&quot; sont des marques de commerce revendiquées par 9562-7303 Québec inc. (en attente); Toutes les marques appartiennent à leur détenteur respectif.<br/>RDCore n'est pas un produit de Microsoft et n'est pas affilié à Microsoft, ni directement, ni indirectement.<br/><br/>If used alone, <em>&quot;Rubberduck&quot; is used as a reference to the legacy open-source project <strong>the same way it has been used publicly since 2015</strong> and without any links or affiliation with any third-party trademark holders of a similar trademark in any jurdisdiction.<br/>&quot;Rubberduck VBA&quot;, &quot;RDCore&quot; and &quot;VIVAT CUCUMIS&quot; are trademarks claimed by 9562-7303 Québec inc. (pending). All trademarks belong to their respective owners.<br/>RDCore is not a Microsoft product and is not affiliated with Microsoft, directly or indirectly.</small>
</p>
