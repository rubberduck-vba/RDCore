using Microsoft.Extensions.Options;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Server.Configuration;

namespace RDCore.LanguageServer.Diagnostics;

/// <summary>
/// The codes whose diagnostics are not reported (<c>Analysis.DisabledDiagnostics</c>): those of the user's settings of the platform, which the client says while
/// the server runs, and those of the workspace's, together.
/// </summary>
internal interface IDisabledDiagnostics
{
    /// <summary>
    /// The codes disabled for the documents of the workspace, which the providers are sent with each document (<see cref="DiagnoseDocumentPayload.DisabledCodes"/>).
    /// </summary>
    IReadOnlySet<RDCoreDiagnosticCode> Codes { get; }
}

/// <remarks>
/// A change of the codes, which the client says (<see cref="ILiveSettings.Changed"/>), changes what is found of every document: the workspace's findings are
/// refreshed (<see cref="IWorkspaceRefresh.Changed"/>), so that a client that pulls them is not answered that nothing changed.
/// </remarks>
internal sealed class DisabledDiagnostics : IDisabledDiagnostics, IDisposable
{
    private readonly IOptions<SdkAppOptions> _options;
    private readonly ILiveSettings _live;
    private readonly IWorkspaceRefresh _refresh;
    private readonly IWorkspaceSettings? _workspace;
    private readonly Func<string> _workspaceRoot;
    private IReadOnlySet<RDCoreDiagnosticCode> _codes;

    public DisabledDiagnostics(IOptions<SdkAppOptions> options, ILiveSettings live, IWorkspaceRefresh refresh, Func<string> workspaceRoot, IWorkspaceSettings? workspace = null)
    {
        (_options, _live, _refresh, _workspaceRoot, _workspace) = (options, live, refresh, workspaceRoot, workspace);
        _codes = Read();
        _live.Changed += SettingsChanged;
    }

    public IReadOnlySet<RDCoreDiagnosticCode> Codes => _codes;

    private IReadOnlySet<RDCoreDiagnosticCode> Read()
    {
        var root = _workspaceRoot();
        var workspace = _workspace is not null && root.Length > 0 ? _workspace.Read(new Uri(root), "Analysis.DisabledDiagnostics") : [];
        return _options.Value.Analysis.DisabledCodesWith(workspace);
    }

    private void SettingsChanged(object? sender, EventArgs e)
    {
        var codes = Read();
        if (!codes.SetEquals(_codes))
        {
            _codes = codes;
            _refresh.Changed();
        }
    }

    public void Dispose() => _live.Changed -= SettingsChanged;
}
