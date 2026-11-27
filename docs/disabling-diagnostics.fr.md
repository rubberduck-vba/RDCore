# Désactiver un diagnostic
<sup>_This document is available in [English](disabling-diagnostics.html)_</sup>

Un diagnostic que vous ne voulez pas voir se désactive par son code. RDCore cesse alors de le signaler, et l'analyseur qui le trouve saute ce travail. Les
codes que vous désactivez sont un de vos paramètres, `Analysis.DisabledDiagnostics`, et valent pour tous les espaces de travail que vous ouvrez.

Les diagnostics des analyseurs de RDCore (`RDC`) et ceux des analyseurs d'une extension peuvent être désactivés. Une erreur de compilation (`VBC`) ou
d'exécution (`VBR`, `VBA`) ne le peut pas : le code qui en a une ne compile pas, ou s'arrête à l'exécution, quels que soient les paramètres.

## Par un correctif rapide

Un diagnostic qui peut être désactivé vient avec deux correctifs rapides, après ses autres correctifs :

- **Désactiver RDC01001 dans tous les espaces de travail** ajoute le code à votre liste.
- **Désactiver RDC01001 dans cet espace de travail** ajoute le code à la liste de l'espace de travail
  ([Pour un seul espace de travail](#pour-un-seul-espace-de-travail)).

Dans le _shell_, `DUCKY` les propose avec les autres actions d'une ligne; dans un éditeur, ils font partie des correctifs rapides du diagnostic.

## Par CONFIG

`CONFIG` règle la liste entière d'un coup, en codes séparés par des virgules :

```text
CONFIG Analysis.DisabledDiagnostics RDC01001, RDC01002
```

- `CONFIG Analysis.DisabledDiagnostics` affiche les codes que vous avez désactivés.
- Pour réactiver un code, réglez la liste sans lui.
- `CONFIG RESET Analysis.DisabledDiagnostics` les réactive tous.

Un code qui ne peut pas être désactivé est refusé. `rdc config` règle la même liste depuis une ligne de commande.

## Quand cela s'applique

Tout de suite, pendant que la plateforme tourne : la prochaine analyse d'un document laisse de côté les codes désactivés, et les diagnostics déjà affichés
sont rafraîchis. Une liste réglée avec `rdc config`, sans plateforme en marche, s'applique au prochain démarrage de la plateforme.

## Pour un seul espace de travail

Un espace de travail peut désactiver ses propres codes, dans la section `Configuration` du fichier `appsettings.json` à sa racine :

```json
{
  "Configuration": {
    "Analysis": { "DisabledDiagnostics": [ "RDC01001" ] }
  }
}
```

Quiconque ouvre l'espace de travail a ces codes désactivés en plus des siens : la liste de l'espace de travail s'ajoute à la sienne et ne lui retire rien.
La plateforme lit le fichier à son démarrage; un code ajouté par le correctif rapide **Désactiver RDC01001 dans cet espace de travail** est désactivé
aussitôt.

## Dans le code source

Le code écrit pour l'ancien Rubberduck peut garder ses annotations `'@Ignore` et `'@IgnoreModule`. Elles sont déconseillées, puisque les paramètres
désactivent les diagnostics, et RDCore les lit comme une suppression des codes qu'elles énumèrent :

```vba
'@IgnoreModule ProcedureNotUsed, RDC01001
```

- Dans un fichier d'un seul module, l'une ou l'autre annotation couvre tout le fichier, où qu'elle soit écrite.
- Dans un fichier de blocs `Class` et `Module`, l'une ou l'autre annotation écrite sur les lignes de commentaire juste au-dessus de l'en-tête d'un
  bloc couvre le module de ce bloc. Ailleurs, `'@IgnoreModule` couvre le module où elle est écrite, et `'@Ignore` ne couvre rien.
- Une annotation sans argument couvre tous les codes qui peuvent être désactivés. Une erreur de compilation ou d'exécution est toujours signalée.

Les analyseurs s'exécutent quand même, et seuls leurs résultats pour ces codes sont écartés. Un argument qui ne nomme rien est
[`RDC00418`](diagnostics/rdc00418.md). Voir [**RD-VBAL §2.6.4** Suppression](specs/rd-vbal.2.6.4.rubberduck-core-diagnostics.md#suppression) pour les
règles.

Voir [Diagnostics](diagnostics/index.md) pour les codes, et
[**RD-VBAL §2.3.3** Configuration](specs/rd-vbal.2.3.3.configuration.md#disabled-diagnostics) pour le paramètre.

---
[ACCUEIL](index.fr.md) • [HOME](index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.md) | 🧩 [BÂTISSONS](getting-started.fr.md) • [BUILD](getting-started.md) | [**RD-VBAL**](specs/rd-vbal.md) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)
