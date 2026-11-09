using RDCore.CLI.Host;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Platform.Components;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/memory/peek</c>: reads one byte of this host's runtime session memory.
/// </summary>
internal sealed class HostPeekHandler(IEnvironmentSessionProvider sessionProvider)
    : RDCoreRequestHandler<HostPeekParams, PeekResult>
{
    protected override Task<PeekResult> HandleAsync(HostPeekParams request, CancellationToken token)
    {
        if (!sessionProvider.IsComposed)
        {
            return Task.FromResult(new PeekResult { Address = request.Address });
        }

        var found = sessionProvider.Session.Storage.TryPeek(new MemoryAddress(request.Address), out var value);
        return Task.FromResult(new PeekResult
        {
            Address = request.Address,
            IsAllocated = found,
            Value = value,
        });
    }
}

/// <summary>
/// Handles <c>rdcore/host/memory/poke</c>: writes one byte of this host's runtime session memory,
/// unchecked, where no program runs.
/// </summary>
internal sealed class HostPokeHandler(IEnvironmentSessionProvider sessionProvider)
    : RDCoreRequestHandler<HostPokeParams, PokeResult>
{
    protected override Task<PokeResult> HandleAsync(HostPokeParams request, CancellationToken token)
    {
        if (!sessionProvider.IsComposed)
        {
            return Task.FromResult(new PokeResult { Address = request.Address });
        }

        var storage = sessionProvider.Session.Storage;
        var written = sessionProvider.Execution.TryWriteMemory(() => storage.TryPoke(new MemoryAddress(request.Address), request.Value), out var refusal);
        return Task.FromResult(new PokeResult { Address = request.Address, IsWritten = written, Error = refusal });
    }
}
