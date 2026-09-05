using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;
using System.Reflection;

namespace RDCore.SDK.Server.Handlers.Platform;

/// <summary>
/// Carries the hosting app's <see cref="CoreServerComponent"/> into the OmniSharp handler container.
/// </summary>
/// <param name="Component">This server's platform component type.</param>
public sealed record class PlatformComponentContext(CoreServerComponent Component);

/// <summary>
/// Answers the <c>rdcore/platform/initialize</c> handshake with this server's component type and the
/// capabilities its entry assembly declares via <c>[assembly: ProvidesCorePlatformClientCapability&lt;T&gt;]</c>.
/// </summary>
[Method(RDCorePlatformHandshake.Method, Direction.ClientToServer)]
public class PlatformInitializeHandler(ILogger<IJsonRpcHandler> logger, PlatformComponentContext context)
    : RDCoreRequestHandler<PlatformInitializeParams, PlatformInitializeResult>
{
    protected override Task<PlatformInitializeResult> HandleAsync(PlatformInitializeParams request, CancellationToken token)
    {
        var provided = ProvidedCorePlatformCapabilities.Reflect(Assembly.GetEntryAssembly()!);
        logger.LogInformation("Platform handshake: {Component} provides [{Provided}]",
            context.Component, string.Join(", ", provided));

        return Task.FromResult(new PlatformInitializeResult
        {
            Component = context.Component,
            Provided = provided,
        });
    }
}
