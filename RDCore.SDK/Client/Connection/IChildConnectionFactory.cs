using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RDCore.SDK.Server;

namespace RDCore.SDK.Client.Connection;

/// <summary>
/// Creates a fresh <see cref="ChildConnection"/> — with its own process and transport — per call.
/// </summary>
public interface IChildConnectionFactory
{
    ChildConnection Create();
}

internal sealed class ChildConnectionFactory(IServiceProvider services) : IChildConnectionFactory
{
    // resolve a fresh IRDCoreServerProcess per connection: one process instance supervises one child.
    public ChildConnection Create() => new(
        services.GetRequiredService<IRDCoreServerProcess>(),
        services.GetRequiredService<ILanguageServerProtocolTransportLayer>(),
        services.GetRequiredService<ILogger<ChildConnection>>());
}
