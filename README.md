# RDCore™

[Anglais](./README.en.md)

### Avant de commencer.

> Nouveau ici? Rubberduck a toujours été une initiative open-source.
> **RDCore l'honore avec une formule Open-Core**. Voir [rubberduckvba.ca](https://rubberduckvba.ca) pour plus d'informations.

Ce référentiel contient différents projets produisant différentes librairies et exécutables.

**Tout code sous licence GPLv3 dépend de code sous licence MIT**, et jamais l'inverse; il y a une _barrière inter-processus_ claire entre les composantes.

- ⚖️ les projets sous licence **MIT** se divisent en deux catégories:
   - 👉 The _LSP and RD-VBA language core_ (SDK).
   - 🧩 Extensions or otherwise _terminal_ (non-library) projects.
- ⚖️ **GPLv3** projects are protected and _unless explicitly authorized in writing by **9562-7303 Québec inc.**_ (through a commercial agreement), any derived work must be released alongside its source code and licensed under GPLv3.

Cet arrangement protège tant les contributeurs historiques qu'actuels, en s'assurant que **l'implémentation du _runtime_ de RDCore demeure dans les mains de sa communauté open-source**.

VIVAT CUCUMIS™

---

## Application

Le référentiel est consistué d'un bouquet d'applications client/serveur LSP : 

- **RDCore.LanguageServer** construit `RDCore.LanguageServer.exe`, la composante responsable de la gestion de l'_espace de travail_, et les services en arrière-plan pour toutes les fonctionnalités IDE supportées par LSP 3.17, des listes de complétion aux refactorings. Vous ne démarrez normalement pas un _serveur LSP_ vous-même : en vertu du protocole, le _client LSP_ s'en charge pour vous.
- **RDCore.Diagnostics** construit `RDCore.Diagnostics.exe`, un serveur LSP _satellite_ détenu par une instance de **RDCore.LanguageServer**, responsable de l'analyse du contexte sémantique de tout ce qui lui passe sous la main.
- **RDCore.Parsing** construit `RDCore.Parsing.exe`, un serveur LSP _satellite_ détenu par une instance de **RDCore.LanguageServer**, responsable de l'analyse du code source et de sa transformation en arborescence abstraite de syntaxe (AST), constitué de noeuds définis dans la librairie SDK.
- **RDCore.Runtime** construit `RDCore.Runtime.exe`, un serveur LSP _satellite_ détenu par une instance de **RDCore.LanguageServer**, détient les implémentations concrètes qui sont clées pour l'interprétation du code et la gestion de la mémoire applicative : **laisser ce logiciel hors de portée d'une license MIT assure que RD-VBA demeure gratuit et open-source pour tous.**
- **RDCore.CLI** construit `rdc.exe`, une application console qui implémente un **client LSP** léger qui consomme le SDK.

...et de librairies :

- **RDCore.SDK** est RD-VBA en boîte : cette librairie modélise, encapsule et expose l'entièreté du système de typage et les sémantiques statiques et run-time du langage dans une seule librairie, complètement documentée.
- **RDCore.Tests** détient la couverture de tests couvrant le SDK / _coeur de langage_.

Le terme _coeur de langage_ ("language core") réfère à un sous-ensemble d'espaces de noms dans la librairie SDK qui ensemble, définissent RD-VBA en tant que langage, le SDK en lui-même étant plus large que le seul coeur de langage. La librairie SDK définit également tout dont que n'importe quelle extension **RDCore** a besoin pour partir du bon pied et focuser sur ce qui l'intéresse.


![RDCore solution projects](./assets/RDCore-solution.png)
