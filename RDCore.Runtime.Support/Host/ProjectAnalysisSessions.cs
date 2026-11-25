using Microsoft.Extensions.Logging;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;

namespace RDCore.Runtime.Support.Host;

/// <summary>
/// A session of a project of the workspace that the host does not run: composed as the runtime session is, so that the semantic analysis pass runs over its
/// modules, and never run, debugged or discarded from.
/// </summary>
/// <param name="Root">The root the project's modules are relative to, and addressed under.</param>
/// <param name="ProjectName">The name the project declares; empty when it declares none.</param>
/// <param name="Session">The session, composed of the project and the libraries it references.</param>
/// <param name="Image">The code of the session: the lowered procedures of its modules, and their semantic models.</param>
/// <param name="ModuleCount">The number of modules the project declares.</param>
public sealed record class ProjectAnalysisSession(Uri Root, string ProjectName, IRuntimeSession Session, ProgramImage Image, int ModuleCount);

/// <summary>
/// The analysis sessions of the host (<c>rdcore/host/project/analyze</c>, <c>rdcore/host/project/release</c>): one for each project of the workspace other than
/// the one the host runs, by its root.
/// </summary>
/// <remarks>
/// A project is its own namespace: a session holds the modules of one project and the libraries that project references, so a name of another project is not
/// bound in it unless the project references that one. A file two projects list is a module of each, in each session.
/// </remarks>
public interface IProjectAnalysisSessions
{
    /// <summary>
    /// The analysis sessions, in no particular order.
    /// </summary>
    IReadOnlyList<ProjectAnalysisSession> All { get; }

    /// <summary>
    /// The analysis session of a project, by the root its modules are addressed under.
    /// </summary>
    /// <param name="root">The project's root, as <c>SymbolUri.Root</c> writes it.</param>
    /// <param name="analysis">The session, when there is one.</param>
    /// <returns>Whether there is one.</returns>
    bool TryGet(Uri root, [NotNullWhen(true)] out ProjectAnalysisSession? analysis);

    /// <summary>
    /// Composes the analysis session of a project, as the runtime session is composed, and replaces the one the project had: the session holds none of the
    /// modules defined in the one it replaces.
    /// </summary>
    /// <remarks>
    /// Nothing outside the session is changed: the current directory, the automation servers and the dialogs are the runtime session's.
    /// </remarks>
    /// <param name="project">The <c>.rdproj</c> project structure.</param>
    /// <param name="root">The root the project's modules are relative to, and addressed under.</param>
    /// <returns>The session.</returns>
    ProjectAnalysisSession Analyze(RDCoreProject project, Uri root);

    /// <summary>
    /// Lets go of the analysis session of a project, by its root.
    /// </summary>
    /// <param name="root">The project's root.</param>
    /// <returns>Whether there was one.</returns>
    bool Release(Uri root);
}

/// <inheritdoc cref="IProjectAnalysisSessions"/>
public sealed class ProjectAnalysisSessions(
    IRuntimeEnvironmentProfile environment,
    IFileSystem fileSystem,
    ILogger<ProjectAnalysisSessions> logger,
    ILibrarySource? librarySource = null) : IProjectAnalysisSessions
{
    // keyed by root, whichever address of the project is asked with.
    private readonly Dictionary<Uri, ProjectAnalysisSession> _sessions = new(SymbolUri.RootComparer);
    private readonly Lock _lock = new();

    /// <inheritdoc/>
    public IReadOnlyList<ProjectAnalysisSession> All
    {
        get
        {
            lock (_lock)
            {
                return [.. _sessions.Values];
            }
        }
    }

    /// <inheritdoc/>
    public bool TryGet(Uri root, [NotNullWhen(true)] out ProjectAnalysisSession? analysis)
    {
        ArgumentNullException.ThrowIfNull(root);
        lock (_lock)
        {
            return _sessions.TryGetValue(root, out analysis);
        }
    }

    /// <inheritdoc/>
    public ProjectAnalysisSession Analyze(RDCoreProject project, Uri root)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(root);

        // nothing it runs prints: its output goes nowhere. Its file statements act on no disk: the modules are read from the file system, and a user's files
        // are never the session's.
        var (session, _, _) = EnvironmentSessionProvider.ComposeSession(
            environment, fileSystem, librarySource, logger, project, root, new RuntimeOutputRouter(), sessionFileSystem: RuntimeSessionComposer.NoFiles());
        var analysis = new ProjectAnalysisSession(root, project.Name, session, EnvironmentSessionProvider.NewImage(logger), project.Modules.Length);
        lock (_lock)
        {
            _sessions[root] = analysis;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("🔍 Analysis session composed for {project} ({root}): {modules} module(s), {references} reference(s).",
                project.Name, root, project.Modules.Length, session.References.Count);
        }

        return analysis;
    }

    /// <inheritdoc/>
    public bool Release(Uri root)
    {
        ArgumentNullException.ThrowIfNull(root);
        lock (_lock)
        {
            return _sessions.Remove(root);
        }
    }
}
