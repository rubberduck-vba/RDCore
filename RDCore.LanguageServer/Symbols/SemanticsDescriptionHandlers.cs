using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.LanguageServer.Symbols;

// rdcore/semantics/describe and /outline: what hover and the document outline know, as data. Neither is gated: they read what any client may read.

internal sealed class SemanticsDescribeHandler(ISemanticsDescriptionService semantics) : RDCoreRequestHandler<SemanticsDescribeRequest, DescribeResult>
{
    protected override async Task<DescribeResult> HandleAsync(SemanticsDescribeRequest request, CancellationToken token)
        => request.Uri is { } uri ? await semantics.DescribeAsync(uri, request.Position.ToLsp(), token) : new DescribeResult();
}

internal sealed class SemanticsOutlineHandler(ISemanticsDescriptionService semantics) : RDCoreRequestHandler<SemanticsOutlineRequest, ModuleOutline>
{
    // a document that is not a module of the workspace has no outline: the answer is null.
    protected override async Task<ModuleOutline> HandleAsync(SemanticsOutlineRequest request, CancellationToken token)
        => (request.Uri is { } uri ? await semantics.OutlineAsync(uri, token) : null)!;
}
