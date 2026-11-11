# Le _shell_ et les commandes rdc
<sup>_This document is available in [English](rdc.html)_</sup>

`rdc.exe`, dans le dossier `RDCore.CLI` de la plateforme, est le programme en ligne de commande de RDCore. Lancé seul, il ouvre le _shell_ RD-VBA.
Avec une commande, il exécute cette commande et se termine.

|Ligne de commande|Ce qu'elle fait|
|---|---|
|`rdc`|Ouvre le _shell_ sur un espace de travail temporaire qui lui est propre, supprimé à la sortie du _shell_.|
|`rdc --workspace <chemin>`|Ouvre le _shell_ sur un espace de travail (voir [Apporter un projet VBA](getting-started.fr.md#apporter-un-projet-vba)).|
|`rdc <commande> [<options>]`|Exécute une commande (ci-dessous), sans le _shell_.|
|`rdc --help`|Liste les options que prend `rdc` lui-même.|
|`rdc <commande> --help`|Liste les options de cette commande.|

À une commande qu'il ne connaît pas, `rdc` répond par la liste des commandes qu'il connaît.

---
## Le _shell_

Le _shell_ exécute ce que vous tapez. Une ligne sans numéro de ligne s'exécute sur-le-champ : `Debug.Print 6 * 7`. Une ligne qui commence par un numéro
de ligne est écrite dans le _programme courant_ : `10 Debug.Print "Bonjour"`. Le programme courant est le corps d'une procédure implicite nommée `Main`,
dans un module implicite nommé `Program`. Il reste en mémoire jusqu'à `NEW`, `LOAD` ou `EXIT`.

La langue du _shell_ est le BASIC de RDCore : la variable qu'une ligne assigne est celle que lit la ligne suivante, sans `Dim`, et `Print` seul est
une instruction.

|Commande|Ce qu'elle fait|
|---|---|
|`HELP`|Liste les commandes du _shell_.|
|`LIST`|Affiche le programme, mis en forme, ou une plage de ses lignes : `LIST 100-200`.|
|`FOLD <ligne#>`|Réduit dans un listage le bloc qui commence à une ligne (un `If`, un `Select Case`, une boucle, un `With`), ou le développe.|
|`INLAYS ON`, `INLAYS OFF`|Affiche dans un listage les indices du serveur de langage (le paramètre auquel chaque argument est donné, le type d'une déclaration qui n'en indique pas), ou les masque.|
|`NEW`|Efface le programme.|
|`SAVE [<nom>]`, `LOAD <nom>`|Écrit le programme dans un fichier texte `.rdc`, ou l'en lit. `SAVE` seul écrit dans le fichier d'où vient le programme.|
|`RUN`|Exécute le programme.|
|`ANALYZE`|Analyse le programme et rapporte ses [diagnostics](diagnostics/index.md).|
|`BREAK <ligne#> [<condition>]`|Place ou retire un point d'arrêt à une ligne; avec une condition, il attend celle-ci : `BREAK 40 I > 5`. `BREAK TOGGLE <ligne#>` le désactive ou le réactive, `BREAK` seul les liste, et `BREAK CLEAR` les retire tous.|
|`CONT [<ligne#>]`|Reprend après un arrêt, ou à partir d'une ligne : `CONT 200`.|
|`STEP`, `STEP INTO`, `STEP OUT`|Exécute une instruction du programme interrompu : sans entrer dans les appels, en y entrant, ou jusqu'à la fin de la procédure courante.|
|`STACK`|Affiche les procédures où se trouve le programme interrompu, de la plus interne à la plus externe.|
|`VARS [<n>]`, `FRAME <n>`|Affiche les variables du programme interrompu. `VARS 1`, ou `FRAME 1` puis `VARS`, porte sur la procédure appelante.|
|`EDITS`|Affiche les modifications faites lors d'un arrêt qui ne sont pas appliquées au programme, et pourquoi (voir [Modifier et continuer](edit-and-continue.fr.md)).|
|`DUCKY [<ligne#>]`|Propose ce qui peut être fait du programme, ou d'une de ses lignes, et fait ce que vous choisissez.|
|`CONFIG`|Affiche vos paramètres et les modifie, avec complétion. Les changements de mise en forme s'appliquent aussitôt.|
|`THEME LIST`, `THEME <nom>`|Liste les thèmes de couleurs du _shell_, ou en choisit un.|
|`CLEAR`|Efface la console.|
|`MEMORY`, `PEEK <adresse>`, `POKE <adresse>, <octet>`|Affiche un sommaire de la mémoire de la session, en lit un octet, ou en écrit un.|
|`EXIT`|Termine la session et quitte. `BYE` et `QUIT` font de même.|

`Ctrl+C` arrête un programme en cours d'exécution. Lors d'un arrêt (une instruction `Stop`, un point d'arrêt, un pas ou `Ctrl+C`), le _shell_ affiche
`INTERRUPTION À LA LIGNE` et le numéro de ligne, puis liste les deux lignes au-dessus de cette ligne et les deux au-dessous, la ligne de l'arrêt étant
mise en évidence. `CONFIG CLI.BreakListingLines` fixe combien de lignes sont listées de chaque côté, à partir du prochain démarrage du _shell_; `0`
n'en liste aucune. Lors d'un arrêt, vous pouvez taper des instructions comme dans le volet _exécution_ du VBIDE, et modifier le programme avant de
reprendre.

> [!NOTE]
> Le _shell_ est une façon d'essayer la plateforme. Il ne prend pas en charge un projet de plus d'une portée de procédure, ni les numéros de ligne à la
> BASIC dans vos propres modules.

---
## Les commandes

Chaque commande prend un espace de travail, soit un dossier, avec ou sans `.rdproj`, soit un seul fichier de module. Sans espace de travail, elle
travaille sur le dossier courant.

|Commande|Alias|Ce qu'elle fait|
|---|---|---|
|`rdc new <chemin>`|`n`|Fait d'un dossier un espace de travail : écrit un `.rdproj` qui nomme chaque fichier de module du dossier. `--name` fixe le nom du projet (celui du dossier par défaut), `--empty` écrit un projet sans fichiers, et `--force` remplace un `.rdproj` existant.|
|`rdc import <chemin>`|`i`|Crée le `.rdproj` d'un projet VB6, à côté de son `.vbp` (voir [Projets VB6](vb6-projects.fr.md)). `--force` remplace un `.rdproj` existant.|
|`rdc run [<chemin>] [-- <arguments>]`|`r`|Exécute le programme de l'espace de travail (ci-dessous). Ce qui suit `--` est la ligne de commande du programme, que retourne `Command$`.|
|`rdc build [<chemin>]`|`bd`|Construit un programme à partir de l'espace de travail : son exécutable et le _runtime_ sur lequel il s'exécute (ci-dessous).|
|`rdc config`|`configure`|Affiche les paramètres de la plateforme et les modifie, comme le `CONFIG` du _shell_, sans démarrer la plateforme.|
|`rdc export-library <bibliothèque>`|`xl`|Décrit une bibliothèque de types de cette machine (un fichier comme `scrrun.dll`, son identifiant ou son nom) dans un fichier qu'un projet peut référencer par son nom. `--dependencies` décrit aussi les bibliothèques dont elle dépend.|
|`rdc compilation [<chemin>]`|`cc`|Indique, pour chaque procédure, si elle s'exécute compilée, et ce qui la garde dans l'interpréteur.|
|`rdc bench [<chemin>]`|`b`|Chronomètre l'interpréteur sur un espace de travail : son analyse syntaxique, son chargement, son analyse, puis chaque `Public Sub Bench*()` de ses modules standard.|
|`rdc describe-ext <extension>`|`x`|Écrit le manifeste d'une extension de la plateforme. Elle s'adresse aux auteurs d'extensions (voir [Bâtir sur RDCore](building-on-rdcore.fr.md)).|
|`rdc install`|`setup`|Installe pour votre compte d'utilisateur la plateforme d'où `rdc` est lancé, et met `rdc` dans votre PATH (ci-dessous). Elle ne prend pas d'espace de travail.|

### Exécuter un programme

`rdc run` exécute le point d'entrée du programme :

- la procédure que le `.rdproj` nomme comme point d'entrée, `Module.Procedure` ou `Procedure`;
- sinon, le `Sub Main` du seul module standard qui en déclare un.

Un projet sans point d'entrée, ou avec un `Sub Main` dans plus d'un module standard, ne s'exécute pas. Un projet dont un module ne se charge pas non
plus. `Debug.Print` écrit sur la sortie standard.

|Option|Ce qu'elle fait|
|---|---|
|`-c`, `--configuration`|`Debug` (par défaut) ou `Release`.|
|`--unattended`|Personne ne répond aux boîtes de dialogue : un `MsgBox` ou un `InputBox` fait échouer l'exécution.|

Le code de sortie indique comment s'est passée l'exécution :

|Code de sortie|Signification|
|---|---|
|`0`|Le programme s'est terminé.|
|`1`|Une erreur d'exécution l'a terminé. Le numéro et la description de l'erreur sont écrits sur la sortie d'erreur.|
|`2`|Il n'a pas pu démarrer : aucun point d'entrée, ou un module qui ne se charge pas.|

### Construire un programme

`rdc build` compile l'espace de travail en un programme qui s'exécute sans l'analyseur syntaxique de RDCore. Il lui faut le même point d'entrée qu'à
`rdc run`.

|Option|Ce qu'elle fait|
|---|---|
|`-o`, `--output`|Le dossier où le programme est écrit; `bin/<configuration>/<runtime>/` dans l'espace de travail par défaut.|
|`-n`, `--name`|Le nom du programme; celui du projet par défaut.|
|`-c`, `--configuration`|`Debug` (par défaut) ou `Release`.|
|`-r`, `--runtime`|La plateforme pour laquelle le programme est construit, comme `win-x64` ou `linux-x64`; celle où s'exécute `rdc` par défaut.|

### Installer la plateforme

`rdc install` installe la plateforme d'où il est lancé, comme une archive de version que vous avez décompressée, pour votre compte d'utilisateur.
Le complément de l'IDE installe la plateforme au même endroit : chacun utilise ce que l'autre a installé.

- Il copie la plateforme dans `RDCore/Platform/versions/<version>/` de votre dossier de données d'application locales : `%LOCALAPPDATA%` sous
  Windows, `~/.local/share` sous Linux, `~/Library/Application Support` sous macOS. Une version déjà installée n'est pas copiée de nouveau, et
  aucune version n'est supprimée.
- Il fait de cette version la version courante, à moins qu'une version plus récente soit installée.
- Il met le `rdc` de la version courante dans votre PATH, à la place de celui d'une version antérieure. Sous Windows, il ajoute le dossier
  `RDCore.CLI` de la version à votre `Path` d'utilisateur. Sous Linux et macOS, il crée le lien `~/.local/bin/rdc` vers celui-ci; quand
  `~/.local/bin` n'est pas dans votre PATH, il ajoute à `~/.profile` (Linux) ou à `~/.zprofile` (macOS) un bloc de lignes qui l'y met. Ouvrez un
  nouveau terminal pour l'utiliser.

|Option|Ce qu'elle fait|
|---|---|
|`--path-only`|Met seulement le `rdc` de la version courante dans votre PATH.|
|`--no-modify-path`|Installe la plateforme sans modifier votre PATH ni votre profil.|

Le code de sortie indique comment s'est passée l'installation :

|Code de sortie|Signification|
|---|---|
|`0`|La plateforme est installée.|
|`1`|Elle ne l'est pas : le dossier d'où `rdc` est lancé n'est pas une plateforme complète, aucune version n'est installée (`--path-only`), ou `~/.local/bin/rdc` est un de vos propres fichiers. Le message indique lequel.|
|`2`|Les options ne sont pas valides.|

---
[ACCUEIL](index.fr.md) • [HOME](index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.html) | 🧩 [BÂTISSONS](getting-started.fr.md) • [BUILD](getting-started.html) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
