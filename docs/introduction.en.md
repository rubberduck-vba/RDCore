# RDCore SDK

##### [Français](./introduction.md)

**This library is open-source** (⚖️MIT) - contributions are welcome! _Please see **CONTRIBUTING.md**_ and sign the _Contributor License Agreement_ (**CLA**) before you submit your first _pull request_ for a smoother experience.

## 🤔 Sounds cool, what is it?

**RDCore** is an analytical _language server platform_ that implements **MS-VBAL** and so gives birth to **RD-VBA**, an implementation that is freed from the baggage of Microsoft, with its own _runtime_.

This makes **RD-VBA** a _legacy_ programming language, but powered by a world-class engine allowing analytical depth that is yet unseen in VBA.

It's a _language server_ (LSP), but also a _LSP client_ in `rdc.exe` - a _console application_ that wants to be a RD-VBA LSP client.

It's also an eventual _ecosystem_ of extensions exploiting a rich analytical model that exposes all the _facts_, and then lets diagnostics have _opinions_ about them.


## 🧩 Extensibility Quick Start

**Two types** of applications are possible :

- **ILanguageServerApp** defines a *serveur* application; its client is the _language server_ (LS) **RDCore.LanguageServer**.
- **ILanguageClientApp** defines a *client* application, for exemple **RDCore.CLI** (`rdc.exe`), or an IDE.

In every case, you will need a **host** and an **application**, so the _entry point_ of any **RDCore** extension should look something like this:

```csharp
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var host = new CoreDiagnosticsAppHost(); // 👈 server
        return await host.RunAsync(args);
    }
}
```

This isn't simplified example code: it's _exactly_ the entry point implementation for the **Core Diagnostics** extension. The entry point of `rdc.exe` is practically identical:

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

## Inherit RDCoreServerApp

Obviously the juicy bits are elsewhere - you still need to implement an actual LSP application:

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
        // 🧩 Configure the LSP handlers specific to your application
    }

    protected override void RegisterServerCapabilities(ILanguageServer server, ClientCapabilities clientCapabilities)
    {
        // 🧩 Register the capabilities of your application with the client (LS)
    }

    protected override void Dispose(bool disposing) 
    {
        // 🧩 Dispose of any _unmanaged resources_ here as needed
    }
}
```

## Inject services

Then a host - this is where you can configure any additional services that might be needed to create an instance of your application:

```csharp
internal class CoreDiagnosticsAppHost() : RDCoreLanguageServerHost<CoreDiagnosticsApp>()
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        // 🧩 Configure dependency injection for any additional services required to instantiate your application
    }
}
```

And then that's it, you're flying! 🚀 
