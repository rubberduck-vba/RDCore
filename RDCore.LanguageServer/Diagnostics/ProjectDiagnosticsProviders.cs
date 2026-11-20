using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Client;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.LanguageServer.Diagnostics;

/// <summary>
/// The extensions that have project analyzers (<see cref="DiagnoseProject"/>), asked what they find of a project over <c>rdcore/diagnostics/project</c>.
/// </summary>
internal interface IProjectDiagnosticsProviders
{
    /// <summary>
    /// What every provider finds of the project, at ranges of its <c>.rdproj</c>; none when no extension has project analyzers.
    /// </summary>
    /// <param name="payload">The project, where its project file writes its name, and the codes that are disabled.</param>
    /// <param name="token">A token that cancels the requests.</param>
    Task<IReadOnlyList<Diagnostic>> DiagnoseAsync(DiagnoseProjectPayload payload, CancellationToken token);
}

internal sealed class ProjectDiagnosticsProviders(IPlatformOrchestrationService orchestration, ILogger<ProjectDiagnosticsProviders> logger)
    : IProjectDiagnosticsProviders
{
    public async Task<IReadOnlyList<Diagnostic>> DiagnoseAsync(DiagnoseProjectPayload payload, CancellationToken token)
    {
        var providers = orchestration.Extensions
            .Where(extension => extension.ExtensionInfo?.Capabilities
                .Any(capability => capability.Name == nameof(DiagnoseProject) && capability.IsSupported) == true)
            .ToList();
        if (providers.Count == 0)
        {
            return [];
        }

        var json = PlatformJson.Serialize(payload);
        var found = new List<Diagnostic>();
        foreach (var provider in providers)
        {
            try
            {
                await provider.WaitForReadyAsync(token);
                var response = await provider.SendRequestAsync<DiagnoseProjectParams, DiagnoseProjectResult>(new DiagnoseProjectParams { Json = json }, token);
                found.AddRange(response?.Diagnostics is { } diagnostics ? ExtensionFindings.Admit(provider.ExtensionInfo, [.. diagnostics], logger) : []);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // one provider failing must not sink the others' diagnostics.
                logger.LogWarning(
                    exception, "❌ {provider} project diagnostics failed for {uri}.", provider.ExtensionInfo?.Title ?? "extension", payload.ProjectFile);
            }
        }

        return found;
    }
}
