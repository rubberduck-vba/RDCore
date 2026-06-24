# Getting Started
> [!Note]
> This documentation may be incomplete at this time.

\[[EN](./getting-started.html)\] | \[[FR](./getting-started.fr.html)\]

It only takes a few lines in your entry point to make your application a RDCore app:

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

## Host

Before you can write these lines, you must define a _host_ by inheriting `RDCoreLanguageClientHost` if you're writing a _client_ :

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

...or by inheriting `VBCoreLanguageServerHost` if you're building a _server_ app instead:

```csharp
internal class CoreDiagnosticsAppHost() : RDCoreLanguageServerHost<CoreDiagnosticsApp>()
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
```

## Application

In both cases, the role of the host is to supply services to the `IServiceCollection` such that the application can be instantiated with injected services.

Then for a client you would inherit `RDCoreClientApp` :

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

...and for a server app we instead inherit the LSP app from `RDCoreServerApp`:

```csharp
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

In any case, the role of this abstraction layer is to configure the _capabilities_ (LSP) of the application, along with the _handlers_ that will be handling the LSP requests and notifications.

## Client or Server?

- A _client_ application is typically an IDE application.
- A _server_ application could be a satellite language server, or a platform extension (plug-in).
- 🌐[LSP 3.17 Specifications](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/)


> [!IMPORTANT]
> 🧩 **RDCore Platform Extensions** need a _manifest_ to enable their _discovery_ by the _environment host_; the schema of this manifest is defined by [ExtensionInfo](./api/RDCore.SDK.Extensibility.ExtensionInfo.html); the _environment host_ may provide _developer tooling_ (CLI) to facilitate the creation of an extension manifest for a given extension.


## Capabilities

RDCore platform extensions with a valid _manifest_ that gets them to initiate a _LSP handshake_ with the LSP _orchestration layer_ must supply initialization parameters that specify a complete set of both LSP (protocol) defined and _environment host-defined **capabilities**_.

> 👉 The complete and exhaustive list of platform capabilities shall be documented in [RD-VBAL §2.0.2](./specs/rd-vbal.2.0.computational-environment.html#202-clientserver-capabilities) as its implementation progresses.

> [!NOTE]
> **First and third party extensions** distributed through the **RDCore Platform Cloud Infrastructure** _MAY_ use a _capability provider_ that _MAY_ validate the availability of certain advanced capabilities by **requiring 2FA authentication**, the validation of an **active subscription** (free or paid), and the validation of the _signed build_ against the certified distribution channel build.

---
[ACCUEIL](index.fr.md) • [HOME](./index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](./introduction.html) | 🧩 [BÂTISSONS](getting-started.fr.md) • BUILD | [**RD-VBAL**](./specs/rd-vbal.html) | [SDK](/api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
