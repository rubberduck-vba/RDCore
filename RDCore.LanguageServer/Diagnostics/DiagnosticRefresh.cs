using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace RDCore.LanguageServer.Diagnostics;

/// <summary>
/// <strong>LSP 3.17</strong> <c>workspace/diagnostic/refresh</c>: a client that pulls its diagnostics is asked to pull them again, when what they are made of
/// changed - the workspace the project file lists, the live settings.
/// </summary>
/// <remarks>
/// <para>
/// A client says it is asked so with <c>workspace.diagnostics.refreshSupport</c>. The capability's value is <see langword="null"/> when the client sent none
/// of it, which is no support.
/// </para>
/// <para>
/// The refresh is a request, which the client answers. OmniSharp 0.19.9's <c>SendDiagnosticRefresh</c> writes it as a notification, with no <c>id</c>, which a
/// client that keeps to the protocol does not take for the request: it is sent here as the request it is.
/// </para>
/// </remarks>
internal static class DiagnosticRefresh
{
    /// <summary>The method of the request.</summary>
    public const string Method = "workspace/diagnostic/refresh";

    /// <summary>Whether the client is asked to pull its diagnostics again.</summary>
    public static bool IsSupportedBy(ILanguageServerFacade server)
        => server.ClientSettings.Capabilities?.Workspace?.Diagnostics is { IsSupported: true, Value.RefreshSupport: true };

    /// <summary>Asks the client to pull its diagnostics again, if it is asked so; the answer is not waited for.</summary>
    public static void SendIfSupported(ILanguageServerFacade server)
    {
        if (IsSupportedBy(server))
        {
            _ = SendAsync(server);
        }
    }

    // what the client answers changes nothing: a client that fails to pull again pulls when it next does.
    private static async Task SendAsync(IResponseRouter server)
    {
        try
        {
            await server.SendRequest(Method).ReturningVoid(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // the request was asked; an error the client answers it with is the client's.
        }
    }
}
