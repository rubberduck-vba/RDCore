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
/// The refactorings it prepares are Extract Method's (<see cref="RefactoringKinds.ExtractMethod"/>), Convert GoSub's
/// (<see cref="RefactoringKinds.ConvertGoSub"/>), Encapsulate Field's (<see cref="RefactoringKinds.EncapsulateField"/>), of the field named at the
/// start of the range, Move Closer To Usage's (<see cref="RefactoringKinds.MoveCloserToUsage"/>), of the variable named at the start of the range,
/// Reorder Parameters'
/// (<see cref="RefactoringKinds.ReorderParameters"/>), of the procedure named at the start of the range or whose parameter is declared there, and Inline
/// Member's (<see cref="RefactoringKinds.InlineMember"/>), of the call or the declaration named at the start of the range; a kind it does not serve is answered
/// an error.
/// </remarks>
internal sealed class RefactorPrepareHandler(
    IExtractMethodService extractMethod, IPlatformClientCapabilitiesService clientCapabilities, IEncapsulateFieldService? encapsulateField = null,
    IMoveCloserToUsageService? closerToUsage = null, IParameterReorderService? parameterReorder = null, IInlineMemberService? inlineMember = null)
    : RDCoreRequestHandler<RefactorPrepareRequest, RefactorPrepareResult>
{
    protected override async Task<RefactorPrepareResult> HandleAsync(RefactorPrepareRequest request, CancellationToken token)
    {
        RefactorRequests.Check(clientCapabilities, request.Kind, request.Uri, encapsulateField, closerToUsage, parameterReorder, inlineMember);
        return request.Kind switch
        {
            RefactoringKinds.EncapsulateField => await encapsulateField!.PrepareAsync(request.Uri!, request.Range.Start, token),
            RefactoringKinds.MoveCloserToUsage => await closerToUsage!.PrepareAsync(request.Uri!, request.Range.Start, token),
            RefactoringKinds.ReorderParameters => await parameterReorder!.PrepareAsync(request.Uri!, request.Range.Start, token),
            RefactoringKinds.InlineMember => await inlineMember!.PrepareAsync(request.Uri!, request.Range.Start, token),
            _ => await extractMethod.PrepareAsync(request.Kind, request.Uri!, request.Range, token),
        };
    }
}

/// <summary>
/// Serves <c>rdcore/refactor/apply</c>: the edit a refactoring of a range makes with the user's answers, or an error that says why it is refused. The edit is
/// the client's to apply.
/// </summary>
internal sealed class RefactorApplyHandler(
    IExtractMethodService extractMethod, IPlatformClientCapabilitiesService clientCapabilities, IEncapsulateFieldService? encapsulateField = null,
    IMoveCloserToUsageService? closerToUsage = null, IParameterReorderService? parameterReorder = null, IInlineMemberService? inlineMember = null)
    : RDCoreRequestHandler<RefactorApplyRequest, RefactorWorkspaceEdit>
{
    protected override async Task<RefactorWorkspaceEdit> HandleAsync(RefactorApplyRequest request, CancellationToken token)
    {
        RefactorRequests.Check(clientCapabilities, request.Kind, request.Uri, encapsulateField, closerToUsage, parameterReorder, inlineMember);
        if (request.Kind == RefactoringKinds.InlineMember)
        {
            var inlined = await inlineMember!.ApplyAsync(request.Uri!, request.Range.Start, request.Answers, request.Accepted, token);
            return inlined.Edit ?? throw new RpcErrorException(RefactorRequests.RequestFailedCode, error: null!, inlined.Refusal ?? string.Empty);
        }

        if (request.Kind == RefactoringKinds.EncapsulateField)
        {
            var encapsulated = await encapsulateField!.ApplyAsync(request.Uri!, request.Range.Start, request.Answers, request.Accepted, token);
            return encapsulated.Edit ?? throw new RpcErrorException(RefactorRequests.RequestFailedCode, error: null!, encapsulated.Refusal ?? string.Empty);
        }

        if (request.Kind == RefactoringKinds.MoveCloserToUsage)
        {
            var moved = await closerToUsage!.ApplyAsync(request.Uri!, request.Range.Start, request.Answers, request.Accepted, token);
            return moved.Edit ?? throw new RpcErrorException(RefactorRequests.RequestFailedCode, error: null!, moved.Refusal ?? string.Empty);
        }

        if (request.Kind == RefactoringKinds.ReorderParameters)
        {
            var reordered = await parameterReorder!.ApplyAsync(request.Uri!, request.Range.Start, request.Answers, request.Accepted, token);
            return reordered.Edit ?? throw new RpcErrorException(RefactorRequests.RequestFailedCode, error: null!, reordered.Refusal ?? string.Empty);
        }

        var answer = await extractMethod.ApplyAsync(request.Kind, request.Uri!, request.Range, request.Answers, request.Accepted, token);
        return answer.Edit ?? throw new RpcErrorException(RefactorRequests.RequestFailedCode, error: null!, answer.Refusal ?? string.Empty);
    }
}

/// <summary>
/// Serves <c>rdcore/refactor/checkIdentifier</c>: whether an identifier the user is typing for a refactoring of a range can be declared where the refactoring
/// declares it, and what it would hide, before the refactoring is applied. A client's dialog asks it as its user types.
/// </summary>
internal sealed class RefactorIdentifierCheckHandler(
    IExtractMethodService extractMethod, IPlatformClientCapabilitiesService clientCapabilities, IEncapsulateFieldService? encapsulateField = null)
    : RDCoreRequestHandler<RefactorCheckIdentifierRequest, RefactorIdentifierCheckResult>
{
    protected override async Task<RefactorIdentifierCheckResult> HandleAsync(RefactorCheckIdentifierRequest request, CancellationToken token)
    {
        RefactorRequests.Check(clientCapabilities, request.Kind, request.Uri, encapsulateField);
        return request.Kind == RefactoringKinds.EncapsulateField
            ? await encapsulateField!.CheckIdentifierAsync(request.Uri!, request.Range.Start, request.Answers, token)
            : await extractMethod.CheckIdentifierAsync(request.Kind, request.Uri!, request.Range, request.Answers, token);
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

    // Encapsulate Field is served when its service is, by prepare, checkIdentifier and apply; Move Closer To Usage, Reorder Parameters and Inline Member, by
    // prepare and apply: they ask no name to check.
    public static void Check(
        IPlatformClientCapabilitiesService clientCapabilities, string kind, Uri? document, IEncapsulateFieldService? encapsulateField = null,
        IMoveCloserToUsageService? closerToUsage = null, IParameterReorderService? parameterReorder = null, IInlineMemberService? inlineMember = null)
    {
        if (!clientCapabilities.Expects(capabilities => capabilities.Refactoring))
        {
            throw new RpcErrorException(InvalidRequestCode, error: null!, $"The client did not advertise the '{nameof(SDK.Client.Refactoring)}' platform capability.");
        }

        var served = kind is RefactoringKinds.ExtractMethod or RefactoringKinds.ConvertGoSub
            || (kind == RefactoringKinds.EncapsulateField && encapsulateField is not null)
            || (kind == RefactoringKinds.MoveCloserToUsage && closerToUsage is not null)
            || (kind == RefactoringKinds.ReorderParameters && parameterReorder is not null)
            || (kind == RefactoringKinds.InlineMember && inlineMember is not null);
        if (!served || document is null)
        {
            throw new RpcErrorException(InvalidParamsCode, error: null!, document is null ? "The request names no document." : $"No refactoring is of the kind '{kind}'.");
        }
    }
}
