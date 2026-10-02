# Let's Build RDCore 
<sup>_Ce document est disponible en [français](getting-started.fr.html)_</sup>

---
## 🚀 Getting Started

1. **First read and accept** the Contributor Licence Agreement (CLA) as appropriate for your situation (it will make the next steps smoother);
1. Browse the _project roadmap_ and pick a ticket under the _current milestone_;
1. _Fork_ the **RDCore** repository to **your GitHub account**;
1. Download a _clone_ of the repository on a workstation that is free of rights (i.e. NOT your employer's computer);
1. Open `RDCore.slnx` un _Microsoft Visual Studio Community Editition 2026_ (free for open-source contributions);
  a. 👉 Alternatively, use `dotnet build` CLI tooling to build the solution.
1. Start a new _branch_ from **main**, named in reference to the selected ticket;
1. Implement and test your contributions in your local branch;
1. When everything works and is ready for review, open a new _pull request_ referring to the selected ticket;
  a. 👉 Mention `Closes #` followed by the ticket number in the body of the _pull request_.
  b. 👉 Such a mention in a _commit_ of your branch history will appear in the ticket history as soon as your commits are _pushed_ to your _fork_, which signals work in progress to other contributors.
1. Sign the CLA by copying the specified signature content into a comment on your PR.
  a. 👉 This will only be need to be done once.
1. Once your _pull request_ is completed, **destroy your feature branch** and resynchronize _main_ to start work on a new ticket.
  a. 👉 The _squash merge_ will destroy the details of your commit history in the central repository, which quickly complicates things if additional commits get added to an already-completed branch; a patch may be submitted by starting a new branch from _main_ resynchronized to contain the _merge commit_.


---
## 📦 Installing a preview build

Preview builds of the platform are published as _prereleases_ on the [GitHub Releases page](https://github.com/rubberduck-vba/RDCore/releases). A preview build is a zip of the whole platform tree: it is not an installer, and it is not a certified build.

1. Download `rdcore-<version>-win-x64.zip` and `SHA256SUMS` from the release;
1. Verify the download: in Git Bash, `sha256sum -c --ignore-missing SHA256SUMS`; in PowerShell, `(Get-FileHash .\rdcore-<version>-win-x64.zip).Hash` must match the zip's line in `SHA256SUMS` (case aside). With the GitHub CLI, `gh attestation verify rdcore-<version>-win-x64.zip -R rubberduck-vba/RDCore --signer-workflow rubberduck-vba/RDCore/.github/workflows/release.yml --source-ref refs/tags/v<version>` also checks that the zip was built by this repository's release workflow, from the `v<version>` tag;
1. Extract the zip to a user-writable folder of its own, one per version, such as `%LOCALAPPDATA%\RDCore\<version>`: the platform writes its `Logs/` inside that folder;
1. Install the [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64) if `dotnet --list-runtimes` doesn't list `Microsoft.NETCore.App 10.x`: preview builds are _framework-dependent_.

To run it:

- Keep the extracted tree whole: the language server finds its platform (`rdcore.json`, the parse server, the extensions) from the parent of its own folder.
- A client launches `RDCore.LanguageServer\RDCore.LanguageServer.exe` with `RDCore.LanguageServer\` as its working directory (`appsettings.json` is read from the working directory) and `--client-process-id <pid> --pipe-name <name> --workspace <uri>`, `<pid>` being the client's own process ID, without which the server and its child processes cannot exit if the client dies; `release.json` describes the same launch under `launch.languageServer`.
- Don't set `RDCORE_PLATFORM_ROOT` in your user or system environment: it overrides the platform root of every RDCore process (and of the processes they start).
- With no arguments, `RDCore.CLI\rdc.exe` is the interactive RD-VBA shell; it also reads `appsettings.json` from its working directory, so start it from `RDCore.CLI\`.

> [!WARNING]
> Preview builds are not signed: SmartScreen may warn about them, and Windows 11 _Smart App Control_ may block them.

> 👉 To assemble the same tree from a clone, run `PlatformPublish.ps1` (it needs `git` on the `PATH`, to record the commit it builds): with no arguments it publishes a Debug tree to `artifacts\rdcore-dev` after asking for confirmation; `-Configuration Release -RuntimeIdentifier win-x64` matches the preview builds, `-VersionSuffix <suffix>` adds a prerelease suffix (e.g. `rc.1`), `-PlatformRoot <path>` picks the output folder (relative to the repository root) and `-Silent` skips the confirmation. The release workflow then adds `THIRD-PARTY-NOTICES.txt`, `SOURCE.md` and `release.json`; maintainers will find the whole procedure in [RELEASING.md](https://github.com/rubberduck-vba/RDCore/blob/main/RELEASING.md).


---
## 🧩 Building a RDCore extension

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

In both cases, the role of the host is to supply services to the `IServiceCollection` such that the application can be instantiated with injected services.

Then for a client you would inherit `RDCoreClientApp` :

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

...and for a server app we instead inherit the LSP app from `RDCoreServerApp`:

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

In any case, the role of this abstraction layer is to configure the _capabilities_ (LSP) of the application, along with the _handlers_ that will be handling the LSP requests and notifications.

### Client or Server?

- A _client_ application is typically an IDE application.
- A _server_ application could be a satellite language server, or a platform extension (plug-in).
- 🌐[LSP 3.17 Specifications](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/)


> [!IMPORTANT]
> 🧩 **RDCore Platform Extensions** need a _manifest_ (`extension.manifest.json`, schema [ExtensionInfo](api/RDCore.SDK.Extensibility.ExtensionInfo.html)) for the language server to _discover_ and bring them up during platform assembly. The manifest is generated by the CLI in _command mode_:
>
> ```
> rdc.exe describe-ext RDCore.Diagnostics.exe --description "…" --unsafe-dev-mode
> ```
>
> `describe-ext` reflects the extension executable's advertised capabilities (its `[assembly: ProvidesCorePlatformClientCapability<T>]` declarations) into the manifest. `PlatformPublish.ps1` runs it once per extension while assembling the platform.


### Capabilities

RDCore platform extensions with a valid _manifest_ that gets them to initiate a _LSP handshake_ with the LSP _orchestration layer_ must supply initialization parameters that specify a complete set of both LSP (protocol) defined and _environment host-defined **capabilities**_. An extension advertises a platform capability such as [`CliCommand`](api/RDCore.SDK.Client.CliCommand.html) with an assembly attribute:

```csharp
[assembly: ProvidesCorePlatformClientCapability<CliCommand>]
```

> 👉 The complete and exhaustive list of platform capabilities shall be documented in [RD-VBAL §2.0.2](specs/rd-vbal.2.0.2.client-server-capabilities.md) as its implementation progresses.

> [!NOTE]
> **First and third party extensions** distributed through the **RDCore Platform Cloud Infrastructure** _MAY_ use a _capability provider_ that _MAY_ validate the availability of certain advanced capabilities by **requiring 2FA authentication**, the validation of an **active subscription** (free or paid), and the validation of the _signed build_ against the certified distribution channel build.

---
## 🧩 Platform-level extensions (SDK and _language core_)

The _lgnauge core_ is engineered to be extended through _server_ type extensions, via an exchange of _capabilities_ giving access to extension points.

Please see [RD-VBAL § 1.1](specs/rd-vbal.1.1.philosophy.md) for more details and the platform's extension philosophy at that level.

---
[ACCUEIL](index.fr.md) • [HOME](index.md) | ℹ️ [BIENVENUE](introduction.fr.md) • [WELCOME](introduction.html) | 🧩 [BÂTISSONS](getting-started.fr.md) • BUILD | [**RD-VBAL**](/RDCore/specs/rd-vbal.html) | [SDK](/RDCore/api/RDCore.SDK.Model.Errors.VBCompileErrorId.html) | 🌐 [rubberduckvba.ca](https://rubberduckvba.ca)

---
