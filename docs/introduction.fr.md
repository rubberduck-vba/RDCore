# Introduction 
<sup>_This document is available in [English](introduction.html)_</sup>

**RDCore™** est un **projet open-source** conçu et soutenu par une société privée et visant à bâtir une plateforme de langage moderne pour _Visual Basic for Applications_ (VBA).
À terme, son SDK fournit un modèle sémantique complet, infrastructure run-time, et surface d'outillage extensible pour analyser, exécuter, et faire évoluer le code VBA **à l'extérieur de son environnement historique**.


---
## 💡 L'idée

Pensez **Roslyn, mais pour VBA**.

RDCore réimagine VBA en tant que plateforme de langage de première classe:
- Un modèle sémantique détaillé
- Un runtime découplé des environnements legacy
- Une architecture modulaire conçue pour l'extensibilité
- Une fondation pour l'outillage, l'analyse, et l'exécution

RDCore vise à développer une plateforme pérenne et extensible pour comprendre et faire évoluer le langage.

VBA n'est pas qu'un runtime vieillissant - c'est aussi une _spécification de langage_. RDCore le traite simplement comme tel.


---
## 🚀 Architecture

- **La librairie RDCore.SDK** est sous licence  **⚖️MIT**;
- **Tout le reste** construit par-dessus, est sous licence **⚖️GPLv3**.

![Survol de la Solution RDCore](images/solution-overview.png)

RDCore est constitué de :
- **RDCore.SDK** (MIT) définit le _coeur de langage_ : syntaxe, symboles, modèle sémantique, système de typage, etc.
- **RDCore.Runtime** (GPLv3) implémente les abstractions définies par le SDK autour des sémantiques run-time, la librairie standard, etc.
- **Hôtes** (GPLv3) incluant un client CLI (rdc.exe), un serveur LSP et les applications satellites lui permettant de manipuler et de comprendre le langage (parser, diagnostics, etc.).


---
## ✨ Ce que RDCore rend possible
- Analyse sémantique profonde de code VBA
- Exécution de code VBA hors du VBIDE
- Outillage langage via le protocole _Language Server_ (LSP)
- Inspection du comportement à l'exécution, faits sémantiques 
- Extension de la plateforme avec des analyseurs et plug-ins


---
## 📊 Statut du projet
RDCore est présentement en phase active de développement **pré-alpha**.

👉 Le **statut courant du projet** est tenu à jour dans **README.md** aux côtés de l'implémentation de la plateforme dans le [référentiel sur GitHub](https://github.com/rubberduck-vba/RDCore).

---
[ACCUEIL](index.fr.md) • [HOME](index.md) | ℹ️ BIENVENUE • [WELCOME](introduction.html) | 🧩 [BÂTISSONS](getting-started.fr.md) • [BUILD](getting-started.html) | [**RD-VBAL**](/specs/rd-vbal.html) | [SDK](/api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
