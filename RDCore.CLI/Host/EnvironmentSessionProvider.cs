using Microsoft.Extensions.Logging;
using RDCore.CLI.Host.Symbols;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;
using System.IO.Abstractions;

namespace RDCore.CLI.Host;

/// <summary>
/// Owns the environment host's single <see cref="IRuntimeSession"/>. The session is composed once,
/// when the language server sends the LSP <c>initialize</c> carrying the workspace root, from the
/// <c>.rdproj</c> project structure and its precompiler constants. Member symbols are added
/// afterwards, as the language server sends their descriptors over <c>rdcore/host/symbols/define</c>.
/// </summary>
/// <remarks>
/// Registered as a singleton in the outer host container so the app can compose it on
/// <c>initialize</c>, and bridged into the language-server handler container so the
/// <c>rdcore/host/symbols/define</c> handler resolves the same instance.
/// </remarks>
public interface IEnvironmentSessionProvider
{
    /// <summary>
    /// Whether <see cref="Compose"/> has run and <see cref="Session"/> is available.
    /// </summary>
    bool IsComposed { get; }

    /// <summary>
    /// The composed runtime session.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The session has not been composed yet — it is composed on the LSP <c>initialize</c> handshake.
    /// </exception>
    IRuntimeSession Session { get; }

    /// <summary>
    /// Composes the session from a loaded project's module structure and precompiler constants.
    /// Replaces any previously composed session.
    /// </summary>
    /// <param name="project">The <c>.rdproj</c> project structure.</param>
    /// <param name="workspaceRoot">The workspace root the project's modules are relative to.</param>
    IRuntimeSession Compose(RDCoreProject project, Uri workspaceRoot);
}

/// <inheritdoc cref="IEnvironmentSessionProvider"/>
public sealed class EnvironmentSessionProvider(
    IRuntimeEnvironmentProfile environment,
    IFileSystem fileSystem,
    ILogger<EnvironmentSessionProvider> logger) : IEnvironmentSessionProvider
{
    private IRuntimeSession? _session;

    /// <inheritdoc/>
    public bool IsComposed => _session is not null;

    /// <inheritdoc/>
    public IRuntimeSession Session => _session ?? throw new InvalidOperationException(
        "The runtime session has not been composed yet; it is composed on the LSP initialize handshake.");

    /// <inheritdoc/>
    public IRuntimeSession Compose(RDCoreProject project, Uri workspaceRoot)
    {
        var configuration = new ConfigurationSymbolProvider(environment, project);
        var modules = new ProjectSymbolProvider(workspaceRoot, project, fileSystem);

        _session = RuntimeSessionComposer.Compose(environment, MapReferences(project.References), [configuration, modules]);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "🧠 Runtime session composed: {modules} module(s), {references} reference(s), {constants} precompiler constant(s) (built-ins included).",
                project.Modules.Length, _session.References.Count, configuration.ProvideSymbols().Count());
        }

        return _session;
    }

    // the .rdproj declares references in precedence order (RD-VBAL §2.3.1.2); the list index is the rank.
    private static IReadOnlyList<ReferencePriorityInfo> MapReferences(RDCoreReference[] references)
        => [.. references.Select((reference, rank) => new ReferencePriorityInfo(reference.Name, rank))];
}
