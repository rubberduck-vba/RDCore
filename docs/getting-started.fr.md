# Démarrage
> [!Note]
> Cette documentation peut être incomplète en ce moment.

\[[EN](./getting-started.html)\] | \[[FR](./getting-started.fr.html)\]


## 🧩 Extension de la plateforme

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

...ou alors en héritant de `RDCoreLanguageServerHost` si vous construisez plutôt un _serveur_ :

```csharp
internal class CoreDiagnosticsAppHost() : RDCoreLanguageServerHost<CoreDiagnosticsApp>()
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
    IRDCoreLanguageServerProcess serverProcess,
    IHealthCheckService<RDCoreConsoleClientApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<RDCoreConsoleClientApp> logger) 
    : RDCoreClientApp(serverProcess, healthCheckService, transportLayer, logger)
{
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

```
internal class CoreDiagnosticsApp : RDCoreServerApp
{
    public CoreDiagnosticsApp(
        IServerStateProvider serverStateProvider, 
        IHealthCheckService<CoreDiagnosticsApp> healthCheckService, 
        ILanguageServerProtocolTransportLayer transportLayer, 
        ILogger<CoreDiagnosticsApp> logger) 
        : base(serverStateProvider, healthCheckService, transportLayer, logger)
    {
    }

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
> 🧩 **Les extensions de la plateforme RDCore** requièrent un _manifest_ pour permettre leur _découverte_ par _l'hôte d'environnement_; le schéma de ce manifest est défini par [ExtensionInfo](./api/RDCore.SDK.Extensibility.ExtensionInfo.html); _l'hôte d'environnement_ peut founir des _outils de développement_ (CLI) pour faciliter la création d'un manifest pour une extension en cours de développement.


### Capacités

Les extensions de la plateforme RDCore avec un _manifest_ valide qui leur permet d'initier un _LSP handshake_ avec la _couche d'orchestration_ LSP doit fournir des paramètres d'initialisation qui spécifient un jeu complet de _capacités_ définies tant par le protocole (LSP) que _définies par l'hôte de l'environnement_.

> 👉 La liste complète et exhaustive des capacités de la plateforme sera documentée à la section [RD-VBAL §2.0.2](./specs/rd-vbal.2.0.computational-environment.html#202-clientserver-capabilities) à mesure que progresse son implémentation.

> [!NOTE]
> **Les extensions tant de première que de tierces parties** distribuées à travers l'**infranuagique RDCore**  _PEUVENT_ utiliser un _capability provider_ qui _PEUT_ valider la disponibilité de certains capacités avancées en **requérant une authentification 2FA**, la validation d'une **inscription active** (gratuite ou payante), et la validation d'un _build signé_ avec le _build officiel_ du canal de distribution certifié.


## 🧩 Extension de la plateforme (SDK)

Le _coeur de langage_ est conçu pour être étendu à travers des extensions de la plateforme de type _serveur_, moyennant un échange de _capacités_ donnant accès à des points d'extensions.

> [!NOTE]
> 🎯 Ni ces _points d'extensions_, ni ces _capacités_ ne sont à ce stade-ci pas encore formellement définies. Leur _découverte_ au fil de l'avancement de la _spécification de la plateforme_ motivera leur spécification et fait _partie intégrante du périmètre_ du projet _open-core_.
>
> Les points d'extensions prévus sont notamment:
>  - **Injection de sémantique des jetons (_token semantics_)**: une extension doit pouvoir enregistrer une _capacité serveur_ permettant à une extension d'être un _fournisseur externe_ de _token semantics_.

---
[ACCUEIL](index.fr.md) • [HOME](./index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](./introduction.html) | 🧩 BÂTISSONS • [BUILD](./getting-started.html) | [**RD-VBAL**](./specs/rd-vbal.html) | [SDK](/api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
