using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/entryPoints</c>: answers with the modules asked about whose members, as the runtime session defined them, include a procedure a
/// program can start with.
/// </summary>
/// <remarks>
/// The members are the ones <c>DefineSymbolsHandler</c> defined: of the headers a declaration has in the branches of a <c>#If</c> block, the live one's. A
/// module is found as <c>rdcore/host/execute</c> finds the one it runs.
/// </remarks>
internal sealed class HostEntryPointsHandler(IEnvironmentSessionProvider sessionProvider)
    : RDCoreRequestHandler<HostEntryPointsParams, HostEntryPointsResult>
{
    protected override Task<HostEntryPointsResult> HandleAsync(HostEntryPointsParams request, CancellationToken token)
    {
        if (!sessionProvider.IsComposed)
        {
            // not an error: the session is composed on the LSP initialize handshake, so a request can arrive first.
            return Task.FromResult(new HostEntryPointsResult());
        }

        var symbols = sessionProvider.Session.Symbols;
        string[] declaring = [.. request.Modules.Where(name => symbols.TryGetModule(name, out var module)
            && ProgramEntryPoint.IsDeclaredBy(module, symbols.MembersOf(module.Uri), request.EntryPoint))];

        return Task.FromResult(new HostEntryPointsResult { Modules = declaring });
    }
}
