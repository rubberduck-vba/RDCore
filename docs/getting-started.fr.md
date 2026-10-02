# Bâtissons RDCore 
<sup>_This document is available in [English](getting-started.html)_</sup>

---
## 🚀 Démarrage

1. **Lisez et acceptez d'abord** l'accord de licence contributeur (CLA) approprié (ça facilitera la suite);
1. Prenez connaissance de la _feuille de route_ du projet et sélectionnez un ticket;
1. Démarrez un _fork_ du référentiel **RDCore** sur **votre compte GitHub**;
1. Téléchargez un _clone_ du référentiel sur un poste libre de droits (donc pas sur un poste fourni par votre employeur);
1. Ouvrez `RDCore.slnx` dans _Microsoft Visual Studio Community Edition 2026_ (gratuit pour contributions open-source);
  à. 👉 Alternativement, utilisez les outils CLI `dotnet build` pour compiler la solution.
1. Démarrez une nouvelle _branche_ à partir de **main**, nommée en référence au ticket sélectionné;
1. Effectuez et testez vos contributions dans votre branche locale;
1. Lorsque tout fonctionne et est prêt pour revue, ouvrez une _pull request_ en y référant le ticket sélectionné;
  a. 👉 Mentionnez `Closes #` suivi du numéro du ticket dans le corps de la _pull request_.
  b. 👉 Une telle mention dans un _commit_ de votre historique apparaîtra sur l'historique de ce ticket dès que vos commits sont poussés dans votre _fork_, ce qui signale un travail en cours aux autres contributeurs.
1. Signez la CLA en copiant le texte de la signature dans un nouveau commentaire sur la PR.
  a. 👉 Ceci ne sera nécessaire qu'une seule fois.
1. Une fois votre _pull request_ complétée, **détruisez la branche** et resynchronisez _main_ pour démarrer un nouveau développement. 
  à. 👉 Le _squash merge_ détruira le détail de l'historique de vos _commits_ dans le référentiel central, ce qui complique rapidement les choses si des commits additionnels s'ajoutent à une branche déjà complétée; un correctif peut être soumis en démarrant une nouvelle branche à partir de _main_ resynchronisé avec le _merge commit_.


---
## 📦 Installer une préversion

Les préversions de la plateforme sont publiées comme _prereleases_ sur la [page des _Releases_ GitHub](https://github.com/rubberduck-vba/RDCore/releases). Une préversion est une archive zip de l'arborescence complète de la plateforme : ce n'est ni un installateur, ni un _build_ certifié.

1. Téléchargez `rdcore-<version>-win-x64.zip` et `SHA256SUMS` à partir de la _release_;
1. Vérifiez le téléchargement : dans Git Bash, `sha256sum -c --ignore-missing SHA256SUMS`; dans PowerShell, `(Get-FileHash .\rdcore-<version>-win-x64.zip).Hash` doit correspondre (sans égard à la casse) à la ligne du zip dans `SHA256SUMS`. Avec la CLI GitHub, `gh attestation verify rdcore-<version>-win-x64.zip -R rubberduck-vba/RDCore --signer-workflow rubberduck-vba/RDCore/.github/workflows/release.yml --source-ref refs/tags/v<version>` vérifie aussi que le zip a été produit par le _workflow_ de publication de ce référentiel, à partir de l'étiquette (_tag_) `v<version>`;
1. Extrayez le zip dans un dossier accessible en écriture à l'utilisateur, un dossier par version, par exemple `%LOCALAPPDATA%\RDCore\<version>` : la plateforme écrit ses journaux (`Logs/`) dans ce dossier;
1. Installez le [_runtime_ .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) (x64) si `dotnet --list-runtimes` ne liste pas `Microsoft.NETCore.App 10.x` : les préversions sont _framework-dependent_.

Pour l'exécuter :

- Conservez l'arborescence extraite au complet : le serveur de langage trouve sa plateforme (`rdcore.json`, le _parse server_, les extensions) à partir du parent de son propre dossier.
- Un client démarre `RDCore.LanguageServer\RDCore.LanguageServer.exe` avec `RDCore.LanguageServer\` comme répertoire de travail (`appsettings.json` est lu à partir du répertoire de travail) et les arguments `--client-process-id <pid> --pipe-name <nom> --workspace <uri>`, où `<pid>` est l'identifiant du processus client, sans lequel le serveur et ses processus enfants ne peuvent pas se terminer si le client meurt; `release.json` décrit ce même lancement sous `launch.languageServer`.
- Ne définissez pas `RDCORE_PLATFORM_ROOT` dans votre environnement utilisateur ou système : cette variable remplace la racine de la plateforme pour tout processus RDCore (et pour les processus qu'il démarre).
- Sans arguments, `RDCore.CLI\rdc.exe` est le _shell_ RD-VBA interactif; il lit lui aussi `appsettings.json` à partir de son répertoire de travail, alors démarrez-le à partir de `RDCore.CLI\`.

> [!WARNING]
> Les préversions ne sont pas signées : SmartScreen peut afficher un avertissement, et le _Smart App Control_ (contrôle intelligent des applications) de Windows 11 peut les bloquer.

> 👉 Pour assembler la même arborescence à partir d'un clone, exécutez `PlatformPublish.ps1` (il requiert `git` dans le `PATH`, pour consigner le _commit_ qu'il compile) : sans arguments, il publie un _build_ Debug dans `artifacts\rdcore-dev` après une demande de confirmation; `-Configuration Release -RuntimeIdentifier win-x64` correspond aux préversions, `-VersionSuffix <suffixe>` ajoute un suffixe de préversion (par ex. `rc.1`), `-PlatformRoot <chemin>` choisit le dossier de sortie (relatif à la racine du référentiel) et `-Silent` omet la confirmation. Le _workflow_ de publication y ajoute ensuite `THIRD-PARTY-NOTICES.txt`, `SOURCE.md` et `release.json`; les mainteneurs trouveront la procédure complète dans [RELEASING.md](https://github.com/rubberduck-vba/RDCore/blob/main/RELEASING.md) (en anglais).


---
## 🧩 Créer une extension RDCore

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

Dans les deux cas, le rôle de l'hôte est de fournir les services au `IServiceCollection` de sorte que l'application puisse être instanciée en lui injectant tous les services dont elle a besoin.

Ensuite pour un client on hérite l'application LSP de `RDCoreClientApp` :

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
        // TODO
        return capabilities;
    }

    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
    {
        // TODO
    }

    protected override async Task OnLanguageClientStartedAsync(ILanguageClient client, CancellationToken token)
    {
        // TODO
    }

    protected override void Dispose(bool disposing) { }
}
```

...et pour un serveur on hérite l'application LSP de `RDCoreServerApp` :

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
        // TODO
    }

    protected override void RegisterServerCapabilities(ILanguageServer server, ClientCapabilities clientCapabilities)
    {
        // TODO
    }

    protected override void Dispose(bool disposing)
    {
        // TODO
    }
}
```

Dans tous les cas, le rôle de ce niveau d'abstraction est de configurer les _capacités_ (LSP) de l'application et les _handlers_ pour la prise en charge de requêtes et notifications LSP.


### Client ou Serveur?

- Une application _client_ est généralement une application de type IDE.
- Une application _serveur_ peut être un serveur de langage satellite ou une extension (plug-in) de la plateforme.
- 🌐[LSP 3.17 Specifications](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/)


> [!IMPORTANT]
> 🧩 **Les extensions de la plateforme RDCore** requièrent un _manifest_ (`extension.manifest.json`, schéma [ExtensionInfo](./api/RDCore.SDK.Extensibility.ExtensionInfo.html)) pour que le serveur de langage puisse les _découvrir_ et les démarrer lors de l'assemblage de la plateforme. Le manifest est généré par la CLI en _mode commande_ :
>
> ```
> rdc.exe describe-ext RDCore.Diagnostics.exe --description "…" --unsafe-dev-mode
> ```
>
> `describe-ext` reflète les capacités déclarées par l'exécutable d'extension (ses déclarations `[assembly: ProvidesCorePlatformClientCapability<T>]`) dans le manifest. `PlatformPublish.ps1` l'exécute une fois par extension lors de l'assemblage de la plateforme.


### Capacités

Les extensions de la plateforme RDCore avec un _manifest_ valide qui leur permet d'initier un _LSP handshake_ avec la _couche d'orchestration_ LSP doit fournir des paramètres d'initialisation qui spécifient un jeu complet de _capacités_ définies tant par le protocole (LSP) que _définies par l'hôte de l'environnement_. Une extension déclare une capacité de plateforme telle que [`CliCommand`](./api/RDCore.SDK.Client.CliCommand.html) au moyen d'un attribut d'assembly :

```csharp
[assembly: ProvidesCorePlatformClientCapability<CliCommand>]
```

> 👉 La liste complète et exhaustive des capacités de la plateforme sera documentée à la section [RD-VBAL §2.0.2](specs/rd-vbal.2.0.2.client-server-capabilities.md) à mesure que progresse son implémentation.

> [!NOTE]
> **Les extensions tant de première que de tierces parties** distribuées à travers l'**infranuagique RDCore**  _PEUVENT_ utiliser un _capability provider_ qui _PEUT_ valider la disponibilité de certains capacités avancées en **requérant une authentification 2FA**, la validation d'une **inscription active** (gratuite ou payante), et la validation d'un _build signé_ avec le _build officiel_ du canal de distribution certifié.


---
## 🧩 Extension de la plateforme (SDK et _coeur de langage_)

Le _coeur de langage_ est conçu pour être étendu à travers des extensions de la plateforme de type _serveur_, moyennant un échange de _capacités_ donnant accès à des points d'extensions.

Voir [RD-VBAL § 1.1](specs/rd-vbal.1.1.philosophy.md) pour les détails et la philosophie d'extension de la plateforme à ce niveau.

---
[ACCUEIL](index.fr.md) • [HOME](./index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.html) | 🧩 BÂTISSONS • [BUILD](getting-started.html) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](/RDCore/api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
