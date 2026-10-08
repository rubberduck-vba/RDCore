using Microsoft.Extensions.Logging;
using RDCore.CLI.Host;
using RDCore.Runtime.Execution;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/discard</c>: takes a module out of the runtime session this host owns - what it declared and what it held, and the code and
/// the semantic model that were made of it.
/// </summary>
/// <remarks>
/// A module that is defined again keeps every name it no longer declares, with the storage it was given and the value it holds. This is what a
/// client that clears a program asks for, so that the next definition starts from nothing.
/// </remarks>
internal sealed class HostDiscardHandler(
    IEnvironmentSessionProvider sessionProvider,
    ILogger<HostDiscardHandler> logger) : RDCoreRequestHandler<HostDiscardParams, DiscardSessionResult>
{
    protected override Task<DiscardSessionResult> HandleAsync(HostDiscardParams request, CancellationToken token)
    {
        if (!sessionProvider.IsComposed)
        {
            return Task.FromResult(new DiscardSessionResult());
        }

        var session = sessionProvider.Session;
        var discarded = new ModuleUnloader(session, sessionProvider.Image).Unload(request.ModuleUri);

        // what the module held is released, and was allocated after the rest of the program's: the free memory at the end of the space is unused again.
        session.Memory.Reclaim();

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("🗑️ {module}: {discarded} symbol(s) discarded.", request.ModuleName, discarded);
        }

        return Task.FromResult(new DiscardSessionResult { Discarded = discarded });
    }
}
