using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;

namespace RDCore.LanguageServer.Folding;

/// <summary>
/// Serves <c>textDocument/foldingRange</c>, projecting each multi-line member declaration of the
/// requested document into a foldable region.
/// </summary>
/// <remarks>
/// <para>
/// Resolves the document against the workspace and parses through <see cref="IParsingClientService"/>,
/// the same way <c>DocumentDiagnosticsService</c> does. Reading only the parse cache would be cheaper and
/// would answer empty at exactly the wrong moment: the workspace parse is a background bring-up that
/// waits on the parsing server process, so it has usually not finished when a client asks for ranges on
/// the document it has just opened. <c>ParseDocumentAsync</c> waits for that readiness, so the request
/// blocks briefly on a cold start instead of returning nothing.
/// </para>
/// <para>
/// A document that is not part of the workspace answers empty without parsing, so a stray file cannot
/// put a failed result into the shared cache.
/// </para>
/// </remarks>
internal sealed class FoldingRangeHandler(
    IWorkspaceDocumentService documents,
    IParsingClientService parsing) : FoldingRangeHandlerBase
{
    public override async Task<Container<FoldingRange>?> Handle(FoldingRangeRequestParam request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToUri();

        if (Resolve(uri) is null)
        {
            return new Container<FoldingRange>();
        }

        var parseResult = await parsing.ParseDocumentAsync(uri, cancellationToken);

        // Deliberately not gated on IsSuccess. A module with syntax errors is when an outline is worth
        // most, and the members that did recover carry usable spans; those that did not are dropped by
        // the span test in the projector.
        if (parseResult.SyntaxTree is not { } module)
        {
            return new Container<FoldingRange>();
        }

        return new Container<FoldingRange>(FoldingRangeProjector.Project(module).ToArray());
    }

    protected override FoldingRangeRegistrationOptions CreateRegistrationOptions(
        FoldingRangeCapability capability, ClientCapabilities clientCapabilities)
        => new();

    private WorkspaceDocument? Resolve(Uri documentUri)
        => documents.GetAllDocuments().FirstOrDefault(document => document.Id.Uri.ToUri().Equals(documentUri));
}
