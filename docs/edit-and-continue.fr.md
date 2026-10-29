# Modifier et continuer
<sup>_This document is available in [English](edit-and-continue.html)_</sup>

---
## ✏️ De quoi il s'agit

Votre programme est interrompu : à un point d'arrêt, à une instruction `Stop`, ou après un pas à pas. Vous lisez ses variables, vous voyez ce qui ne va pas,
et vous modifiez le code sur-le-champ. Puis vous poursuivez : `CONT` dans le REPL, **Continuer** ou un pas dans le débogueur. Le programme reprend là où il
était, avec votre code modifié, sans recommencer.

L'éditeur VBA d'Office en fait une partie. Mais bien des modifications l'amènent à réinitialiser le projet (« Cette action réinitialisera votre projet,
continuer quand même? »), et tout ce que le programme avait accumulé jusque-là est perdu. RD-VBA applique toutes les modifications qu'il peut appliquer
correctement, soit plus que l'éditeur VBA. Quand une modification ne peut pas être appliquée, RD-VBA ne met pas fin à votre programme : il continue
d'attendre là où il est, vous dit quelle modification fait obstacle et pourquoi, et vous laisse choisir. Vous pouvez annuler la modification et poursuivre,
ou relancer le programme.

## ✅ Ce qui s'applique quand vous poursuivez

**Le code qui ne s'exécute pas en ce moment :**
- Le corps d'une procédure où le programme ne se trouve pas. Son prochain appel exécute le nouveau code. C'est vrai aussi d'une méthode d'une classe, même
  quand des objets de la classe existent.

<!-- S2: the four bullets below depend on the active-frame body remap. -->
**La procédure où vous êtes interrompu, et les procédures qui l'ont appelée :**
- Modifiez n'importe quelle instruction de la procédure où vous êtes interrompu. Une ligne modifiée au-dessus de l'interruption n'est pas exécutée de
  nouveau. Une ligne modifiée, insérée ou supprimée au-dessous est exécutée telle qu'elle est maintenant, quand le programme y arrive.
- Modifiez la prochaine ligne à exécuter, celle de l'interruption (la ligne d'un point d'arrêt, ou celle qui suit un `Stop`), et elle s'exécute modifiée.
- Dans une procédure qui a appelé celle où vous êtes, modifiez les lignes qui suivent son appel. Elles s'exécutent modifiées au retour de l'appel. Sa ligne
  d'appel doit rester telle qu'elle était (voir plus bas).
- Les lignes peuvent bouger : une modification qui ajoute ou retire des lignes au-dessus de l'interruption est appliquée, et les points d'arrêt suivent les
  lignes où ils étaient.

**Les déclarations que vous ajoutez :**
- Une procédure, dans un module ou une classe.
- Une variable de module (`Public` ou `Private`), une `Const`, un `Declare`.
- Un `Type` ou un `Enum`, ou un membre ajouté à un `Enum`.

<!-- S2: the changes below are applied in S1 only where they touch no procedure the program is in; where they reach one (a Const or an Option line its body
uses), applying them needs the active-frame body remap. -->
**Les autres changements que le programme relit :**
- Une procédure, une variable, une `Const` ou un `Type` retiré, quand rien de ce que le programme exécute ne s'en sert encore.
- La valeur d'une `Const` ou d'un membre d'un `Enum`, sauf une `Const` qui dimensionne un tableau qui existe déjà (voir plus bas).
- Les paramètres d'une procédure où le programme ne se trouve pas, et ses appelants, qui sont vérifiés de nouveau.
- Une instruction `Declare` modifiée.
- Une ligne `Option` : `Option Compare`, `Option Base`, `Option Explicit`, `Option Private Module`.

Les variables gardent leurs valeurs à travers chaque modification qui s'applique. Cela comprend les variables de module, les variables `Static` et les champs
de vos objets.

## ⏸️ Ce qui attend la prochaine exécution

Certaines modifications ne peuvent pas être appliquées à un programme déjà en cours, parce que le programme détient quelque chose que la modification
changerait sous lui. RD-VBA ne devine pas, et n'applique jamais la moitié d'une modification. Quand **Continuer** ou un pas rencontre l'une d'elles, le
programme est refusé et continue d'attendre, et la modification est nommée avec sa raison. Annulez la modification et poursuivez, ou relancez le programme
pour l'avoir.

| Vous avez changé… | Pourquoi cela attend |
|---|---|
| Une variable locale ajoutée à une procédure où se trouve le programme, ou retirée | Les variables de la procédure sont déjà disposées. L'appliquer est prévu pour une version ultérieure. |
| Un champ ajouté à une classe dont le programme peut détenir des objets | Les objets créés avant la modification n'auraient pas de place pour lui. L'appliquer est prévu pour une version ultérieure. |
| Le type d'une variable locale, ou d'une variable, pendant qu'elle détient une valeur | Il faudrait convertir la valeur sous le programme. |
| Une `Const` qui dimensionne un tableau, changée de valeur | Le tableau existe déjà, à son ancienne taille. |
| Les paramètres d'une procédure où se trouve le programme | Ses arguments ont déjà été passés, selon l'ancienne liste de paramètres. |
| La ligne au milieu de laquelle se trouve une procédure appelante (sa ligne d'appel) | Elle est déjà exécutée à moitié. |
| La ligne d'ouverture ou de fermeture d'un bloc où vous vous trouvez : un `For`, un `For Each`, un `With`, un `Select Case`, un `Do` | Le compteur de la boucle, l'objet du `With` ou la valeur du `Select` ont été faits par l'ancienne ligne. |
| Un `GoSub` d'où le programme reviendra, ou le point où retourne un `Resume` | Ils pointent dans le code tel qu'il était. |
| Ce qu'une classe implémente (`Implements`), ou ses `Event`, pendant que des objets de la classe existent | Ces objets sont atteints par ce que la classe implémente. |

**Du code qui ne compile pas :** une faute de frappe, une procédure renommée que quelque chose appelle encore, une variable non déclarée sous
`Option Explicit`. Il n'y a pas de programme à poursuivre tant qu'il ne compile pas de nouveau. Corrigez-le, et **Continuer** poursuit.

## 👀 Où vous le voyez

### Dans l'éditeur

Dès que vous faites une modification qui ne s'appliquera pas, l'éditeur affiche une information :
[**RDC20001** Edit not applied to the waiting program](diagnostics/rdc20001.md) (en anglais). Elle se trouve sur la déclaration ou la
procédure modifiée, là où elle est maintenant, ou, quand le code ne compile plus, là où il ne compile pas. Elle dit pourquoi, et disparaît quand vous annulez
la modification, ou quand le programme se termine.

### Dans le REPL

`CONT` et `STEP` disent quelles modifications font obstacle, chacune avec sa raison, et le programme continue d'attendre :

```text
> 10 Debug.Print "avant"
> 20 Stop
> 30 Debug.Print "après"
> RUN
  avant
  INTERRUPTION À LA LIGNE 20
  ✅ PRÊT.
> 15 Dim Ajout As Long
> CONT
  ⛔ IMPOSSIBLE DE POURSUIVRE
    Des modifications faites pendant que le programme attendait ne lui sont pas appliquées :
    Program.Main: une variable locale ajoutée à une procédure où se trouve le programme, ou retirée, ce qui n'est pas encore appliqué à un programme en attente
  RUN pour recommencer.
  ✅ PRÊT.
```

`EDITS` liste en tout temps ce qui fait obstacle, sans essayer de poursuivre :

```text
> EDITS
  ⚠️ Des modifications faites pendant que le programme attendait ne lui sont pas appliquées :
    Program.Main: une variable locale ajoutée à une procédure où se trouve le programme, ou retirée, ce qui n'est pas encore appliqué à un programme en attente
  RUN pour recommencer.
  ✅ PRÊT.
```

Quand rien ne fait obstacle, ou qu'aucun programme n'attend, il le dit :

```text
> EDITS
  Aucune modification n'attend : CONT poursuit avec toutes les modifications faites.
  ✅ PRÊT.
```

```text
> EDITS
  Aucun programme n'attend.
  ✅ PRÊT.
```

### Dans le débogueur de VS Code

- **Enregistrez le fichier** pour qu'une modification soit appliquée. Le débogueur lit ce qui est enregistré quand vous poursuivez, faites un pas ou fixez la
  prochaine instruction, et une modification non enregistrée n'est pas vue.
- Quand une modification fait obstacle, **Continuer** et les pas sont refusés : le programme reste en pause, et la console de débogage dit quelles
  modifications, et pourquoi.
- Le panneau Problèmes n'affiche pas RDC20001 pour un programme que VS Code débogue. La console de débogage le porte à sa place.

## ⚖️ Comparé à l'éditeur VBA

Mesuré dans Excel (64 bits) à un `Stop` dans une procédure, sur 44 sortes de modifications :

| Sorte de modification | Éditeur VBA | RD-VBA |
|---|---|---|
| Une procédure où vous n'êtes pas | poursuit | poursuit |
| Des instructions de la procédure où vous êtes interrompu, au-dessus, à ou au-dessous de l'interruption | poursuit | poursuit <!-- S2 --> |
| Des instructions d'un appelant, hors de sa ligne d'appel | poursuit | poursuit <!-- S2 --> |
| La ligne d'appel d'un appelant | **réinitialise** | attend, et nomme la modification |
| Une variable locale ajoutée (`Dim`) | poursuit | attend, et nomme la modification (pour l'instant) |
| Une variable locale retirée; une `Static` ajoutée | **réinitialise** | attend, et nomme la modification (pour l'instant) |
| Une variable locale changée de type | **réinitialise** | attend, et nomme la modification |
| Une variable de module `Private`, une `Const` ou un `Declare` ajoutés | poursuit | poursuit |
| Une variable de module `Public` ajoutée | **réinitialise** | poursuit |
| Une procédure ajoutée, dans un module ou une classe | **réinitialise** | poursuit |
| Un `Type` ou un `Enum` ajouté, ou un membre ajouté à un `Enum` | **réinitialise** | poursuit |
| La valeur d'une `Const` changée | **réinitialise** | poursuit, sauf si elle dimensionne un tableau (attend) |
| Les paramètres d'une procédure changés, pour une où vous n'êtes pas | **réinitialise** | poursuit |
| La même chose, pour celle où vous êtes | **réinitialise** | attend, et nomme la modification |
| Un `Declare` changé; une ligne `Option` ajoutée ou retirée | **réinitialise** | poursuit |
| Le corps d'une méthode d'une classe dont des objets existent | poursuit | poursuit |
| Un champ ajouté à une classe dont des objets existent | poursuit | attend, et nomme la modification (pour l'instant) |

Là où l'éditeur VBA réinitialise, il demande d'abord, et **Annuler** défait la modification. Là où RD-VBA attend, rien n'est perdu. Le programme reste où il
était, et vous choisissez : annuler la modification et poursuivre, ou relancer.

## Voir aussi

- [**RDC20001** Edit not applied to the waiting program](diagnostics/rdc20001.md) (en anglais)
- [Bâtissons](getting-started.fr.md)

---
[ACCUEIL](index.fr.md) • [HOME](./index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.html) | 🧩 [BÂTISSONS](getting-started.fr.md) • [BUILD](getting-started.html) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](/RDCore/api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
