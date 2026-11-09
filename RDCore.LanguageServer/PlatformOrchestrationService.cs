using RDCore.LanguageServer.Services.Platform;
using RDCore.SDK.Client;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Platform;

namespace RDCore.LanguageServer;

internal interface IPlatformOrchestrationService
{
    public IRDCoreClientApp RuntimeEnvironment { get; }
    public IRDCoreClientApp ParsingService { get; }
    public IEnumerable<IRDCoreClientApp> Extensions { get; }

    public IPlatformOrchestrationService RegisterCoreComponent(Func<IRDCoreServerProxyFactory, IRDCoreClientApp> client);
    IPlatformOrchestrationService RegisterExtension(ExtensionInfo manifest, Func<IRDCoreServerProxyFactory, IRDCoreClientApp> client);
}

internal sealed class PlatformOrchestrationService(IRDCoreServerProxyFactory factory) : IPlatformOrchestrationService
{
    private IRDCoreClientApp? _runtimeEnvironment;
    public IRDCoreClientApp RuntimeEnvironment => _runtimeEnvironment!;

    private IRDCoreClientApp? _parsingService;
    public IRDCoreClientApp ParsingService => _parsingService!;

    private readonly Dictionary<ExtensionInfo, IRDCoreClientApp> _extensions = [];
    public IEnumerable<IRDCoreClientApp> Extensions => _extensions.Values;

    public IPlatformOrchestrationService RegisterCoreComponent(Func<IRDCoreServerProxyFactory, IRDCoreClientApp> client)
    {
        var component = client(factory);
        switch (component.PlatformComponent)
        {
            case CoreServerComponent.EnvironmentHost:
                _runtimeEnvironment = component;
                break;
            case CoreServerComponent.ParsingServer:
                _parsingService = component;
                break;
        }
        return this;
    }
    public IPlatformOrchestrationService RegisterExtension(ExtensionInfo manifest, Func<IRDCoreServerProxyFactory, IRDCoreClientApp> client)
    {
        _extensions[manifest] = client(factory);
        return this;
    }
}
