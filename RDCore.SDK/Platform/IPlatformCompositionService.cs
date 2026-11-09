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
}

public class PlatformCompositionService(IFileSystem fileSystem, IPlatformEnvironment environment) : IPlatformCompositionService
{
    private PlatformManifest? _cached;

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