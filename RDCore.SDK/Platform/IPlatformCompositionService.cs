using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Reflection;
using System.Text.Json;

namespace RDCore.SDK.Platform;

public interface IPlatformCompositionService
{
    PlatformManifest GetManifest();
    ImmutableArray<ExtensionInfo> GetExtensions();
}

public class PlatformCompositionService(IFileSystem fileSystem, IPlatformEnvironment environment, IExtensionsProvider extensions) : IPlatformCompositionService
{
    private PlatformManifest? _cached;
    private ImmutableArray<ExtensionInfo>? _extensions;

    public ImmutableArray<ExtensionInfo> GetExtensions()
    {
        _extensions ??= [.. extensions.Discover()];
        return _extensions.Value;
    }

    private static JsonSerializerOptions _serializationOptions = new(){ PropertyNameCaseInsensitive = true };
    public PlatformManifest GetManifest()
    {
        if (_cached is null)
        {
            var content = fileSystem.File.ReadAllText(environment.ManifestPath);
            _cached = JsonSerializer.Deserialize<PlatformManifest>(content, _serializationOptions)
                ?? throw new InvalidOperationException();
        }
        return _cached;
    }
}


/// <summary>
/// An <em>abstract factory</em> that creates clients for platform components.
/// </summary>
public interface IRDCoreServerProxyFactory
{
    /// <summary>
    /// Creates a client proxy for a platform component.
    /// </summary>
    /// <param name="platformComponent">The component the proxy launches and connects to.</param>
    /// <param name="capabilities">The platform capabilities the proxy expects the child to provide.</param>
    /// <param name="configureHandlers">App-specific LSP handler configuration for the proxy's client.</param>
    /// <param name="configureServices">App-specific service registrations for the proxy's client.</param>
    /// <param name="extensionInfo">
    /// The extension manifest when <paramref name="platformComponent"/> is <see cref="CoreServerComponent.Extension"/>;
    /// supplies the folder and executable name the proxy resolves the child from.
    /// </param>
    RDCoreServerProxy Create(CoreServerComponent platformComponent, CorePlatformClientCapabilities capabilities,
        Action<IRDCoreLSPHandlerConfigurationBuilder>? configureHandlers = default,
        Action<IServiceCollection>? configureServices = default,
        ExtensionInfo? extensionInfo = default);
}

public class RDCoreServerProxy : RDCoreClientApp
{
    private readonly CoreServerComponent _platformComponent;
    private readonly CorePlatformClientCapabilities _capabilities;
    private readonly Action<IRDCoreLSPHandlerConfigurationBuilder> _configureHandlers;
    private readonly Action<IServiceCollection> _configureServices;

    public RDCoreServerProxy(
        IOptions<SdkAppOptions> options,
        CoreServerComponent platformComponent,
        CorePlatformClientCapabilities capabilities,
        Action<IRDCoreLSPHandlerConfigurationBuilder> configureHandlers,
        Action<IServiceCollection> configureServices,
        IChildConnectionFactory connectionFactory,
        ILogger<RDCoreClientApp> logger,
        ExtensionInfo? extensionInfo = default)
        : base(options, connectionFactory, logger)
    {
        _platformComponent = platformComponent;
        _capabilities = capabilities;
        _configureHandlers = configureHandlers;
        _configureServices = configureServices;
        ExtensionInfo = extensionInfo;
    }

    public override CoreServerComponent PlatformComponent => _platformComponent;

    // platform capabilities travel over rdcore/platform/initialize, not the LSP Experimental node.
    protected override ClientCapabilities ConfigureClientCapabilities(ClientCapabilities capabilities) => capabilities;

    protected override CorePlatformClientCapabilities GetExpectedCapabilities() => _capabilities;

    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder) => _configureHandlers(builder);

    protected override void ConfigureServices(IServiceCollection services) => _configureServices(services);

    // the owning language server escalates a terminal fault through its own shutdown path
    // (CoreLanguageServerApp.BringUpCoreComponentAsync), so the proxy does nothing here.
    protected override void OnConnectionTerminated() { }

    protected override void Dispose(bool disposing) { }
}