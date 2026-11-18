# Bancs d'essai : automatisation d'Excel
<sup>_This document is available in [English](excel-automation.md)_</sup>

---
Du code VBA qui pilote Excel par son modèle objet, en liaison tardive : un bloc de cellules lu d'un coup dans un `Variant`, traité, puis écrit d'un
coup dans une autre feuille, et ensuite le même travail fait cellule par cellule. Chaque cas donne le temps de chacune de ses phases.

La source est le module [`ExcelCases.bas`](https://github.com/rubberduck-vba/RDCore/blob/main/RDCore.CLI/Bench/Excel/ExcelCases.bas), intégré à
`rdc`. Ces cas exigent Excel : ils ne sont donc mesurés que lors de l'exécution de référence (voir [Mesurer MS-VBA](index.fr.md#mesurer-ms-vba)), et
leurs badges donnent la date de l'exécution de référence plutôt qu'un chiffre à jour. Chaque badge donne le temps de RD-VBA en multiple de celui de
MS-VBA ([comment les chiffres sont mesurés](../performance.fr.md#comment-les-chiffres-sont-mesurés)).

## Comment les cas s'exécutent

Avec `rdc bench-msvba`, les deux côtés exécutent les cas contre une instance d'Excel qui leur est propre, dans un autre processus : chaque appel au
modèle objet d'Excel franchit donc une frontière de processus. RD-VBA les exécute à partir du programme `Program.bas`, qui démarre cette instance et
prend le nombre d'exécutions sur la ligne de commande :

[!code-vb[](../../RDCore.CLI/Bench/Excel/Program.bas#Main)]

MS-VBA exécute `RunExcelCases` à partir d'un classeur dans Excel, contre une deuxième instance d'Excel qu'il démarre. `RunExcelCases` désactive la
mise à jour de l'écran, les événements et le calcul automatique, remplit une feuille de 100 000 × 10 cellules, et exécute les cas dans cet ordre :

[!code-vb[](../../RDCore.CLI/Bench/Excel/ExcelCases.bas#RunExcelCases)]

## Déclarations

Le module déclare le compteur dont il lit ses temps, en microsecondes :

[!code-vb[](../../RDCore.CLI/Bench/Excel/ExcelCases.bas#Declarations)]

[!code-vb[](../../RDCore.CLI/Bench/Excel/ExcelCases.bas#Since)]

La feuille source contient des nombres, et une cellule vide là où la somme de la ligne et de la colonne est un multiple de 7 :

[!code-vb[](../../RDCore.CLI/Bench/Excel/ExcelCases.bas#Fill)]

## Cas

### Block

Les cellules lues dans un `Variant` en un seul appel (`read`), chaque élément qui n'est pas `Empty` doublé sans appel à Excel (`pass`), et le tableau
écrit dans l'autre feuille en un seul appel (`write`). `total` réunit les trois. Le cas s'exécute sur 1 000 × 10 cellules, puis sur 100 000 × 10.

| Phase | 1 000 × 10 | 100 000 × 10 |
|---|---|---|
| Lecture | ![Excel.Block1000x10.Read](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block1000x10.Read.json) | ![Excel.Block100000x10.Read](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block100000x10.Read.json) |
| Passe | ![Excel.Block1000x10.Pass](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block1000x10.Pass.json) | ![Excel.Block100000x10.Pass](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block100000x10.Pass.json) |
| Écriture | ![Excel.Block1000x10.Write](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block1000x10.Write.json) | ![Excel.Block100000x10.Write](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block100000x10.Write.json) |
| Total | ![Excel.Block1000x10.Total](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block1000x10.Total.json) | ![Excel.Block100000x10.Total](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.Block100000x10.Total.json) |

[!code-vb[](../../RDCore.CLI/Bench/Excel/ExcelCases.bas#Block)]

### CellByCell

Le même travail sur 1 000 × 10 cellules, une cellule à la fois : un appel à Excel pour lire chaque cellule et un autre pour l'écrire, soit 20 000
appels par exécution.

![Excel.CellByCell1000x10.Total](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/rubberduck-vba/RDCore/badges/bench-Excel.CellByCell1000x10.Total.json)

[!code-vb[](../../RDCore.CLI/Bench/Excel/ExcelCases.bas#CellByCell)]

## Voir aussi

- [Bancs d'essai](index.fr.md)
- [Performance](../performance.fr.md#automatisation-dexcel)

---
[ACCUEIL](../index.fr.md) • [HOME](../index.md) | ℹ️ [BIENVENUE](../introduction.fr.md) • [WELCOME](../introduction.md) | 🧩 [BÂTISSONS](../getting-started.fr.md) • [BUILD](../getting-started.md) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
