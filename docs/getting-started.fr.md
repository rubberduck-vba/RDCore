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

> 👉 La liste complète et exhaustive des capacités de la plateforme sera documentée à la section [RD-VBAL §2.0.2](./specs/rd-vbal.2.0.computational-environment.html#202-clientserver-capabilities) à mesure que progresse son implémentation.

> [!NOTE]
> **Les extensions tant de première que de tierces parties** distribuées à travers l'**infranuagique RDCore**  _PEUVENT_ utiliser un _capability provider_ qui _PEUT_ valider la disponibilité de certains capacités avancées en **requérant une authentification 2FA**, la validation d'une **inscription active** (gratuite ou payante), et la validation d'un _build signé_ avec le _build officiel_ du canal de distribution certifié.


---
## 🧩 Extension de la plateforme (SDK et _coeur de langage_)

Le _coeur de langage_ est conçu pour être étendu à travers des extensions de la plateforme de type _serveur_, moyennant un échange de _capacités_ donnant accès à des points d'extensions.

Voir [RD-VBAL § 1.1](/specs/rd-vbal.1.1.philosophy.md) pour les détails et la philosophie d'extension de la plateforme à ce niveau.

---
[ACCUEIL](index.fr.md) • [HOME](./index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.html) | 🧩 BÂTISSONS • [BUILD](getting-started.html) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](/RDCore/api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
