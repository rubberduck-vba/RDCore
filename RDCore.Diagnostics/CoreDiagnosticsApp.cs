using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.Diagnostics.Analyzers;
using RDCore.Diagnostics.Handlers;
using RDCore.SDK.Client;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;

namespace RDCore.Diagnostics;

internal class CoreDiagnosticsAppHost() : RDCorePlatformServerHost<CoreDiagnosticsApp>()
{
    protected override void ConfigureAdditionalExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DiagnosticsOptions>(configuration.GetSection("Configuration:Diagnostics"));
    }
}


internal class CoreDiagnosticsApp(
    IOptions<SdkAppOptions> options,
    IOptions<DiagnosticsOptions> diagnosticsOptions,
    IServerStateProvider serverStateProvider,
    IHealthCheckService<CoreDiagnosticsApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<CoreDiagnosticsApp> logger)
: RDCoreServerApp(options, serverStateProvider, healthCheckService, transportLayer, logger)
{
    public override CoreServerComponent PlatformComponent => CoreServerComponent.Extension;

    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
    {
        builder.WithHandler<DiagnoseDocumentHandler>();
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ICoreDiagnosticsFactory, DiagnosticFactory>();

        // the analyzers are built by the container of the language server library, whose own AddOptions would supply an unconfigured default: it is handed the
        // instance the host configured.
        services.AddSingleton(diagnosticsOptions);

        // every analyzer the extension has, which the handler calls for each document it diagnoses.
        services.AddSingleton<IModuleAnalyzer, OptionExplicitAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, ObsoleteCallStatementAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, NotAllPathsReturnValueAnalyzer>();

        // the ones that read how the module is written.
        services.AddSingleton<IModuleAnalyzer, OptionBaseAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, TypeDefDirectiveAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, ImplicitByRefModifierAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, ImplicitPublicMemberAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, ImplicitVariantDeclarationAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, ImplicitVariantReturnTypeAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, IntegerDataTypeAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, ModuleScopeDimAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, MultilineParameterAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, MultipleDeclarationsAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, MisleadingByRefParameterAnalyzer>();

        // the obsolete syntax.
        services.AddSingleton<IModuleAnalyzer, ObsoleteRemCommentAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, ObsoleteErrorStatementAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, ObsoleteGlobalModifierAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, ObsoleteLetStatementAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, ObsoleteTypeHintAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, ObsoleteWhileWendAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, ObsoleteOnLocalErrorStatementAnalyzer>();

        // what the host vouches for about a member, and the names a module chooses.
        services.AddSingleton<IModuleAnalyzer, ImplementationsShouldBePrivateAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, UseMeaningfulIdentifierNamesAnalyzer>();
        services.AddSingleton<IModuleAnalyzer, HungarianNotationAnalyzer>();
    }

    protected override void Dispose(bool disposing)
    {
    }

    protected override void RegisterServerCapabilities(ILanguageServer server, ClientCapabilities clientCapabilities)
    {
    }
}