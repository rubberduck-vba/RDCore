using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.External.Protocol;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Channels;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Server.Services.States;
using System.IO.Pipes;

namespace RDCore.External.Hosting;

/// <summary>
/// The external host: the process that makes the calls an environment host's program makes to the outside world.
/// </summary>
/// <remarks>
/// <para>
/// What a program reaches outside the platform - the objects of an automation server, the functions of a native library - runs code the platform did not write, in the
/// process that calls it. A failure of that code takes the process down with it, so it is not the process that owns the session: the environment host starts this one
/// when its program can reach out, and a program whose call took it down is told so, as an error it can handle, by a session that is still there.
/// </para>
/// <para>
/// The connection the environment host started this process with is the platform's, as any component's is, and says no more than that it is up. The calls themselves
/// go over a channel of their own (<see cref="CallChannel"/>), on a pipe this process opens before it is connected to.
/// </para>
/// <para>
/// Nothing here is particular to a platform: the servers it reaches are the ones the process was composed with (<see cref="ExternalAutomationService"/>).
/// </para>
/// </remarks>
public sealed class ExternalHostApp(
    IOptions<SdkAppOptions> options,
    IServerStateProvider serverStateProvider,
    IHealthCheckService<ExternalHostApp> healthCheckService,
    ILanguageServerProtocolTransportLayer transportLayer,
    ExternalAutomationService automation,
    ILogger<ExternalHostApp> logger)
    : RDCoreServerApp(options, serverStateProvider, healthCheckService, transportLayer, logger)
{
    private CallChannel? _calls;

    /// <inheritdoc/>
    public override CoreServerComponent PlatformComponent => CoreServerComponent.ExternalHost;

    /// <inheritdoc/>
    protected override void ConfigureHandlers(IRDCoreLSPHandlerConfigurationBuilder builder)
    {
    }

    /// <inheritdoc/>
    protected override void RegisterServerCapabilities(ILanguageServer server, ClientCapabilities clientCapabilities)
    {
    }

    // the pipe the calls go over is there before the environment host is told this process is up, which is when it connects to it.
    /// <inheritdoc/>
    protected override Task BeforeRunAsync(string[] args)
    {
        var pipe = new NamedPipeServerStream(
            ExternalProtocol.CallsPipeName(options.Value.Platform.Transport.PipeConfig.PipeName), PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
        _ = AcceptCallsAsync(pipe);
        return base.BeforeRunAsync(args);
    }

    private async Task AcceptCallsAsync(NamedPipeServerStream pipe)
    {
        try
        {
            await pipe.WaitForConnectionAsync();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            LogIfEnabled(LogLevel.Warning, $"🔌 The environment host did not connect to the calls pipe: {exception.Message}");
            return;
        }

        _calls = new CallChannel(pipe);
        automation.ServeOn(_calls);
        LogIfEnabled(LogLevel.Information, "🔌 The calls channel is up.");
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing) => _calls?.Dispose();
}
