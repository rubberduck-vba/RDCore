# Désactiver un diagnostic
<sup>_This document is available in [English](disabling-diagnostics.html)_</sup>

Un diagnostic que vous ne voulez pas voir se désactive par son code. RDCore cesse alors de le signaler, et l'analyseur qui le trouve saute ce travail. Les
codes que vous désactivez sont un de vos paramètres, `Analysis.DisabledDiagnostics`, et valent pour tous les espaces de travail que vous ouvrez.

Les diagnostics des analyseurs de RDCore (`RDC`) et ceux des analyseurs d'une extension peuvent être désactivés. Une erreur de compilation (`VBC`) ou
d'exécution (`VBR`, `VBA`) ne le peut pas : le code qui en a une ne compile pas, ou s'arrête à l'exécution, quels que soient les paramètres.

## Par un correctif rapide

Un diagnostic qui peut être désactivé vient avec un correctif rapide, après ses autres correctifs : **Désactiver RDC01001 dans tous les espaces de
travail**. Le choisir ajoute le code à votre liste. Dans le _shell_, `DUCKY` le propose avec les autres actions d'une ligne; dans un éditeur, c'est un
des correctifs rapides du diagnostic.

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

> [!NOTE]
> **Non implémenté.** Un diagnostic ne peut pas être désactivé pour un seul espace de travail.

Voir [Diagnostics](diagnostics/index.md) pour les codes, et
[**RD-VBAL §2.3.3** Configuration](specs/rd-vbal.2.3.3.configuration.md#disabled-diagnostics) pour le paramètre.

---
[ACCUEIL](index.fr.md) • [HOME](index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.md) | 🧩 [BÂTISSONS](getting-started.fr.md) • [BUILD](getting-started.md) | [**RD-VBAL**](specs/rd-vbal.md) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)
