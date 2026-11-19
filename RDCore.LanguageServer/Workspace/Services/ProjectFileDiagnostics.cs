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
/// (RDC31001, <see cref="ProjectReferenceCycles"/>).
/// </summary>
internal interface IProjectFileDiagnostics
{
    /// <summary>
    /// The diagnostics of each <c>.rdproj</c> that has some, as the projects are now; none when the code is disabled.
    /// </summary>
    IReadOnlyList<(DocumentUri Uri, IReadOnlyList<Diagnostic> Diagnostics)> Current();

    /// <summary>
    /// Publishes the diagnostics of each <c>.rdproj</c> to a client that does not pull them, and clears those of a <c>.rdproj</c> that has none now.
    /// </summary>
    void Publish();
}

internal sealed class ProjectFileDiagnostics(IWorkspaceProjects projects, IDiagnosticsPublisher publisher, IDisabledDiagnostics? disabled = null)
    : IProjectFileDiagnostics
{
    // a core diagnostic the language server reports itself, of a project rather than of a module: the core extension's registry of RDC codes names it
    // CircularProjectReference.
    internal static readonly RDCoreDiagnosticCode CircularProjectReference = new(RDCoreDiagnosticCode.CorePrefix, 31001);

    private static readonly DiagnosticFactory Findings = new();

    private readonly object _publishing = new();
    private HashSet<DocumentUri> _published = [];

    public IReadOnlyList<(DocumentUri Uri, IReadOnlyList<Diagnostic> Diagnostics)> Current()
    {
        if (disabled?.Codes.Contains(CircularProjectReference) == true)
        {
            return [];
        }

        var all = projects.Projects.Where(project => project.Project.ProjectInfo is not null).ToList();
        var cycles = ProjectReferenceCycles.Find(all.Select(project => project.Project.ProjectInfo));
        if (cycles.IsEmpty)
        {
            return [];
        }

        // a cycle never goes through a name two projects have, so the name on a path is the project's.
        var found = new List<(DocumentUri, IReadOnlyList<Diagnostic>)>();
        foreach (var project in all)
        {
            var name = project.Project.ProjectInfo.Name;
            var diagnostics = cycles
                .Where(cycle => cycle.Path.Contains(name, StringComparer.OrdinalIgnoreCase))
                .Select(cycle => Findings.FromAnalyzerFinding(new AnalyzerFinding(CircularProjectReference, SourceRange.Empty, DiagnosticSeverity.Error,
                    string.Format(WorkspaceMessages.CircularProjectReference, name, string.Join(" → ", cycle.Path)))))
                .ToList();

            if (diagnostics.Count > 0)
            {
                found.Add((DocumentUri.FromFileSystemPath(Path.Combine(project.Root, ProjectFile.FileName)), diagnostics));
            }
        }

        return found;
    }

    public void Publish()
    {
        if (!publisher.IsEnabled)
        {
            return;
        }

        lock (_publishing)
        {
            var current = Current();
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
    }
}
