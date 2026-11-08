# Démarrer avec RDCore
<sup>_This document is available in [English](getting-started.html)_</sup>

> [!NOTE]
> **La plateforme est en pré-alpha.** Attendez-vous à des aspérités, et à des changements d'une version à l'autre.

---
## Installer la plateforme

Il vous faut le [_runtime_ .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) (x64). Vous l'avez probablement déjà : `dotnet --list-runtimes` affiche `Microsoft.NETCore.App 10.x` si c'est le cas.

1. **Téléchargez** `rdcore-<version>-win-x64.zip` à partir de la [page des _Releases_ GitHub](https://github.com/rubberduck-vba/RDCore/releases). [rubberduckvba.ca](https://rubberduckvba.ca) offre exactement le même fichier.
1. **Extrayez-le** où bon vous semble, `Program Files` compris. Gardez le dossier extrait au complet : chaque partie de la plateforme y trouve les autres.

C'est tout : il n'y a pas d'installateur, et rien à inscrire.

> [!TIP]
> Pour vérifier le téléchargement, prenez aussi `SHA256SUMS` dans la _release_. Dans PowerShell, `(Get-FileHash .\rdcore-<version>-win-x64.zip).Hash` doit correspondre à la ligne du zip qui s'y trouve; dans Git Bash, `sha256sum -c --ignore-missing SHA256SUMS` le vérifie pour vous.

> [!WARNING]
> Les préversions ne sont pas encore signées : SmartScreen peut afficher un avertissement, et le _Smart App Control_ de Windows 11 peut les bloquer.

---
## Le _shell_ RD-VBA

Ouvrez le dossier `RDCore.CLI` et lancez **`rdc.exe`**. Si tout va bien, vous verrez l'écran d'accueil de la plateforme :

![Écran d'accueil du CLI RDCore](images/rdvba-cli-splash.png)

Vous voilà quelque part entre le mode programme du _BASIC_ du _Commodore 64_, le volet _exécution_ (_immediate_) du VBIDE, et un éditeur moderne :

- Tapez `HELP` pour la liste des commandes.
- Tapez n'importe quelle instruction RD-VBA pour l'exécuter sur-le-champ : `Debug.Print 6 * 7`.
- Commencez une ligne par un _numéro de ligne_ pour l'écrire dans le _programme courant_ : `10 Debug.Print "Bonjour"`.

Le programme courant est le corps d'une procédure implicite nommée `Main`, dans un module implicite nommé `Program`. Il reste en mémoire jusqu'à ce que vous fassiez `NEW`, que vous en chargiez un autre avec `LOAD`, ou que vous quittiez avec `EXIT`.

| Commande | Ce qu'elle fait |
|---|---|
| `LIST` | Affiche le programme, mis en forme. `FOLD` replie et déplie ses blocs. |
| `RUN` | L'exécute. |
| `SAVE` / `LOAD` | L'écrit dans un fichier texte `.rdc`, ou l'en lit. |
| `BREAK <ligne#> [<condition>]` | Pose un point d'arrêt; `BREAK TOGGLE <ligne#>` le désactive ou le réactive. `CONT` reprend après un arrêt, et `STEP` exécute une instruction. |
| `VARS`, `STACK` | Affiche les variables et la pile d'appels lors d'un arrêt. |
| `CONFIG` | Affiche et modifie vos paramètres, avec complétion. Les changements de mise en forme s'appliquent aussitôt. |

Lors d'un arrêt, vous pouvez taper des instructions comme dans le volet _exécution_ du VBIDE, et modifier le programme avant de reprendre.

> [!NOTE]
> Le _shell_ est une démo et une façon d'essayer la plateforme, pas un IDE. Il ne prend pas en charge un projet de plus d'une portée de procédure, ni les numéros de ligne à la BASIC dans vos propres modules.

---
## Apporter un projet VBA

RDCore travaille avec les fichiers sources de votre projet, exportés de l'éditeur VBA. Rubberduck (_legacy_) exporte un projet entier en un clic; le VBIDE exporte un module à la fois, à moins de l'automatiser avec la bibliothèque _VBIDE Extensibility_.

Placez les fichiers exportés dans un dossier à part, puis faites-en un espace de travail RDCore. À partir du dossier `RDCore.CLI` :

```
rdc new <chemin>
```

Cette commande écrit un fichier `.rdproj` dans ce dossier, qui nomme chaque fichier source qu'elle y trouve. Les fichiers qu'elle ne reconnaît pas sont listés sous « autres fichiers » : ils font quand même partie de l'espace de travail, simplement pas du code source.

Ouvrez ensuite le _shell_ sur cet espace de travail avec `rdc --workspace <chemin>`.

---
## Utiliser RDCore à partir d'un IDE

Le _shell_ est amusant, mais le vrai travail se fait dans le serveur de langage derrière lui, et un serveur de langage a besoin d'un éditeur. Nous recommandons **[HexIDE](https://github.com/hexide-io/HexIDE)** : contrairement à la plupart des éditeurs, il a été conçu pour les développeurs VB, et vous vous y sentirez chez vous.

> [!NOTE]
> La prise en charge de RDCore dans HexIDE est en cours de développement, dans le module d'extension [RDCore.HexIDE](https://github.com/rubberduck-vba/RDCore.HexIDE).

---
## Bon à savoir

- **RDCore s'exécute en 64 bits**, alors ce qu'il charge doit l'être aussi :
  - les _UserForms_ requièrent Office 64 bits (Microsoft Forms 2.0 en 64 bits);
  - la bibliothèque d'une instruction `Declare` doit être une DLL 64 bits;
  - l'automatisation d'Office (`CreateObject("Excel.Application")`) requiert que l'application Office soit installée.
- **Vos paramètres** sont conservés dans `%APPDATA%\RDCore`, et les **journaux** dans `%LOCALAPPDATA%\RDCore\Logs`. Rien n'est écrit dans le dossier extrait : mettre à jour, c'est remplacer ce dossier.
- **RDCore parle français et anglais**, dans la langue d'affichage de votre machine. `CONFIG Environment.Regional.Language en` (ou `fr`) en choisit une au prochain démarrage de RDCore. Ce paramètre ne change pas la façon d'écrire les nombres ni les dates.
- **Vos projets vous appartiennent** : la GPLv3 couvre RDCore lui-même, et non le code que vous écrivez ni ce que RDCore en construit (voir l'[AVIS](https://github.com/rubberduck-vba/RDCore/blob/main/NOTICE.md)).
- **Vous voulez contribuer, ou bâtir sur la plateforme?** Voyez [Bâtir sur RDCore](building-on-rdcore.fr.md).

> [!NOTE]
> La version française des documents techniques, lorsque disponible, utilise les termes originaux _en anglais_ qui conservent la précision de leur signification, plutôt qu'une traduction approximative qui pourrait facilement être plus confondante qu'utile.

---
[ACCUEIL](index.fr.md) • [HOME](./index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.html) | 🧩 BÂTISSONS • [BUILD](getting-started.html) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](/RDCore/api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
