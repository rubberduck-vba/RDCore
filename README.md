# RDCore™

[Anglais](./README.en.md)

### Avant de commencer.

> Nouveau ici? Rubberduck a toujours été une initiative open-source.
> **RDCore l'honore avec une formule Open-Core**. Voir [rubberduckvba.ca](https://rubberduckvba.ca) pour plus d'informations.

Ce référentiel contient différents projets produisant différentes librairies et exécutables.

**Tout code sous licence GPLv3 dépend de code sous licence MIT**, et jamais l'inverse; il y a une _barrière inter-processus_ claire entre les composantes.

- ⚖️ les projets sous licence **MIT** se divisent en deux catégories:
   - 👉 The _LSP and RD-VBA language core_ (SDK).
   - 🧩 Extensions or otherwise _terminal_ (non-library) projects.
- ⚖️ **GPLv3** projects are protected and _unless explicitly authorized in writing by **9562-7303 Québec inc.**_ (through a commercial agreement), any derived work must be released alongside its source code and licensed under GPLv3.

Cet arrangement protège tant les contributeurs historiques qu'actuels, en s'assurant que **l'implémentation du _runtime_ de RDCore demeure dans les mains de sa communauté open-source**.

VIVAT CUCUMIS™

---

## Application

Le référentiel est consistué d'un bouquet d'applications client/serveur LSP : 

- **RDCore.LanguageServer** construit `RDCore.LanguageServer.exe`, la composante responsable de la gestion de l'_espace de travail_, et les services en arrière-plan pour toutes les fonctionnalités IDE supportées par LSP 3.17, des listes de complétion aux refactorings. Vous ne démarrez normalement pas un _serveur LSP_ vous-même : en vertu du protocole, le _client LSP_ s'en charge pour vous.
- **RDCore.Diagnostics** construit `RDCore.Diagnostics.exe`, un serveur LSP _satellite_ détenu par une instance de **RDCore.LanguageServer**, responsable de l'analyse du contexte sémantique de tout ce qui lui passe sous la main.
- **RDCore.Parsing** construit `RDCore.Parsing.exe`, un serveur LSP _satellite_ détenu par une instance de **RDCore.LanguageServer**, responsable de l'analyse du code source et de sa transformation en arborescence abstraite de syntaxe (AST), constitué de noeuds définis dans la librairie SDK.
- **RDCore.Runtime** construit `RDCore.Runtime.exe`, un serveur LSP _satellite_ détenu par une instance de **RDCore.LanguageServer**, détient les implémentations concrètes qui sont clées pour l'interprétation du code et la gestion de la mémoire applicative : **laisser ce logiciel hors de portée d'une license MIT assure que RD-VBA demeure gratuit et open-source pour tous.**
- **RDCore.CLI** construit `rdc.exe`, une application console qui implémente un **client LSP** léger qui consomme le SDK.

...et de librairies :

- **RDCore.SDK** est RD-VBA en boîte : cette librairie modélise, encapsule et expose l'entièreté du système de typage et les sémantiques statiques et run-time du langage dans une seule librairie, complètement documentée.
- **RDCore.Tests** détient la couverture de tests couvrant le SDK / _coeur de langage_.

Le terme _coeur de langage_ ("language core") réfère à un sous-ensemble d'espaces de noms dans la librairie SDK qui ensemble, définissent RD-VBA en tant que langage, le SDK en lui-même étant plus large que le seul coeur de langage. La librairie SDK définit également tout dont que n'importe quelle extension **RDCore** a besoin pour partir du bon pied et focuser sur ce qui l'intéresse.


![RDCore solution projects](./assets/RDCore-solution.png)

## Extensibilité

La plateforme **RDCore** est entièrement documentée et est conçue pour être étendue. 

**Deux types** d'applications sont possibles :

- **ILanguageServerApp** définit une application *serveur*, dont le client est le serveur LSP principal **RDCore.LanguageServer**.
- **ILanguageClientApp** définit une application *client*, par exemple **RDCore.CLI** (`rdc.exe`), ou alors un IDE.


### Créer une application *Serveur*

Pour créer une nouvelle application *serveur*:

- Créer d'abord un `CancellationTokenSource`, qui contrôlera la sortie de l'hôte.
- Créer ensuite un `RDCoreLanguageServerHost` en passant le `CancellationTokenSource` dans son construteur.
  - 🧩 **RDCoreLanguageServerHost** peut être utilisée *tel quel*, ou alors hérité pour `override` la configuration par défaut de l'application.
- Ouvrir un bloc `try..catch` et appeler (`await`) la méthode asynchrone `RunAsync` de l'application en lui passant les arguments reçus au point d'entrée.
- Capter `OperationCanceledException` pour la sortie normale.
- Capter toute autre exception pour une sortie en erreur.
- Quitter le processus en retournant le `ExitCode` de l'application.


```csharp
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var processTokenSource = new CancellationTokenSource();
        var app = new RDCoreLanguageServerHost(processTokenSource);

        try
        {
            await app.RunAsync(args);
        }
        catch (OperationCanceledException exception)
        {
            app.LogIfEnabled(LogLevel.Debug, exception.Message);
        }
        catch (Exception exception)
        {
            app.LogIfEnabled(LogLevel.Critical, exception.ToString());
            return -1;
        }

        return app.ExitCode;
    }
}
```

Implémenter ensuite `ILanguageServerApp` en héritant de la classe abstraite `LanguageServerApp`:
- Implémenter la méthode abstraite `RegisterServerCapabilitiesAsync` pour enregistrer les capacités LSP de votre application.
- Implémenter la méthode abstraite `ConfigureHandlers` pour configurer les *handlers* LSP de votre application.


### Créer une application *Client*


Pour créer une nouvelle application *serveur*:

- Créer d'abord un `CancellationTokenSource`, qui contrôlera la sortie de l'hôte.
- Créer ensuite un `RDCoreLanguageClientHost` en passant le `CancellationTokenSource` dans son construteur.
  - 🧩 **RDCoreLanguageClientHost** peut être utilisée *tel quel*, ou alors hérité pour `override` la configuration par défaut de l'application.
- Ouvrir un bloc `try..catch` et appeler (`await`) la méthode asynchrone `RunAsync` de l'application en lui passant les arguments reçus au point d'entrée.
- Capter `OperationCanceledException` pour la sortie normale.
- Capter toute autre exception pour une sortie en erreur.
- Quitter le processus en retournant le `ExitCode` de l'application.

```csharp
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var processTokenSource = new CancellationTokenSource();
        var app = new RDCoreLanguageClientHost(processTokenSource);

        try
        {
            await app.RunAsync(args);
        }
        catch (OperationCanceledException exception)
        {
            app.LogIfEnabled(LogLevel.Debug, exception.Message);
        }
        catch (Exception exception)
        {
            app.LogIfEnabled(LogLevel.Critical, exception.ToString());
            return -1;
        }

        return app.ExitCode;
    }
}
```

Implémenter ensuite `ILanguageClientApp` en héritant de la classe abstraite `LanguageClientApp`:
- Implémenter la méthode abstraite `RegisterServerCapabilitiesAsync` pour enregistrer les capacités LSP de votre application.
- Implémenter la méthode abstraite `ConfigureHandlers` pour configurer les *handlers* LSP de votre application.

```csharp
internal class MyApp(
    IHealthCheckService healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<MyApp> logger) 
    : LanguageClientApp(healthCheckService, transportLayer, logger)
{
    protected override ClientCapabilities ConfigureClientCapabilities(ClientCapabilities capabilities)
    {
        // ...
        return capabilities;
    }

    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
    {
        // ...
    }

    protected override void Dispose(bool disposing) { }
}
```

Les services passés au constructeur sont résolus et injectés automatiquement.
