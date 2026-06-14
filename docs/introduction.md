# RDCore SDK

##### [English](./introduction.en.md)

**Cette librairie est open-source** (⚖️MIT) - les contributions sont bienvenues! _SVP lire **CONTRIBUTING.md**_ et signer l'_Entente de Licence Contributeur_ (**CLA**) avant de soumettre votre première _pull request_ pour faciliter le processus.

## 🤔 Qu'est-ce que c'est au juste?

**RDCore** est une _plateforme de langage_ analytique qui implémente **MS-VBAL** et donne ainsi naissance à **RD-VBA**, une implémentation libérée du bagage de Microsoft, avec son propre _runtime_.

**RD-VBA** est donc un langage de programmation _legacy_, mais propulsé par un engin à la fine pointe et permettant une profondeur d'analyse jamais vue en VBA.

C'est donc un _serveur de langage_ (LSP), mais aussi un _client LSP_ en `rdc.exe` - une application _console_ qui se veut implémenter un client LSP pour RD-VBA.

C'est aussi un éventuel _écosystème_ d'extensions exploitant un riche modèle analytique qui expose tous les faits et laisse les diagnostics avoir des opinions à leur sujet.


## 🧩 Démarrage rapide - extensibilité

**Deux types** d'applications sont possibles :

- **ILanguageServerApp** définit une application *serveur*, dont le client est le serveur LSP principal **RDCore.LanguageServer**.
- **ILanguageClientApp** définit une application *client*, par exemple **RDCore.CLI** (`rdc.exe`), ou alors un IDE.

Dans les deux cas, vous aurez besoin d'un **hôte** et d'une **application**, et donc le point d'entrée de toute extension **RDCore** devrait ressembler à ceci:

```csharp
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var host = new CoreDiagnosticsAppHost(); // 👈 serveur
        return await host.RunAsync(args);
    }
}
```

Ceci n'est pas un exemple simplifié : c'est _exactement_ le code à l'entrée de l'extension **Core Diagnostics**. Celui de `rdc.exe` est pratiquement identique:

```csharp
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var host = new RDCoreConsoleClientHost(); // 👈 client
        return await host.RunAsync(args);
    }
}
```

## Hériter de RDCoreServerApp

Évidemment le code intéressant est ailleurs : pour une extension, il vous faudra implémenter l'application LSP dans une classe héritée de `RDCoreServerApp` :

```csharp
internal class MyRDCoreApp : RDCoreServerApp
{
    public MyRDCoreApp(
        // 🧩 Injecter ici toute dépendance spécifique à votre application
        IOptions<SdkServerOptions> options, 
        IServerStateProvider serverStateProvider, 
        IHealthCheckService<MyRDCoreApp> healthCheckService, 
        ILanguageServerProtocolTransportLayer transportLayer, 
        ILogger<MyRDCoreApp> logger) 
        : base(options, serverStateProvider, healthCheckService, transportLayer, logger)
    {
    }

    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
    {
        // 🧩 Configurer ici les handlers LSP spécifiques à votre application
    }

    protected override void RegisterServerCapabilities(ILanguageServer server, ClientCapabilities clientCapabilities)
    {
        // 🧩 Enregistrer ici avec le client (LS) les fonctionnalités de votre application
    }

    protected override void Dispose(bool disposing) 
    {
        // 🧩 Disposer ici au besoin de toute resource non _managée_
    }
}
```

## Injection des services

Ajouter ensuite un _hôte_ et configurez-y l'injection des services requis pour pouvoir créer une instance de votre application :

```csharp
internal class CoreDiagnosticsAppHost() : RDCoreLanguageServerHost<CoreDiagnosticsApp>()
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        // 🧩 Configurer ici l'injection de tout service supplémentaire que vous ajoutez au constructeur de votre application
    }
}
```

Et puis ça y est, vous êtes lancés! 🚀 
