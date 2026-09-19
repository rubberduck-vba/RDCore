# RDCore™
<sup>_This document is available in [English](./README.md)_</sup>

![VIVAT CUCUMIS](./assets/vivat-cucumis-stonecore.png)

## Avant de commencer.
> 👋 Bonjour! Nouveau ici? _Rubberduck_ a toujours été une initiative open-source. **RDCore l'honore avec une formule Open-Core**.  
> <small>Voir [rubberduckvba.ca](https://rubberduckvba.ca) pour plus d'informations.</small>

Ce dépôt contient différents projets **en cours de développement** produisant diverses bibliothèques et exécutables, sous un modèle de licence relativement simple :

- **La bibliothèque RDCore.SDK** (incluant sa documentation) est sous licence **⚖️MIT**;
- **Tout le reste** construit autour d'elle est sous licence **⚖️GPLv3**.

Cet arrangement protège les contributeurs historiques et actuels tout en permettant l'avenir : **l'implémentation du runtime RDCore demeurera open-source**.

👉 Nous construisons ici une base solide pour le coeur du langage. Le [site de documentation](https://rubberduck-vba.github.io/RDCore/index.fr.html) reste la référence principale, mais la plateforme produit désormais des livrables réels : `rdc.exe` conduit un workspace du chargement à l'analyse jusqu'à la définition des symboles, résolus à travers les modules par un véritable symbole-resolver ordonné selon MS-VBAL.

<a id="projectstatus"/>

## 📊 Statut du projet
RDCore est en développement actif **pré-alpha**. La **spécification** et la **documentation** sont les livrables stables; la plateforme s'exécute de bout en bout (workspace → parse → symbols) mais n'est pas terminée ni publiée.

**Contributions**

- Individuelles : ✅ Ouvertes ([CLA](CLA.md))  
- Corporatives : ⏳ Planifié  


[![Build and Test](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml/badge.svg)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml)
[![Coverage](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml)

| Projet | Rôle | Couverture |
|---|---|---|
| **RDCore.Runtime** | Sémantiques d'exécution RD-VBA + bibliothèque standard | [![RDCore.Runtime](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.Runtime.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.ParseServer** | Parser sans état | [![RDCore.ParseServer](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.ParseServer.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.SDK** | Coeur du langage + plomberie partagée | [![RDCore.SDK](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.SDK.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.CLI** | Client CLI / hôte d'environnement | [![RDCore.CLI](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-rdc.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.LanguageServer** | Coordonnateur de la plateforme | [![RDCore.LanguageServer](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.LanguageServer.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.Diagnostics** | Extension d'inspection de base | [![RDCore.Diagnostics](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.Diagnostics.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |

✅ Le parser et la résolution de symboles sont fonctionnels, mais le **language server** est encore en développement.  
👉 Les fonctionnalités LSP sont **[UP FOR GRABS!](https://github.com/rubberduck-vba/RDCore/issues?q=is%3Aissue%20state%3Aopen%20label%3Ardcore-language-server)**


---
### Voir aussi
- [Getting Started](https://rubberduck-vba.github.io/RDCore/getting-started.html)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)
- [RD-VBAL](https://rubberduck-vba.github.io/RDCore/specs/rd-vbal.1.0.introduction.html)


> [!NOTE]
> La version française des documents techniques, lorsque disponible, utilise les termes originaux _en anglais_ qui conservent la précision de leur signification, plutôt qu'une traduction approximative qui pourrait facilement être plus confondante qu'utile.

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
