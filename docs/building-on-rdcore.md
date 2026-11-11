# Building on RDCore
<sup>_Ce document est disponible en [français](building-on-rdcore.fr.html)_</sup>

This page is for contributors, and for anyone building an extension or a client on the platform. To use RDCore, see [Getting started](getting-started.md).

---
## Contributing

1. **First read and accept** the Contributor Licence Agreement (CLA) that fits your situation. It makes the next steps smoother.
1. Browse the _project roadmap_ and pick a ticket under the _current milestone_.
1. _Fork_ the **RDCore** repository to **your GitHub account**.
1. _Clone_ the repository on a workstation that is free of rights (that is, NOT your employer's computer).
1. Open `RDCore.slnx` in _Microsoft Visual Studio Community Edition 2026_ (free for open-source contributions).
   - You can also build the solution with `dotnet build`.
1. Start a new _branch_ from **main**, named after the ticket.
1. Implement and test your contribution in your local branch.
1. When it works and is ready for review, open a _pull request_ that refers to the ticket.
   - Write `Closes #` and the ticket number in the body of the _pull request_.
   - The same mention in a _commit_ of your branch shows in the ticket's history as soon as you _push_ the commit to your _fork_. Other contributors see that the ticket is being worked on.
1. Sign the CLA: copy the signature text it gives into a comment on your pull request.
   - You only do this once.
1. Once your _pull request_ is merged, **delete your branch** and resynchronize _main_ before you start on another ticket.
   - A pull request is merged as one _squash_ commit, without the commits of your branch. More commits on a branch that was already merged are hard to merge again. To submit a fix, start a new branch from _main_, once _main_ has the merged commit.


---
## Building the platform from source

To assemble the platform tree that a preview build ships, run `PlatformPublish.ps1` from a clone. It needs `git` on the `PATH`, to record the commit it builds. It runs under PowerShell 7 (`pwsh`) on Windows, Linux and macOS, and under Windows PowerShell 5.1 on Windows.

- With no arguments, it asks for confirmation, then publishes a Debug tree to `artifacts/rdcore-dev`.
- `-Configuration Release -RuntimeIdentifier win-x64` matches the Windows preview build; `linux-x64`, `osx-x64` and `osx-arm64` match the Linux and macOS ones.
- `-VersionSuffix <suffix>` adds a prerelease suffix, such as `rc.1`.
- `-PlatformRoot <path>` picks the output folder, relative to the repository root.
- `-Silent` skips the confirmation.

The release workflow then adds `THIRD-PARTY-NOTICES.txt`, `SOURCE.md` and `release.json`. Maintainers will find the whole procedure in [RELEASING.md](https://github.com/rubberduck-vba/RDCore/blob/main/RELEASING.md).


---
## Building an RDCore extension

It only takes a few lines in your entry point to make your application an RDCore app:

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

### Host

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

...or by inheriting `RDCorePlatformServerHost` if you're building a _server_ app instead:

```csharp
internal class CoreDiagnosticsAppHost() : RDCorePlatformServerHost<CoreDiagnosticsApp>()
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
```

### Application

In both cases, the host adds services to the `IServiceCollection`, and the application is created with those services injected.

Then, for a client, inherit `RDCoreClientApp`:

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

...and for a server, inherit `RDCoreServerApp` instead:

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

Either way, the application configures its LSP _capabilities_, and the _handlers_ that handle the LSP requests and notifications.

### Client or Server?

- A _client_ application is typically an IDE application.
- A _server_ application could be a satellite language server, or a platform extension (plug-in).
- [LSP 3.17 Specifications](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/)


> [!IMPORTANT]
> **RDCore Platform Extensions** need a _manifest_ (`extension.manifest.json`, schema [ExtensionInfo](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Extensibility.ExtensionInfo.html)) for the language server to _discover_ and bring them up during platform assembly. The manifest is generated by the CLI in _command mode_:
>
> ```
> rdc.exe describe-ext RDCore.Diagnostics.exe --description "…" --unsafe-dev-mode
> ```
>
> `describe-ext` reflects the extension executable's advertised capabilities (its `[assembly: ProvidesCorePlatformClientCapability<T>]` declarations) into the manifest. `PlatformPublish.ps1` runs it once per extension while assembling the platform.

> [!IMPORTANT]
> **Referenced libraries** (`Excel`, `Word`, `Access`, `ADODB`, `Scripting`, …) are known to a project by their _name_, as if the library were VBA code, and what each declares ships with the platform as a _library description_ in `Symbols/<Name>.json` (format: [LibraryDescription](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Runtime.Libraries.LibraryDescription.html)). A project is checked against the description, so it can be analyzed on a machine that does not have the library. A library that the platform has no description of is a reference that cannot be resolved, and the types that name it are compile errors. Libraries that depend on one another are rejected, not resolved.
>
> A description states the type library it was made from, by its identifier and version, and each library it depends on (`dependsOn`) by its name and the identifier and version of the type library it was built against. A reference that gives an identifier and a version is to the description made from that type library: `Symbols/<Name>.<qualifier>.json` describes another type library of the same name, such as `DAO.dao2535.json` beside `DAO.json` ([**RD-VBAL §2.3.1.2** The library a reference is](specs/rd-vbal.2.3.1.2.session-services.md#the-library-a-reference-is)).
>
> A description is generated, never written by hand, on a Windows machine that has the library, by the CLI in _command mode_:
>
> ```
> rdc.exe export-library Excel --dependencies --output Symbols
> ```
>
> `export-library` takes a file (`scrrun.dll`), the identifier the machine gave the library, or its name; when several versions of a library are registered, the latest is described. A file that holds more than one library is given with the number of the library's resource, as a `.vbp` references it: `MSVBVM60.DLL\3` is `VBRUN`, where `MSVBVM60.DLL` alone is its first library, `VBA`. `--dependencies` describes the libraries it depends on too, which a project needs in order to reference it: each is the type library the library is linked with, and one whose name the output folder already has for another type library is written as `<Name>.<file stem>.json`. A member whose type is declared by a library the machine does not have is not described, and the export says which.
>
> The description is what a project is _checked_ against; what it _runs_ against are the objects of the library on the machine that runs it. `New Scripting.Dictionary` is a real dictionary, and `New Excel.Application` starts a real Excel, driven from the sidelines through its object model. These are the automation servers of Windows. On another system, a program that creates one of these objects is told that the platform has none (error 429), but the same program can be analyzed anywhere. Every such call passes through the same interception chain as any call outside the workspace, and an administrator can refuse them all with `"AllowAutomation": false` in `appsettings.json` (error 70, _Permission denied_).


### Capabilities

The language server brings up each extension that has a valid _manifest_, and starts an _LSP handshake_ with it. The extension's initialization parameters must state every **capability** it has: those the protocol (LSP) defines, and those the _environment host_ defines. An extension advertises a platform capability such as [`CliCommand`](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.Client.CliCommand.html) with an assembly attribute:

```csharp
[assembly: ProvidesCorePlatformClientCapability<CliCommand>]
```

> [RD-VBAL §2.0.2](specs/rd-vbal.2.0.2.client-server-capabilities.md) describes the platform capabilities.

> [!NOTE]
> **First and third party extensions** distributed through the **RDCore Platform Cloud Infrastructure** _MAY_ use a _capability provider_ that _MAY_ validate the availability of certain advanced capabilities by **requiring 2FA authentication**, the validation of an **active subscription** (free or paid), and the validation of the _signed build_ against the certified distribution channel build.

---
## Platform-level extensions (SDK and _language core_)

The _language core_ is engineered to be extended through _server_ type extensions, via an exchange of _capabilities_ giving access to extension points.

Please see [RD-VBAL § 1.1](specs/rd-vbal.1.1.philosophy.md) for more details and the platform's extension philosophy at that level.

---
[ACCUEIL](index.fr.md) • [HOME](index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.html) | 🧩 [BÂTISSONS](getting-started.fr.md) • [BUILD](getting-started.html) | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](https://rubberduck-vba.github.io/RDCore.SDK/api/RDCore.SDK.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
