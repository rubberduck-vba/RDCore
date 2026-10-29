using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace RDCore.LanguageServer.Workspace.Services;

/// <summary>
/// What the client shows of the documents it did not change, refreshed when what it is made of changed: another module of the workspace, the project, the live
/// settings (<strong>LSP 3.17</strong> <c>workspace/diagnostic/refresh</c>, <c>workspace/semanticTokens/refresh</c>, <c>workspace/inlayHint/refresh</c>,
/// <c>workspace/codeLens/refresh</c>).
/// </summary>
/// <remarks>
/// <para>
/// A module's diagnostics, semantic tokens, inlay hints and code lenses are of the module and of what the others declare and how they use it: a procedure that
/// another module calls, removed, makes the call an error there. What the client pulls of a document it did not change is pulled again only when it is asked to.
/// </para>
/// <para>
/// The client is asked once for a burst of changes - each keystroke of a name is a change - when the burst is over (<see cref="Burst"/>). What a change made
/// is told at once by <see cref="Generation"/>, which the result id of a document's diagnostics is made of: a pull that comes before the request is not answered
/// <c>unchanged</c> for a document whose diagnostics may have changed with another's.
/// </para>
/// </remarks>
internal interface IWorkspaceRefresh
{
    /// <summary>A number that is another one once the workspace changed in a way that can change what is found out about a module that did not change.</summary>
    long Generation { get; }

    /// <summary>
    /// The workspace changed in a way that can change what is found out about a module that did not change: the client is asked to refresh what it shows of every
    /// document, once the burst of changes is over.
    /// </summary>
    void Changed();

    /// <summary>
    /// What the code of a module refers to changed, and nothing it declares: what is found out about the other modules is as it was, and only what is counted
    /// of the references to their members - their code lenses - is refreshed, once the burst of changes is over. <see cref="Generation"/> is what it was.
    /// </summary>
    void ReferencesChanged();

    /// <summary>The server whose client is asked; until it is attached, a change only changes <see cref="Generation"/>.</summary>
    /// <param name="server">The server, initialized: what its client said it is asked for is known.</param>
    void Attach(ILanguageServerFacade server);
}

// a burst of no time asks the client at once, at each change.
internal sealed class WorkspaceRefresh(TimeSpan burst) : IWorkspaceRefresh, IDisposable
{
    /// <summary>How long after a change the client is asked to refresh, unless another change comes first.</summary>
    public static readonly TimeSpan Burst = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Each refresh, and whether the client says it is asked for it (<c>refreshSupport</c>); a capability with no value - what is there for a client that sent
    /// none - is no support.
    /// </summary>
    /// <remarks>
    /// Each refresh is a request, which the client answers. OmniSharp 0.19.9's <c>SendDiagnosticRefresh</c>, <c>SendSemanticTokensRefresh</c>,
    /// <c>SendInlayHintRefresh</c> and <c>SendCodeLensRefresh</c> write it as a notification, with no <c>id</c>, which a client that keeps to the protocol does not
    /// take for the request: each is sent here as the request it is.
    /// </remarks>
    public static IReadOnlyList<(string Method, Func<WorkspaceClientCapabilities, bool> IsSupported)> Requests { get; } =
    [
        ("workspace/diagnostic/refresh", workspace => workspace.Diagnostics is { IsSupported: true, Value.RefreshSupport: true }),
        ("workspace/semanticTokens/refresh", workspace => workspace.SemanticTokens is { IsSupported: true, Value.RefreshSupport: true }),
        ("workspace/inlayHint/refresh", workspace => workspace.InlayHint is { IsSupported: true, Value.RefreshSupport: true }),
        (CodeLensRefresh, workspace => workspace.CodeLens is { IsSupported: true, Value.RefreshSupport: true }),
    ];

    /// <summary>The refresh of what is counted of a member's references, which is all a change of what a module's code refers to asks for.</summary>
    public const string CodeLensRefresh = "workspace/codeLens/refresh";

    private readonly object _sync = new();
    private Timer? _timer;
    private long _generation;
    private bool _everything;
    private ILanguageServerFacade? _server;

    public long Generation => Interlocked.Read(ref _generation);

    public void Attach(ILanguageServerFacade server) => _server = server;

    public void Changed()
    {
        Interlocked.Increment(ref _generation);
        Schedule(everything: true);
    }

    public void ReferencesChanged() => Schedule(everything: false);

    private void Schedule(bool everything)
    {
        if (burst <= TimeSpan.Zero)
        {
            Send(everything);
            return;
        }

        // a change that comes before the burst is over puts the request off: the client is asked once, for all of them, and for every refresh when any of
        // them asked for every refresh.
        lock (_sync)
        {
            _everything |= everything;
            _timer ??= new Timer(_ => SendScheduled());
            _timer.Change(burst, Timeout.InfiniteTimeSpan);
        }
    }

    private void SendScheduled()
    {
        bool everything;
        lock (_sync)
        {
            (everything, _everything) = (_everything, false);
        }

        Send(everything);
    }

    /// <summary>The methods of the refreshes a client said it is asked for.</summary>
    public static IEnumerable<string> SupportedBy(ILanguageServerFacade server)
        => server.ClientSettings.Capabilities?.Workspace is { } workspace
            ? Requests.Where(request => request.IsSupported(workspace)).Select(request => request.Method)
            : [];

    private void Send(bool everything)
    {
        if (_server is not { } server)
        {
            return;
        }

        foreach (var method in SupportedBy(server).Where(method => everything || method == CodeLensRefresh))
        {
            _ = SendAsync(server, method);
        }
    }

    // what the client answers changes nothing: a client that fails to refresh does when it next pulls.
    private static async Task SendAsync(IResponseRouter server, string method)
    {
        try
        {
            await server.SendRequest(method).ReturningVoid(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // the request was asked; an error the client answers it with is the client's.
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
