using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.SDK;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Runtime;

/// <summary>
/// The client of the language server, as the dialogs of a program reach it: whether it shows them, and how it is asked to.
/// </summary>
internal interface IInteractionClient
{
    /// <summary>
    /// Whether the client said it answers a program's dialogs (<see cref="SDK.Client.SessionInteraction"/>), and not that nobody is there to
    /// (<see cref="SDK.Client.Unattended"/>), which wins.
    /// </summary>
    bool ShowsDialogs { get; }

    /// <summary>The name the client gave itself when it connected (<c>clientInfo.name</c>); <see langword="null"/> when it gave none.</summary>
    string? Name { get; }

    /// <summary>Asks the client to answer a <c>MsgBox</c> (<c>rdcore/session/interaction/msgBox</c>).</summary>
    Task<MessageBoxResult> MessageBoxAsync(SessionMessageBoxRequest request, CancellationToken token);

    /// <summary>Asks the client to answer an <c>InputBox</c> (<c>rdcore/session/interaction/inputBox</c>).</summary>
    Task<InputBoxResult> InputBoxAsync(SessionInputBoxRequest request, CancellationToken token);
}

/// <summary>
/// Relays the dialogs of the program that runs in the environment host to the client of the language server, when the client shows dialogs.
/// </summary>
/// <remarks>
/// <para>
/// The host asks whatever its language server's client is; a client that did not expect <see cref="SDK.Client.SessionInteraction"/> - or no client yet - is never
/// asked, and the answer is that nobody was (<see cref="MessageBoxResult.NotHandled"/>), so that the host shows the dialog of the platform it runs on.
/// </para>
/// <para>
/// A client that did expect it is the one that answers, and the host shows no dialog of its own after it: one that did not handle the dialog declined it
/// (<see cref="MessageBoxResult.Declined"/>), and an answer that is none of the dialog's buttons is told as the failure it is
/// (<see cref="MessageBoxResult.Failure"/>), naming the client, the buttons and the answer. The program's run fails either way: a native dialog in a host nobody
/// sees is a run that waits forever.
/// </para>
/// </remarks>
internal interface IClientInteractionRelay
{
    /// <summary>Attaches the client, once the connection to it is live.</summary>
    void Attach(IInteractionClient client);

    /// <summary>Relays a <c>MsgBox</c> from the host.</summary>
    Task<MessageBoxResult> MessageBoxAsync(HostMessageBoxParams request, CancellationToken token);

    /// <summary>Relays an <c>InputBox</c> from the host.</summary>
    Task<InputBoxResult> InputBoxAsync(HostInputBoxParams request, CancellationToken token);
}

// while the client is asked, its diagnostics pulls are declined (OpenDialogs): what they are made of waits for the program, which waits for the client.
internal sealed class ClientInteractionRelay(OpenDialogs dialogs, ILogger<ClientInteractionRelay>? logger = null) : IClientInteractionRelay
{
    private readonly ILogger _logger = logger ?? NullLogger<ClientInteractionRelay>.Instance;
    private readonly OpenDialogs _dialogs = dialogs;
    private IInteractionClient? _client;

    public void Attach(IInteractionClient client) => _client = client;

    public async Task<MessageBoxResult> MessageBoxAsync(HostMessageBoxParams request, CancellationToken token)
    {
        if (_client is not { ShowsDialogs: true } client)
        {
            return MessageBoxResult.NotHandled;
        }

        var answer = await _dialogs.WhileOpenAsync(() => client.MessageBoxAsync(
            new SessionMessageBoxRequest { Prompt = request.Prompt, Title = request.Title, Style = request.Style, Output = request.Output }, token));
        if (!answer.Handled)
        {
            _logger.LogWarning("🙅 {Client} declined to answer the program's MsgBox ({Buttons}).", NameOf(client), VBMsgBoxButtons.NameOf((VBMsgBoxStyle)request.Style));
            return answer with { Declined = true };
        }

        var style = (VBMsgBoxStyle)request.Style;
        if (VBMsgBoxButtons.ChoicesOf(style).Contains((VBMsgBoxResult)answer.Result))
        {
            return answer;
        }

        var failure = string.Format(Exceptions.VBRuntimeError_UnusableDialogAnswer_NoSuchButton, NameOf(client), VBMsgBoxButtons.NameOf(style), answer.Result);
        _logger.LogWarning("⚠️ {Failure}", failure);
        return answer with { Failure = failure };
    }

    public async Task<InputBoxResult> InputBoxAsync(HostInputBoxParams request, CancellationToken token)
    {
        if (_client is not { ShowsDialogs: true } client)
        {
            return InputBoxResult.NotHandled;
        }

        var answer = await _dialogs.WhileOpenAsync(() => client.InputBoxAsync(
            new SessionInputBoxRequest
            {
                Prompt = request.Prompt, Title = request.Title, Default = request.Default, XPos = request.XPos, YPos = request.YPos, Output = request.Output,
            }, token));
        if (answer.Handled)
        {
            return answer;
        }

        _logger.LogWarning("🙅 {Client} declined to answer the program's InputBox.", NameOf(client));
        return answer with { Declined = true };
    }

    private static string NameOf(IInteractionClient client) => client.Name is { Length: > 0 } name ? name : Exceptions.UnnamedClient;
}

/// <summary>
/// The client of a running language server: what it expects is what it said in the platform handshake.
/// </summary>
/// <param name="server">The language server, connected to its client.</param>
internal sealed class LanguageServerInteractionClient(ILanguageServer server) : IInteractionClient
{
    public bool ShowsDialogs => server.Services.GetService<IPlatformClientCapabilitiesService>() is { } expected
        && expected.Expects(capabilities => capabilities.SessionInteraction) && !expected.Expects(capabilities => capabilities.Unattended);

    public string? Name => server.ClientSettings?.ClientInfo?.Name;

    public Task<MessageBoxResult> MessageBoxAsync(SessionMessageBoxRequest request, CancellationToken token) => server.SendRequest(request, token);

    public Task<InputBoxResult> InputBoxAsync(SessionInputBoxRequest request, CancellationToken token) => server.SendRequest(request, token);
}

/// <summary>
/// Handles <c>rdcore/host/interaction/msgBox</c>: the program that runs in the environment host opened a <c>MsgBox</c>.
/// </summary>
internal sealed class HostMessageBoxHandler(IClientInteractionRelay relay) : IJsonRpcRequestHandler<HostMessageBoxParams, MessageBoxResult>
{
    public Task<MessageBoxResult> Handle(HostMessageBoxParams request, CancellationToken cancellationToken) => relay.MessageBoxAsync(request, cancellationToken);
}

/// <summary>
/// Handles <c>rdcore/host/interaction/inputBox</c>: the program that runs in the environment host opened an <c>InputBox</c>.
/// </summary>
internal sealed class HostInputBoxHandler(IClientInteractionRelay relay) : IJsonRpcRequestHandler<HostInputBoxParams, InputBoxResult>
{
    public Task<InputBoxResult> Handle(HostInputBoxParams request, CancellationToken cancellationToken) => relay.InputBoxAsync(request, cancellationToken);
}
