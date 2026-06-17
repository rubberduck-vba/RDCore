# Introduction

##### ([English](./introduction.en.md))

**RDCore** est une plateforme de langage moderne pour Visual Basic for Applications (VBA).
Son SDK fournit un modèle sémantique complet, infrastructure run-time, et surface d'outillage extensible pour analyser, exécuter, 
et faire évoluer le code VBA **à l'extérieur de son environnement historique**.

## 🚀L'idée

Pensez **Roslyn, mais pour VBA**.

RDCore réimagine VBA en tant que plateforme de langage de première classe:
- Un modèle sémantique détaillé
- Un runtime découplé des environnements legacy
- Une architecture modulaire conçue pour l'extensibilité
- Une fondation pour l'outillage, l'analyse, et l'exécution

RDCore vise à développer une plateforme pérenne et extensible pour comprendre et faire évoluer le langage.

VBA n'est pas qu'un runtime vieillissant - c'est aussi une _spécification de langage_. RDCore le traite simplement comme tel.

## Architecture

- **La librairie RDCore.SDK** est sous licence  **⚖️MIT**;
- **Tout le reste** construit par-dessus, est sous licence **⚖️GPLv3**.

![RDCore solution](images/solution-overview.png)

RDCore est constitué de :
- **RDCore.SDK** (MIT) définit le _coeur de langage_ : syntaxe, symboles, modèle sémantique, système de typage, etc.
- **RDCore.Runtime** (GPLv3) implémente les abstractions définies par le SDK autour des sémantiques run-time, la librairie standard, etc.
- **Hôtes** (GPLv3) incluant un client CLI (rdc.exe), un serveur LSP, et plusieurs autres applications qui orchestrent l'exécution et les interactions.


## ✨Ce que RDCore rend possible
- Analyse sémantique profonde de code VBA
- Exécution de code VBA hors du VBIDE
- Outillage langage via le protocole _Language Server_ (LSP)
- Inspection du comportement à l'exécution, faits sémantiques 
- Extension de la plateforme avec des analyseurs et plug-ins


## 📊Statut du projet
RDCore est présentement en phase active de développement pré-release.
- Architecture: ✅ stable
- SDK langage: ✅ largement défini
- Runtime: 🚧 implémentation en cours
- Librarie standard: 🚧 partiellement définie
- Hôte CLI (rdc.exe): 🚧 initialisation
- Contributions publiques: ❌ pas encore ouvertes

---
 V I V A T 🩷 C U C U M I S ™  
 [Accueil](./index.md) | 🧩[Démarrage](./getting-started.md) | 🔍[Documentation](/api) | 🌐[rubberduckvba.ca](https://rubberduckvba.ca)

---

<p align="center">
<img alt="Logo™ 9562-7303 Québec inc." src="images/vector-ducky.svg" style="width:200px; margin-top:72px;" /><br/>
<small>© Copyright <strong>9562-7303 Québec inc.</strong> (2026)<br/>
<em>"Rubberduck" est utilisé pour fins de référence au projet open-source legacy <strong>utilisé publiquement ainsi depuis 2015</strong> et sans lien ni affiliation avec tout tiers détenteur d'une marque semblable dans quelque juridiction que ce soit. "RDCore" et "VIVAT CUCUMIS" sont des marques de commerce revendiquées par 9562-7303 Québec inc. (en attente)<br/>
"Rubberduck" is used as a reference to the legacy open-source project <strong>the same way it has been used publicly since 2015</strong> and without any links or affiliation with any third-party trademark holders of a similar trademark in any jurdisdiction. "RDCore" and "VIVAT CUCUMIS" are trademarks claimed by 9562-7303 Québec inc. (pending)
</small>
</p>
