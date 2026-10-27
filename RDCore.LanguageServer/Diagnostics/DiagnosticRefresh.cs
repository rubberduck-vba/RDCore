using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace RDCore.LanguageServer.Diagnostics;

/// <summary>
/// <strong>LSP 3.17</strong> <c>workspace/diagnostic/refresh</c>: a client that pulls its diagnostics is asked to pull them again, when what they are made of
/// changed - the workspace the project file lists, the live settings.
/// </summary>
/// <remarks>
/// A client says it is asked so with <c>workspace.diagnostics.refreshSupport</c>. The capability's value is <see langword="null"/> when the client sent none
/// of it, which is no support.
/// </remarks>
internal static class DiagnosticRefresh
{
    /// <summary>Whether the client is asked to pull its diagnostics again.</summary>
    public static bool IsSupportedBy(ILanguageServerFacade server)
        => server.ClientSettings.Capabilities?.Workspace?.Diagnostics is { IsSupported: true, Value.RefreshSupport: true };

    /// <summary>Asks the client to pull its diagnostics again, if it is asked so.</summary>
    public static void SendIfSupported(ILanguageServerFacade server)
    {
        if (IsSupportedBy(server))
        {
            server.Workspace.SendDiagnosticRefresh(new DiagnosticRefreshParams());
        }
    }
}
