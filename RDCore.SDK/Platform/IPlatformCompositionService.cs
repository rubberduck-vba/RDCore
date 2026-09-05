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

public class PlatformCompositionService(IFileSystem fileSystem, IExtensionsProvider extensions) : IPlatformCompositionService
{
    private static readonly string _manifestFileName = "rdcore.json";
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
            var path = fileSystem.Path.Combine(fileSystem.Directory.GetParent(fileSystem.Directory.GetCurrentDirectory())!.FullName, _manifestFileName);
            var content = fileSystem.File.ReadAllText(path);
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
    RDCoreServerProxy Create(CoreServerComponent platformComponent, CorePlatformClientCapabilities capabilities,
        Action<IRDCoreLSPHandlerConfigurationBuilder>? configureHandlers = default,
        Action<IServiceCollection>? configureServices = default);
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
        ILogger<RDCoreClientApp> logger)
        : base(options, connectionFactory, logger)
    {
        _platformComponent = platformComponent;
        _capabilities = capabilities;
        _configureHandlers = configureHandlers;
        _configureServices = configureServices;
    }

    public override CoreServerComponent PlatformComponent => _platformComponent;

    protected override ClientCapabilities ConfigureClientCapabilities(ClientCapabilities capabilities)
    {
        capabilities.Experimental = new Dictionary<string, JToken>() { ["rdcore"] = JToken.FromObject(_capabilities) };
        return capabilities;
    }

    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder) => _configureHandlers(builder);

    protected override void ConfigureServices(IServiceCollection services) => _configureServices(services);

    protected override void Dispose(bool disposing) { }
}