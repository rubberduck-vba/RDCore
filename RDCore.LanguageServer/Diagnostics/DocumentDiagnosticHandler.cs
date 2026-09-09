using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Diagnostics;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.LanguageServer.Diagnostics;

// LSP 3.17 textDocument/diagnostic (pull). The client asks, this handler delegates the fan-out and
// staleness gate to IDocumentDiagnosticsService and maps the aggregated platform diagnostics to LSP.
internal sealed class DocumentDiagnosticHandler(IDocumentDiagnosticsService diagnostics) : DocumentDiagnosticHandlerBase
{
    public override async Task<RelatedDocumentDiagnosticReport> Handle(DocumentDiagnosticParams request, CancellationToken cancellationToken)
    {
        var result = await diagnostics.GetAsync(request.TextDocument.Uri.ToUri(), request.PreviousResultId, cancellationToken);

        if (result.Unchanged)
        {
            return new RelatedUnchangedDocumentDiagnosticReport { ResultId = result.ResultId };
        }

        return new RelatedFullDocumentDiagnosticReport
        {
            ResultId = result.ResultId,
            Items = new Container<LspDiagnostic>(result.Diagnostics.Select(ToLspDiagnostic)),
        };
    }

    protected override DiagnosticsRegistrationOptions CreateRegistrationOptions(
        DiagnosticClientCapabilities capability, ClientCapabilities clientCapabilities)
        => new()
        {
            Identifier = "rdcore",
            // one module's diagnostics never depend on another's, and workspace diagnostics aren't served yet.
            InterFileDependencies = false,
            WorkspaceDiagnostics = false,
        };

    private static LspDiagnostic ToLspDiagnostic(PlatformDiagnostic diagnostic)
    {
        var range = diagnostic.Location.Range;
        return new LspDiagnostic
        {
            Range = new LspRange(
                new Position(range.Start.Line, range.Start.Character),
                new Position(range.End.Line, range.End.Character)),
            Severity = (LspDiagnosticSeverity)diagnostic.Severity,
            Code = diagnostic.Code,
            Source = diagnostic.Source,
            Message = diagnostic.Message,
            // the faulted-token detail rides Data so a client can surface it without a second request.
            Data = diagnostic.Verbose is null ? null : JToken.FromObject(diagnostic.Verbose),
        };
    }
}
