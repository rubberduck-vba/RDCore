using RDCore.CLI.Host;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/session/status</c>: reports the state of the runtime session this host
/// owns, including what its memory allocator currently accounts for.
/// </summary>
internal sealed class HostSessionStatusHandler(IEnvironmentSessionProvider sessionProvider)
    : RDCoreRequestHandler<HostSessionStatusParams, SessionStatusResult>
{
    protected override Task<SessionStatusResult> HandleAsync(HostSessionStatusParams request, CancellationToken token)
    {
        if (!sessionProvider.IsComposed)
        {
            // not an error: the session is composed on the LSP initialize handshake, so a status
            // request can legitimately arrive first. The caller decides whether to wait.
            return Task.FromResult(new SessionStatusResult());
        }

        var session = sessionProvider.Session;
        return Task.FromResult(new SessionStatusResult
        {
            IsComposed = true,
            ProjectName = sessionProvider.ProjectName,
            ModuleCount = sessionProvider.ModuleCount,
            ReferenceCount = session.References.Count,
            Memory = SessionMemoryStatus.From(session.Memory.Info),
        });
    }
}
