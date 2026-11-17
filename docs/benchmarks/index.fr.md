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

Les cas d'automatisation d'Excel exigent Excel : ils ne sont donc mesurés que lors de l'exécution de référence (voir
[Automatisation d'Excel](../performance.fr.md#automatisation-dexcel)).

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

Pour chaque cas, le rapport donne son exécution la plus rapide et sa médiane, ce qu'a alloué son exécution la plus rapide, et les collectes de
mémoire de chaque génération faites pendant qu'elle s'exécutait. Avant les cas, il donne le temps qu'a pris le corpus pour être analysé
syntaxiquement, chargé et analysé.

L'exécution de référence garde la plus rapide de sept exécutions, comme le fait `rdc bench --runs 7`. Les temps sur une autre machine diffèrent de
ceux de la machine de référence : comparez donc des exécutions faites sur la même machine, soit deux compilations de `rdc`, soit votre code avant et
après une modification.

`rdc bench <chemin>` chronomètre les `Public Sub Bench*()` des modules standard d'un espace de travail plutôt que le corpus (voir
[Le _shell_ et les commandes rdc](../rdc.fr.md)).

## Voir aussi

- [Performance](../performance.fr.md)
- [Le _shell_ et les commandes rdc](../rdc.fr.md)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
