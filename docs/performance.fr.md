# Performance
<sup>_This document is available in [English](performance.html)_</sup>

---
## À quoi s'attendre

RD-VBA exécute la plus grande partie d'un programme en code compilé. Les chiffres ci-dessous le comparent à MS-VBA exécutant le même code dans Excel,
sur la même machine (Windows 11 x64, Excel 64 bits). Chacun est le temps que prend RD-VBA, en multiple du temps que prend MS-VBA pour le même travail :
sous 1, c'est plus rapide; au-dessus de 1, plus lent. Ils décrivent les réglages par défaut : le moteur d'exécution `Compiled` et la configuration de programme `Debug` (`--configuration`, voir [Le _shell_ et les commandes rdc](rdc.fr.md)).

| Genre de code | Temps comparé à MS-VBA |
|---|---|
| Arithmétique sur des variables `Long` et `Double`, conditions, `Select Case`, variables locales et statiques | 0,3–0,8× (plus rapide) |
| Affectation et comparaison de chaînes | 0,7–2× |
| Arithmétique sur des `Variant` | environ 2× |
| Appels à vos propres procédures, récursion, arguments `ByRef` | 3–4× |
| `On Error Resume Next` dans une boucle qui lève des erreurs | environ 5× |
| Appels aux membres de vos propres classes et interfaces | environ 7× |
| Arithmétique sur des `Currency` et des `Date` | environ 7,5× |
| Remplir et additionner un tableau typé | environ 9× |
| Construire une chaîne morceau par morceau, concaténation dans une boucle | 7–19× |
| Affecter un champ d'un type défini par l'utilisateur | environ 19× |
| Créer un objet à chaque itération | environ 23× |
| Affectation `Set`, affectation d'une propriété d'un objet | environ 36× |
| Appels à la bibliothèque standard (`Len`, `CStr`, `CLng`) dans une boucle | environ 75× |
| Lire et écrire les éléments d'un tableau contenu dans un `Variant` | environ 120× |
| L'instruction `Mid` | environ 135× |

### Automatisation d'Excel

| Travail | Temps comparé à MS-VBA |
|---|---|
| Écrire un bloc de cellules d'un coup (`Range(...).Value = valeurs`) | 1,5–2× |
| Lire un bloc de cellules d'un coup (`valeurs = Range(...).Value`) | environ 45× |
| Lire et écrire les cellules une à une (`Cells(r, c).Value`) | environ 550× |

Chaque appel au modèle objet d'Excel passe du programme RD-VBA à Excel, puis revient. Un appel coûte beaucoup plus cher à RD-VBA qu'à MS-VBA : c'est
le nombre d'appels qui décide du temps que prend le code.

## Écrire du code qui s'exécute bien

- **Travaillez les plages par blocs.** Lisez une plage dans un tableau avec un seul `Range(...).Value`, travaillez sur le tableau, puis réécrivez-le
  avec une seule affectation. 10 000 cellules traitées une à une prennent environ 2,5 minutes; en bloc, moins d'un tiers de seconde.
- **Gardez typées les valeurs d'une boucle critique.** Une boucle sur des `Long` ou des `Double` s'exécute plus vite que dans MS-VBA. La même boucle
  sur des `Variant`, ou sur un tableau contenu dans un `Variant`, est plus lente.
- **Voyez ce qui s'exécute compilé.** `rdc compilation` liste les procédures qui ne s'exécutent pas compilées, et ce qui garde chacune dans
  l'interpréteur. Le code dans l'interpréteur est plus lent que le code compilé.
- **Mesurez votre propre code.** `rdc bench` chronomètre chaque `Public Sub Bench*()` des modules standard d'un espace de travail. Lancez-le avant et
  après une modification (voir [Le _shell_ et les commandes rdc](rdc.fr.md)).

## Voir aussi

- [Le _shell_ et les commandes rdc](rdc.fr.md)
- [Démarrer avec RDCore](getting-started.fr.md)

---
[ACCUEIL](index.fr.md) • [HOME](./index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.html) | 🧩 [BÂTISSONS](getting-started.fr.md) • [BUILD](getting-started.html) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
