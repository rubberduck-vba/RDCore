using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using OmniSharp.Extensions.JsonRpc.Server.Messages;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace RDCore.SDK.Server.Services;

/// <summary>
/// A pre-<c>Initialize</c> gate for incoming JSON-RPC messages that, unlike <c>OmniSharp</c>'s own
/// internal <c>LspServerReceiver</c>, lets a bare <c>Exit</c> notification through.
/// </summary>
/// <remarks>
/// <a href="https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#initialize">Server lifecycle § Initialize Request</a>
/// carves out an explicit exception: until <c>Initialize</c> completes, the server must reject any
/// other request with a <c>ServerNotInitialized</c> (<c>-32002</c>) error and silently drop any other
/// notification — <strong>except</strong> <c>Exit</c>, which lets a client that changes its mind before
/// ever initializing still terminate the server. <c>OmniSharp</c>'s own receiver does not implement
/// that exception: it drops a pre-<c>Initialize</c> <c>Exit</c> like any other notification, so
/// <see cref="Handlers.Lifecycle.ExitHandler"/> never runs, <see cref="States.IServerStateProvider.OnExit"/>
/// never fires, and the host's own wait on the process token
/// (<c>RDCoreServerApp.RunAsync</c>'s <c>serverExit.WaitAsync(...)</c>) hangs instead of exiting.
/// 👉 Registered ahead of <c>OmniSharp</c>'s own <c>IReceiver</c> via <c>LanguageServerOptions.Services</c>
/// (see <c>ConfigureCoreSdkHandlers</c>) — the internal registration uses DryIoc's <c>Keep</c> semantics,
/// so whichever <see cref="IReceiver"/> is registered first wins.
/// </remarks>
internal sealed class RDCoreLifecycleReceiver(ILogger<RDCoreLifecycleReceiver> logger) : Receiver
{
    public override (IEnumerable<Renor> results, bool hasResponse) GetRequests(JToken container)
    {
        if (_initialized)
        {
            return base.GetRequests(container);
        }

        var (parsed, hasResponse) = base.GetRequests(container);
        var allowed = new List<Renor>();

        foreach (var renor in parsed)
        {
            if (renor.IsResponse || renor.IsError
                || (renor.IsRequest && renor.Request!.Method == GeneralNames.Initialize)
                || (renor.IsNotification && (renor.Notification!.Method == GeneralNames.Initialized || renor.Notification!.Method == GeneralNames.Exit)))
            {
                allowed.Add(renor);
                continue;
            }

            if (renor.IsRequest)
            {
                logger.LogWarning("Rejecting request {Method} received before Initialize.", renor.Request!.Method);
                allowed.Add(new RpcError(renor.Request!.Id, renor.Request!.Method, new ErrorMessage(-32002, "Server Not Initialized")));
            }
            else if (renor.IsNotification)
            {
                logger.LogWarning("Dropping notification {Method} received before Initialize.", renor.Notification!.Method);
            }
        }

        return (allowed, hasResponse);
    }
}
