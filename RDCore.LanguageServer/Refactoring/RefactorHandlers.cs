using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// Serves <c>rdcore/refactor/prepare</c>: what a refactoring of a range would ask the user, and what it refuses or would change. A client that shows a dialog
/// of its own asks it: the capability is <see cref="SDK.Client.Refactoring"/>'s.
/// </summary>
/// <remarks>
/// The refactorings it prepares are Extract Method's (<see cref="RefactoringKinds.ExtractMethod"/>) and Convert GoSub's (<see cref="RefactoringKinds.ConvertGoSub"/>);
/// a kind it does not serve is answered an error.
/// </remarks>
internal sealed class RefactorPrepareHandler(IExtractMethodService extractMethod, IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<RefactorPrepareRequest, RefactorPrepareResult>
{
    protected override async Task<RefactorPrepareResult> HandleAsync(RefactorPrepareRequest request, CancellationToken token)
    {
        RefactorRequests.Check(clientCapabilities, request.Kind, request.Uri);
        return await extractMethod.PrepareAsync(request.Kind, request.Uri!, request.Range, token);
    }
}

/// <summary>
/// Serves <c>rdcore/refactor/apply</c>: the edit a refactoring of a range makes with the user's answers, or an error that says why it is refused. The edit is
/// the client's to apply.
/// </summary>
internal sealed class RefactorApplyHandler(IExtractMethodService extractMethod, IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<RefactorApplyRequest, RefactorWorkspaceEdit>
{
    protected override async Task<RefactorWorkspaceEdit> HandleAsync(RefactorApplyRequest request, CancellationToken token)
    {
        RefactorRequests.Check(clientCapabilities, request.Kind, request.Uri);
        var answer = await extractMethod.ApplyAsync(request.Kind, request.Uri!, request.Range, request.Answers, request.Accepted, token);
        return answer.Edit ?? throw new RpcErrorException(RefactorRequests.RequestFailedCode, error: null!, answer.Refusal ?? string.Empty);
    }
}

/// <summary>
/// Serves <c>rdcore/refactor/checkIdentifier</c>: whether an identifier the user is typing for a refactoring of a range can be declared where the refactoring
/// declares it, and what it would hide, before the refactoring is applied. A client's dialog asks it as its user types.
/// </summary>
internal sealed class RefactorIdentifierCheckHandler(IExtractMethodService extractMethod, IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<RefactorCheckIdentifierRequest, RefactorIdentifierCheckResult>
{
    protected override async Task<RefactorIdentifierCheckResult> HandleAsync(RefactorCheckIdentifierRequest request, CancellationToken token)
    {
        RefactorRequests.Check(clientCapabilities, request.Kind, request.Uri);
        return await extractMethod.CheckIdentifierAsync(request.Kind, request.Uri!, request.Range, request.Answers, token);
    }
}

// what the refactor requests check before they are served.
file static class RefactorRequests
{
    // LSP "RequestFailed": the request is valid, and is refused.
    public const int RequestFailedCode = -32803;

    // JSON-RPC 2.0 "Invalid Params".
    private const int InvalidParamsCode = -32602;

    // JSON-RPC 2.0 "Invalid Request".
    private const int InvalidRequestCode = -32600;

    public static void Check(IPlatformClientCapabilitiesService clientCapabilities, string kind, Uri? document)
    {
        if (!clientCapabilities.Expects(capabilities => capabilities.Refactoring))
        {
            throw new RpcErrorException(InvalidRequestCode, error: null!, $"The client did not advertise the '{nameof(SDK.Client.Refactoring)}' platform capability.");
        }

        if (kind is not (RefactoringKinds.ExtractMethod or RefactoringKinds.ConvertGoSub) || document is null)
        {
            throw new RpcErrorException(InvalidParamsCode, error: null!, document is null ? "The request names no document." : $"No refactoring is of the kind '{kind}'.");
        }
    }
}
