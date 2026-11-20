# Bancs d'essai
<sup>_This document is available in [English](index.md)_</sup>

---
`rdc bench` chronomètre un ensemble de modules VBA intégré à `rdc` : le corpus. Chaque cas est une `Public Sub Bench*()` de l'un de ses modules
standard, et ses modules de classe contiennent ce qu'appellent les cas. Les pages ci-dessous donnent la source de chaque cas, telle qu'elle s'exécute.
Son chiffre, et la façon dont les chiffres sont mesurés, sont à la page [Performance](../performance.fr.md).

## Les groupes

| Groupe | Module | Ce que mesurent ses cas |
|---|---|---|
| [Appels](calls.fr.md) | `Calls.bas` | Des appels à une procédure, aux membres d'un objet et à la bibliothèque standard |
| [Instructions](statements.fr.md) | `Statements.bas` | `Set`, l'affectation d'un membre et d'un champ, `Select Case`, des conditions, l'instruction `Mid$`, `On Error Resume Next` |
| [Tableaux](arrays.fr.md) | `Arrays.bas` | Chaque élément d'un tableau de `Long` et de tableaux `Variant` à deux dimensions |
| [Chaînes](strings.fr.md) | `Strings.bas` | L'affectation, la concaténation et la comparaison, et des chaînes passées à une procédure |
| [Calcul numérique](numeric.fr.md) | `Numeric.bas` | De l'arithmétique sur des opérandes typés et `Variant` |
| [Variables et paramètres](variables-and-parameters.fr.md) | `Slots.bas` | Des paramètres et des variables locales, une variable de module, une variable locale `Static` |
| [Bibliothèque standard](standard-library.fr.md) | `StdLib.bas` | Une fonction de la bibliothèque standard par cas |
| [Automatisation d'Excel](excel-automation.fr.md) | `ExcelCases.bas` | Des cellules d'une feuille lues et écrites, en bloc et cellule par cellule |

Les cas d'automatisation d'Excel ne font pas partie du corpus. Ils exigent Excel : ils ne sont donc mesurés que lors de l'exécution de référence
(voir [Mesurer MS-VBA](#mesurer-ms-vba)).

## Exécuter les bancs d'essai

Une fois `rdc` installé (voir [Démarrer avec RDCore](../getting-started.fr.md)), cette commande exécute chaque cas du corpus :

```
rdc bench
```

| Option | Ce qu'elle fait |
|---|---|
| `--runs <n>`, `-r <n>` | Exécute chaque cas `n` fois (3 par défaut). |
| `--filter <texte>`, `-f <texte>` | N'exécute que les cas dont le nom `Module.Procedure` contient le texte, par exemple `--filter Strings.` |
| `--list`, `-l` | Liste les cas sans les exécuter. |
| `--engine <moteur>`, `-e <moteur>` | Exécute les cas avec `Compiled` ou `Interpreter`, au lieu du moteur d'exécution configuré. |
| `--no-warm-up` | Chronomètre chaque cas dès sa première exécution. |

Avant ses exécutions chronométrées, chaque cas est exécuté sans chronomètre jusqu'à ce que .NET ait fini d'optimiser le code qu'il appelle : au moins
31 fois, puis jusqu'à ce que le compilateur JIT n'ait rien compilé pendant 250 ms, pendant 5 secondes au plus. D'ici là, une exécution peut prendre
plusieurs fois plus de temps que les suivantes. La ligne `warm-up` du rapport donne le nombre
d'exécutions non chronométrées de l'ensemble des cas, ou `none` avec `--no-warm-up`.

Pour chaque cas, le rapport donne son exécution la plus rapide et sa médiane, ce qu'a alloué son exécution la plus rapide, et les collectes de
mémoire de chaque génération faites pendant qu'elle s'exécutait. Avant les cas, il donne le temps qu'a pris le corpus pour être analysé
syntaxiquement, chargé et analysé.

L'exécution de référence garde la plus rapide de sept exécutions, comme le fait `rdc bench --runs 7`. Les temps sur une autre machine diffèrent de
ceux de la machine de référence : comparez donc des exécutions faites sur la même machine, soit deux compilations de `rdc`, soit votre code avant et
après une modification.

`rdc bench <chemin>` chronomètre les `Public Sub Bench*()` des modules standard d'un espace de travail plutôt que le corpus (voir
[Le _shell_ et les commandes rdc](../rdc.fr.md)).

## Mesurer MS-VBA

L'exécution de référence mesure MS-VBA et RD-VBA sur la même machine, avec `rdc bench-msvba`. Elle exige Windows, Excel et le paramètre « Accès
approuvé au modèle d'objet du projet VBA » d'Excel (Fichier > Options > Centre de gestion de la confidentialité > Paramètres du Centre de gestion de
la confidentialité > Paramètres des macros).

```
rdc bench-msvba
```

La commande exécute le corpus et les cas d'automatisation d'Excel dans MS-VBA, dans un Excel masqué, et dans RD-VBA, en rondes alternées : MS-VBA en
premier dans les rondes impaires, RD-VBA en premier dans les rondes paires. RD-VBA réchauffe chaque cas du corpus comme le fait `rdc bench`; MS-VBA
l'exécute d'abord deux fois sans chronomètre, puisqu'il compile une procédure une seule fois, au premier appel. La commande garde l'exécution la
plus rapide de chaque cas de chaque côté, sur l'ensemble des rondes, et écrit les chiffres dans `msvba.json`. Rien n'est écrit à moins que chaque
cas ait un chiffre des deux côtés.

| Option | Ce qu'elle fait |
|---|---|
| `--out <fichier>`, `-o <fichier>` | Écrit les chiffres dans ce fichier au lieu de `msvba.json`. |
| `--rounds <n>` | Exécute `n` rondes (5 par défaut). |
| `--excel-rounds <n>` | Exécute les cas d'automatisation d'Excel dans les `n` premières rondes (3 par défaut; 0 n'en exécute aucun). |
| `--runs <n>`, `-r <n>` | Exécute chaque cas du corpus `n` fois par ronde (7 par défaut). |
| `--excel-runs <n>` | Exécute chaque cas d'automatisation d'Excel `n` fois par ronde (5 par défaut). |

La ligne `method` du fichier dit comment ses rondes ont été exécutées. Les chiffres de MS-VBA du site viennent de
`RDCore.CLI/Bench/Reference/msvba.json`.

## Voir aussi

- [Performance](../performance.fr.md)
- [Le _shell_ et les commandes rdc](../rdc.fr.md)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
