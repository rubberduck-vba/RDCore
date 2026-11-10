# Bâtir sur RDCore
<sup>_This document is available in [English](building-on-rdcore.html)_</sup>

Cette page s'adresse aux contributeurs, et à quiconque crée une extension ou un client sur la plateforme. Pour utiliser RDCore, voyez [Démarrage](getting-started.fr.md).

---
## Contribuer

1. **Lisez et acceptez d'abord** l'accord de licence contributeur (CLA) qui convient à votre situation. Ça facilitera la suite.
1. Parcourez la _feuille de route_ du projet et choisissez un ticket du _jalon en cours_.
1. Faites un _fork_ du référentiel **RDCore** dans **votre compte GitHub**.
1. Faites un _clone_ du référentiel sur un poste libre de droits (donc pas sur un poste fourni par votre employeur).
1. Ouvrez `RDCore.slnx` dans _Microsoft Visual Studio Community Edition 2026_ (gratuit pour les contributions open source).
   - Vous pouvez aussi compiler la solution avec `dotnet build`.
1. Démarrez une nouvelle _branche_ à partir de **main**, nommée d'après le ticket.
1. Effectuez et testez votre contribution dans votre branche locale.
1. Lorsqu'elle fonctionne et est prête pour revue, ouvrez une _pull request_ qui fait référence au ticket.
   - Écrivez `Closes #` suivi du numéro du ticket dans le corps de la _pull request_.
   - La même mention dans un _commit_ de votre branche paraît dans l'historique du ticket dès que vous poussez le commit dans votre _fork_. Les autres contributeurs voient ainsi que le ticket est en cours.
1. Signez la CLA : copiez le texte de signature qu'elle donne dans un commentaire sur votre pull request.
   - Vous ne le faites qu'une seule fois.
1. Une fois votre _pull request_ fusionnée, **supprimez votre branche** et resynchronisez _main_ avant de commencer un autre ticket.
   - Une pull request est fusionnée en un seul commit (_squash_), sans les commits de votre branche. D'autres commits sur une branche déjà fusionnée se fusionnent mal. Pour soumettre un correctif, démarrez une nouvelle branche à partir de _main_, une fois que _main_ a le commit fusionné.


---
## Compiler la plateforme à partir des sources

Pour assembler l'arborescence de la plateforme que livre une préversion, exécutez `PlatformPublish.ps1` à partir d'un clone. Il requiert `git` dans le `PATH`, pour consigner le _commit_ qu'il compile. Il s'exécute avec PowerShell 7 (`pwsh`) sous Windows, Linux et macOS, et avec Windows PowerShell 5.1 sous Windows.

- Sans arguments, il demande une confirmation, puis publie un _build_ Debug dans `artifacts/rdcore-dev`.
- `-Configuration Release -RuntimeIdentifier win-x64` correspond à la préversion pour Windows; `linux-x64`, `osx-x64` et `osx-arm64`, à celles pour Linux et macOS.
- `-VersionSuffix <suffixe>` ajoute un suffixe de préversion, par exemple `rc.1`.
- `-PlatformRoot <chemin>` choisit le dossier de sortie, relatif à la racine du référentiel.
- `-Silent` omet la confirmation.

Le _workflow_ de publication y ajoute ensuite `THIRD-PARTY-NOTICES.txt`, `SOURCE.md` et `release.json`. Les mainteneurs trouveront la procédure complète dans [RELEASING.md](https://github.com/rubberduck-vba/RDCore/blob/main/RELEASING.md) (en anglais).


---
## Créer une extension RDCore

Il suffit de quelques lignes dans votre point d'entrée pour que votre application **RDCore** soit prise en charge :

```csharp
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var host = new RDCoreConsoleClientHost();
        return await host.RunAsync(args);
    }
}
```

### Hôte

Avant de pouvoir écrire ces lignes, il faudra définir votre _hôte_ en héritant de `RDCoreLanguageClientHost` si vous construisez un _client_ :

```csharp
internal class RDCoreConsoleClientHost() : RDCoreLanguageClientHost<RDCoreConsoleClientApp>()
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddSingleton<IAppThemeService, AppThemeService>()
            .AddSingleton<IAppThemeLoaderService, AppThemeLoaderService>()
            .AddSingleton<IConsoleMessageWriter, DefaultConsoleMessageWriter>()
            .AddSingleton<ILoggerProvider, RDCoreConsoleLoggerProvider>()
            .AddSingleton<ShowSplashCommand>();
    }
}
```

...ou alors en héritant de `RDCorePlatformServerHost` si vous construisez plutôt un _serveur_ :

```csharp
internal class CoreDiagnosticsAppHost() : RDCorePlatformServerHost<CoreDiagnosticsApp>()
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
```

### Application

Dans les deux cas, l'hôte ajoute des services au `IServiceCollection`, et l'application est créée avec ces services injectés.

Ensuite, pour un client, héritez de `RDCoreClientApp` :

```csharp
internal class RDCoreConsoleClientApp(
    IOptions<SdkAppOptions> options,
    IChildConnectionFactory connectionFactory,
    ILogger<RDCoreConsoleClientApp> logger)
    : RDCoreClientApp(options, connectionFactory, logger)
{
    public override CoreServerComponent PlatformComponent => CoreServerComponent.ClientApp;

    protected override void ConfigureServices(IServiceCollection services)
    {
    }

    protected override ClientCapabilities ConfigureClientCapabilities(ClientCapabilities capabilities)
    {
        return capabilities;
    }

    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
    {
    }

    protected override void Dispose(bool disposing) { }
}
```

...et pour un serveur, héritez plutôt de `RDCoreServerApp` :

```csharp
internal class CoreDiagnosticsApp(
    IOptions<SdkAppOptions> options,
    IServerStateProvider serverStateProvider,
    IHealthCheckService<CoreDiagnosticsApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<CoreDiagnosticsApp> logger)
    : RDCoreServerApp(options, serverStateProvider, healthCheckService, transportLayer, logger)
{
    public override CoreServerComponent PlatformComponent => CoreServerComponent.Extension;

    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
    {
    }

    protected override void RegisterServerCapabilities(ILanguageServer server, ClientCapabilities clientCapabilities)
    {
    }

    protected override void Dispose(bool disposing) { }
}
```

Dans les deux cas, l'application configure ses _capacités_ LSP, et les _handlers_ qui prennent en charge les requêtes et notifications LSP.


### Client ou Serveur?

- Une application _client_ est généralement une application de type IDE.
- Une application _serveur_ peut être un serveur de langage satellite ou une extension (plug-in) de la plateforme.
- [LSP 3.17 Specifications](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/)


> [!IMPORTANT]
> **Les extensions de la plateforme RDCore** requièrent un _manifest_ (`extension.manifest.json`, schéma [ExtensionInfo](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Extensibility.ExtensionInfo.html)) pour que le serveur de langage puisse les _découvrir_ et les démarrer lors de l'assemblage de la plateforme. Le manifest est généré par la CLI en _mode commande_ :
>
> ```
> rdc.exe describe-ext RDCore.Diagnostics.exe --description "…" --unsafe-dev-mode
> ```
>
> `describe-ext` reflète les capacités déclarées par l'exécutable d'extension (ses déclarations `[assembly: ProvidesCorePlatformClientCapability<T>]`) dans le manifest. `PlatformPublish.ps1` l'exécute une fois par extension lors de l'assemblage de la plateforme.

> [!IMPORTANT]
> **Les bibliothèques référencées** (`Excel`, `Word`, `Access`, `ADODB`, `Scripting`, …) sont connues d'un projet par leur _nom_, comme si la bibliothèque était du code VBA, et ce que chacune déclare est livré avec la plateforme sous forme de _description de bibliothèque_ dans `Symbols/<Nom>.json` (format : [LibraryDescription](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Libraries.LibraryDescription.html)). Un projet est vérifié contre la description : il peut donc être analysé sur une machine qui n'a pas la bibliothèque. Une bibliothèque dont la plateforme n'a pas de description est une référence qui ne peut pas être résolue, et les types qui la nomment sont des erreurs de compilation. Les bibliothèques qui dépendent les unes des autres sont rejetées, non résolues.
>
> Une description indique la bibliothèque de types dont elle est tirée, par son identifiant et sa version, et chaque bibliothèque dont elle dépend (`dependsOn`) par son nom et l'identifiant et la version de la bibliothèque de types contre laquelle elle a été construite. Une référence qui donne un identifiant et une version désigne la description tirée de cette bibliothèque de types : `Symbols/<Nom>.<qualificatif>.json` décrit une autre bibliothèque de types du même nom, comme `DAO.dao2535.json` à côté de `DAO.json` ([**RD-VBAL §2.3.1.2** The library a reference is](specs/rd-vbal.2.3.1.2.session-services.md#the-library-a-reference-is)).
>
> Une description est générée, jamais écrite à la main, sur une machine Windows qui a la bibliothèque, par la CLI en _mode commande_ :
>
> ```
> rdc.exe export-library Excel --dependencies --output Symbols
> ```
>
> `export-library` accepte un fichier (`scrrun.dll`), l'identifiant que la machine a donné à la bibliothèque, ou son nom; quand plusieurs versions d'une bibliothèque sont enregistrées, la plus récente est décrite. Un fichier qui contient plus d'une bibliothèque se donne avec le numéro de la ressource de la bibliothèque, comme un `.vbp` la référence : `MSVBVM60.DLL\3` est `VBRUN`, alors que `MSVBVM60.DLL` seul est sa première bibliothèque, `VBA`. `--dependencies` décrit aussi les bibliothèques dont elle dépend, qu'un projet doit avoir pour la référencer : chacune est la bibliothèque de types à laquelle la bibliothèque est liée, et celle dont le dossier de sortie a déjà le nom pour une autre bibliothèque de types est écrite sous `<Nom>.<racine du fichier>.json`. Un membre dont le type est déclaré par une bibliothèque que la machine n'a pas n'est pas décrit, et l'exportation dit lequel.
>
> La description est ce contre quoi un projet est _vérifié_; ce contre quoi il _s'exécute_, ce sont les objets de la bibliothèque sur la machine qui l'exécute. `New Scripting.Dictionary` est un vrai dictionnaire, et `New Excel.Application` démarre un vrai Excel, piloté de l'extérieur par son modèle objet. Ce sont les serveurs d'automation de Windows. Sur un autre système, un programme qui crée un de ces objets apprend que la plateforme n'en a pas (erreur 429), mais le même programme peut être analysé n'importe où. Chacun de ces appels passe par la même chaîne d'interception que tout appel à l'extérieur de l'espace de travail, et un administrateur peut tous les refuser avec `"AllowAutomation": false` dans `appsettings.json` (erreur 70, _Permission denied_).


### Capacités

Le serveur de langage démarre chaque extension qui a un _manifest_ valide, et entame avec elle un _LSP handshake_. Les paramètres d'initialisation de l'extension doivent déclarer chaque **capacité** qu'elle a : celles que définit le protocole (LSP), et celles que définit l'_hôte de l'environnement_. Une extension déclare une capacité de plateforme telle que [`CliCommand`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.CliCommand.html) au moyen d'un attribut d'assembly :

```csharp
[assembly: ProvidesCorePlatformClientCapability<CliCommand>]
```

> La section [RD-VBAL §2.0.2](specs/rd-vbal.2.0.2.client-server-capabilities.md) décrit les capacités de la plateforme.

> [!NOTE]
> **Les extensions tant de première que de tierces parties** distribuées à travers l'**infranuagique RDCore** _PEUVENT_ utiliser un _capability provider_ qui _PEUT_ valider la disponibilité de certaines capacités avancées en **requérant une authentification 2FA**, la validation d'une **inscription active** (gratuite ou payante), et la validation d'un _build signé_ avec le _build officiel_ du canal de distribution certifié.


---
## Extension de la plateforme (SDK et _coeur de langage_)

Le _coeur de langage_ est conçu pour être étendu à travers des extensions de la plateforme de type _serveur_, moyennant un échange de _capacités_ donnant accès à des points d'extensions.

Voir [RD-VBAL § 1.1](specs/rd-vbal.1.1.philosophy.md) pour les détails et la philosophie d'extension de la plateforme à ce niveau.

---
[ACCUEIL](index.fr.md) • [HOME](./index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.html) | 🧩 [BÂTISSONS](getting-started.fr.md) • [BUILD](getting-started.html) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
