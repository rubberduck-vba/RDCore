# RDCore
<sup>_This document is available in [English](./README.md)_</sup>

![VIVAT CUCUMIS](./assets/vivat-cucumis-stonecore.png)

## Avant de commencer.
> 👋 Bonjour! Nouveau ici? _Rubberduck_ a toujours été une initiative open-source. **RDCore l'honore avec une formule Open-Core**.  
> <small>Voir [rubberduckvba.ca](https://rubberduckvba.ca) pour plus d'informations.</small>

Ce dépôt contient différents projets **en cours de développement** produisant diverses bibliothèques et exécutables, sous une seule licence :

- **Tout ce que contient ce dépôt** est sous licence **⚖️GPLv3 ou toute version ultérieure**. Il est construit sur les paquets [RDCore.SDK](https://github.com/rubberduck-vba/RDCore.SDK), sous licence **⚖️MIT** et publiés sur NuGet.
- **Vos projets vous appartiennent :** le code VBA et RD-VBA que vous écrivez, et tout ce que RDCore en construit, sont à vous, sous la licence de votre choix (voir [NOTICE.md](NOTICE.md)).

Cet arrangement protège les contributeurs historiques et actuels tout en permettant l'avenir : **l'implémentation du runtime RDCore demeurera open-source**.

👉 Nous construisons ici une base solide pour le coeur du langage. Le [site de documentation](https://rubberduck-vba.github.io/RDCore/index.fr.html) reste la référence principale, mais la plateforme produit désormais des livrables réels : `rdc.exe` conduit un workspace du chargement à l'analyse jusqu'à la définition des symboles, résolus à travers les modules par un véritable symbole-resolver ordonné selon MS-VBAL.

<a id="projectstatus"/>

## 📊 Statut du projet
RDCore est en développement actif **alpha**. La **spécification** et la **documentation** sont les livrables stables; la plateforme s'exécute de bout en bout (workspace → parse → symbols → execution) mais n'est pas encore terminée. Elle est publiée en [préversions](https://github.com/rubberduck-vba/RDCore/releases), pour évaluation : voir [Démarrer avec RDCore](https://rubberduck-vba.github.io/RDCore/getting-started.fr.html).

**Contributions**

- Individuelles : ✅ Ouvertes ([CLA](CLA.md))  
- Corporatives : ⏳ Planifié  


[![Build and Test](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml/badge.svg)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml)
[![Coverage](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml)

| Projet | Rôle | Couverture |
|---|---|---|
| **RDCore.Runtime** | Sémantiques d'exécution RD-VBA + bibliothèque standard | [![RDCore.Runtime](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.Runtime.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.Runtime.LanguageCore** | Le coeur de langage : la liaison des noms, la sémantique statique, le jeu d'instructions et les déclarations de la bibliothèque standard | [![RDCore.Runtime.LanguageCore](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.Runtime.LanguageCore.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.Runtime.Support** | La session dans laquelle un programme s'exécute, et l'exécuteur que partagent `rdc run` et un programme compilé | [![RDCore.Runtime.Support](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.Runtime.Support.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.External** | Abstractions du monde extérieur : serveurs d'automation, bibliothèques natives | [![RDCore.External](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.External.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.External.Windows** | Le monde extérieur sous Windows : automation COM, BSTR/VARIANT et ordinaux des appels natifs | [![RDCore.External.Windows](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.External.Windows.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.ParseServer** | Serveur de parsing sans état, autour de RDCore.Parsing | [![RDCore.ParseServer](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.ParseServer.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.Parsing** | Le parser VBA : grammaire, arbres syntaxiques | [![RDCore.Parsing](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.Parsing.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.Symbols** | Symboles du workspace, bâtis des arbres syntaxiques | [![RDCore.Symbols](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.Symbols.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.CLI** | Client CLI / hôte d'environnement | [![RDCore.CLI](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-rdc.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.LanguageServer** | Coordonnateur de la plateforme | [![RDCore.LanguageServer](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.LanguageServer.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |
| **RDCore.Diagnostics** | Extension d'inspection de base | [![RDCore.Diagnostics](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage-RDCore.Diagnostics.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml) |

✅ Le parser, la résolution de symboles et le **language server** sont fonctionnels. Le serveur répond aux requêtes _diagnostics_ (pull et push),
_completion_, _signature help_, _hover_, _inlay hints_, _semantic tokens_, _document_ et _workspace symbols_, _folding_ et _selection ranges_, _definition_,
_declaration_, _type definition_ et _implementation_, _references_ et _highlights_, _call_ et _type hierarchy_, _inline values_, _rename_, _linked editing_ et _code actions_.

### Performance

Le temps que prend RD-VBA pour exécuter le corpus de `rdc bench`, en multiple du temps que prend MS-VBA pour le même code dans Excel (sous 1×, c'est
plus rapide). Les chiffres de RD-VBA sont mesurés à chaque fusion, par rapport à une exécution de référence de MS-VBA sur Excel 16 (64 bits),
Windows 11 x64, 12 cœurs. Les cas d'automatisation d'Excel exigent Excel : ils viennent de l'exécution de référence seulement.
[Tous les cas, et comment ils sont mesurés](https://rubberduck-vba.github.io/RDCore/performance.fr.html).

| Groupe | Le plus rapide | Médiane | Le plus lent |
|---|---|---|---|
| Appels | [![Appels, le plus rapide](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-calls-best.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/calls.fr.html) | [![Appels, médiane](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-calls-median.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/calls.fr.html) | [![Appels, le plus lent](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-calls-worst.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/calls.fr.html) |
| Instructions | [![Instructions, le plus rapide](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-statements-best.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/statements.fr.html) | [![Instructions, médiane](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-statements-median.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/statements.fr.html) | [![Instructions, le plus lent](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-statements-worst.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/statements.fr.html) |
| Tableaux | [![Tableaux, le plus rapide](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-arrays-best.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/arrays.fr.html) | [![Tableaux, médiane](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-arrays-median.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/arrays.fr.html) | [![Tableaux, le plus lent](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-arrays-worst.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/arrays.fr.html) |
| Chaînes | [![Chaînes, le plus rapide](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-strings-best.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/strings.fr.html) | [![Chaînes, médiane](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-strings-median.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/strings.fr.html) | [![Chaînes, le plus lent](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-strings-worst.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/strings.fr.html) |
| Calcul numérique | [![Calcul numérique, le plus rapide](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-numeric-best.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/numeric.fr.html) | [![Calcul numérique, médiane](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-numeric-median.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/numeric.fr.html) | [![Calcul numérique, le plus lent](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-numeric-worst.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/numeric.fr.html) |
| Variables et paramètres | [![Variables et paramètres, le plus rapide](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-slots-best.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/variables-and-parameters.fr.html) | [![Variables et paramètres, médiane](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-slots-median.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/variables-and-parameters.fr.html) | [![Variables et paramètres, le plus lent](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-slots-worst.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/variables-and-parameters.fr.html) |
| Bibliothèque standard | [![Bibliothèque standard, le plus rapide](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-stdlib-best.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/standard-library.fr.html) | [![Bibliothèque standard, médiane](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-stdlib-median.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/standard-library.fr.html) | [![Bibliothèque standard, le plus lent](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-stdlib-worst.json)](https://rubberduck-vba.github.io/RDCore/benchmarks/standard-library.fr.html) |
| Automatisation d'Excel (exécution de référence) | [![Automatisation d'Excel, le plus rapide](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-excel-best.json)](https://rubberduck-vba.github.io/RDCore/performance.fr.html#automatisation-dexcel) | [![Automatisation d'Excel, médiane](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-excel-median.json)](https://rubberduck-vba.github.io/RDCore/performance.fr.html#automatisation-dexcel) | [![Automatisation d'Excel, le plus lent](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-excel-worst.json)](https://rubberduck-vba.github.io/RDCore/performance.fr.html#automatisation-dexcel) |


---
### Voir aussi
- [Getting Started](https://rubberduck-vba.github.io/RDCore/getting-started.html)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)
- [RD-VBAL](https://rubberduck-vba.github.io/RDCore/specs/rd-vbal.1.0.introduction.html)
- [RDCore.SDK](https://github.com/rubberduck-vba/RDCore.SDK) : le modèle du langage et l'infrastructure commune sur lesquels reposent tous les composants et extensions de RDCore
- [RDCore.HexIDE](https://github.com/rubberduck-vba/RDCore.HexIDE) : le module d'extension qui intègre RDCore à HexIDE
- [HexIDE](https://github.com/hexide-io/HexIDE) : l'IDE conçu pour les développeurs VB, que RDCore.HexIDE étend


> [!NOTE]
> La version française des documents techniques, lorsque disponible, utilise les termes originaux _en anglais_ qui conservent la précision de leur signification, plutôt qu'une traduction approximative qui pourrait facilement être plus confondante qu'utile.

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
