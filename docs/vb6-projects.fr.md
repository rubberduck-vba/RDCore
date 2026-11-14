# Projets VB6
<sup>_This document is available in [English](vb6-projects.html)_</sup>

RDCore travaille sur un _espace de travail_ : un dossier muni d'un fichier de projet, `.rdproj`, qui nomme les modules, les autres fichiers et les
références du projet. Un projet VBA en obtient un avec `rdc new` (voir [Apporter un projet VBA](getting-started.fr.md#apporter-un-projet-vba)). Un
projet Visual Basic 6 l'obtient de son `.vbp`.

---
## Importer un `.vbp`

```
rdc import <chemin>
```

`<chemin>` est le `.vbp`, ou un dossier qui en contient un. Le `.rdproj` est écrit à côté du `.vbp`, qui reste tel quel. Le `.rdproj` garde la trace du
`.vbp` dont il est issu (`SourceProject`), ce qui fait de l'espace de travail un espace VB6 pour un éditeur qui ne précise pas le langage lui-même.

|Du `.vbp`|Dans le `.rdproj`|
|---|---|
|Le nom du projet|Le nom du projet.|
|Les modules, les classes et les formulaires|Des modules.|
|Les contrôles utilisateur, les pages de propriétés, les documents utilisateur, les concepteurs, les documents liés, le fichier de ressources|Des fichiers du projet, qui ne sont pas compilés.|
|Les références|Des références aux bibliothèques dont la plateforme a une description, par GUID et par version.|
|Les arguments de compilation conditionnelle (`CondComp`)|Les constantes de précompilation du projet.|

Ce qui ne peut pas être repris est omis, et l'importation dit quoi et pourquoi :

- une référence à une bibliothèque dont la plateforme n'a pas de description (`rdc export-library` peut en décrire une; voir [Le _shell_ et les commandes rdc](rdc.fr.md#les-commandes));
- une référence à un autre projet;
- un contrôle ActiveX : la plateforme n'en héberge aucun;
- un fichier hors du dossier du projet;
- l'objet de démarrage et le type du projet;
- une ligne `CondComp` que VB6 lui-même refuse.

Les règles complètes sont dans [**RD-VBAL §2.2.3.5** Importing a VB6 Project](specs/rd-vbal.2.2.3.projectfile.md#2235-importing-a-vb6-project).

### Importer de nouveau

Après une modification du projet dans l'IDE de VB6, importez de nouveau le `.vbp` : `rdc import --force <chemin>`. Le `.vbp` remplace ce que le
`.rdproj` dit du nom, des références, des fichiers et des arguments de compilation conditionnelle. Le `.rdproj` garde le reste, comme ses fichiers de
paramètres. Sans `--force`, `rdc import` garde un `.rdproj` existant.

---
## Le langage

La plateforme sert un espace de travail en VBA, à moins que son client nomme un autre langage. Un client nomme VB6 `vb6`
([**RD-VBAL §2.0.2.1.1** The language](specs/rd-vbal.2.0.2.client-server-capabilities.md#20211-the-language)).

Servi en VB6, un projet a deux bibliothèques qu'il ne liste pas parmi ses références, comme dans l'IDE de VB6 :

- `VBRUN` : les constantes de tout projet VB6 (`vbTwips`, `vbHourglass`, `vbChecked`, `vbLeftButton`) et la classe `PropertyBag`;
- `VB` : `Form`, ses contrôles, et les objets `App`, `Screen`, `Printer`, `Clipboard` et `Forms`.

Les contrôles d'un formulaire sont des noms de son module. Un contrôle d'un groupe de contrôles est nommé avec son indice : `Command1(0)`.

---
## Exécuter un projet VB6

L'importation ne reprend pas l'objet de démarrage. `rdc run` et `rdc build` démarrent le programme au `Sub Main` du seul module standard qui en déclare
un, ou à la procédure que le `.rdproj` nomme dans sa section `build` :

```json
"build": { "entryPoint": "Module1.Main" }
```

Voir [Exécuter un programme](rdc.fr.md#exécuter-un-programme).

---
[ACCUEIL](index.fr.md) • [HOME](index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.html) | 🧩 [BÂTISSONS](getting-started.fr.md) • [BUILD](getting-started.html) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
