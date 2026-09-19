# RDCore™
<sup>_Ce document est disponible en [français](./README.fr.md)_</sup>

![VIVAT CUCUMIS](./assets/vivat-cucumis-stonecore.png)

## Before we begin.
> 👋 Hi! New here? _Rubberduck_ was always an open-source initiative. **RDCore honors it with an Open-Core formula**.  
> <small>See [rubberduckvba.ca](https://rubberduckvba.ca) for more information.</small>

This repository contains different projects **under active development** producing different libraries and executables, under a relatively simple licensing model:

- **The RDCore.SDK library** (including its documentation) is licensed under **⚖️MIT**;
- **Everything else** built around it is licensed under **⚖️GPLv3**.

This arrangement protects both the legacy and current contributors while enabling the future: **The RDCore runtime implementation shall remain open-source**.

👉 We're building a solid _language core_ foundation here. The [documentation site](https://rubberduck-vba.github.io/RDCore/index.html) remains the main reference, but the platform is now producing real deliverables: `rdc.exe` carries a workspace from load through parse to symbol definition, end to end, resolved across modules by a real MS-VBAL-ordered symbol resolver.

<a id="projectstatus"/>

## 📊 Project Status
RDCore is in active **pre-alpha** development. The **specification** and **documentation** are the stable deliverables; the platform runs end to end (workspace → parse → symbols) but is not completed nor released yet.

**Contributions**  

- Individuals: ✅ Open ([CLA](CLA.md))  
- Corporate: ⏳ Planned  


[![Build and Test](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml/badge.svg)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml)
[![Coverage](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml)

| Project | Role | Coverage |
|---|---|---|
| **RDCore.Runtime** | RD-VBA runtime semantics + standard library | [![RDCore.Runtime](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.Runtime.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.ParseServer** | Stateless parser | [![RDCore.ParseServer](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.ParseServer.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.SDK** | Language core + shared plumbing | [![RDCore.SDK](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.SDK.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.CLI** | CLI client / environment host | [![RDCore.CLI](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-rdc.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.LanguageServer** | Platform coordinator | [![RDCore.LanguageServer](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.LanguageServer.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.Diagnostics** | Core diagnostics extension | [![RDCore.Diagnostics](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.Diagnostics.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |

✅ Parser and symbol resolution are functional, but the **language server** is still under development.  
👉 LSP features are **[UP FOR GRABS!](https://github.com/rubberduck-vba/RDCore/issues?q=is%3Aissue%20state%3Aopen%20label%3Ardcore-language-server)**


---
### See also
- [Getting Started](https://rubberduck-vba.github.io/RDCore/getting-started.html)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)
- [RD-VBAL](https://rubberduck-vba.github.io/RDCore/specs/rd-vbal.1.0.introduction.html)

<hr/>
<p align='left' style='margin-left: 32px;'>
<a href='https://rubberduck-vba.github.io/RDCore/index.fr.html'>ACCUEIL</a> • <a href='https://rubberduck-vba.github.io/RDCore/index.html'>HOME</a>  | ℹ️ <a href='https://rubberduck-vba.github.io/RDCore/introduction.fr.html'>BIENVENUE</a> • <a href='https://rubberduck-vba.github.io/RDCore/introduction.html'>WELCOME</a>  | 🧩 <a href='https://rubberduck-vba.github.io/RDCore/getting-started.fr.html'>BÂTISSONS</a> • <a href='https://rubberduck-vba.github.io/RDCore/getting-started.html'>BUILD</a>  | <a href='https://rubberduck-vba.github.io/RDCore/specs/rd-vbal.html'><strong>RD-VBAL</strong></a>  |  <a href='https://rubberduck-vba.github.io/RDCore/api/RDCore.SDK.Model.Errors.VBCompileErrorId.html'>SDK</a>  | 🌐 <a href='https://rubberduckvba.ca'>rubberduckvba.ca</a>
</p>
<hr/>
<p align='center'><img alt='Logo™ 9562-7303 Québec inc.' src='./assets/vector-ducky.svg' style='width:200px; align:center;' /></p>
<h6 align='center'>V I V A T ❤️ C U C U M I S ™</h6>
<p align='center' style='font-size:8pt;'>
<small>© Copyright <strong>9562-7303 Québec inc.</strong> (2026)<br/><em>Seul, &quot;Rubberduck&quot; est utilisé pour fins de référence au projet open-source legacy <strong>utilisé publiquement ainsi depuis 2015</strong> et sans lien ni affiliation avec tout tiers détenteur d'une marque semblable dans quelque juridiction que ce soit.<br/>&quot;Rubberduck VBA&quot;, &quot;RDCore&quot; et &quot;VIVAT CUCUMIS&quot; sont des marques de commerce revendiquées par 9562-7303 Québec inc. (en attente); Toutes les marques appartiennent à leur détenteur respectif.<br/>RDCore n'est pas un produit de Microsoft et n'est pas affilié à Microsoft, ni directement, ni indirectement.<br/><br/>If used alone, <em>&quot;Rubberduck&quot; is used as a reference to the legacy open-source project <strong>the same way it has been used publicly since 2015</strong> and without any links or affiliation with any third-party trademark holders of a similar trademark in any jurdisdiction.<br/>&quot;Rubberduck VBA&quot;, &quot;RDCore&quot; and &quot;VIVAT CUCUMIS&quot; are trademarks claimed by 9562-7303 Québec inc. (pending). All trademarks belong to their respective owners.<br/>RDCore is not a Microsoft product and is not affiliated with Microsoft, directly or indirectly.</small>
</p>
