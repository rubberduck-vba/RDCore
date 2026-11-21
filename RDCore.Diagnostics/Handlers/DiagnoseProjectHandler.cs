using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Platform;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Diagnostics.Handlers;

[Method(RDCoreExtensionProtocol.DiagnoseProject)]
internal sealed class DiagnoseProjectHandler(
    ICoreDiagnosticsFactory diagnostics, IEnumerable<IProjectAnalyzer> analyzers, ILogger<DiagnoseProjectHandler> logger)
    : RDCoreRequestHandler<DiagnoseProjectParams, DiagnoseProjectResult>
{
    protected override Task<DiagnoseProjectResult> HandleAsync(DiagnoseProjectParams request, CancellationToken token)
    {
        var payload = PlatformJson.Deserialize<DiagnoseProjectPayload>(request.Json);
        var found = ProjectAnalyzerHost.Run(analyzers, payload, logger).Select(diagnostics.FromAnalyzerFinding).ToList();

        logger.LogInformation("📥 {method}: {uri} → {count} diagnostic(s)", RDCoreExtensionProtocol.DiagnoseProject, payload.ProjectFile, found.Count);
        return Task.FromResult(new DiagnoseProjectResult { Diagnostics = found });
    }
}
