# RDCore™
<sup>_This document is available in [English](./README.md)_</sup>

[![Build and Test](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml/badge.svg)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml)
[![Coverage](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/coverage.json)](https://github.com/rubberduck-vba/RDCore/actions/workflows/build.yml)

![VIVAT CUCUMIS](./assets/vivat-cucumis-stonecore.png)

## Avant de commencer.
👋 Bonjour! Nouveau ici? _Rubberduck_ a toujours été une initiative open-source.
**RDCore l'honore avec une formule Open-Core**. Voir [rubberduckvba.ca](https://rubberduckvba.ca) pour plus de détails.

Ce référentiel contient différents projets **en phase de développement actif** produisant différentes librairies et exécutables sous un modèle de licence relativement simple :
- **La librairie RDCore.SDK** (incluant sa documentation) est sous licence **⚖️MIT**;
- **Tout le reste** est construit autour et sous licence **⚖️GPLv3**.

Cet arrangement protège tant les contributeurs historiques qu'actuels, tout en protégeant son avenir : **l'implémentation du _runtime_ de RDCore demeurera open-source**.

👉 Nous construisons ici une solide fondation pour le _coeur de langage_. Le [site de documentation](https://rubberduck-vba.github.io/RDCore/index.fr.html) demeure la référence principale, mais la plateforme commence à produire de vrais livrables : `rdc.exe` mène un _workspace_ du chargement à l'analyse jusqu'à la définition des symboles, de bout en bout, résolus à travers les modules par un véritable _symbol resolver_ ordonné selon MS-VBAL.

### Dans ce document
- [Statut du projet](#projectstatus)

### Voir aussi
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [CODE_OF_CONDUCT.fr.md](CODE_OF_CONDUCT.fr.md)

---
# RDCore
[RD-VBAL §1.0.1](https://rubberduck-vba.github.io/RDCore/specs/rd-vbal.1.0.introduction.html#101-rdcore)  
**RDCore**™ est une plateforme de _serveur de langage_ (LSP) dont les travaux d'implémentation sont **présentement en cours**. À la cible, les livrables de RDCore sont :
- 🚧 **rdc.exe**: un _environnement hôte_ RD-VBA configurable et extensible, client LSP (CLI), doté d'un _mode commande_ (`rdc.exe <verbe>`, p. ex. `describe-ext`);
- 🚧 **RDCore.LanguageServer.exe**: le serveur d'orchestration LSP de la plateforme;
- 🚧 **RDCore.ParseServer.exe**: le _parser_ de la plateforme est une application serveur LSP satellite détenue et orchestrée par le serveur de langage principal;
- 🚧 **RDCore.Diagnostics.exe**: une extension _core_ de la plateforme qui envoie les _diagnostics_ au serveur de langage principal de façon asynchrone;
- 🚧 **RDCore.Runtime.dll**: une librairie renfermant l'implémentation de toute la sémantique et mécanismes du run-time de RD-VBA, _incluant une implémentation de la librairie VBA standard_;
- ✅ **RDCore.SDK.dll**: une librairie exposant les abstractions de la plateforme RDCore et encapsulant les implémentations de base du _coeur de langage_ RD-VBA.


### ✨ Ce que RDCore rend envisageable
Entre autres :
- **Analyse sémantique** de code VBA à une profondeur que seuls des _analyseurs LSP_ peuvent atteindre
- **Exécution** de code VBA hors du VBIDE
- **Outils de développement** via le protocole _Language Server_ (LSP)
- **Inspection de l'exécution**, comportements et _faits sémantiques_ 
- **Extensions de la plateforme** avec des analyseurs et plug-ins

<a id="projectstatus"/>

### 📊 Statut du projet
RDCore est en phase active de développement **pré-alpha**. La **spécification** et la **documentation** sont les livrables stables; la plateforme s'exécute de bout en bout (_workspace_ → analyse → symboles) mais n'est pas encore publiée. Un portrait sommaire par projet — non suivi par tickets, simplement l'état des lieux :

**RDCore.SDK** — modèle de langage + plomberie partagée · ✅ stable

| Domaine | |
|---|---|
| Système de types statiques, modèle de types _runtime_ | ✅ |
| Résolution de symboles — arbre de _lexical scope_, _shadowing_, ambiguïté (RD-VBAL §2.3.1.2) | ✅ |
| Directives `Implements` (MS-VBAL §5.2.4.2) — modules de classe et _resolver_ conscients des interfaces implémentées | 🚧 `VBClassModuleSymbol.ImplementedInterfaces` résolu par nom à travers la composition (récursivement, une chaîne de `Implements` se résout donc en entier) et reflété dans `VBClassType.Supertypes`; la surface de membres d'une classe n'est pas fusionnée avec celle d'une interface implémentée, et la validation complète des §5.2.4.2/§5.3.1.9 (auto-référence, doublons, correspondance de noms `Foo_Bar`, restriction des modules extensibles) n'est pas encore modélisée |
| Sémantiques statiques — opérateurs, _let-coercions_, _set-coercion_ | ✅ |
| Sémantiques statiques — règles par nœud (_simple names_, _member access_, `New`, `Me`, littéraux, expressions d'index, accès dictionnaire, `TypeOf...Is`) | ✅ chaque règle est complète et testée isolément |
| Sémantiques statiques — _tree evaluator_ (dispatch récursif sur une vraie expression, incl. dispatch par jeton d'opérateur) | ✅ couvre les chaînes de _member access_/index/accès dictionnaire, les opérateurs, `New` et `TypeOf...Is`; l'accès relatif à `With` (`.Member`, `!Member`) se résout aussi désormais, via un _walker_ d'instructions (ci-dessous) qui propage le type cible du bloc `With` englobant le plus proche; rien en production n'appelle encore l'un ou l'autre |
| Sémantiques statiques — _walker_ d'arbre d'instructions (récursion dans les blocs `If`/`Do`/`For`/`Select Case`/`With`, propageant le type cible de `With` dans leur corps, validant la coercion `Let`/`Set` des affectations) | ✅ collecte toutes les erreurs de compilation trouvées dans tout l'arbre d'instructions plutôt que de s'arrêter à la première, contrairement au _tree evaluator_ d'expressions; `LSet`/`RSet` (sémantiques distinctes, pas encore modélisées) sont différées; rien en production ne l'appelle encore |
| Hôtes, transport, cycle de vie des connexions, racine de plateforme | ✅ |
| Modèle de capacités (_handshake_ plateforme + LSP) | 🚧 informatif, sans application; la CLI et les extensions déclarent `CliCommand` |

**RDCore.Parsing** → `RDCore.ParseServer.exe` · 🚧 · [RD-VBAL §3](https://rubberduck-vba.github.io/RDCore/specs/rd-vbal.3.0.syntax-tree.html)

| Domaine | |
|---|---|
| Analyse document complet — directives, déclarations, membres d'UDT | ✅ |
| Grammaire d'expressions et d'opérateurs — arithmétique, logique, relationnelle, concaténation, incluant les conditions `#If` | 🚧 `New`/`TypeOf...Is` sont analysés mais pas modélisés — préservés en `UnbuiltExpressionTriviaNode`, pas un vrai nœud; le reste ✅ |
| Analyse des littéraux — numériques et chaînes | ✅ |
| Nœuds d'AST de _statements_ | 🚧 la plupart des formes sont faites; `Mid`/`LSet`/`RSet`, `On...GoTo`/`On...GoSub` calculés restent |
| Analyse de fragment ancré | ✅ |

**RDCore.LanguageServer** — orchestrateur + serveur LSP · 🚧 · [RD-VBAL](https://rubberduck-vba.github.io/RDCore/specs/rd-vbal.html)

| Domaine | |
|---|---|
| Cycle de vie LSP | ✅ |
| Orchestration de la plateforme (démarrage, santé, arrêt) | ✅ enfants _core_ + extensions découvertes |
| Chargement du _workspace_ → aller-retour d'analyse → extraction de symboles → définition | ✅ résolus à travers les modules (UDT/Enum, _project scope_) |
| Fonctionnalités LSP _document_ et _workspace_ | 👉 à saisir — spécifié |

**RDCore.CLI** → `rdc.exe` — client LSP + hôte d'environnement · 🚧 · [RD-VBAL §2.3](https://rubberduck-vba.github.io/RDCore/specs/rd-vbal.2.3.application-host.html)

| Domaine | |
|---|---|
| Mode client (`--workspace`) pilote la plateforme de bout en bout | ✅ |
| Session _runtime_ composée depuis `.rdproj` (`--host`) | ✅ |
| Symboles de session (`rdcore/host/symbols/define`) | 🚧 définition seulement |
| Modèle de mémoire / d'allocation de session | 🚧 couche de comptabilité; stockage adressable prévu |
| Mode commande — répartition de verbes, `describe-ext` | ✅ fournisseurs de commandes natifs + extensions |
| REPL interactif | 🎯 |

**RDCore.Runtime** — sémantiques _runtime_ RD-VBA + librairie standard VBA · 🚧

| Domaine | |
|---|---|
| Sémantiques _runtime_ — opérateurs | ✅ |
| Sémantiques _runtime_ — _let-coercions_ | 🚧 |
| Sémantiques _runtime_ — _Set-coercion_ (MS-VBAL §5.5.2.2) | 🚧 le passage de `Nothing` et la vérification de compatibilité de classe sont implémentés; cette dernière reconnaît désormais aussi une vraie relation `Implements` (via `VBClassType.Supertypes`, ci-dessus), pas seulement une correspondance de classe exacte; rien en production ne l'appelle encore |
| Sémantiques _runtime_ — _statements_ | 🎯 les sémantiques propres au `With` (cible → variable anonyme, MS-VBAL §5.4.2.21) sont en place pour une cible de type classe; aucun interpréteur d'arbre d'instructions n'existe encore pour quelque type d'instruction que ce soit, donc rien n'invoque ceci depuis une exécution réelle |
| Librairie standard (`IStd*`) | 🎯 |
| Interpréteur · _IR lowering_ | 🎯 prévu |

**RDCore.Diagnostics** — extension d'inspection _core_ · 🚧 · [RD-VBAL §2.6](https://rubberduck-vba.github.io/RDCore/specs/rd-vbal.2.6.diagnostics.html) — squelette d'analyseur; découverte depuis son _manifest_ généré et démarrée par le serveur de langage lors de l'assemblage de la plateforme.

**Tests** · 🎯 cible ~70% de couverture de lignes (le badge ci-haut est à jour) — sémantiques d'opérateurs et de _let-coercions_ (_runtime_ + statiques) bien couvertes; grammaire du _parser_ et CLI minces; les _statements_ / l'interpréteur du _runtime_ n'ont encore rien à couvrir.

**Contributions** — individuelles ✅ ouvertes ([CLA](CLA.fr.md)) · corporatives ⏳ à venir

<sub>✅ fait / stable · 🚧 en cours · 🎯 non entamé · 👉 à saisir</sub>

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
