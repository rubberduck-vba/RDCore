# RDCore dans HexIDE
<sup>_This document is available in [English](hexide.html)_</sup>

[HexIDE](https://github.com/hexide-io/HexIDE) est un IDE conçu pour les développeurs Visual Basic. Le module d'extension RDCore pour HexIDE,
[RDCore.HexIDE](https://github.com/rubberduck-vba/RDCore.HexIDE), y intègre RDCore.

> [!NOTE]
> **Le module d'extension est en pré-alpha.** Il fonctionne avec HexIDE v0.5.0 et les versions ultérieures, qui ont la prise en
> charge des modules d'extension sur laquelle il repose.

---
## Ce que fait le module d'extension

- **Il ouvre les projets RDCore.** HexIDE ouvre un `.rdproj` comme un projet, et en liste les modules, les autres fichiers et les références. Le
  fichier de projet appartient à RDCore : le module d'extension le lit, et suit les modifications que le serveur de langage de RDCore y écrit.
- **Il fait fonctionner le serveur de langage de RDCore** tant qu'un projet RDCore est ouvert : ses [diagnostics](diagnostics/index.md), la complétion
  et les autres fonctions de l'éditeur viennent de RDCore.
- **Il offre le débogueur de RDCore à F5.** Un programme s'exécute et se débogue dans le _runtime_ de RDCore, avec points d'arrêt, exécution pas à
  pas et [Modifier et continuer](edit-and-continue.fr.md) sur les modifications enregistrées.
- **Il installe la plateforme**, avec votre consentement : il télécharge la plateforme à partir des versions publiées de RDCore, la vérifie et la
  tient à jour.

Un projet VB6 s'ouvre dans HexIDE une fois qu'il a un `.rdproj` (voir [Projets VB6](vb6-projects.fr.md)).

---
## Obtenir le module d'extension

Obtenez le module d'extension à partir de HexIDE. Il est publié séparément de la plateforme. Une fois installé, il utilise la plateforme que
`rdc install` a installée (voir [Obtenir la plateforme](getting-started.fr.md#obtenir-la-plateforme)), ou installe lui-même la plateforme,
avec votre consentement.

Pour plutôt construire le module d'extension à partir de son code source :

1. Installez la plateforme, ou construisez-la (voir [Obtenir la plateforme](getting-started.fr.md#obtenir-la-plateforme)).
1. Construisez le module d'extension et disposez-le en paquet de module d'extension HexIDE, comme l'indique son
   [README](https://github.com/rubberduck-vba/RDCore.HexIDE/blob/main/README.fr.md). Copiez le paquet dans le dossier `addins` à côté de
   l'exécutable de HexIDE.
1. Si vous avez construit la plateforme, indiquez-la au module d'extension : dans `%LOCALAPPDATA%\RDCore\HexIDE\settings.json`, donnez à
   `platformRoot` le dossier de la plateforme. Ce paramètre fixé, le module d'extension ne télécharge rien.

Un paquet que vous construisez n'est pas signé : HexIDE ne le charge qu'en mode développeur, avec les modules d'extension non signés permis.

---
[ACCUEIL](index.fr.md) • [HOME](index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.html) | 🧩 [BÂTISSONS](getting-started.fr.md) • [BUILD](getting-started.html) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
