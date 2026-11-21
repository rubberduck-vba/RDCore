using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Diagnostics;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Workspace.Services;

/// <summary>
/// What is wrong with the projects of the workspace themselves, said of their <c>.rdproj</c>: each project on a circular reference of projects
/// (RDC31001, <see cref="ProjectReferenceCycles"/>), and what the project analyzers of the extensions find of each project
/// (<see cref="IProjectDiagnosticsProviders"/>).
/// </summary>
internal interface IProjectFileDiagnostics
{
    /// <summary>
    /// The diagnostics of each <c>.rdproj</c> that has some, as the projects are now; none of a code that is disabled.
    /// </summary>
    /// <param name="token">A token that cancels the requests to the extensions.</param>
    Task<IReadOnlyList<(DocumentUri Uri, IReadOnlyList<Diagnostic> Diagnostics)>> CurrentAsync(CancellationToken token);

    /// <summary>
    /// Publishes the diagnostics of each <c>.rdproj</c> to a client that does not pull them, and clears those of a <c>.rdproj</c> that has none now.
    /// </summary>
    /// <param name="token">A token that cancels the requests to the extensions.</param>
    Task PublishAsync(CancellationToken token);
}

internal sealed class ProjectFileDiagnostics(
    IWorkspaceProjects projects, IDiagnosticsPublisher publisher, IDisabledDiagnostics? disabled = null, IProjectDiagnosticsProviders? providers = null)
    : IProjectFileDiagnostics
{
    // a core diagnostic the language server reports itself, of a project rather than of a module: the core extension's registry of RDC codes names it
    // CircularProjectReference.
    internal static readonly RDCoreDiagnosticCode CircularProjectReference = new(RDCoreDiagnosticCode.CorePrefix, 31001);

    private static readonly DiagnosticFactory Findings = new();

    private readonly SemaphoreSlim _publishing = new(1, 1);
    private HashSet<DocumentUri> _published = [];

    public async Task<IReadOnlyList<(DocumentUri Uri, IReadOnlyList<Diagnostic> Diagnostics)>> CurrentAsync(CancellationToken token)
    {
        var all = projects.Projects.Where(project => project.Project.ProjectInfo is not null).ToList();
        var disabledCodes = disabled?.Codes ?? new HashSet<RDCoreDiagnosticCode>();
        var cycles = ProjectReferenceCycles.Find(all.Select(project => project.Project.ProjectInfo))
            .Where(_ => !disabledCodes.Contains(CircularProjectReference))
            .ToList();

        var found = new List<(DocumentUri, IReadOnlyList<Diagnostic>)>();
        foreach (var project in all)
        {
            // a cycle never goes through a name two projects have, so the name on a path is the project's.
            var name = project.Project.ProjectInfo.Name;
            var file = Path.Combine(project.Root, ProjectFile.FileName);
            var diagnostics = cycles
                .Where(cycle => cycle.Path.Contains(name, StringComparer.OrdinalIgnoreCase))
                .Select(cycle => Findings.FromAnalyzerFinding(new AnalyzerFinding(CircularProjectReference, SourceRange.Empty, DiagnosticSeverity.Error,
                    string.Format(WorkspaceMessages.CircularProjectReference, name, string.Join(" → ", cycle.Path)))))
                .ToList();

            // a project the workspace has no .rdproj of yet (IsSynthesized) has no file to report what the extensions find against, nor a name its user gave.
            if (providers is not null && !project.Project.IsSynthesized)
            {
                var payload = new DiagnoseProjectPayload(new Uri(file), project.Project.ProjectInfo, NameRangeOf(file)) { DisabledCodes = [.. disabledCodes] };
                diagnostics.AddRange(await providers.DiagnoseAsync(payload, token));
            }

            if (diagnostics.Count > 0)
            {
                found.Add((DocumentUri.FromFileSystemPath(file), diagnostics));
            }
        }

        return found;
    }

    public async Task PublishAsync(CancellationToken token)
    {
        if (!publisher.IsEnabled)
        {
            return;
        }

        await _publishing.WaitAsync(token);
        try
        {
            var current = await CurrentAsync(token);
            foreach (var (uri, diagnostics) in current)
            {
                publisher.Publish(uri.ToUri(), version: null, diagnostics);
            }

            var now = current.Select(each => each.Uri).ToHashSet();
            foreach (var gone in _published.Where(uri => !now.Contains(uri)))
            {
                publisher.Clear(gone.ToUri());
            }

            _published = now;
        }
        finally
        {
            _publishing.Release();
        }
    }

    // where the project file writes the project's name, for an analyzer's finding of the name; none when the file cannot be read.
    private static SourceRange? NameRangeOf(string file)
    {
        try
        {
            return File.Exists(file) ? ProjectFile.NameRangeIn(File.ReadAllText(file)) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
