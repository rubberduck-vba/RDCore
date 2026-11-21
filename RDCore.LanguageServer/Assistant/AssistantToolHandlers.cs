using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Assistant;
using RDCore.SDK.Server.Services;
using System.Globalization;

namespace RDCore.LanguageServer.Assistant;

/// <summary>
/// Serves <c>rdcore/tools/list</c>: the tools of the server's catalog, in the language of the client's user interface.
/// </summary>
internal sealed class ToolsListHandler(AssistantToolCatalog catalog, IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<ToolsListRequest, ToolsListReply>
{
    protected override Task<ToolsListReply> HandleAsync(ToolsListRequest request, CancellationToken token)
    {
        AssistantToolGate.Check(clientCapabilities);
        return Task.FromResult(new ToolsListReply(catalog.List(CultureInfo.CurrentUICulture)));
    }
}

/// <summary>
/// Serves <c>rdcore/tools/call</c>: calls a tool of the server's catalog.
/// </summary>
internal sealed class ToolsCallHandler(AssistantToolCatalog catalog, IPlatformClientCapabilitiesService clientCapabilities, IncomingDocumentChanges incoming)
    : RDCoreRequestHandler<ToolCallRequest, ToolCallReply>
{
    protected override async Task<ToolCallReply> HandleAsync(ToolCallRequest request, CancellationToken token)
    {
        AssistantToolGate.Check(clientCapabilities);

        // a tool answers of the files as the client last said they are: an edit the client applied, and said it wrote, is read before the tool is called.
        await incoming.WhenAllFileChangesReadAsync(token);
        return new ToolCallReply(await catalog.CallAsync(request, token));
    }
}

// a client that did not say it expects the tools is not served them.
internal static class AssistantToolGate
{
    // JSON-RPC 2.0 "Invalid Request".
    private const int InvalidRequestCode = -32600;

    public static void Check(IPlatformClientCapabilitiesService clientCapabilities)
    {
        if (!clientCapabilities.Expects(capabilities => capabilities.AssistantTools))
        {
            throw new RpcErrorException(InvalidRequestCode, error: null!,
                string.Format(CultureInfo.CurrentUICulture, AssistantToolTexts.NotAdvertised, nameof(AssistantTools)));
        }
    }
}
